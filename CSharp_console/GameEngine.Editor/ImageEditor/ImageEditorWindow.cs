using System.Diagnostics;
using System.Numerics;
using System.Windows.Forms;
using GameEngine.Imaging;
using GameEngine.Imaging.Codecs;
using GameEngine.Imaging.Processing;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// 画像エディターの窓 (v2.3)
//   上   : Save / Save As / Reset / Undo / Redo、表示 (After / Before / 並べる / ワイプ)、色の出し方 (Color / Luminance / Normal)、拡大
//   左   : 処理の並び (上から順に掛ける。ON/OFF・上下・消す。Add で足す)
//   中央 : 画像 (ImageView。ホイールで拡大、ドラッグで移動、ダブルクリックで全体)
//   右   : 選んだ処理の設定 (トーンカーブのグラフ / 周波数フィルターのスペクトル など)
//   非破壊 : 元の画像は変えず、処理の並び (ImagePipeline) を変える。Undo / Redo はその JSON を積む
//   計算はバックグラウンド (新しい変更が来たら古い計算はやめる)。プレビューは長い辺 2048 まで縮めた画像で計算し、
//   保存の時だけ原寸で計算し直す (Preview at full resolution で原寸のプレビューにもできる)
//   GPU のテクスチャは結果が変わる度に作り、古い物は Graphics.ReleaseUITexture で返す (GPU が使い終わってから消える)
// ==========================================================================
public sealed partial class ImageEditorWindow : IDisposable
{
    /// <summary>プレビューの長い辺 (これより大きい画像は縮めて計算する)</summary>
    public const int PreviewMaxSize = 2048;

    public enum CompareMode { After, Before, SideBySide, Split }
    public enum DisplayMode { Color, Luminance, Normal }

    private readonly Engine _engine;
    private readonly AssetDatabase _database;
    private readonly int _id;

    public string FullPath { get; private set; }
    public string FileName => System.IO.Path.GetFileName(FullPath);
    public ImageInfo Info { get; private set; }

    /// <summary>読み込んだ元の画像 (原寸。変えない)</summary>
    public ImageBuffer Original { get; private set; }

    /// <summary>プレビューの計算に使う画像 (Original か、縮めた物)</summary>
    public ImageBuffer PreviewSource { get; private set; }

    /// <summary>今の処理の並び (UI が直接変える)</summary>
    public ImagePipeline Pipeline { get; private set; } = new();

    /// <summary>最後にプレビューを計算した結果 (PreviewSource の大きさ)</summary>
    public ImageBuffer? Result { get; private set; }

    public CompareMode Compare = CompareMode.Split;
    public DisplayMode Display = DisplayMode.Color;
    public NormalMapSettings NormalView { get; } = new();   // Display = Normal の時の法線の作り方
    public bool FullResolutionPreview;
    public ImageSaveOptions SaveOptions { get; } = new();

