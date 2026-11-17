using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// 物の欄 (v1.18 GO2) のエディターの側
//   - Ping : Hierarchy でその物を光らせる (親を開き、そこまでスクロール。1.5 秒で消える。Unity の EditorGUIUtility.PingObject)
//   - 別のシーンの物への参照を断る (編集中だけ。サブシーンの中の物 ⇔ メインシーンの物など)
//   - Hierarchy でドラッグしている物を Inspector に渡す
// ==========================================================================
internal sealed partial class EditorApp
{
    private const double PingSeconds = 1.5;
    private Entity _ping;
    private double _pingStart;
    private bool   _pingScroll;
    private readonly HashSet<Entity> _pingOpen = new();   // 開く親

    /// <summary>今光らせている物 (確認用。光り終わったら Null)</summary>
    internal Entity PingedEntity => World.IsAlive(_ping) && ImGui.GetTime() - _pingStart < PingSeconds ? _ping : Entity.Null;
    /// <summary>Ping した物まで Hierarchy をスクロールした回数 (確認用)</summary>
    internal int PingScrollCount { get; private set; }

    /// <summary>Hierarchy でその物を光らせる</summary>
    public void Ping(Entity entity)
    {
        if (!World.IsAlive(entity)) return;
        _ping = entity;
        _pingStart = ImGui.GetTime();
        _pingScroll = true;
        _showHierarchy = true;
        // 親を全部開く (Transform の親。Transform の無いサブシーンの物は持ち主)
        _pingOpen.Clear();
        var transforms = World.GetPool<Transform>();
        var current = entity;
        for (int depth = 0; depth < 256; depth++)
        {
            var parent = transforms.Has(current.Id) ? transforms.Get(current.Id).Parent : Entity.Null;
            if (!World.IsAlive(parent)) parent = hierarchyParentOf(current, out _);
            if (!World.IsAlive(parent) || !_pingOpen.Add(parent)) break;
            current = parent;
        }
    }

    // Hierarchy の 1 行を描く前 : Ping した物の親なら開く
    private void beforeHierarchyNode(Entity entity)
    {
        if (_pingScroll && _pingOpen.Contains(entity)) ImGui.SetNextItemOpen(true);
    }

    /// <summary>確認用 : この物の Hierarchy の行の場所を TrackedRowRect に書く</summary>
    internal Entity TrackedRow;
    internal (Vector2 Min, Vector2 Max) TrackedRowRect;

    // Hierarchy の 1 行を描いた後 : Ping した物なら光らせる (最初の 1 回はそこまでスクロール)
    private void afterHierarchyNode(Entity entity)
    {
        if (entity == TrackedRow) TrackedRowRect = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        if (entity != _ping) return;
        var t = ImGui.GetTime() - _pingStart;
        if (t >= PingSeconds) { _ping = Entity.Null; return; }
        if (_pingScroll)
        {
            ImGui.SetScrollHereY(0.5f);
            _pingScroll = false;
            _pingOpen.Clear();
            PingScrollCount++;
        }
        var alpha = (float)(1.0 - t / PingSeconds);
        var min = ImGui.GetItemRectMin() - new Vector2(2, 1);
        var max = ImGui.GetItemRectMax() + new Vector2(2, 1);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.2f, 0.35f * alpha)), 3.0f);
        draw.AddRect(min, max, ImGui.GetColorU32(new Vector4(1.0f, 0.85f, 0.2f, alpha)), 3.0f, ImDrawFlags.None, 2.0f);
    }

    /// <summary>owner の欄に target を入れられない理由 (入れられれば null)。編集中に別のシーンの物を指すのを断る</summary>
    internal string? ReferenceRefusal(Entity owner, Entity target)
    {
        if (_playing) return null;   // 再生中は保存しないので、どのシーンの物でも良い
        var ownerScene = sceneKey(owner);
        var targetScene = sceneKey(target);
        if (ReferenceEquals(ownerScene, targetScene)) return null;
        return $"'{NameOf(World, target)}' is in {sceneLabel(targetScene)}, but this field belongs to {sceneLabel(ownerScene)}.\n" +
               "References across scenes are not saved. Find it at runtime instead (GameObject.Find / FindWithTag).";

        // メインシーンの物は null (SceneId 0 の物も、今のメインシーンの物も同じ)
        Scene? sceneKey(Entity e) => World.IsAlive(e) && SceneSerializer.IsOutsideMainScene(World, e) ? Scenes.GetScene(e) : null;
        static string sceneLabel(Scene? scene) => scene == null ? "the main scene" : $"scene '{scene.Name}'";
    }

    // Hierarchy でドラッグしている物 (ドラッグしていなければ Null)
    private unsafe Entity draggedFromHierarchy()
    {
        var payload = ImGui.GetDragDropPayload();
        return payload.NativePtr != null && payload.IsDataType("ENTITY") && World.IsAlive(_dragged) ? _dragged : Entity.Null;
    }
}
