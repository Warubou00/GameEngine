using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// v2.4 : スプライト (GameObject > 2D > Sprite / Animated Sprite、スプライトのアニメーションの窓)
//   作る物はエディターのカメラの前 5 m。Animated Sprite は Project で選んでいるアニメーション (.spriteanim.json) を入れる
// ==========================================================================
internal sealed partial class EditorApp
{
    /// <summary>スプライトを作る (v2.4)。animation があれば SpriteAnimator も付ける。元に戻せる</summary>
    public Entity CreateSprite(string? sprite = null, SpriteAnimation? animation = null, bool animated = false)
    {
        var forward = Vector3.Transform(Vector3.UnitZ, cameraRotation);
        var entity = World.CreateEntity(animated || animation != null ? "Animated Sprite" : "Sprite");
        World.Add(entity, new Transform(_cameraPosition + forward * 5.0f));
        World.Add(entity, new SpriteRenderer(sprite ?? ""));
        if (animated || animation != null) World.Add(entity, new SpriteAnimator(animation));
        Selected = entity;
        _undo.Record(new EntitiesEdit($"Create {World.Get<EntityInfo>(entity).Name}", SceneSerializer.Save(World, new[] { entity }), new List<Guid> { EditorWorld.IdOf(World, entity) }, create: true));
        return entity;
    }

    // GameObject > 2D
    private void drawSpriteMenu()
    {
        if (!ImGui.BeginMenu("2D")) return;
        var selected = _project.Selected;
        if (ImGui.MenuItem("Sprite"))
            CreateSprite(selected is { Kind: AssetKind.Texture } image ? image.Path : null);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Uses the image selected in Project (if any)");
        if (ImGui.MenuItem("Animated Sprite"))
        {
            SpriteAnimation? animation = null;
            if (selected is { Kind: AssetKind.SpriteAnimation } a)
            {
                try { animation = Engine.Assets.LoadSpriteAnimation(a.Path); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException) { Console.WriteLine($"[Sprite] {ex.Message}"); }
            }
            CreateSprite(null, animation, animated: true);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Uses the sprite animation selected in Project (if any)");
        ImGui.EndMenu();
    }

    /// <summary>スプライトのアニメーションの窓で開く (v2.4)</summary>
    public bool OpenSpriteAnimation(string path) => _spriteEditor.Open(path);

    /// <summary>確認用 : 光線で物を選ぶ (シーンビューのクリックと同じ道)</summary>
    internal Entity PickForTest(Vector3 origin, Vector3 direction) => _picker.Pick(origin, direction, out _);

    /// <summary>スプライトのアニメーションの窓 (v2.4)</summary>
    public SpriteAnimationEditor SpriteEditor => _spriteEditor;
}