    public int SelectedIndex = -1;
    public bool HasFocus { get; private set; }
    public bool IsClosed { get; private set; }
    public bool Dirty => Pipeline.ToJson() != _savedJson;
    public int SaveCount { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>計算が終わった回数 (確認用)</summary>
    public int ProcessedCount { get; private set; }
    public double LastProcessMs { get; private set; }
    public bool IsProcessing => _job != null && !_job.IsCompleted;
    public bool IsSaving => _saveJob != null && !_saveJob.IsCompleted;

    // GPU のテクスチャ (0xFFFFFFFF = 無し)
    public uint BeforeTexture => _beforeTexture;
    public uint AfterTexture => _afterTexture;
    public uint SpectrumTexture => _spectrumTexture;
    private uint _afterTexture = NoTexture, _beforeTexture = NoTexture, _spectrumTexture = NoTexture;
    public const uint NoTexture = 0xFFFFFFFFu;
    /// <summary>今までに作ったテクスチャ / 返したテクスチャの数 (確認用 : 漏れていないか)</summary>
    public int TexturesCreated { get; private set; }
    public int TexturesReleased { get; private set; }

    private readonly PipelineCache _cache = new();
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private string _committed;
    private string _savedJson;
    private string? _requestedKey;
    private int _requestVersion;
    private Task? _job;
    private CancellationTokenSource? _cancel;
    private (int Version, ImageBuffer Result, byte[] AfterRgba, byte[]? BeforeRgba, string? BeforeKey, byte[]? SpectrumRgba, string SpectrumKey, int Width, int Height, double Ms)? _finished;
    private readonly object _lock = new();
    private Task<string?>? _saveJob;
    private string? _saveMessage;
    private bool _focusNext = true;
    private bool _askClose;

    internal ImageEditorWindow(Engine engine, AssetDatabase database, string fullPath, int id)
    {
        _engine = engine;
        _database = database;
        _id = id;
        FullPath = fullPath;
        var decoded = ImageCodec.Load(fullPath);
        Info = decoded.Info;
        Original = decoded.Image;
        PreviewSource = Original.DownscaleToFit(PreviewMaxSize);
        _committed = _savedJson = Pipeline.ToJson();
        _view = new ImageView();
    }

    public void Focus() => _focusNext = true;

    /// <summary>プレビューの縮小率 (PreviewSource / Original)</summary>
    public float PreviewScale => (float)PreviewSource.Width / Original.Width;

    // ---------------------------------------------------------------------
    // 処理の並びの操作 (UI と確認用)
    // ---------------------------------------------------------------------

    public ImageOperation Add(ImageOperation operation)
    {
        int at = SelectedIndex >= 0 && SelectedIndex < Pipeline.Operations.Count ? SelectedIndex + 1 : Pipeline.Operations.Count;
        Pipeline.Operations.Insert(at, operation);
        SelectedIndex = at;
        Commit();
        return operation;
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= Pipeline.Operations.Count) return;
        Pipeline.Operations.RemoveAt(index);
        SelectedIndex = Math.Min(SelectedIndex, Pipeline.Operations.Count - 1);
        Commit();
    }

    public void Move(int index, int delta)
    {
        int to = index + delta;
        if (index < 0 || index >= Pipeline.Operations.Count || to < 0 || to >= Pipeline.Operations.Count) return;
        (Pipeline.Operations[index], Pipeline.Operations[to]) = (Pipeline.Operations[to], Pipeline.Operations[index]);
        if (SelectedIndex == index) SelectedIndex = to;
        Commit();
    }

    /// <summary>処理を全部外す (Undo できる)</summary>
    public void Reset()
    {
        Pipeline.Operations.Clear();
        SelectedIndex = -1;
        Commit();
    }

    /// <summary>今の並びを元に戻す記録に入れる (変わっていなければ何もしない)</summary>
    public void Commit()
    {
        var json = Pipeline.ToJson();
        if (json == _committed) return;
        _undo.Add(_committed);
        if (_undo.Count > 200) _undo.RemoveAt(0);
        _redo.Clear();
        _committed = json;
    }

    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;

    public void Undo() => restore(_undo, _redo);
    public void Redo() => restore(_redo, _undo);

    private void restore(List<string> from, List<string> to)
    {
        Commit();   // つまみを動かしている途中の物も記録に入れてから
        if (from.Count == 0) return;
        to.Add(_committed);
        _committed = from[^1];
        from.RemoveAt(from.Count - 1);
        Pipeline = ImagePipeline.FromJson(_committed);
        SelectedIndex = Math.Min(SelectedIndex, Pipeline.Operations.Count - 1);
    }

    // ---------------------------------------------------------------------
    // バックグラウンドの計算
    // ---------------------------------------------------------------------

    private string requestKey() => $"{Pipeline.ToJson()}|{Display}|{System.Text.Json.JsonSerializer.Serialize(NormalView)}|{FullResolutionPreview}|{spectrumKey()}";

    /// <summary>スペクトルを出す処理 (選んでいる周波数フィルター) の番号。無ければ -1</summary>
    public int SpectrumIndex => SelectedIndex >= 0 && SelectedIndex < Pipeline.Operations.Count && Pipeline.Operations[SelectedIndex] is FrequencyFilterOperation ? SelectedIndex : -1;

    /// <summary>スペクトルの大きさ (表示用。正方形)</summary>
    public const int SpectrumSize = 256;

