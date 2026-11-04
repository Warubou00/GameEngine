using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// Project (アセットブラウザ。ED5)
//   左 : フォルダの木 / 右 : 今のフォルダの中身 (タイル)。検索すると全部のフォルダから探す
//   - テクスチャは縮小した画像 (サムネイル) を出す。読むのは別のスレッド、GPU へ送るのは 1 フレームに 4 枚まで
//   - ドラッグ : モデルはシーンビュー / Hierarchy へ落とすと置ける。インスペクターのアセットの欄へも落とせる。
//     シーンをシーンビューへ落とすと開く
//   - ダブルクリック : フォルダは入る / シーンは開く
//   - Assets のファイルが増えた / 消えた / 名前が変わったら (FileSystemWatcher)、一覧を作り直す (新しいファイルには .meta が付く)
// ==========================================================================
internal sealed class ProjectPanel : IDisposable
{
    // ドラッグ中のアセット (ImGui の荷物は ID だけ渡し、中身はここに置く)
    public static AssetEntry? Dragged { get; private set; }
    public const string Payload = "ASSET";

    private readonly Engine _engine;
    private readonly AssetDatabase _database;
    private readonly Action<string> _openScene;
    private readonly FileSystemWatcher? _watcher;
    private volatile bool _changed;
    private double _changedAt;

    private string  _folder = AssetDatabase.AssetsFolder;
    private string  _search = "";
    private AssetEntry? _selected;
    private float   _tileSize = 84.0f;

    // サムネイル
    private readonly Dictionary<Guid, uint> _thumbnails = new();
    private readonly HashSet<Guid> _requested = new();
    private readonly ConcurrentQueue<(Guid Guid, int Width, int Height, byte[]? Pixels)> _decoded = new();
    private const int ThumbnailSize = 96;

    public ProjectPanel(Engine engine, AssetDatabase database, Action<string> openScene)
    {
        _engine    = engine;
        _database  = database;
        _openScene = openScene;
        try
        {
            _watcher = new FileSystemWatcher(database.AssetsRoot) { IncludeSubdirectories = true, EnableRaisingEvents = true };
            _watcher.Created += (_, e) => onChanged(e.FullPath);
            _watcher.Deleted += (_, e) => onChanged(e.FullPath);
            _watcher.Renamed += (_, e) => onChanged(e.FullPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Assets] File watching is not available : {ex.Message}");
        }
    }

