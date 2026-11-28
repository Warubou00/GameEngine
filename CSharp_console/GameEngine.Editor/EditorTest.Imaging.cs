using GameEngine.Imaging;
using GameEngine.Imaging.Codecs;
using GameEngine.Imaging.Processing;

namespace GameEngine.Editor;

// ==========================================================================
// v2.3 の確認 (--editor-test-section imaging) : 画像エディター
//   1. 読み書き : 4 つの形式を自前で書いて読む / System.Drawing (確認でだけ使う) で読んで同じ / 壊れたファイルは例外
//   2. 窓 : プロジェクトの画像を開く → 全部の処理を足す → 計算が終わり GPU のテクスチャになる → Undo / Redo / Reset
//   3. 表示 : Luminance / Normal、周波数フィルターのスペクトル、大きい画像は縮めてプレビュー (保存は原寸)
//   4. 保存 : Save As (原寸で計算し直した物と同じ) / 上書き / 閉じるとテクスチャを全部返す
//   imaging-view : 撮影用 (処理を足した窓を 30 秒出したまま)
// ==========================================================================
internal sealed partial class EditorTest
{
    private const string ImagingFolder = "Assets/_ImageEditorTest";

    private static ImageBuffer testImage(int width, int height, bool alpha)
    {
        var image = new ImageBuffer(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float r = (float)x / (width - 1), g = (float)y / (height - 1), b = 0.5f + 0.5f * MathF.Sin(x * 0.15f) * MathF.Cos(y * 0.11f);
                float a = alpha ? Math.Clamp(1.2f - MathF.Sqrt((x - width / 2f) * (x - width / 2f) + (y - height / 2f) * (y - height / 2f)) / (width * 0.5f), 0f, 1f) : 1f;
                if ((x / 16 + y / 16) % 2 == 0) { r *= 0.7f; g *= 0.7f; }   // 輪郭が出るように市松模様
                image.SetPixel(x, y, r, g, b, a);
            }
        return image;
    }

    private static float maxDifference(ImageBuffer a, ImageBuffer b, bool alpha = true)
    {
        if (a.Width != b.Width || a.Height != b.Height) return float.MaxValue;
        float max = 0;
        for (int i = 0; i < a.Pixels.Length; i++) if (alpha || i % 4 != 3) max = Math.Max(max, Math.Abs(a.Pixels[i] - b.Pixels[i]));
        return max;
    }