    // スペクトルはフィルターに入る画像 (前の処理の結果) の物。前の処理が変わった時だけ作り直す
    private string spectrumKey()
    {
        int index = SpectrumIndex;
        if (index < 0) return "";
        return $"{index}:{PreviewSource.Width}:{new ImagePipeline { Operations = Pipeline.Operations.Take(index).ToList() }.ToJson()}";
    }

    private string _shownSpectrumKey = "";

    /// <summary>変わっていれば計算を始める (毎フレーム呼ぶ)</summary>
    private void updateProcessing()
    {
        if (FullResolutionPreview != _previewIsFull)
        {
            _previewIsFull = FullResolutionPreview;
            PreviewSource = _previewIsFull ? Original : Original.DownscaleToFit(PreviewMaxSize);
            _requestedKey = null;
            _beforeKey = null;
        }
        var key = requestKey();
        if (key != _requestedKey)
        {
            _requestedKey = key;
            // 元の画像の表示 (Before) は、色の出し方 / 法線の作り方 / 大きさが変わった時だけ作り直す (受け取った時に _beforeKey を変える)
            var beforeKey = $"{Display}|{System.Text.Json.JsonSerializer.Serialize(NormalView)}|{PreviewSource.Width}";
            var spectrum = spectrumKey();
            // スペクトルは、出している物と違う時だけ (受け取った時に _shownSpectrumKey を変える。途中でやめた計算の分も次で作る)
            start(beforeKey != _beforeKey ? beforeKey : null, spectrum != _shownSpectrumKey && spectrum != "" ? SpectrumIndex : -1, spectrum);
        }
        lock (_lock)
        {
            if (_finished is { } done && done.Version == _requestVersion)
            {
                Result = done.Result;
                LastProcessMs = done.Ms;
                ProcessedCount++;
                replaceTexture(ref _afterTexture, done.Width, done.Height, done.AfterRgba);
                if (done.BeforeRgba != null) { replaceTexture(ref _beforeTexture, done.Width, done.Height, done.BeforeRgba); _beforeKey = done.BeforeKey; }
                if (done.SpectrumRgba != null) { replaceTexture(ref _spectrumTexture, SpectrumSize, SpectrumSize, done.SpectrumRgba); _shownSpectrumKey = done.SpectrumKey; }
                _finished = null;
            }
        }
    }

    private bool _previewIsFull;
    private string? _beforeKey;

    private void start(string? beforeKey, int spectrumIndex, string spectrumKey)
    {
        _cancel?.Cancel();
        var cancel = _cancel = new CancellationTokenSource();
        int version = ++_requestVersion;
        var pipeline = ImagePipeline.FromJson(Pipeline.ToJson());   // UI が変える物とは別の複製で計算する
        var source = PreviewSource;
        var display = Display;
        var normal = new NormalMapSettings
        {
            Strength = NormalView.Strength, BlurSigma = NormalView.BlurSigma, Gradient = NormalView.Gradient,
            Convention = NormalView.Convention, InvertHeight = NormalView.InvertHeight,
        };
        var scale = (float)source.Width / Original.Width;
        var previous = _job ?? Task.CompletedTask;
        // 1 つずつ順に (キャッシュを 2 つの計算で同時に触らないように)
        _job = previous.ContinueWith(_ =>
        {
            if (cancel.IsCancellationRequested) return;
            try
            {
                var sw = Stopwatch.StartNew();
                var ctx = new ProcessContext { Scale = scale, Cancellation = cancel.Token };
                var result = pipeline.Run(source, ctx, _cache);
                var after = ToDisplay(result, display, normal, ctx).ToRgba8();
                var before = beforeKey != null ? ToDisplay(source, display, normal, ctx).ToRgba8() : null;
                byte[]? spectrum = null;
                if (spectrumIndex >= 0)
                {
                    // フィルターに入る画像 = 1 つ前の処理の結果 (キャッシュにあればそれ。この計算の中なのでキャッシュを読んでよい)
                    var input = spectrumIndex == 0 ? source
                              : _cache.StageOutput(spectrumIndex - 1) ?? new ImagePipeline { Operations = pipeline.Operations.Take(spectrumIndex).ToList() }.Run(source, ctx);
                    spectrum = Fft2D.Spectrum(input, SpectrumSize, cancel.Token).ToRgba8();
                }
                lock (_lock) _finished = (version, result, after, before, beforeKey, spectrum, spectrumKey, source.Width, source.Height, sw.Elapsed.TotalMilliseconds);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Console.WriteLine($"[ImageEditor] Processing failed : {ex}");
            }
        }, TaskScheduler.Default);
    }

