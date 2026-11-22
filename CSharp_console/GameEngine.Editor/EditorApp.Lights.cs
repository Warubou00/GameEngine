using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// v2.2 : ライト (GameObject > Light / シーンビューの印 / 選んだライトの範囲)
//   作る : Directional / Point / Spot (エディターのカメラの前 5 m。スポットは真下を向ける)
//   印   : 全部のライトの位置に小さな印 (ライトの色の丸 + 種類の線)。クリックで選べる (ライトには形が無いので、物の選び方では選べない)
//   範囲 : 選んだ点光源は届く距離の球 (3 つの輪) / スポットは円錐 (外側 + 内側) / 平行光源は光の向きの矢印
// ==========================================================================
internal sealed partial class EditorApp
{
    private readonly List<(Entity Entity, Vector2 Screen)> _lightIcons = new();
    private const float LightIconRadius = 9.0f;
    public int LightIconCount => _lightIcons.Count;   // 確認用 (最後に描いたライトの印の数)

    public enum LightKind { Directional, Point, Spot }

    /// <summary>ライトを作る (v2.2。エディターのカメラの前 5 m)</summary>
    public Entity CreateLight(LightKind kind)
    {
        var forward  = Vector3.Transform(Vector3.UnitZ, cameraRotation);
        var position = _cameraPosition + forward * 5.0f;
        Entity entity;
        switch (kind)
        {
            case LightKind.Directional:
                entity = World.CreateEntity("Directional Light");
                World.Add(entity, Transform.LookingAt(position, position + new Vector3(0.4f, -1.0f, 0.6f)));
                World.Add(entity, new DirectionalLight());
                break;
            case LightKind.Point:
                entity = World.CreateEntity("Point Light");
                World.Add(entity, new Transform(position));
                World.Add(entity, new PointLight { Intensity = 5.0f });
                break;
            default:
                entity = World.CreateEntity("Spot Light");
                // +Z を X 軸で 90 度回すと真下 (-Y)
                World.Add(entity, new Transform(position + new Vector3(0, 2, 0)) { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI * 0.5f) });
                World.Add(entity, new SpotLight { Intensity = 10.0f });
                break;
        }
        Selected = entity;
        _undo.Record(new EntitiesEdit($"Create {World.Get<EntityInfo>(entity).Name}", SceneSerializer.Save(World, new[] { entity }), new List<Guid> { EditorWorld.IdOf(World, entity) }, create: true));
        return entity;
    }

    // GameObject > Light
    private void drawLightMenu()
    {
        if (!ImGui.BeginMenu("Light")) return;
        if (ImGui.MenuItem("Directional Light")) CreateLight(LightKind.Directional);
        if (ImGui.MenuItem("Point Light"))       CreateLight(LightKind.Point);
        if (ImGui.MenuItem("Spot Light"))        CreateLight(LightKind.Spot);
        ImGui.EndMenu();
    }

    // 全部のライトの印 (クリックで選べるように、描いた位置を覚える)
    private void drawLightIcons(SceneCamera camera, ImDrawListPtr draw)
    {
        _lightIcons.Clear();
        void icons<T>(int kind) where T : struct, IComponent
        {
            var pool = World.GetPool<T>();
            var entities = pool.Entities;
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!World.Has<Transform>(entity)) continue;
                var matrix = Hierarchy.GetWorldMatrix(World, entity);
                if (!camera.Project(matrix.Translation, out var screen)) continue;
                var color = kind switch
                {
                    0 => World.Get<DirectionalLight>(entity).Color,
                    1 => World.Get<PointLight>(entity).Color,
                    _ => World.Get<SpotLight>(entity).Color,
                };
                var active = World.IsActive(entity);
                var rgb = ImGui.GetColorU32(new Vector4(Math.Clamp(color.X, 0, 1), Math.Clamp(color.Y, 0, 1), Math.Clamp(color.Z, 0, 1), active ? 1.0f : 0.4f));
                var outline = entity == _selected ? 0xFF30A0FFu : (active ? 0xFF202020u : 0x80202020u);
                draw.AddCircleFilled(screen, LightIconRadius, outline);
                draw.AddCircleFilled(screen, LightIconRadius - 2.0f, rgb);
                // 種類 : 平行光源 = 光の向きの短い線 / 点光源 = 8 方向の短い線 / スポット = 向きの円錐の 2 本
                var forward = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, matrix));
                if (kind == 1)
                {
                    for (int r = 0; r < 8; r++)
                    {
                        var a = r * MathF.Tau / 8;
                        var d = new Vector2(MathF.Cos(a), MathF.Sin(a));
                        draw.AddLine(screen + d * (LightIconRadius + 2), screen + d * (LightIconRadius + 6), rgb, 2.0f);
                    }
                }
                else if (camera.Project(matrix.Translation + forward * 0.5f, out var tip))
                {
                    var dir = tip - screen;
                    if (dir.LengthSquared() > 1e-4f)
                    {
                        dir = Vector2.Normalize(dir);
                        var side = new Vector2(-dir.Y, dir.X);
                        if (kind == 0) draw.AddLine(screen + dir * LightIconRadius, screen + dir * (LightIconRadius + 12), rgb, 2.0f);
                        else
                        {
                            draw.AddLine(screen + dir * LightIconRadius, screen + (dir * 2.2f + side) * LightIconRadius, rgb, 2.0f);
                            draw.AddLine(screen + dir * LightIconRadius, screen + (dir * 2.2f - side) * LightIconRadius, rgb, 2.0f);
                        }
                    }
                }
                _lightIcons.Add((entity, screen));
            }
        }
        icons<DirectionalLight>(0);
        icons<PointLight>(1);
        icons<SpotLight>(2);
    }

    // クリックした所にライトの印があれば、そのライト (無ければ Null)
    private Entity pickLightIcon(Vector2 mouse)
    {
        var best = Entity.Null;
        var bestDistance = LightIconRadius + 3.0f;
        foreach (var (entity, screen) in _lightIcons)
        {
            var distance = Vector2.Distance(mouse, screen);
            if (distance < bestDistance) { bestDistance = distance; best = entity; }
        }
        return best;
    }

    // 選んだライトの範囲
    private void drawLightRange(SceneCamera camera, ImDrawListPtr draw)
    {
        if (!World.IsAlive(_selected) || !World.Has<Transform>(_selected)) return;
        var matrix   = Hierarchy.GetWorldMatrix(World, _selected);
        var position = matrix.Translation;
        var forward  = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, matrix));
        var right    = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, matrix));
        var up       = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, matrix));
        const uint color = 0xFF40E0FF, dim = 0x9040E0FF;   // 黄色 / 薄い黄色

        void line(Vector3 a, Vector3 b, uint c)
        {
            if (camera.Project(a, out var pa) && camera.Project(b, out var pb)) draw.AddLine(pa, pb, c, 1.5f);
        }
        void circle(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, uint c)
        {
            Vector2 previous = default;
            var hasPrevious = false;
            for (int i = 0; i <= 48; i++)
            {
                var a = i * MathF.Tau / 48;
                var ok = camera.Project(center + (axisA * MathF.Cos(a) + axisB * MathF.Sin(a)) * radius, out var point);
                if (ok && hasPrevious) draw.AddLine(previous, point, c, 1.5f);
                previous = point;
                hasPrevious = ok;
            }
        }

        if (World.Has<PointLight>(_selected))
        {
            var light = World.Get<PointLight>(_selected);
            circle(position, Vector3.UnitY, Vector3.UnitZ, light.Range, color);
            circle(position, Vector3.UnitX, Vector3.UnitZ, light.Range, color);
            circle(position, Vector3.UnitX, Vector3.UnitY, light.Range, color);
            if (light.SourceRadius > 0.0f) circle(position, right, up, light.SourceRadius, dim);
        }
        if (World.Has<SpotLight>(_selected))
        {
            var light = World.Get<SpotLight>(_selected);
            void cone(float angleDegrees, uint c, bool edges)
            {
                var angle  = Math.Clamp(angleDegrees, 0.0f, 80.0f) * MathEx.Deg2Rad;
                var center = position + forward * (light.Range * MathF.Cos(angle));
                var radius = light.Range * MathF.Sin(angle);
                circle(center, right, up, radius, c);
                if (!edges) return;
                for (int i = 0; i < 4; i++)
                {
                    var a = i * MathF.Tau / 4;
                    line(position, center + (right * MathF.Cos(a) + up * MathF.Sin(a)) * radius, c);
                }
                line(position, position + forward * light.Range, dim);
            }
            cone(light.OuterConeAngle, color, true);
            if (light.InnerConeAngle > 0.0f) cone(light.InnerConeAngle, dim, false);
        }
        if (World.Has<DirectionalLight>(_selected))
        {
            // 光の向きの矢印 (2 m)
            var tip = position + forward * 2.0f;
            line(position, tip, color);
            line(tip, tip - forward * 0.4f + right * 0.2f, color);
            line(tip, tip - forward * 0.4f - right * 0.2f, color);
        }
    }
}
