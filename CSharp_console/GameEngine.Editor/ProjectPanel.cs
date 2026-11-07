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
//   - ファイルが書き換わったら読み直す (ED5 後半) : モデル → 同じ番号のまま読み直す / テクスチャ → 使っているモデルとマテリアル /
//     マテリアル → 値を読み直す。自分 (エディター) が書いたファイルは読み直さない
//   - Create Material : 今のフォルダにマテリアルを作る。マテリアルを選ぶとインスペクターで値を変えられる
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
    private readonly ConcurrentDictionary<string, byte> _modified = new(StringComparer.OrdinalIgnoreCase);   // 書き換わったファイル (別スレッドから)

    // 選んだアセット (インスペクターがマテリアルを出すのに使う)。Version は選び直した回数
    public AssetEntry? Selected => _selected;
    // プレハブのファイルが外で変わった時 (エディターが置いた物を作り直す。戻り値 = 置いた物の数。ED8)
    public Func<Prefab, int>? PrefabChanged;
    // シェーダーグラフをダブルクリックした時 (グラフの窓で開く。SH4)
    public Action<string>? OpenShaderGraph;
    public Action<string>? OpenPreview;   // AN5 : FBX をダブルクリック
    public int SelectionVersion { get; private set; }

    private string  _folder = AssetDatabase.AssetsFolder;
    private string  _search = "";
    private AssetEntry? _selected;
    // 名前を書いているアセット (ED10。F2 / 右クリック > Rename)
    private string? _renaming;
    private string  _renameText = "";
    private bool    _renameFocus;
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
            _watcher.Changed += (_, e) => onModified(e.FullPath);
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
        if (File.Exists(path)) _modified[path] = 0;   // 新しいファイルも取り込みの対象 (ED7 : AssetImporter)
        _changed = true;
    }

    // 別のスレッドから呼ばれる : 書き換わったファイルを覚えて、0.3 秒後にメインのスレッドで読み直す
    private void onModified(string path)
    {
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || Directory.Exists(path)) return;
        _modified[path] = 0;
        _changed = true;
    }

    public void Dispose() => _watcher?.Dispose();

    // 書き換わったファイルを読み直す (メインのスレッド)
    private void reloadModified()
    {
        foreach (var file in _modified.Keys.ToList())
        {
            _modified.TryRemove(file, out _);
            if (_engine.Assets.IsOwnWrite(file)) continue;   // エディターが書いた (マテリアルの保存など)
            var path = _database.ToAssetPath(file);
            if (path == null || _database.Find(path) is not { } entry) continue;
            try
            {
                var reloaded = entry.Kind switch
                {
                    AssetKind.Model    => _engine.Assets.ReloadModel(path) ? 1 : 0,
                    AssetKind.Texture  => _engine.Assets.ReloadTexture(path),
                    AssetKind.Material => _engine.Assets.ReloadMaterial(path) ? 1 : 0,
                    AssetKind.Shader   => _engine.Assets.ReloadShader(path) ? 1 : 0,   // SH2 : コンパイルし直す (失敗したら前の物のまま。理由は Console)
                    AssetKind.ShaderGraph => _engine.Assets.ReloadShader(path) ? 1 : 0,   // SH4 : 外で書き換えた時 (グラフの窓の保存は同じ中身なので何もしない)
                    AssetKind.FbxModel or AssetKind.FbxSkeleton or AssetKind.FbxAnimation => _engine.Assets.ReloadFbx(path) ? 1 : 0,   // AN1
                    AssetKind.Prefab   => _engine.Assets.ReloadPrefab(path) is { } prefab ? PrefabChanged?.Invoke(prefab) ?? 1 : 0,   // ED8 : 置いた物を作り直す
                    _ => 0,
                };
                if (entry.Kind == AssetKind.Texture) { _thumbnails.Remove(entry.Guid); _requested.Remove(entry.Guid); }   // サムネイルも作り直す
                // 拡張の取り込み (ED7) : その拡張子の AssetImporter があれば呼ぶ
                if (EditorExtensions.ImporterFor(path) is { } importer)
                {
                    importer.OnImport(path, file);
                    Console.WriteLine($"[Assets] Imported {path} ({importer.GetType().Name})");
                }
                if (reloaded > 0) Console.WriteLine($"[Assets] Reloaded {path}{(entry.Kind == AssetKind.Texture ? $" ({reloaded} users)" : "")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Assets] Failed to reload {path} : {ex.Message}");
            }
        }
    }

    public unsafe void Draw(ref bool open)
    {
        // ファイルの変化 : 書き込み中のファイルを読まないよう、最後の変化から 0.3 秒待ってから作り直す
        if (_changed) { _changed = false; _changedAt = Time.TotalTime; }
        if (_changedAt > 0 && Time.TotalTime - _changedAt > 0.3)
        {
            _changedAt = 0;
            var before = _database.Version;
            var created = _database.Refresh();
            if (_database.Version != before) Console.WriteLine($"[Assets] Refreshed : {_database.Entries.Count()} assets{(created > 0 ? $" ({created} new)" : "")}");
            reloadModified();
        }
        uploadThumbnails();
        if (Dragged != null && !ImGui.IsMouseDown(ImGuiMouseButton.Left) && ImGui.GetDragDropPayload().NativePtr == null) Dragged = null;

        if (ImGui.Begin("Project", ref open))
        {
            // F2 : 選んでいるアセットの名前を書く (ED10。Project にフォーカスがある時)
            if (_renaming == null && _selected != null && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
                !ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F2, false))
                BeginRename(_selected);
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
        if (ImGui.Button("Create Material")) CreateMaterial();
        ImGui.SameLine();
        if (ImGui.Button("Create Shader")) CreateShader();   // SH2
        ImGui.SameLine();
        if (ImGui.Button("Create Shader Graph") && CreateShaderGraph() is { } graphPath) OpenShaderGraph?.Invoke(graphPath);   // SH4
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
        var renaming = _renaming == path;
        var open = ImGui.TreeNodeEx(renaming ? $"##{path}" : $"{Path.GetFileName(path)}##{path}", flags);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) { _folder = path; _search = ""; Select(_database.Find(path)); }
        if (path != AssetDatabase.AssetsFolder && ImGui.BeginPopupContextItem($"folder{path}"))
        {
            if (ImGui.MenuItem("Rename", "F2") && _database.Find(path) is { } folder) BeginRename(folder);
            if (ImGui.MenuItem("Show in Explorer")) Process.Start("explorer.exe", $"\"{_database.FullPath(path)}\"");
            ImGui.EndPopup();
        }
        if (renaming)
        {
            ImGui.SameLine();
            drawRenameField(ImGui.GetContentRegionAvail().X);
        }
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
            // FBX (AN1) : 読めない / 中身が名前と合わない物は赤い枠と「!」
            if (fbxError(entry) is { } error)
            {
                draw.AddRect(start + new Vector2(8), start + size - new Vector2(8), 0xFF3030FFu, 6.0f, ImDrawFlags.None, 3.0f);
                draw.AddText(start + new Vector2(14, 12), 0xFF4040FFu, "!");
            }
        }
        if (clicked) Select(entry);
        // 右クリック (ED10)
        if (ImGui.BeginPopupContextItem("tile"))
        {
            Select(entry);
            if (ImGui.MenuItem("Rename", "F2")) BeginRename(entry);
            if (ImGui.MenuItem("Show in Explorer")) Process.Start("explorer.exe", entry.Kind == AssetKind.Folder ? $"\"{_database.FullPath(entry.Path)}\"" : $"/select,\"{_database.FullPath(entry.Path)}\"");
            ImGui.EndPopup();
        }
        // 名前 (長ければ切る)。名前を書いている時は入力の欄 (ED10)
        if (_renaming == entry.Path)
        {
            var after = ImGui.GetCursorScreenPos();
            ImGui.SetCursorScreenPos(start + new Vector2(0, _tileSize - 2));
            drawRenameField(_tileSize);
            ImGui.SetCursorScreenPos(after);
        }
        else
        {
            var name = entry.Name;
            while (name.Length > 3 && ImGui.CalcTextSize(name).X > _tileSize) name = name[..^2];
            if (name != entry.Name) name = name[..^2] + "...";   // 「…」はフォントの字の範囲に無い
            draw.AddText(start + new Vector2((_tileSize - ImGui.CalcTextSize(name).X) * 0.5f, _tileSize), ImGui.GetColorU32(ImGuiCol.Text), name);
        }
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (entry.Kind == AssetKind.Folder) { _folder = entry.Path; _search = ""; }
            else if (entry.Kind == AssetKind.Scene) _openScene(_database.FullPath(entry.Path));
            else if (entry.Kind == AssetKind.ShaderGraph) OpenShaderGraph?.Invoke(entry.Path);   // SH4
            else if (entry.Kind is AssetKind.FbxModel or AssetKind.FbxSkeleton or AssetKind.FbxAnimation) OpenPreview?.Invoke(entry.Path);   // AN5
            else if (EditorExtensions.ImporterFor(entry.Path) is { } importer)
            {
                try { importer.OnOpen(entry.Path, _database.FullPath(entry.Path)); }
                catch (Exception ex) { Console.WriteLine($"[Assets] {importer.GetType().Name}.OnOpen : {ex.Message}"); }
            }
        }
        // FBX のエラーも出す (AN1。以前はパスだけで、先に出した FBX のエラーを上書きしていた)
        if (hovered) ImGui.SetTooltip(fbxError(entry) is { } fbxProblem ? $"{entry.Path}\n{fbxProblem}" : entry.Path);

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

    private static (string Label, uint Color) iconOf(AssetEntry entry) =>
        entry.Kind != AssetKind.Folder && EditorExtensions.ImporterFor(entry.Path) is { } importer ? (importer.Label, importer.Color) : builtInIcon(entry);

    private static (string Label, uint Color) builtInIcon(AssetEntry entry) => entry.Kind switch
    {
        AssetKind.Folder   => ("Folder", 0xFF3C8CC8u),
        AssetKind.Model    => (Path.GetExtension(entry.Path).TrimStart('.').ToUpperInvariant(), 0xFF8C5A3Cu),
        AssetKind.Texture  => ("IMG", 0xFF5A5A5Au),
        AssetKind.Sound    => ("WAV", 0xFF3CA05Au),
        AssetKind.Scene    => ("Scene", 0xFF8C3C8Cu),
        AssetKind.Material => ("Mat", 0xFF3CA0A0u),
        AssetKind.Prefab   => ("Prefab", 0xFFE0A050u),
        AssetKind.Shader   => ("HLSL", 0xFF6040B0u),   // SH2
        AssetKind.ShaderGraph => ("Graph", 0xFF8040C0u),   // SH4
        AssetKind.FbxModel     => ("FBX Mod", 0xFF5A7A2Au),   // AN1
        AssetKind.FbxSkeleton  => ("FBX Skl", 0xFF7A5A2Au),
        AssetKind.FbxAnimation => ("FBX Anm", 0xFF2A5A9Au),
        AssetKind.Other when entry.Path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) => ("FBX ?", 0xFF3030A0u),   // 種類の分からない .fbx
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
                Select(_database.Find(path));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Assets] Failed to import {file} : {ex.Message}");
            }
        }
    }

    // ---------------------------------------------------------------------
    // 名前を変える (ED10)。書くのは拡張子を除いた名前 (拡張子はそのまま)
    // ---------------------------------------------------------------------

    public void BeginRename(AssetEntry entry)
    {
        if (entry.Path == AssetDatabase.AssetsFolder) return;
        Select(entry);
        // タイルが見えるように、そのアセットのフォルダを出す (今のフォルダの木の欄で書く時はそのまま)
        if (entry.Kind != AssetKind.Folder || _folder != entry.Path) { _folder = parentOf(entry.Path); _search = ""; }
        _renaming    = entry.Path;
        _renameText  = AssetDatabase.SplitName(entry.Name, entry.Kind == AssetKind.Folder).Stem;
        _renameFocus = true;
    }

    public bool IsRenaming => _renaming != null;

    private void drawRenameField(float width)
    {
        ImGui.SetNextItemWidth(Math.Max(60.0f, width));
        if (_renameFocus) { ImGui.SetKeyboardFocusHere(); _renameFocus = false; }
        ImGui.InputText("##rename", ref _renameText, 256, ImGuiInputTextFlags.AutoSelectAll);
        if (!ImGui.IsItemDeactivated()) return;
        var path = _renaming!;
        _renaming = null;
        if (!ImGui.IsKeyPressed(ImGuiKey.Escape)) Rename(path, _renameText);   // Enter / 外をクリック = 決める
    }

    /// <summary>アセットの名前を変える (.meta も一緒。Id は変わらない)。戻り値 = 新しいパス (できなければ null。理由は Console)</summary>
    public string? Rename(string assetPath, string newName)
    {
        try
        {
            var newPath = _database.Rename(assetPath, newName);
            // 今のフォルダ / 選んでいる物を新しいパスに
            if (_folder.Equals(assetPath, StringComparison.OrdinalIgnoreCase) || _folder.StartsWith(assetPath + "/", StringComparison.OrdinalIgnoreCase))
                _folder = newPath + _folder[assetPath.Length..];
            Select(_database.Find(newPath));
            return newPath;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Assets] Cannot rename {assetPath} : {ex.Message}");
            return null;
        }
    }

    public void Select(AssetEntry? entry)
    {
        _selected = entry;
        SelectionVersion++;
    }

    // 今のフォルダにシェーダー (.hlsl) の雛形を作って選ぶ (SH2)。書き方の例が入っている
    public string? CreateShader(string name = "New Shader")
    {
        try
        {
            var path = $"{_folder}/{name}.hlsl";
            for (int i = 1; File.Exists(_database.FullPath(path)); i++) path = $"{_folder}/{name} {i}.hlsl";
            File.WriteAllText(_database.FullPath(path), ShaderTemplate, new System.Text.UTF8Encoding(false));
            _database.Refresh();
            Select(_database.Find(path));
            Console.WriteLine($"[Assets] Created {path}");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Assets] Failed to create a shader : {ex.Message}");
            return null;
        }
    }

    // FBX のエラー (AN1。読み込んだ時の結果。ただの .fbx は名前の付け方を教える)
    private string? fbxError(AssetEntry entry)
    {
        if (entry.Kind == AssetKind.Other && entry.Path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            return "Name FBX files xxx.mod.fbx (model) / xxx.skl.fbx (skeleton) / xxx.anm.fbx (animation)";
        if (entry.Kind is not (AssetKind.FbxModel or AssetKind.FbxSkeleton or AssetKind.FbxAnimation)) return null;
        try { return _engine.Assets.LoadFbx(entry.Path).Error; }
        catch (IOException ex) { return ex.Message; }
    }

    // 自作の FBX のサンプルを Assets/Samples/Fbx に書く (AN1。Assets > Create FBX Samples)
    public List<string> CreateFbxSamples()
    {
        var folder = _database.FullPath(FbxSamples.Folder);
        var written = FbxSamples.WriteAll(folder);
        _database.Refresh();
        foreach (var name in written) _engine.Assets.ReloadFbx($"{FbxSamples.Folder}/{name}");
        _folder = FbxSamples.Folder;
        Console.WriteLine($"[Assets] Wrote {written.Count} FBX samples to {FbxSamples.Folder}");
        return written;
    }

    // 今のフォルダにシェーダーグラフ (.shadergraph.json) を作って選ぶ (SH4)。出力と色 / 粗さのパラメーターが繋がった物
    public string? CreateShaderGraph(string name = "New Shader Graph")
    {
        try
        {
            var path = $"{_folder}/{name}{ShaderGraph.Extension}";
            for (int i = 1; File.Exists(_database.FullPath(path)); i++) path = $"{_folder}/{name} {i}{ShaderGraph.Extension}";
            File.WriteAllText(_database.FullPath(path), ShaderGraph.CreateDefault().ToJson(), new System.Text.UTF8Encoding(false));
            _database.Refresh();
            Select(_database.Find(path));
            Console.WriteLine($"[Assets] Created {path}");
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Assets] Failed to create a shader graph : {ex.Message}");
            return null;
        }
    }

    public const string ShaderTemplate = """
        // ==========================================================================
        // 自分のシェーダー (マテリアルの Shader に入れて使う。保存するとコンパイルし直す)
        //   @queue opaque | transparent / @blend opaque | alpha | additive | premultiplied / @cull back | front | none / @zwrite on | off
        //   @param float | color | vector | texture | texture3d  名前  既定の値  range(最小, 最大)
        //   IN : WorldPosition / LocalPosition / Normal / ViewDirection / CameraPosition / UV / Color / ScreenUV / Time / LocalToWorld / WorldToLocal
        //   Lit(albedo, normal, position, metallic, roughness) : シーンと同じライティング (戻り値は線形の色)
        // ==========================================================================
        // @queue opaque
        // @param color   BaseColor  (0.8, 0.5, 0.3, 1)
        // @param texture MainTexture white
        // @param float   Roughness  0.6 range(0.02, 1)
        // @param float   Pulse      0   range(0, 2)
        float4 Surface(SurfaceInput IN)
        {
            float4 texel  = MainTexture.Sample(g_Sampler, IN.UV);
            float3 albedo = ToLinear(BaseColor.rgb * texel.rgb);
            float3 color  = Lit(albedo, IN.Normal, IN.WorldPosition, 0.0f, Roughness);
            color += albedo * Pulse * (0.5f + 0.5f * sin(IN.Time * 3.0f));   // 光る (放射)
            return float4(color, 1.0f);
        }
        """;

    // 今のフォルダにマテリアルを作って選ぶ
    public Material? CreateMaterial()
    {
        try
        {
            var material = _engine.Assets.CreateMaterialAsset(_folder);
            Select(_database.Find(material.Path));
            Console.WriteLine($"[Assets] Created {material.Path}");
            return material;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Assets] Failed to create a material : {ex.Message}");
            return null;
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