    /// <summary>表示の色 (Color = そのまま / Luminance = 輝度のグレー / Normal = 輝度から作った法線マップ)</summary>
    public static ImageBuffer ToDisplay(ImageBuffer image, DisplayMode mode, NormalMapSettings normal, ProcessContext ctx)
    {
        switch (mode)
        {
            case DisplayMode.Luminance:
            {
                var result = new ImageBuffer(image.Width, image.Height);
                new GrayscaleOperation().Apply(image, result, ctx);
                return result;
            }
            case DisplayMode.Normal:
            {
                var result = new ImageBuffer(image.Width, image.Height);
                NormalMap.Generate(image, result, normal, ctx);
                return result;
            }
            default:
                return image;
        }
    }

    /// <summary>今の計算が終わるまで待つ (確認用)</summary>
    public bool WaitForProcessing(int timeoutMs = 30000)
    {
        var job = _job;
        return job == null || job.Wait(timeoutMs);
    }

    // ---------------------------------------------------------------------
    // GPU のテクスチャ
    // ---------------------------------------------------------------------

    private void replaceTexture(ref uint texture, int width, int height, byte[] rgba)
    {
        var created = _engine.Graphics.CreateUITexture(width, height, rgba);
        if (created == NoTexture) { LastError = "Failed to create a preview texture"; return; }
        TexturesCreated++;
        releaseTexture(texture);
        texture = created;
    }

    private void releaseTexture(uint texture)
    {
        if (texture == NoTexture) return;
        if (_engine.Graphics.ReleaseUITexture(texture)) TexturesReleased++;
    }

    // ---------------------------------------------------------------------
    // 保存
    // ---------------------------------------------------------------------