    private static ImageBuffer gdiLoad(string path)
    {
        using var bitmap = new System.Drawing.Bitmap(path);
        var image = new ImageBuffer(bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var row = new byte[bitmap.Width * 4];
        for (int y = 0; y < bitmap.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
            for (int x = 0; x < bitmap.Width; x++) image.SetPixel(x, y, row[x * 4 + 2] / 255f, row[x * 4 + 1] / 255f, row[x * 4] / 255f, row[x * 4 + 3] / 255f);
        }
        bitmap.UnlockBits(data);
        return image;
    }

    private IEnumerable<int> waitFor(Func<bool> done, int frames = 600)
    {
        for (int i = 0; i < frames && !done(); i++) yield return 0;
    }

    private IEnumerable<int> imageEditor()
    {
        var database = _app.Database;
        var folder = database.FullPath(ImagingFolder);
        if (Directory.Exists(folder)) Directory.Delete(folder, true);   // 前に止めた確認の残り
        Directory.CreateDirectory(folder);

        // ---------- 1. 読み書き ----------
        var source = testImage(160, 96, alpha: true);
        foreach (var format in new[] { ImageFormat.Png, ImageFormat.Bmp, ImageFormat.Tga })
        {
            var path = Path.Combine(folder, "codec" + ImageCodec.ExtensionOf(format));
            ImageCodec.Save(path, source);
            var back = ImageCodec.Load(path);
            var expected = ImageBuffer.FromRgba8(source.Width, source.Height, source.ToRgba8());
            check(maxDifference(back.Image, expected) < 1e-6f && back.Info.HasAlpha, $"[imaging] {format} write / read ({back.Info.Description})");
            if (format != ImageFormat.Tga) check(maxDifference(gdiLoad(path), expected) < 1e-6f, $"[imaging] {format} written file is read by System.Drawing");
        }
        {
            var path = Path.Combine(folder, "codec.jpg");
            ImageCodec.Save(path, source, new ImageSaveOptions { JpegQuality = 95 });
            var mine = ImageCodec.Load(path).Image;
            check(maxDifference(mine, gdiLoad(path), alpha: false) < 0.05f && maxDifference(mine, source, alpha: false) < 0.25f, "[imaging] JPEG write / read (matches System.Drawing)");
        }
        {
            var bytes = ImageCodec.Encode(source, ImageFormat.Png);
            bytes[bytes.Length / 2] ^= 0x5A;
            bool threw = false;
            try { ImageCodec.Decode(bytes); } catch (ImageFormatException) { threw = true; }
            check(threw, "[imaging] broken PNG -> ImageFormatException");
        }
        check(ImageEditorManager.CanOpen("a.PNG") && ImageEditorManager.CanOpen("a.jpeg") && ImageEditorManager.CanOpen("a.tga") && !ImageEditorManager.CanOpen("a.dds"), "[imaging] openable extensions (.dds is not)");

        // ---------- 2. 窓 ----------
        var assetPath = ImagingFolder + "/editme.png";
        ImageCodec.Save(database.FullPath(assetPath), testImage(256, 160, alpha: false));
        database.Refresh();
        yield return 0;
        check(database.Find(assetPath)?.Kind == AssetKind.Texture, "[imaging] test image is a texture asset");
        var window = _app.OpenImage(assetPath);
        check(window != null && _app.ImageEditors.Windows.Count == 1, "[imaging] OpenImage opens a window");
        if (window == null) yield break;
        check(_app.OpenImage(database.FullPath(assetPath)) == window && _app.ImageEditors.Windows.Count == 1, "[imaging] opening the same image again focuses the same window");
        foreach (var s in waitFor(() => window.AfterTexture != ImageEditorWindow.NoTexture && window.BeforeTexture != ImageEditorWindow.NoTexture)) yield return s;
        check(window.AfterTexture != ImageEditorWindow.NoTexture && window.BeforeTexture != ImageEditorWindow.NoTexture && window.Result != null, "[imaging] preview textures are created");
        check(maxDifference(window.Result!, window.Original) == 0f, "[imaging] no operation = original");

        foreach (var factory in ImageOperation.Factories)
        {
            var op = window.Add(factory());
            int before = window.ProcessedCount;
            foreach (var s in waitFor(() => window.ProcessedCount > before && !window.IsProcessing)) yield return s;
            yield return 0;
            check(window.ProcessedCount > before && window.Result != null && window.LastError == null, $"[imaging] {op.DisplayName} processed ({window.LastProcessMs:F1} ms)");
        }
        int count = window.Pipeline.Operations.Count;
        check(count == ImageOperation.Factories.Count && window.UndoCount == count, $"[imaging] all operations in the stack ({count}, undo {window.UndoCount})");
        var full = window.Pipeline.ToJson();
        window.Undo();
        check(window.Pipeline.Operations.Count == count - 1 && window.RedoCount == 1, "[imaging] undo removes the last operation");
        window.Redo();
        check(window.Pipeline.ToJson() == full, "[imaging] redo restores it");
        window.Reset();
        check(window.Pipeline.Operations.Count == 0, "[imaging] reset clears the stack");
        foreach (var s in waitFor(() => !window.IsProcessing && window.Result != null && maxDifference(window.Result, window.PreviewSource) == 0f)) yield return s;
        check(maxDifference(window.Result!, window.PreviewSource) == 0f, "[imaging] reset shows the original again");
        window.Undo();
        check(window.Pipeline.ToJson() == full, "[imaging] reset can be undone");

        // つまみの途中の変更 (Commit 前) も Undo に入る
        window.Reset();
        var gamma = (GammaOperation)window.Add(new GammaOperation { Gamma = 1.5f });
        gamma.Gamma = 2.5f;   // UI のつまみと同じ (まだ Commit していない)
        int undoBefore = window.UndoCount;
        window.Undo();
        check(window.Pipeline.Operations.Count == 1 && ((GammaOperation)window.Pipeline.Operations[0]).Gamma == 1.5f && window.UndoCount == undoBefore, "[imaging] undo while dragging goes back to the value before the drag");

        // ---------- 3. 表示 ----------
        window.Reset();
        window.Add(new SobelOperation());
        foreach (var s in waitFor(() => !window.IsProcessing && window.ProcessedCount > 0)) yield return s;
        var afterColor = window.AfterTexture;
        window.Display = ImageEditorWindow.DisplayMode.Luminance;
        int processed = window.ProcessedCount;
        foreach (var s in waitFor(() => window.ProcessedCount > processed && !window.IsProcessing)) yield return s;
        check(window.ProcessedCount > processed && window.AfterTexture != afterColor, "[imaging] luminance display re-creates the textures");
        window.Display = ImageEditorWindow.DisplayMode.Normal;
        processed = window.ProcessedCount;
        foreach (var s in waitFor(() => window.ProcessedCount > processed && !window.IsProcessing)) yield return s;
        var flat = ImageEditorWindow.ToDisplay(new ImageBuffer(8, 8), ImageEditorWindow.DisplayMode.Normal, window.NormalView, ProcessContext.Default).GetPixel(3, 3);
        check(window.ProcessedCount > processed && Math.Abs(flat.R - 0.5f) < 1e-5f && Math.Abs(flat.B - 1f) < 1e-5f, "[imaging] normal display (flat = (0.5, 0.5, 1))");
        window.Display = ImageEditorWindow.DisplayMode.Color;

        window.Add(new FrequencyFilterOperation { Radius = 0.1f });
        check(window.SpectrumIndex == window.SelectedIndex, "[imaging] frequency filter selected");
        foreach (var s in waitFor(() => window.SpectrumTexture != ImageEditorWindow.NoTexture && !window.IsProcessing)) yield return s;
        check(window.SpectrumTexture != ImageEditorWindow.NoTexture, "[imaging] spectrum texture for the frequency filter");

        // ---------- 4. 保存 ----------
        window.Reset();
        window.Add(new GaussianBlurOperation { Sigma = 2f });
        window.Add(new ToneCurveOperation { Rgb = [new(0, 0), new(0.5f, 0.7f), new(1, 1)] });
        window.Add(new MosaicOperation { BlockSize = 8, Amount = 0.5f });
        var outPath = database.FullPath(ImagingFolder + "/editme_out.png");
        window.Save(outPath);
        check(window.WaitForSave() && File.Exists(outPath), "[imaging] Save As writes the file");
        yield return 0;
        var expectedSave = ImageBuffer.FromRgba8(window.Original.Width, window.Original.Height, window.Pipeline.Run(window.Original).ToRgba8());
        check(maxDifference(ImageCodec.Load(outPath).Image, expectedSave) < 1e-6f && !window.Dirty, "[imaging] saved image = pipeline at full resolution, not dirty after save");
        check(!File.Exists(Path.Combine(folder, ".editme_out.png.tmp")), "[imaging] no temporary file is left");

        // 上書き (TGA)
        var tgaAsset = ImagingFolder + "/overwrite.tga";
        ImageCodec.Save(database.FullPath(tgaAsset), testImage(64, 64, alpha: true));
        database.Refresh();
        var tgaWindow = _app.OpenImage(tgaAsset);
        check(tgaWindow != null && tgaWindow.Info.Format == ImageFormat.Tga && tgaWindow.Info.HasAlpha, "[imaging] open TGA with alpha");
        if (tgaWindow != null)
        {
            tgaWindow.Add(new InvertOperation());
            check(tgaWindow.Dirty, "[imaging] dirty after a change");
            tgaWindow.Save();
            check(tgaWindow.WaitForSave(), "[imaging] overwrite finished");
            yield return 0;
            var reread = ImageCodec.Load(database.FullPath(tgaAsset)).Image;
            var original = tgaWindow.Original.GetPixel(10, 20);
            var saved = reread.GetPixel(10, 20);
            check(Math.Abs(saved.R - (1 - original.R)) < 0.003f && Math.Abs(saved.A - original.A) < 0.003f, "[imaging] overwritten TGA is inverted, alpha kept");
        }

        // 大きい画像 : プレビューは縮める、保存は原寸
        var bigAsset = ImagingFolder + "/big.bmp";
        ImageCodec.Save(database.FullPath(bigAsset), testImage(3000, 1200, alpha: false));
        database.Refresh();
        var bigWindow = _app.OpenImage(bigAsset);
        if (bigWindow != null)
        {
            check(bigWindow.PreviewSource.Width == ImageEditorWindow.PreviewMaxSize && Math.Abs(bigWindow.PreviewScale - 2048f / 3000f) < 1e-3f, $"[imaging] large image preview is downscaled ({bigWindow.PreviewSource.Width}x{bigWindow.PreviewSource.Height})");
            bigWindow.Add(new MosaicOperation { BlockSize = 30 });
            foreach (var s in waitFor(() => bigWindow.ProcessedCount > 0 && !bigWindow.IsProcessing)) yield return s;
            var bigOut = database.FullPath(ImagingFolder + "/big_out.jpg");
            bigWindow.Save(bigOut);
            check(bigWindow.WaitForSave() && ImageCodec.Load(bigOut).Info.Width == 3000, "[imaging] large image is saved at full resolution");
        }

        // テクスチャを漏らさない : 開いている窓は After / Before / (スペクトル) だけ持つ。閉じると全部返す
        for (int i = 0; i < 6; i++)
        {
            ((GaussianBlurOperation)window.Pipeline.Operations[0]).Sigma = 1f + i;
            int p = window.ProcessedCount;
            foreach (var s in waitFor(() => window.ProcessedCount > p && !window.IsProcessing)) yield return s;
        }
        int live = window.TexturesCreated - window.TexturesReleased;
        check(live <= 3, $"[imaging] old preview textures are released ({window.TexturesCreated} created, {window.TexturesReleased} released)");
        var windows = _app.ImageEditors.Windows.ToList();
        _app.ImageEditors.CloseAll();
        check(_app.ImageEditors.Windows.Count == 0 && windows.All(w => w.TexturesCreated == w.TexturesReleased), "[imaging] closing releases every texture");
        for (int i = 0; i < 5; i++) yield return 0;   // 返したテクスチャが GPU の後で消える (落ちない)
        check(true, "[imaging] frames after releasing textures");

        Directory.Delete(folder, true);
        if (File.Exists(folder + ".meta")) File.Delete(folder + ".meta");
        database.Refresh();
        yield return 0;
    }

    // 撮影用 (--editor-test-section imaging-view) : 処理を足した窓を 30 秒出したまま
    private IEnumerable<int> imageEditorView()
    {
        var database = _app.Database;
        var folder = database.FullPath(ImagingFolder);
        Directory.CreateDirectory(folder);
        var assetPath = ImagingFolder + "/view.png";
        var path = Environment.GetEnvironmentVariable("IMAGING_VIEW_SOURCE");
        if (path != null && File.Exists(path)) ImageCodec.Save(database.FullPath(assetPath), ImageCodec.Load(path).Image);
        else ImageCodec.Save(database.FullPath(assetPath), testImage(512, 320, alpha: false));
        database.Refresh();
        yield return 0;
        var window = _app.OpenImage(assetPath);
        if (window == null) yield break;
        var mode = Environment.GetEnvironmentVariable("IMAGING_VIEW_MODE") ?? "curve";
        switch (mode)
        {
            case "frequency": window.Add(new FrequencyFilterOperation { Radius = 0.08f, Feather = 0.03f }); break;
            case "normal": window.Add(new GaussianBlurOperation { Sigma = 1 }); window.Display = ImageEditorWindow.DisplayMode.Normal; break;
            case "luminance": window.Add(new SepiaOperation()); window.Display = ImageEditorWindow.DisplayMode.Luminance; break;
            default: window.Add(new ToneCurveOperation { Rgb = [new(0, 0), new(0.3f, 0.15f), new(0.7f, 0.85f), new(1, 1)] }); break;
        }
        Console.WriteLine($"[Test] imaging-view : {mode}");
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 30) yield return 0;
        _app.ImageEditors.CloseAll();
        Directory.Delete(folder, true);
        if (File.Exists(folder + ".meta")) File.Delete(folder + ".meta");
        database.Refresh();
    }
}
