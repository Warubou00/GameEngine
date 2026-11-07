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
//   - 右クリック > Create (v1.1) : Folder / Scripts > Behaviour / Material / Shader / Shader Graph を、そのフォルダに作る
//     スクリプトは作ってすぐ名前を書く欄になり、決めた名前が中の型の名前にもなる (Unity と同じ)
//     .cs をダブルクリック : Visual Studio で sln とその .cs を開く (ScriptEditor)
//   - 右クリック > Delete / Delete キー (v1.1) : 確かめてから、ごみ箱へ (.meta も一緒)
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
    // Project の窓にフォーカスがあるか (前のフレーム。Delete キーをシーンの物ではなくアセットに使う。v1.1)
    public bool HasFocus { get; private set; }
    // 消すか聞いているアセット (v1.1。確かめる窓を出す)
    public string? PendingDelete { get; private set; }
    private bool _openDeletePopup;
    // 作ったばかりのスクリプト (名前を決めたら中の型の名前も変える。v1.1)
    private string? _newScript;
    public int SelectionVersion { get; private set; }

    private string  _folder = AssetDatabase.AssetsFolder;
    private string  _search = "";
    private AssetEntry? _selected;
    // 名前を書いているアセット (ED10。F2 / 右クリック > Rename)
    private string? _renaming;
    private bool    _renamingDisplay;   // 書いているのは表示名 (右クリック > Display Name...)。ファイルの名前は変えない
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

        HasFocus = false;
        if (ImGui.Begin("Project", ref open))
        {
            HasFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
            // F2 : 選んでいるアセットの名前を書く (ED10。Project にフォーカスがある時)
            if (_renaming == null && _selected != null && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) &&
                !ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(ImGuiKey.F2, false))
                BeginRename(_selected);
            // Delete : 選んでいるアセットを消す (確かめてから。v1.1)
            if (_renaming == null && _selected != null && HasFocus && !ImGui.GetIO().WantTextInput && ImGui.IsKeyPressed(ImGuiKey.Delete, false))
                RequestDelete(_selected);
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
                // 何も無い所の右クリック : 今のフォルダに作る (v1.1)
                if (ImGui.BeginPopupContextWindow("itemsMenu", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
                {
                    createMenu(_folder);
                    if (ImGui.MenuItem("Show in Explorer")) Process.Start("explorer.exe", $"\"{_database.FullPath(_folder)}\"");
                    ImGui.EndPopup();
                }
                ImGui.EndChild();
                ImGui.EndTable();
            }
            // 選んだアセットの情報
            if (_selected != null) ImGui.TextDisabled($"{_selected.Path}   ({_selected.Kind}, id {_selected.Guid})");
            else ImGui.TextDisabled($"{_database.Entries.Count()} assets");
            drawDeletePopup();
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
            var crumb = string.Join('/', parts.Take(i + 1));
            if (ImGui.SmallButton($"{_database.Find(crumb)?.Label ?? parts[i]}##crumb{i}")) _folder = crumb;   // 表示名があれば表示名
        }
    }

    private void drawFolder(string path)
    {
        var children = _database.Entries.Where(e => e.Kind == AssetKind.Folder && parentOf(e.Path) == path).OrderBy(e => e.Label, StringComparer.OrdinalIgnoreCase).ToList();
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;
        if (path == AssetDatabase.AssetsFolder) flags |= ImGuiTreeNodeFlags.DefaultOpen;
        if (_folder.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)) ImGui.SetNextItemOpen(true, ImGuiCond.Once);   // 今のフォルダまで開く
        if (path == _folder) flags |= ImGuiTreeNodeFlags.Selected;
        var renaming = _renaming == path;
        var entry = _database.Find(path);
        var open = ImGui.TreeNodeEx(renaming ? $"##{path}" : $"{entry?.Label ?? Path.GetFileName(path)}##{path}", flags);
        if (entry?.DisplayName != null && ImGui.IsItemHovered()) ImGui.SetTooltip($"{entry.DisplayName}\n{entry.Path}");   // 表示名の時は本当の名前も
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) { _folder = path; _search = ""; Select(_database.Find(path)); }
        if (path == AssetDatabase.AssetsFolder && ImGui.BeginPopupContextItem($"folder{path}"))   // Assets : 作るだけ (v1.1)
        {
            createMenu(path);
            ImGui.EndPopup();
        }
        if (path != AssetDatabase.AssetsFolder && ImGui.BeginPopupContextItem($"folder{path}"))
        {
            createMenu(path);
            ImGui.Separator();
            if (ImGui.MenuItem("Rename", "F2") && _database.Find(path) is { } folder) BeginRename(folder);
            displayNameMenu(entry);
            if (ImGui.MenuItem("Show in Explorer")) Process.Start("explorer.exe", $"\"{_database.FullPath(path)}\"");
            ImGui.Separator();
            if (ImGui.MenuItem("Delete", "Del") && entry != null) RequestDelete(entry);
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
            ? _database.Entries.Where(e => e.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
                                           (e.DisplayName?.Contains(_search, StringComparison.OrdinalIgnoreCase) ?? false))   // 表示名でも探す
            : _database.Entries.Where(e => parentOf(e.Path) == _folder);
        var list = items.OrderBy(e => e.Kind != AssetKind.Folder).ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase).ToList();
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
            createMenu(entry.Kind == AssetKind.Folder ? entry.Path : parentOf(entry.Path));   // v1.1 : フォルダならその中に
            ImGui.Separator();
            if (entry.Kind == AssetKind.Script && ImGui.MenuItem("Open")) OpenScript(entry.Path);
            if (ImGui.MenuItem("Rename", "F2")) BeginRename(entry);
            displayNameMenu(entry);
            if (ImGui.MenuItem("Show in Explorer")) Process.Start("explorer.exe", entry.Kind == AssetKind.Folder ? $"\"{_database.FullPath(entry.Path)}\"" : $"/select,\"{_database.FullPath(entry.Path)}\"");
            ImGui.Separator();
            if (ImGui.MenuItem("Delete", "Del")) RequestDelete(entry);
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
            var name = entry.Label;   // 表示名があれば表示名 (色を少し変えて、ファイルの名前と違うと分かるように)
            while (name.Length > 3 && ImGui.CalcTextSize(name).X > _tileSize) name = name[..^2];
            if (name != entry.Label) name = name[..^2] + "...";   // 「…」はフォントの字の範囲に無い
            var nameColor = entry.DisplayName != null ? 0xFFFFD890u : ImGui.GetColorU32(ImGuiCol.Text);
            draw.AddText(start + new Vector2((_tileSize - ImGui.CalcTextSize(name).X) * 0.5f, _tileSize), nameColor, name);
        }
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
        {
            if (entry.Kind == AssetKind.Folder) { _folder = entry.Path; _search = ""; }
            else if (entry.Kind == AssetKind.Scene) _openScene(_database.FullPath(entry.Path));
            else if (entry.Kind == AssetKind.ShaderGraph) OpenShaderGraph?.Invoke(entry.Path);   // SH4
            else if (entry.Kind is AssetKind.FbxModel or AssetKind.FbxSkeleton or AssetKind.FbxAnimation) OpenPreview?.Invoke(entry.Path);   // AN5
            else if (entry.Kind == AssetKind.Script) OpenScript(entry.Path);   // v1.1 : Visual Studio で sln と .cs を開く
            else if (EditorExtensions.ImporterFor(entry.Path) is { } importer)
            {
                try { importer.OnOpen(entry.Path, _database.FullPath(entry.Path)); }
                catch (Exception ex) { Console.WriteLine($"[Assets] {importer.GetType().Name}.OnOpen : {ex.Message}"); }
            }
        }
        // FBX のエラーも出す (AN1。以前はパスだけで、先に出した FBX のエラーを上書きしていた)
        if (hovered)
        {
            var tip = entry.DisplayName != null ? $"{entry.DisplayName}\n{entry.Path}" : entry.Path;   // 表示名の時は本当のパスも
            ImGui.SetTooltip(fbxError(entry) is { } fbxProblem ? $"{tip}\n{fbxProblem}" : tip);
        }

        // ドラッグ (フォルダ以外)
        if (entry.Kind != AssetKind.Folder && ImGui.BeginDragDropSource())
        {
            Dragged = entry;
            int dummy = 0;
            ImGui.SetDragDropPayload(Payload, (IntPtr)(&dummy), sizeof(int));
            ImGui.Text($"{entry.Label} ({entry.Kind})");
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
        AssetKind.Script   => ("C#", 0xFF9A3C68u),     // v1.1
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
        _renamingDisplay = false;
        _renameText  = AssetDatabase.SplitName(entry.Name, entry.Kind == AssetKind.Folder).Stem;
        _renameFocus = true;
    }

    // ---------------------------------------------------------------------
    // 表示名 : Project に出す名前だけを変える (ファイル / フォルダの名前はそのまま。.meta の displayName)
    // ---------------------------------------------------------------------

    private void displayNameMenu(AssetEntry? entry)
    {
        if (entry == null || entry.Path == AssetDatabase.AssetsFolder) return;
        if (ImGui.MenuItem("Display Name...")) BeginDisplayName(entry);
        if (entry.DisplayName != null && ImGui.MenuItem($"Clear Display Name ({entry.Name})")) SetDisplayName(entry.Path, null);
    }

    /// <summary>表示名を書く欄を出す (Enter で決める / Esc でやめる / 空にすると表示名を消す)</summary>
    public void BeginDisplayName(AssetEntry entry)
    {
        BeginRename(entry);
        if (_renaming == null) return;
        _renamingDisplay = true;
        _renameText = entry.DisplayName ?? "";
    }

    /// <summary>表示名を変える (null / 空 = 消す)。戻り値 = できたか (できなければ理由は Console)</summary>
    public bool SetDisplayName(string assetPath, string? displayName)
    {
        try
        {
            var label = _database.SetDisplayName(assetPath, displayName);
            Select(_database.Find(assetPath));
            Console.WriteLine(label != null ? $"[Assets] Display name of {assetPath} : {label}" : $"[Assets] Display name of {assetPath} cleared");
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Assets] Cannot set the display name of {assetPath} : {ex.Message}");
            return false;
        }
    }

    public bool IsRenaming => _renaming != null;

    private void drawRenameField(float width)
    {
        ImGui.SetNextItemWidth(Math.Max(60.0f, width));
        if (_renameFocus) { ImGui.SetKeyboardFocusHere(); _renameFocus = false; }
        if (_renamingDisplay) ImGui.InputTextWithHint("##rename", "display name (empty = file name)", ref _renameText, 256, ImGuiInputTextFlags.AutoSelectAll);
        else ImGui.InputText("##rename", ref _renameText, 256, ImGuiInputTextFlags.AutoSelectAll);
        if (!ImGui.IsItemDeactivated()) return;
        var path = _renaming!;
        _renaming = null;
        var newScript = _newScript == path;
        _newScript = null;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) return;   // Enter / 外をクリック = 決める
        if (_renamingDisplay) SetDisplayName(path, _renameText);
        else if (Rename(path, _renameText) is { } renamed && newScript && renamed != path)
        {
            // 作ったばかりのスクリプト : 中の型の名前もファイルの名前に (雛形から書き換えていなければ。v1.1)
            if (ScriptEditor.RenameTemplate(_database.FullPath(renamed), Path.GetFileNameWithoutExtension(path), Path.GetFileNameWithoutExtension(renamed)))
                Console.WriteLine($"[Assets] {Path.GetFileName(renamed)} : the type is {ScriptEditor.ToTypeName(Path.GetFileNameWithoutExtension(renamed))}");
        }
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
        if (_database.Find($"{_folder}/{name}") is { } created) BeginRename(created);   // v1.1 : すぐ名前を書く
    }

    // ---------------------------------------------------------------------
    // 右クリック > Create (v1.1)。folder に作る (今のフォルダもそこにする)
    // ---------------------------------------------------------------------
    private void createMenu(string folder)
    {
        if (!ImGui.BeginMenu("Create")) return;
        if (ImGui.MenuItem("Folder")) { _folder = folder; newFolder(); }
        if (ImGui.BeginMenu("Scripts"))
        {
            if (ImGui.MenuItem("Behaviour")) { _folder = folder; CreateBehaviour(); }
            ImGui.EndMenu();
        }
        ImGui.Separator();
        if (ImGui.MenuItem("Material")) { _folder = folder; CreateMaterial(); }
        if (ImGui.MenuItem("Shader")) { _folder = folder; CreateShader(); }
        if (ImGui.MenuItem("Shader Graph")) { _folder = folder; if (CreateShaderGraph() is { } graph) OpenShaderGraph?.Invoke(graph); }
        ImGui.EndMenu();
    }

    /// <summary>
    /// 今のフォルダに Behaviour (コンポーネント + System) のスクリプトを作り、名前を書く欄にする (v1.1)。
    /// 名前を決めると中の型の名前も変わる。戻り値 = アセットのパス (できなければ null)
    /// </summary>
    public string? CreateBehaviour(string name = ScriptEditor.DefaultBehaviourName, bool rename = true)
    {
        try
        {
            var path = $"{_folder}/{name}.cs";
            for (int i = 1; File.Exists(_database.FullPath(path)); i++) path = $"{_folder}/{name}{i}.cs";
            ScriptEditor.WriteScript(_database.FullPath(path), ScriptEditor.BehaviourTemplate(ScriptEditor.ToTypeName(Path.GetFileNameWithoutExtension(path))));
            _database.Refresh();
            Console.WriteLine($"[Assets] Created {path}");
            if (_database.Find(path) is { } entry)
            {
                Select(entry);
                if (rename) { BeginRename(entry); _newScript = path; }
            }
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Assets] Failed to create a script : {ex.Message}");
            return null;
        }
    }

    /// <summary>確認用 : 今のフォルダを folder にする (右クリック > Create はそのフォルダに作る)</summary>
    public void CreateFolderForTest(string folder) { _folder = folder; _search = ""; }

    /// <summary>スクリプトを外のエディター (Visual Studio) で開く (sln も作る。v1.1)</summary>
    public void OpenScript(string assetPath)
    {
        var how = ScriptEditor.Open(_database.ProjectRoot, _database.FullPath(assetPath));
        Console.WriteLine($"[Scripts] {how}");
    }

    /// <summary>消すか聞く窓を出す (v1.1。Assets は消せない)</summary>
    public void RequestDelete(AssetEntry entry)
    {
        if (entry.Path == AssetDatabase.AssetsFolder) return;
        PendingDelete = entry.Path;
        _openDeletePopup = true;
    }

    private void drawDeletePopup()
    {
        if (_openDeletePopup) { ImGui.OpenPopup("Delete Asset"); _openDeletePopup = false; }
        if (!ImGui.BeginPopupModal("Delete Asset", ImGuiWindowFlags.AlwaysAutoResize)) return;
        var path = PendingDelete ?? "";
        var entry = _database.Find(path);
        var inside = entry?.Kind == AssetKind.Folder ? _database.Entries.Count(e => e.Path.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase)) : 0;
        ImGui.Text($"Delete {entry?.Label ?? path}?");
        ImGui.TextDisabled(path);
        if (inside > 0) ImGui.Text($"The folder and the {inside} assets in it are deleted.");
        ImGui.Text("The files go to the Recycle Bin (with their .meta).");
        ImGui.Spacing();
        if (ImGui.Button("Delete", new Vector2(110, 0)) || ImGui.IsKeyPressed(ImGuiKey.Enter))
        {
            DeleteAsset(path);
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(110, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            PendingDelete = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    /// <summary>アセットを消す (確かめない。ごみ箱へ。.meta も)。戻り値 = できたか (できなければ理由は Console)</summary>
    public bool DeleteAsset(string assetPath, bool toRecycleBin = true)
    {
        PendingDelete = null;
        try
        {
            var count = _database.Delete(assetPath, toRecycleBin);
            bool under(string? p) => p != null && (p.Equals(assetPath, StringComparison.OrdinalIgnoreCase) || p.StartsWith(assetPath + "/", StringComparison.OrdinalIgnoreCase));
            if (under(_selected?.Path)) Select(null);
            if (under(_folder)) _folder = parentOf(assetPath);
            if (under(_renaming)) _renaming = null;
            Console.WriteLine($"[Assets] Deleted {assetPath}{(count > 1 ? $" ({count} assets)" : "")}{(toRecycleBin ? " (Recycle Bin)" : "")}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException)
        {
            Console.WriteLine($"[Assets] Cannot delete {assetPath} : {ex.Message}");
            return false;
        }
    }
}