    /// <summary>原寸で計算し直して書く (バックグラウンド)。path = null は元のファイルへ上書き</summary>
    public void Save(string? path = null)
    {
        if (IsSaving) return;
        var target = path ?? FullPath;
        var pipeline = ImagePipeline.FromJson(Pipeline.ToJson());
        var original = Original;
        var options = new ImageSaveOptions { JpegQuality = SaveOptions.JpegQuality, JpegSubsample = SaveOptions.JpegSubsample, TgaRle = SaveOptions.TgaRle, WriteAlpha = SaveOptions.WriteAlpha };
        var json = pipeline.ToJson();
        _saveMessage = $"Saving {System.IO.Path.GetFileName(target)}...";
        _saveJob = Task.Run(() =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var result = pipeline.Run(original);
                ImageCodec.Save(target, result, options);
                Console.WriteLine($"[ImageEditor] Saved {target} ({result.Width}x{result.Height}, {sw.ElapsedMilliseconds} ms)");
                return (string?)null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ImageEditor] Failed to save {target} : {ex.Message}");
                return ex.Message;
            }
        });
        _saveJob.ContinueWith(t =>
        {
            if (t.Result == null)
            {
                _savedJson = json;
                SaveCount++;
                _saveMessage = $"Saved {System.IO.Path.GetFileName(target)}";
            }
            else
            {
                LastError = t.Result;
                _saveMessage = $"Save failed : {t.Result}";
            }
        });
    }

    /// <summary>保存が終わるまで待つ (確認用)</summary>
    public bool WaitForSave(int timeoutMs = 60000)
    {
        var job = _saveJob;
        if (job == null) return true;
        if (!job.Wait(timeoutMs)) return false;
        // ContinueWith (保存した印) も終わるまで
        var until = DateTime.Now.AddMilliseconds(2000);
        while (_saveMessage != null && _saveMessage.StartsWith("Saving") && DateTime.Now < until) Thread.Sleep(10);
        return true;
    }

    private void saveAsDialog()
    {
        var format = ImageCodec.FormatFromExtension(FullPath) ?? ImageFormat.Png;
        using var dialog = new SaveFileDialog
        {
            Title = "Save image as",
            Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|TGA (*.tga)|*.tga",
            FilterIndex = format switch { ImageFormat.Png => 1, ImageFormat.Jpeg => 2, ImageFormat.Bmp => 3, _ => 4 },
            InitialDirectory = System.IO.Path.GetDirectoryName(FullPath),
            FileName = System.IO.Path.GetFileNameWithoutExtension(FullPath) + "_edit" + ImageCodec.ExtensionOf(format),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(_engine.Window) != DialogResult.OK) return;
        var path = dialog.FileName;
        if (ImageCodec.FormatFromExtension(path) == null) path += ImageCodec.ExtensionOf(dialog.FilterIndex switch { 1 => ImageFormat.Png, 2 => ImageFormat.Jpeg, 3 => ImageFormat.Bmp, _ => ImageFormat.Tga });
        Save(path);
    }

    public void Close() => IsClosed = true;

    public void Dispose()
    {
        _cancel?.Cancel();
        try { _job?.Wait(5000); } catch { }
        try { _saveJob?.Wait(30000); } catch { }
        releaseTexture(_afterTexture); _afterTexture = NoTexture;
        releaseTexture(_beforeTexture); _beforeTexture = NoTexture;
        releaseTexture(_spectrumTexture); _spectrumTexture = NoTexture;
        _cache.Reset(null);
    }

    // ---------------------------------------------------------------------
    // 窓
    // ---------------------------------------------------------------------

    private readonly ImageView _view;

    public void Draw()
    {
        HasFocus = false;
        if (IsClosed) return;
        updateProcessing();

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(new Vector2(Math.Min(1400, viewport.WorkSize.X * 0.8f), Math.Min(860, viewport.WorkSize.Y * 0.8f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize * 0.5f + new Vector2(24, 24) * ((_id - 1) % 6), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        if (_focusNext) { ImGui.SetNextWindowFocus(); _focusNext = false; }
        ImGui.SetNextWindowBgAlpha(1f);   // 後ろの窓が透けると画像の色が分かりにくい
        bool open = true;
        var title = $"Image Editor - {FileName}{(Dirty ? " *" : "")}###ImageEditor{_id}";
        var visible = ImGui.Begin(title, ref open, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!open)
        {
            if (Dirty) _askClose = true;
            else Close();
        }
        if (visible)
        {
            HasFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
            drawToolbar();
            var avail = ImGui.GetContentRegionAvail();
            const float LeftWidth = 250, RightWidth = 330;
            ImGui.BeginChild("##ops", new Vector2(LeftWidth, avail.Y), ImGuiChildFlags.Borders);
            drawOperationList();
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginChild("##view", new Vector2(Math.Max(100, avail.X - LeftWidth - RightWidth - 16), avail.Y), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            _view.Draw(this);
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginChild("##settings", new Vector2(RightWidth, avail.Y), ImGuiChildFlags.Borders);
            drawSettings();
            ImGui.EndChild();
            if (HasFocus) handleShortcuts();
        }
        drawClosePrompt();
        ImGui.End();

        // つまみを離したら (ドラッグの途中は 1 つにまとめる) 元に戻す記録に入れる
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) Commit();
    }

    private void handleShortcuts()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput || !io.KeyCtrl) return;
        if (ImGui.IsKeyPressed(ImGuiKey.Z)) { if (io.KeyShift) Redo(); else Undo(); }
        if (ImGui.IsKeyPressed(ImGuiKey.Y)) Redo();
        if (ImGui.IsKeyPressed(ImGuiKey.S)) Save();
    }

    private void drawToolbar()
    {
        if (ImGui.Button("Save")) ImGui.OpenPopup("##confirmSave");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Overwrite {FileName} (full resolution)");
        ImGui.SameLine();
        if (ImGui.Button("Save As...")) saveAsDialog();
        ImGui.SameLine();
        if (ImGui.Button("Options")) ImGui.OpenPopup("##saveOptions");
        ImGui.SameLine();
        ImGui.BeginDisabled(Pipeline.Operations.Count == 0);
        if (ImGui.Button("Reset")) Reset();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(_undo.Count == 0);
        if (ImGui.Button("Undo")) Undo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(_redo.Count == 0);
        if (ImGui.Button("Redo")) Redo();
        ImGui.EndDisabled();

        ImGui.SameLine(); ImGui.TextDisabled("|"); ImGui.SameLine();
        int compare = (int)Compare;
        ImGui.SetNextItemWidth(120);
        if (ImGui.Combo("##compare", ref compare, "After\0Before\0Side by side\0Split\0")) Compare = (CompareMode)compare;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Before = original / After = processed");
        ImGui.SameLine();
        int display = (int)Display;
        ImGui.SetNextItemWidth(110);
        if (ImGui.Combo("##display", ref display, "Color\0Luminance\0Normal\0")) Display = (DisplayMode)display;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Color : as is / Luminance : Y = R*0.30 + G*0.59 + B*0.11 / Normal : normal map from luminance");
        ImGui.SameLine();
        if (ImGui.Button("Fit")) _view.RequestFit();
        ImGui.SameLine();
        if (ImGui.Button("1:1")) _view.RequestActualSize(this);
        ImGui.SameLine();
        ImGui.BeginDisabled(Original.Width <= PreviewMaxSize && Original.Height <= PreviewMaxSize);
        ImGui.Checkbox("Full res", ref FullResolutionPreview);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip($"Preview at full resolution (slower). The preview is computed at {PreviewSource.Width}x{PreviewSource.Height}. Saving always uses the full resolution.");

        ImGui.SameLine();
        var status = IsSaving ? _saveMessage : IsProcessing ? "Processing..." : $"{LastProcessMs:F0} ms";
        ImGui.TextDisabled($"{Info.Description}  {Info.Width}x{Info.Height}  {status}");
        if (!IsSaving && _saveMessage != null) { ImGui.SameLine(); ImGui.TextColored(_saveMessage.StartsWith("Save failed") ? new Vector4(1, 0.4f, 0.4f, 1) : new Vector4(0.5f, 1, 0.5f, 1), _saveMessage); }

        if (ImGui.BeginPopup("##confirmSave"))
        {
            ImGui.Text($"Overwrite {FileName} with the processed image?");
            if (ImageCodec.FormatFromExtension(FullPath) == ImageFormat.Jpeg) ImGui.TextDisabled("JPEG : alpha is dropped and the image is re-compressed.");
            if (ImGui.Button("Overwrite")) { Save(); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopup("##saveOptions"))
        {
            int quality = SaveOptions.JpegQuality;
            if (ImGui.SliderInt("JPEG quality", ref quality, 1, 100)) SaveOptions.JpegQuality = quality;
            bool subsample = SaveOptions.JpegSubsample;
            if (ImGui.Checkbox("JPEG 4:2:0 (smaller)", ref subsample)) SaveOptions.JpegSubsample = subsample;
            bool rle = SaveOptions.TgaRle;
            if (ImGui.Checkbox("TGA RLE", ref rle)) SaveOptions.TgaRle = rle;
            int alpha = SaveOptions.WriteAlpha switch { null => 0, true => 1, false => 2 };
            if (ImGui.Combo("Alpha", ref alpha, "Auto (if not opaque)\0Always\0Never\0")) SaveOptions.WriteAlpha = alpha switch { 0 => null, 1 => true, _ => false };
            ImGui.EndPopup();
        }
    }

    private void drawClosePrompt()
    {
        var id = $"Unsaved changes##close{_id}";
        if (_askClose) { ImGui.OpenPopup(id); _askClose = false; }
        bool keep = true;
        if (ImGui.BeginPopupModal(id, ref keep, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"{FileName} has unsaved changes.");
            if (ImGui.Button("Save and close")) { Save(); WaitForSave(); Close(); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button("Discard")) { Close(); ImGui.CloseCurrentPopup(); }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
}
