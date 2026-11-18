using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// インスペクターの「アセットの欄」(v1.20 GO3)。型 → (アセットの種類 / パスの取り方 / 読み方) の表で描く
//
//   [ Assets/Prefabs/Bullet.prefab.json   ][...]  Bullet
//
//   - パスを書いて Enter で読む (以前と同じ) / Project から落とす (以前と同じ) / [...] = 選ぶ窓 (その種類のアセットの一覧と検索)
//   - 以前は型ごとに drawValue の switch に並べ、落とせる種類も型から if で決めていた。表にまとめ、新しい型 (Prefab / SceneReference) も
//     同じ形で足した。ユーザーのアセットの型も EditorApi.RegisterAssetField で足せる (保存は SceneSerializer.RegisterConverter)
// ==========================================================================
internal sealed partial class Inspector
{
    /// <summary>アセットの欄の型の情報</summary>
    internal sealed record AssetField(Type Type, AssetKind[] Kinds, Func<object?, string> PathOf, Func<string, object?> Load, Action<string>? OnDoubleClick = null);

    private readonly Dictionary<Type, AssetField> _assetFields = new();

    /// <summary>アセットの欄の型を足す (同じ型は置き換え)</summary>
    public void RegisterAssetField<T>(AssetKind[] kinds, Func<T, string?> pathOf, Func<string, T> load, Action<string>? onDoubleClick = null)
    {
        _assetFields[typeof(T)] = new AssetField(typeof(T), kinds, v => v is T t ? pathOf(t) ?? "" : "", path => load(path), onDoubleClick);
    }

    internal AssetField? FindAssetField(Type type) => _assetFields.GetValueOrDefault(type);

    // エンジンのアセットの型 (以前 drawValue の switch に並べていた物と同じ読み方)
    private void registerEngineAssetFields()
    {
        var assets = _engine.Assets;
        RegisterAssetField<Model>(new[] { AssetKind.Model, AssetKind.FbxModel }, m => m.IsValid ? m.Path : "", assets.LoadModel);   // AN2 : FBX のモデルも
        RegisterAssetField<Sound>(new[] { AssetKind.Sound }, s => s.IsValid ? s.Path : "", assets.LoadSound);
        RegisterAssetField<Material>(new[] { AssetKind.Material }, m => m?.Path, assets.LoadMaterial);
        RegisterAssetField<Skeleton>(new[] { AssetKind.FbxSkeleton }, s => s?.Path, assets.LoadSkeleton);   // AN3
        RegisterAssetField<AnimationClip>(new[] { AssetKind.FbxAnimation }, c => c?.Path, assets.LoadAnimationClip);
        RegisterAssetField<WidgetAsset>(new[] { AssetKind.Widget }, w => w?.Path, assets.LoadWidget);   // v1.7
        RegisterAssetField<CollisionAsset>(new[] { AssetKind.Collision }, c => c?.Path, assets.LoadCollisionAsset,
                                           path => OpenCollision?.Invoke(path));   // v1.9 : ダブルクリックで窓を開く
        RegisterAssetField<CollisionMesh>(new[] { AssetKind.Model, AssetKind.FbxModel }, m => m?.Path, path => assets.LoadCollisionMesh(assets.LoadModel(path)));
        RegisterAssetField<Prefab>(new[] { AssetKind.Prefab }, p => p?.Path, assets.LoadPrefab);   // v1.20
        RegisterAssetField<SceneReference>(new[] { AssetKind.Scene }, s => s.Path, path => new SceneReference(path));   // v1.20 (読み込みはしない)
    }

    // ---------------------------------------------------------------------
    // 欄
    // ---------------------------------------------------------------------

    private string? _assetPickerField;
    private AssetKind[] _assetPickerKinds = Array.Empty<AssetKind>();
    private Type? _assetPickerUserDataType;   // v2.0 UD2 : UserData の欄の型 (窓を開いている間だけ。閉じたら捨てる)
    private string _assetPickerSearch = "";
    private bool _assetPickerFocus;
    private readonly List<AssetEntry> _assetPickerItems = new();
    private string? _assetPickerBuiltFor;
    private const string AssetPickerPopup = "Select Asset##assetpicker";

    /// <summary>この名前のアセットの欄の選ぶ窓を次に描く時に開く (確認 / 撮影用)</summary>
    internal string? AssetPickerOpenRequest;
    internal bool AssetPickerOpen => _assetPickerField != null;
    internal IReadOnlyList<AssetEntry> AssetPickerItems => _assetPickerItems;

