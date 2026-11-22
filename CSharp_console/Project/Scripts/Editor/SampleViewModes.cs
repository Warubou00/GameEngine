using System.Numerics;
using GameEngine;
using GameEngine.Editor;
using ImGuiNET;

namespace GameEngine_console.Editor;

// ==========================================================================
// シーンビューの表示モードを足す例 (v2.2。シーンビューの「View : ...」のプルダウンに出る)
//   [SceneViewMode("名前", order)] を付けて SceneViewMode を継ぐ。保存するとエディターが読み直す
//   できること : CreatePipeline (自分の RenderPipeline で描く) / OnEnable・OnDisable (設定を変えて戻す) /
//               OnUpdate (DebugDraw で足す) / OnOptionsGUI (プルダウンの中の設定) / OnOverlayGUI (絵の上の凡例)
// ==========================================================================

// 例 1 : 光を切って、テクスチャ / マテリアルの色だけを見る (描画の設定を変えて、外す時に戻す)
[SceneViewMode("Unlit (Albedo)", 200)]
public sealed class UnlitViewMode : SceneViewMode
{
    private LightingModel _saved;

    public override void OnEnable()
    {
        _saved = Engine.Graphics.LightingModel;
        Engine.Graphics.LightingModel = LightingModel.Unlit;
    }

    public override void OnDisable() => Engine.Graphics.LightingModel = _saved;
}

// 例 2 : 普段の絵に、点光源 / スポットライトの届く範囲を重ねる (OnUpdate で DebugDraw の線を足す)
[SceneViewMode("Light Ranges", 210)]
public sealed class LightRangesViewMode : SceneViewMode
{
    private bool _spots = true;

    public override void OnUpdate()
    {
        foreach (var entity in World.Entities)
        {
            if (!World.Has<Transform>(entity) || !World.IsActive(entity)) continue;
            var transform = World.Get<Transform>(entity);
            if (World.Has<PointLight>(entity))
            {
                var light = World.Get<PointLight>(entity);
                DebugDraw.WireSphere(transform.WorldPosition, light.Range, color(light.Color));
            }
            if (_spots && World.Has<SpotLight>(entity))
            {
                var light = World.Get<SpotLight>(entity);
                var forward = transform.Forward;
                var angle = Math.Clamp(light.OuterConeAngle, 1.0f, 80.0f) * MathF.PI / 180.0f;
                var center = transform.WorldPosition + forward * (light.Range * MathF.Cos(angle));
                var right = Vector3.Normalize(Vector3.Cross(MathF.Abs(forward.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY, forward));
                var up = Vector3.Cross(forward, right);
                DebugDraw.Circle(center, right, up, light.Range * MathF.Sin(angle), color(light.Color));
                DebugDraw.Line(transform.WorldPosition, center + right * light.Range * MathF.Sin(angle), color(light.Color));
                DebugDraw.Line(transform.WorldPosition, center - right * light.Range * MathF.Sin(angle), color(light.Color));
            }
        }
    }

    public override void OnOptionsGUI() => ImGui.Checkbox("Spot lights", ref _spots);

    private static uint color(Vector3 c) => DebugDraw.Color(c.X, c.Y, c.Z, 0.8f);
}
