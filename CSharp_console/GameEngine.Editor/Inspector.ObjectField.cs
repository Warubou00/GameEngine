using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// インスペクターの「物の欄」(v1.18 GO2。Unity の ObjectField)。Entity / GameObject のフィールド
//
//   [ Player (GameObject)        ][...]  Target
//
//   - Hierarchy から物をドラッグして落とす (型に合わない物 / 別のシーンの物は断り、理由を出す)
//   - 欄をクリック : Hierarchy のその物を光らせる (Ping。親を開いてそこまでスクロール)。ダブルクリック : その物を選ぶ
//   - [...] : 選ぶ窓 (名前で検索 / t:型 / tag:タグ。見えている行だけを描くので、物が何万あっても重くならない)
//   - 右クリック : Ping / Select / Clear
//   - 消えた物を指している時は Missing (赤)、何も無い時は None
//   - 別のシーンの物は入れられない (保存すると読み込み順で外れるため。Unity と同じ。実行中に GameObject.Find で探す)
//     ゲームの再生中は、どのシーンの物でも入れられる (保存しないので)
// ==========================================================================
internal sealed partial class Inspector
{
    /// <summary>Hierarchy でその物を光らせる (EditorApp.Ping)</summary>
    public Action<Entity>? PingEntity;
    /// <summary>その物を選ぶ</summary>
    public Action<Entity>? SelectEntity;
    /// <summary>Hierarchy でドラッグしている物 (無ければ Null)</summary>
    public Func<Entity>? DraggedEntity;
    /// <summary>owner の欄に target を入れられない理由 (入れられれば null。別のシーンなど)</summary>
    public Func<Entity, Entity, string?>? ReferenceRefusal;

    private const string PickerPopup = "Select Object##picker";
    private string? _pickerField;            // 選ぶ窓を開いている欄 (ImGui の ID の文字列)
    private string  _pickerSearch = "";
    private bool    _pickerFocus;
    private readonly List<Entity> _pickerItems = new();
    private string? _pickerBuiltFor;          // 一覧を作った時の検索の文字 (変わったら作り直す)
    private int     _pickerBuiltVersion = -1; // 一覧を作った時の物の数 (増えた / 減ったら作り直す)
    private Type?   _pickerRequired;          // 一覧を作った時の要るコンポーネント
    private string? _dropRefusal;             // 落とそうとしている物を断る理由 (落とす前に出す)

    /// <summary>この名前の欄の選ぶ窓を次に描く時に開く (確認 / 画面の撮影用)</summary>
    internal string? PickerOpenRequest;
    /// <summary>物の欄の場所 (確認用。欄の名前 → 左上 / 右下。マウスで落とす確認に使う)</summary>
    internal readonly Dictionary<string, (Vector2 Min, Vector2 Max)> FieldRects = new();

    /// <summary>選ぶ窓を開いているか (確認用)</summary>
    internal bool PickerOpen => _pickerField != null;

    /// <summary>今の選ぶ窓の一覧 (確認用)</summary>
    internal IReadOnlyList<Entity> PickerItems => _pickerItems;

    // ---------------------------------------------------------------------
    // 入れられるか (落とす / 選ぶ窓 / 確認から共通)
    // ---------------------------------------------------------------------

    /// <summary>owner の欄に target を入れられない理由 (入れられれば null)</summary>
    //   required : 相手に要るコンポーネント (ComponentRef<T> の T。v1.20 GO3)
    internal string? RefuseReference(Entity owner, Entity target, Type? required = null)
    {
        if (!World.IsAlive(target)) return "The object no longer exists.";
        if (required != null && findPool(required)?.Has(target.Id) != true)
            return $"'{EditorApp.NameOf(World, target)}' has no {ComponentRegistry.Get(required).DisplayName} component.";
        if (ReferenceRefusal?.Invoke(owner, target) is { } reason) return reason;
        return null;
    }

    /// <summary>欄の型 (Entity / GameObject / ComponentRef&lt;T&gt;) の値にする</summary>
    internal object MakeReference(Type fieldType, Entity target) =>
        // object にしてから選ぶ (そのまま ?: で書くと GameObject が暗黙に Entity へ変わり、GameObject の欄に Entity が入った)
        EntityRefs.IsEntityRef(fieldType) ? EntityRefs.Create(fieldType, World, target) :   // v1.20 GO3
        fieldType == typeof(GameObject) ? (object)(target.IsNull ? GameObject.None : new GameObject(World, target)) : (object)target;

    // 型の入れ物 (無ければ null。GetPool(Type) と違って作らない。ゲームの型の空の入れ物を増やさないため)
    private IComponentPool? findPool(Type type)
    {
        foreach (var pool in World.Pools) if (pool.ComponentType == type) return pool;
        return null;
    }