    // 別のスレッドから呼ばれる。.meta と DDS キャッシュ (自分たちが作る物) は無視
    private void onChanged(string path)
    {
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) return;
        _changed = true;
    }

    public void Dispose() => _watcher?.Dispose();

    public unsafe void Draw(ref bool open)
    {
        // ファイルの変化 : 書き込み中のファイルを読まないよう、最後の変化から 0.3 秒待ってから作り直す
        if (_changed) { _changed = false; _changedAt = Time.TotalTime; }
        if (_changedAt > 0 && Time.TotalTime - _changedAt > 0.3)
        {
            _changedAt = 0;
            var created = _database.Refresh();
            Console.WriteLine($"[Assets] Refreshed : {_database.Entries.Count()} assets{(created > 0 ? $" ({created} new)" : "")}");
        }
        uploadThumbnails();
        if (Dragged != null && !ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.GetDragDropPayload().NativePtr == null) Dragged = null;

        if (ImGui.Begin("Project", ref open))
        {
            drawToolbar();
            ImGui.Separator();
            var height = ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeightWithSpacing();
            if (ImGui.BeginTable("project", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV, new Vector2(0, height)))
            {
                ImGui.TableSetupColumn("folders", ImGuiTableColumnFlags.WidthFixed, 180.0f);
                ImGui.TableSetupColumn("items", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableNextColumn();
                ImGui.BeginChild("folders");
                drawFolder(AssetDatabase.AssetsFolder);
                ImGui.EndChild();
                ImGui.TableNextColumn();
                ImGui.BeginChild("items");
                drawItems();
                ImGui.EndChild();
                ImGui.EndTable();
            }
            // 選んだアセットの情報
            if (_selected != null) ImGui.TextDisabled($"{_selected.Path}   ({_selected.Kind}, id {_selected.Guid})");
            else ImGui.TextDisabled($"{_database.Entries.Count()} assets");
        }
        ImGui.End();
    }

    private unsafe void drawToolbar()
    {
        if (ImGui.Button("Import...")) import();
        ImGui.SameLine();
        if (ImGui.Button("New Folder")) newFolder();
        ImGui.SameLine();
        if (ImGui.Button("Show in Explorer")) Process.Start("explorer.exe", $"\"{_database.FullPath(_selected?.Path ?? _folder)}\"");
        ImGui.SameLine();
        if (ImGui.Button("Refresh")) _changedAt = 1e-9;   // 次のフレームで作り直す
        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##search", "Search", ref _search, 128);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        ImGui.SliderFloat("##size", ref _tileSize, 56.0f, 140.0f, "size %.0f");
        // 今のフォルダ (クリックでそこへ)
        var parts = _folder.Split('/');
        for (int i = 0; i < parts.Length; i++)
        {
            ImGui.SameLine(0, i == 0 ? 16 : 2);
            if (i > 0) { ImGui.TextDisabled(">"); ImGui.SameLine(0, 2); }
            if (ImGui.SmallButton($"{parts[i]}##crumb{i}")) _folder = string.Join('/', parts.Take(i + 1));
        }
    }

    private void drawFolder(string path)
    {
        var children = _database.Entries.Where(e => e.Kind == AssetKind.Folder && parentOf(e.Path) == path).OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;
        if (path == AssetDatabase.AssetsFolder) flags |= ImGuiTreeNodeFlags.DefaultOpen;
        if (path == _folder) flags |= ImGuiTreeNodeFlags.Selected;
        var open = ImGui.TreeNodeEx($"{Path.GetFileName(path)}##{path}", flags);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) { _folder = path; _search = ""; }
        if (open)
        {
            foreach (var child in children) drawFolder(child.Path);
            ImGui.TreePop();
        }
    }

    private static string parentOf(string path)
    {
        var index = path.LastIndexOf('/');
        return index < 0 ? "" : path[..index];
    }

    private void drawItems()
    {
        IEnumerable<AssetEntry> items = _search.Length > 0
            ? _database.Entries.Where(e => e.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))
            : _database.Entries.Where(e => parentOf(e.Path) == _folder);
        var list = items.OrderBy(e => e.Kind != AssetKind.Folder).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) { ImGui.TextDisabled(_search.Length > 0 ? "No match" : "(empty folder : drop files into Assets, or Import...)"); return; }

        var columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + 8) / (_tileSize + 8)));
        for (int i = 0; i < list.Count; i++)
        {
            if (i % columns != 0) ImGui.SameLine(0, 8);
            drawTile(list[i]);
        }
    }

    private unsafe void drawTile(AssetEntry entry)
    {
        ImGui.PushID(entry.Guid.ToString());
        ImGui.BeginGroup();
        var start = ImGui.GetCursorScreenPos();
        var size = new Vector2(_tileSize, _tileSize);
        var clicked = ImGui.InvisibleButton("tile", size + new Vector2(0, ImGui.GetTextLineHeight()));
        var hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();

        // 背景 / 選んだ印
        var background = entry == _selected ? ImGui.GetColorU32(ImGuiCol.HeaderActive) : hovered ? ImGui.GetColorU32(ImGuiCol.HeaderHovered) : 0x20FFFFFFu;
        draw.AddRectFilled(start, start + size + new Vector2(0, ImGui.GetTextLineHeight()), background, 4.0f);
        if (entry.Kind == AssetKind.Texture && thumbnail(entry) is { } texture)
        {
            draw.AddImage((IntPtr)texture, start + new Vector2(6), start + size - new Vector2(6));
        }
        else
        {
            var (label, color) = iconOf(entry);
            draw.AddRectFilled(start + new Vector2(10), start + size - new Vector2(10), color, 6.0f);
            var textSize = ImGui.CalcTextSize(label);
            draw.AddText(start + (size - textSize) * 0.5f, 0xFFFFFFFF, label);
        }
        // 名前 (長ければ切る)
        var name = entry.Name;
        while (name.Length > 3 && ImGui.CalcTextSize(name).X > _tileSize) name = name[..^2];
        if (name != entry.Name) name = name[..^2] + "...";   // 「…」はフォントの字の範囲に無い
        draw.AddText(start + new Vector2((_tileSize - ImGui.CalcTextSize(name).X) * 0.5f, _tileSize), ImGui.GetColorU32(ImGuiCol.Text), name);

        if (clicked) _selected = entry;
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (entry.Kind == AssetKind.Folder) { _folder = entry.Path; _search = ""; }
            else if (entry.Kind == AssetKind.Scene) _openScene(_database.FullPath(entry.Path));
        }
        if (hovered) ImGui.SetTooltip(entry.Path);

        // ドラッグ (フォルダ以外)
        if (entry.Kind != AssetKind.Folder && ImGui.BeginDragDropSource())
        {
            Dragged = entry;
            int dummy = 0;
            ImGui.SetDragDropPayload(Payload, (IntPtr)(&dummy), sizeof(int));
            ImGui.Text($"{entry.Name} ({entry.Kind})");
            ImGui.EndDragDropSource();
        }
        ImGui.EndGroup();
        ImGui.PopID();
    }

    private static (string Label, uint Color) iconOf(AssetEntry entry) => entry.Kind switch
    {
        AssetKind.Folder   => ("Folder", 0xFF3C8CC8u),
        AssetKind.Model    => (Path.GetExtension(entry.Path).TrimStart('.').ToUpperInvariant(), 0xFF8C5A3Cu),
        AssetKind.Texture  => ("IMG", 0xFF5A5A5Au),
        AssetKind.Sound    => ("WAV", 0xFF3CA05Au),
        AssetKind.Scene    => ("Scene", 0xFF8C3C8Cu),
        AssetKind.Material => ("Mat", 0xFF3CA0A0u),
        _                  => (Path.GetExtension(entry.Path).TrimStart('.').ToUpperInvariant(), 0xFF404040u),
    };

    // ドラッグで落とせる所が呼ぶ : 落とされたアセット (kind が合わなければ null)
    public static unsafe AssetEntry? AcceptDrop(params AssetKind[] kinds)
    {
        if (Dragged == null || !kinds.Contains(Dragged.Kind)) return null;
        var payload = ImGui.AcceptDragDropPayload(Payload);
        return payload.NativePtr != null ? Dragged : null;
    }

    // ---------------------------------------------------------------------
    // サムネイル : 別のスレッドで画像を読んで縮め、メインのスレッドで GPU のテクスチャにする
    // ---------------------------------------------------------------------

    private uint? thumbnail(AssetEntry entry)
    {
        if (_thumbnails.TryGetValue(entry.Guid, out var id)) return id == uint.MaxValue ? null : id;
        if (_requested.Add(entry.Guid))
        {
            var file = _database.FullPath(entry.Path);
            var guid = entry.Guid;
            Task.Run(() => _decoded.Enqueue(decode(guid, file)));
        }
        return null;
    }

    private static (Guid, int, int, byte[]?) decode(Guid guid, string file)
    {
        try
        {
            using var source = new Bitmap(file);
            var scale = Math.Min((float)ThumbnailSize / source.Width, (float)ThumbnailSize / source.Height);
            int width = Math.Max(1, (int)(source.Width * scale)), height = Math.Max(1, (int)(source.Height * scale));
            using var small = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(small))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, width, height);
            }
            var data = small.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++) System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * width * 4, width * 4);
            small.UnlockBits(data);
            for (int i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);   // BGRA → RGBA
            return (guid, width, height, pixels);
        }
        catch (Exception)
        {
            return (guid, 0, 0, null);   // 読めない形式 (DDS / TGA など) はアイコンで出す
        }
    }

    private void uploadThumbnails()
    {
        for (int i = 0; i < 4 && _decoded.TryDequeue(out var item); i++)
        {
            _thumbnails[item.Guid] = item.Pixels == null ? uint.MaxValue : _engine.Graphics.CreateUITexture(item.Width, item.Height, item.Pixels);
        }
    }

    // ---------------------------------------------------------------------
    // 持ってくる / フォルダを作る
    // ---------------------------------------------------------------------

    private void import()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Import assets", Multiselect = true,
            Filter = "Assets (*.obj;*.pmx;*.png;*.jpg;*.jpeg;*.wav)|*.obj;*.pmx;*.png;*.jpg;*.jpeg;*.wav|All files|*.*",
        };
        if (dialog.ShowDialog(_engine.Window) != DialogResult.OK) return;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var path = _database.Import(file, _folder);
                Console.WriteLine($"[Assets] Imported {file} -> {path}");
                _selected = _database.Find(path);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Assets] Failed to import {file} : {ex.Message}");
            }
        }
    }

    private void newFolder()
    {
        var name = "New Folder";
        for (int i = 1; Directory.Exists(_database.FullPath($"{_folder}/{name}")); i++) name = $"New Folder {i}";
        Directory.CreateDirectory(_database.FullPath($"{_folder}/{name}"));
        _database.Refresh();
    }
}