    /// <summary>アセットの選ぶ窓の一覧を作る (確認からも呼ぶ)。search : 名前 / パスの一部</summary>
    //   v2.0 UD2 : userDataType = UserData の欄の型 (その型 / 派生の型のファイルだけ。null なら型で絞らない)
    internal void BuildAssetPickerItems(AssetKind[] kinds, string search, Type? userDataType = null)
    {
        _assetPickerItems.Clear();
        _assetPickerBuiltFor = search;
        var database = AssetDatabase.Current;
        if (database == null) return;
        search = search.Trim();
        foreach (var entry in database.Entries)
        {
            if (Array.IndexOf(kinds, entry.Kind) < 0) continue;
            if (userDataType != null && !IsUserDataOf(entry, userDataType)) continue;
            if (search.Length > 0 && entry.Path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 &&
                entry.Label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            _assetPickerItems.Add(entry);
        }
        _assetPickerItems.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));
    }

    // path : 今のパス (無ければ "")。load : パス → 値 (空のパスは default)。戻り値 : 変えたか
    private bool drawAssetField(string label, AssetKind[] kinds, string path, ref object? value, Func<string, object?> load, Type valueType, Action<string>? onDoubleClick = null)
    {
        var shownLabel = label.Contains("##") ? label[..label.IndexOf("##", StringComparison.Ordinal)] : label;
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var buttonWidth = ImGui.GetFrameHeight() * 1.4f;
        ImGui.PushID(label);
        var edited = path;
        ImGui.SetNextItemWidth(Math.Max(40.0f, ImGui.CalcItemWidth() - buttonWidth - spacing));
        // 空の時は None (型) を薄く出す (以前は空の欄だけで、何の欄か分かりにくかった)
        var entered = ImGui.InputTextWithHint("##path", $"None ({(valueType == typeof(string) ? string.Join(" / ", kinds) : valueType.Name)})", ref edited, 512, ImGuiInputTextFlags.EnterReturnsTrue);
        if (onDoubleClick != null && path.Length > 0 && ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) onDoubleClick(path);
        // Project からアセットを落とす (ED5)
        if (ImGui.BeginDragDropTarget())
        {
            if (ProjectPanel.AcceptDrop(kinds) is { } asset) { edited = asset.Path; entered = true; }
            ImGui.EndDragDropTarget();
        }
        if (ImGui.IsItemHovered() && !ImGui.IsItemActive()) ImGui.SetTooltip($"Drop a {string.Join(" / ", kinds)} from Project, type a path and press Enter, or click [...]");

        ImGui.SameLine(0, spacing);
        if (ImGui.Button("...##assetpick", new Vector2(buttonWidth, 0)) | AssetPickerOpenRequest == label)
        {
            AssetPickerOpenRequest = null;
            _assetPickerField = label;
            _assetPickerKinds = kinds;
            _assetPickerUserDataType = typeof(UserData).IsAssignableFrom(valueType) ? valueType : null;   // v2.0 UD2
            _assetPickerSearch = "";
            _assetPickerBuiltFor = null;
            _assetPickerFocus = true;
            ImGui.OpenPopup(AssetPickerPopup);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Pick a {string.Join(" / ", kinds)}");
        if (_assetPickerField == label)
        {
            ImGui.SetNextWindowPos(new Vector2(ImGui.GetItemRectMax().X, ImGui.GetItemRectMax().Y + 2), ImGuiCond.Appearing, new Vector2(1, 0));
            if (drawAssetPicker(path) is { } picked) { edited = picked; entered = true; }
        }
        ImGui.SameLine(0, spacing);
        ImGui.TextUnformatted(shownLabel);
        ImGui.PopID();

        if (!entered || edited == path) return false;
        try
        {
            value = string.IsNullOrWhiteSpace(edited) ? (valueType.IsValueType ? Activator.CreateInstance(valueType) : null) : load(edited);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Inspector] {ex.Message}");
            return false;
        }
    }

    // アセットの選ぶ窓。選んだらそのパス ("" = None)、選んでいなければ null
    private unsafe string? drawAssetPicker(string current)
    {
        ImGui.SetNextWindowSize(new Vector2(420, 440), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(1.0f);
        if (!ImGui.BeginPopup(AssetPickerPopup))
        {
            _assetPickerField = null;
            _assetPickerUserDataType = null;   // v2.0 : ゲームの型を持ち続けない (読み直した古い DLL が解放されるように)
            return null;
        }
        string? result = null;
        if (_assetPickerFocus) { ImGui.SetKeyboardFocusHere(); _assetPickerFocus = false; }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search name or path", ref _assetPickerSearch, 128);
        if (_assetPickerBuiltFor != _assetPickerSearch) BuildAssetPickerItems(_assetPickerKinds, _assetPickerSearch, _assetPickerUserDataType);
        ImGui.TextDisabled($"{_assetPickerItems.Count} {(_assetPickerUserDataType != null ? _assetPickerUserDataType.Name : string.Join(" / ", _assetPickerKinds))}");
        ImGui.Separator();
        if (ImGui.BeginChild("list", new Vector2(0, 0)))
        {
            if (ImGui.Selectable("None", current.Length == 0)) { result = ""; ImGui.CloseCurrentPopup(); }
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(_assetPickerItems.Count);
            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var entry = _assetPickerItems[i];
                    if (ImGui.Selectable($"{entry.Label}##{entry.Guid}", string.Equals(entry.Path, current, StringComparison.OrdinalIgnoreCase)))
                    {
                        result = entry.Path;
                        ImGui.CloseCurrentPopup();
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip(entry.Path);
                    ImGui.SameLine();
                    ImGui.TextDisabled($"  {Path.GetDirectoryName(entry.Path)?.Replace('\\', '/')}");
                }
            }
            clipper.End();
            clipper.Destroy();
        }
        ImGui.EndChild();
        ImGui.EndPopup();
        return result;
    }
}