    /// <summary>選ぶ窓の一覧を作る (確認からも呼ぶ)。search : 名前の一部 / t:型の名前 / tag:タグ</summary>
    //   required : 相手に要るコンポーネント (ComponentRef<T> の T。持つ物だけを並べる。v1.20 GO3)
    internal void BuildPickerItems(string search, Type? required = null)
    {
        _pickerItems.Clear();
        _pickerRequired = required;
        _pickerBuiltFor = search;
        _pickerBuiltVersion = World.EntityCount;
        search = search.Trim();
        Type? componentType = null;
        string? tag = null;
        if (search.StartsWith("t:", StringComparison.OrdinalIgnoreCase))
        {
            var typeName = search[2..].Trim();
            componentType = World.Pools.Select(p => p.ComponentType)
                .FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(ComponentRegistry.Get(t).DisplayName, typeName, StringComparison.OrdinalIgnoreCase));
            if (componentType == null) return;   // 無い型 : 何も出さない
            search = "";
        }
        else if (search.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
        {
            tag = search[4..].Trim();
            search = "";
        }
        var pool = componentType != null ? World.GetPool(componentType) : null;
        var requiredPool = required != null ? findPool(required) : null;
        if (required != null && requiredPool == null) return;   // 誰も持っていない
        var infos = World.GetPool<EntityInfo>();
        foreach (var e in World.Entities)
        {
            if (pool != null && !pool.Has(e.Id)) continue;
            if (requiredPool != null && !requiredPool.Has(e.Id)) continue;
            if (tag != null && !string.Equals(infos.Has(e.Id) ? infos.Get(e.Id).Tag : null, tag, StringComparison.OrdinalIgnoreCase)) continue;
            if (search.Length > 0 && EditorApp.NameOf(World, e).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
            _pickerItems.Add(e);
        }
    }

    // ---------------------------------------------------------------------
    // 欄
    // ---------------------------------------------------------------------

    // value : 今の値 (Entity / GameObject の箱)。target : 指している物 (この World の物でなければ Null)
    // required : 相手に要るコンポーネント (ComponentRef<T> の T。v1.20 GO3)
    private bool drawObjectField(string label, Type fieldType, ref object? value, Entity target, Entity owner, Type? required = null)
    {
        var typeName = required != null ? ComponentRegistry.Get(required).DisplayName : fieldType == typeof(GameObject) ? "GameObject" : "Entity";
        var lostComponent = required != null && World.IsAlive(target) && findPool(required)?.Has(target.Id) != true;   // 相手が T を外した
        var alive = World.IsAlive(target);
        var missing = !target.IsNull && !alive;
        var text = lostComponent ? $"{EditorApp.NameOf(World, target)} (no {typeName})" :
                   alive ? $"{EditorApp.NameOf(World, target)} ({typeName})" : missing ? $"Missing ({typeName})" : $"None ({typeName})";
        missing |= lostComponent;   // 赤で出す
        var shownLabel = label.Contains("##") ? label[..label.IndexOf("##", StringComparison.Ordinal)] : label;
        var changed = false;

        ImGui.PushID(label);
        var width = ImGui.CalcItemWidth();
        var buttonWidth = ImGui.GetFrameHeight() * 1.4f;
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;

        // 欄 (左寄せのボタン)
        ImGui.PushStyleVar(ImGuiStyleVar.ButtonTextAlign, new Vector2(0.0f, 0.5f));
        ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.FrameBg));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGui.GetColorU32(ImGuiCol.FrameBgHovered));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGui.GetColorU32(ImGuiCol.FrameBgActive));
        if (missing) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.0f, 0.45f, 0.4f, 1.0f));
        else if (!alive) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        var clicked = ImGui.Button($"{text}##field", new Vector2(Math.Max(40.0f, width - buttonWidth - spacing), 0));
        ImGui.PopStyleColor(missing || !alive ? 4 : 3);
        ImGui.PopStyleVar();
        FieldRects[label] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (clicked && alive) PingEntity?.Invoke(target);
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && alive) SelectEntity?.Invoke(target);

        // Hierarchy から落とす
        var dragged = DraggedEntity?.Invoke() ?? Entity.Null;
        if (ImGui.BeginDragDropTarget())
        {
            _dropRefusal = World.IsAlive(dragged) ? RefuseReference(owner, dragged, required) : null;
            unsafe
            {
                // 断る物は落としても入れない (理由は下のツールチップ)
                var payload = ImGui.AcceptDragDropPayload("ENTITY", _dropRefusal != null ? ImGuiDragDropFlags.AcceptNoDrawDefaultRect : ImGuiDragDropFlags.None);
                if (payload.NativePtr != null && _dropRefusal == null && World.IsAlive(dragged))
                {
                    value = MakeReference(fieldType, dragged);
                    changed = true;
                }
            }
            if (_dropRefusal != null) ImGui.SetTooltip(_dropRefusal);
            ImGui.EndDragDropTarget();
        }
        else if (ImGui.IsItemHovered() && !ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            ImGui.SetTooltip(alive ? "Click : ping in Hierarchy / Double-click : select\nDrop an object from Hierarchy to replace" :
                             missing ? "The referenced object was deleted" : "Drop an object from Hierarchy, or click [...] to pick one");
        }

        // 右クリック
        if (ImGui.BeginPopupContextItem("menu"))
        {
            if (ImGui.MenuItem("Ping", null, false, alive)) PingEntity?.Invoke(target);
            if (ImGui.MenuItem("Select", null, false, alive)) SelectEntity?.Invoke(target);
            if (ImGui.MenuItem("Clear", null, false, !target.IsNull)) { value = MakeReference(fieldType, Entity.Null); changed = true; }
            ImGui.EndPopup();
        }

        // 選ぶ窓を開くボタン
        ImGui.SameLine(0, spacing);
        if (ImGui.Button("...##pick", new Vector2(buttonWidth, 0)) | PickerOpenRequest == label)
        {
            PickerOpenRequest = null;
            _pickerField = label;
            _pickerSearch = "";
            _pickerBuiltFor = null;
            _pickerFocus = true;
            ImGui.OpenPopup(PickerPopup);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Pick a {typeName} from the scene");
        // 選ぶ窓は [...] の下に、右端を揃えて出す (Inspector は画面の右にあるので左へ開く。画面の外へは ImGui が押し戻す)
        if (_pickerField == label) ImGui.SetNextWindowPos(new Vector2(ImGui.GetItemRectMax().X, ImGui.GetItemRectMax().Y + 2), ImGuiCond.Appearing, new Vector2(1, 0));
        if (_pickerField == label && drawPicker(fieldType, owner, target, ref value, required)) changed = true;

        ImGui.SameLine(0, spacing);
        ImGui.TextUnformatted(shownLabel);
        ImGui.PopID();
        return changed;
    }

    // 選ぶ窓 (検索 + 見えている行だけ描く一覧)
    private unsafe bool drawPicker(Type fieldType, Entity owner, Entity current, ref object? value, Type? required)
    {
        ImGui.SetNextWindowSize(new Vector2(380, 440), ImGuiCond.Appearing);
        ImGui.SetNextWindowBgAlpha(1.0f);   // 後ろの Hierarchy が透けて読みにくかった
        if (!ImGui.BeginPopup(PickerPopup))
        {
            _pickerField = null;   // 閉じた
            return false;
        }
        var changed = false;
        if (_pickerFocus) { ImGui.SetKeyboardFocusHere(); _pickerFocus = false; }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##search", "Search name  (t:Camera / tag:Enemy)", ref _pickerSearch, 128);
        if (_pickerBuiltFor != _pickerSearch || _pickerBuiltVersion != World.EntityCount || _pickerRequired != required) BuildPickerItems(_pickerSearch, required);
        ImGui.TextDisabled($"{_pickerItems.Count} objects");
        ImGui.Separator();

        if (ImGui.BeginChild("list", new Vector2(0, 0)))
        {
            if (ImGui.Selectable("None", current.IsNull))
            {
                value = MakeReference(fieldType, Entity.Null);
                changed = true;
                ImGui.CloseCurrentPopup();
            }
            var clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
            clipper.Begin(_pickerItems.Count);
            while (clipper.Step())
            {
                for (int i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                {
                    var e = _pickerItems[i];
                    if (!World.IsAlive(e)) continue;
                    var refusal = RefuseReference(owner, e, required);
                    var inactive = !World.IsActive(e);
                    if (refusal != null) ImGui.BeginDisabled();
                    else if (inactive) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
                    if (ImGui.Selectable($"{EditorApp.NameOf(World, e)}##{e.Id}", e == current))
                    {
                        value = MakeReference(fieldType, e);
                        changed = true;
                        ImGui.CloseCurrentPopup();
                    }
                    if (refusal != null) ImGui.EndDisabled();
                    else if (inactive) ImGui.PopStyleColor();
                    if (refusal != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(refusal);
                    // 親の名前 (同じ名前の物を見分ける)
                    if (World.Has<Transform>(e) && World.IsAlive(World.Get<Transform>(e).Parent))
                    {
                        ImGui.SameLine();
                        ImGui.TextDisabled($"  in {EditorApp.NameOf(World, World.Get<Transform>(e).Parent)}");
                    }
                }
            }
            clipper.End();
            clipper.Destroy();
        }
        ImGui.EndChild();
        ImGui.EndPopup();
        return changed;
    }
}
