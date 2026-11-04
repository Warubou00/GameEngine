using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// シーンビューの道具 (ED4)
//   SceneCamera : エディターのカメラの向き / 視野から、ワールドの点 → シーンビューの点、マウスの点 → 光線 を計算する
//   Picker      : クリックした物を選ぶ。モデルの三角形に光線を当てる (形は C++ から 1 回だけ写して覚える) +
//                 物理の当たり (Collider だけの物)。近い方を選ぶ
//   Gizmo       : 移動 / 回転 / 拡大縮小のハンドル (ImGui の線で描く)。ローカル / ワールド、スナップ
// ==========================================================================
internal readonly struct SceneCamera
{
    public readonly Vector3    Position;
    public readonly Quaternion Rotation;
    public readonly Vector2    Min, Size;      // シーンビューの位置と大きさ (ImGui の座標 = 窓の中の座標)
    private readonly float     _tanHalf, _aspect;

    public SceneCamera(Vector3 position, Quaternion rotation, float fieldOfViewDegrees, Vector2 min, Vector2 size)
    {
        Position = position; Rotation = rotation; Min = min; Size = size;
        _tanHalf = MathF.Tan(fieldOfViewDegrees * MathEx.Deg2Rad * 0.5f);
        _aspect  = size.X / Math.Max(size.Y, 1.0f);
    }

    public Vector3 Forward => Vector3.Transform(Vector3.UnitZ, Rotation);

    // ワールドの点 → シーンビューの点 (カメラの後ろなら false)
    public bool Project(Vector3 world, out Vector2 screen)
    {
        var v = Vector3.Transform(world - Position, Quaternion.Conjugate(Rotation));
        screen = default;
        if (v.Z < 0.01f) return false;
        var x = v.X / (v.Z * _tanHalf * _aspect);
        var y = v.Y / (v.Z * _tanHalf);
        screen = Min + new Vector2((x * 0.5f + 0.5f) * Size.X, (0.5f - y * 0.5f) * Size.Y);
        return true;
    }

    // シーンビューの点 → 光線の向き (正規化済み)
    public Vector3 RayDirection(Vector2 screen)
    {
        var p = (screen - Min) / Size;
        var v = new Vector3((p.X * 2.0f - 1.0f) * _tanHalf * _aspect, (1.0f - p.Y * 2.0f) * _tanHalf, 1.0f);
        return Vector3.Normalize(Vector3.Transform(v, Rotation));
    }

    // 画面でだいたい同じ大きさに見える長さ (ギズモの大きさ)
    public float WorldSize(Vector3 at, float fraction) =>
        Math.Max(Vector3.Dot(at - Position, Forward), 0.05f) * _tanHalf * 2.0f * fraction;
}

internal sealed class Picker
{
    private sealed class Shape
    {
        public float[] Positions = Array.Empty<float>();
        public uint[]  Indices   = Array.Empty<uint>();
        public Vector3 Min, Max;
    }

    private readonly Engine _engine;
    private readonly Dictionary<string, Shape?> _shapes = new();
    public Picker(Engine engine) => _engine = engine;

    // 覚えた形を捨てる (モデルを読み直した / 解放した時。ED5)
    public void Clear() => _shapes.Clear();

    // モデルの形 (ローカルの箱)。形が無ければ false
    public bool TryGetBounds(Model model, out Vector3 min, out Vector3 max)
    {
        var shape = shapeOf(model);
        min = shape?.Min ?? default;
        max = shape?.Max ?? default;
        return shape != null;
    }

    private Shape? shapeOf(Model model)
    {
        if (!model.IsValid) return null;
        if (_shapes.TryGetValue(model.Path, out var cached)) return cached;
        Shape? shape = null;
        if (_engine.Assets.GetModelGeometry(model, out var positions, out var indices))
        {
            shape = new Shape { Positions = positions, Indices = indices, Min = new Vector3(float.MaxValue), Max = new Vector3(float.MinValue) };
            for (int i = 0; i + 2 < positions.Length; i += 3)
            {
                var p = new Vector3(positions[i], positions[i + 1], positions[i + 2]);
                shape.Min = Vector3.Min(shape.Min, p);
                shape.Max = Vector3.Max(shape.Max, p);
            }
        }
        _shapes[model.Path] = shape;
        return shape;
    }

    // 光線に一番近く当たった物 (無ければ Entity.Null)
    public Entity Pick(Vector3 origin, Vector3 direction, out float distance)
    {
        var world = _engine.World;
        var best = Entity.Null;
        distance = float.MaxValue;

        var renderers = world.GetPool<MeshRenderer>();
        var transforms = world.GetPool<Transform>();
        var entities = renderers.Entities;
        var components = renderers.Components;
        for (int i = 0; i < components.Length; i++)
        {
            var entity = entities[i];
            if (!world.IsActive(entity) || !transforms.Has(entity.Id)) continue;
            var shape = shapeOf(components[i].Model);
            if (shape == null) continue;
            // 光線をモデルのローカルへ移す (向きは正規化しないので、距離 t はワールドと同じ)
            var matrix = Hierarchy.GetWorldMatrix(world, entity);
            if (!Matrix4x4.Invert(matrix, out var inverse)) continue;
            var o = Vector3.Transform(origin, inverse);
            var d = Vector3.TransformNormal(direction, inverse);
            if (!rayBox(o, d, shape.Min, shape.Max, out var boxT) || boxT > distance) continue;
            var t = rayTriangles(o, d, shape, distance);
            if (t < distance) { distance = t; best = entity; }
        }

        // 物理の当たり (形の無い Collider だけの物も選べるように)
        if (_engine.Physics.Raycast(origin, direction, out var hit, Math.Min(distance, 10000.0f)) && hit.Distance < distance && world.IsAlive(hit.Entity))
        {
            distance = hit.Distance;
            best = hit.Entity;
        }
        return best;
    }

    private static bool rayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t)
    {
        float near = 0.0f, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float oa = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
            float da = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float mn = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float mx = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (MathF.Abs(da) < 1e-9f) { if (oa < mn || oa > mx) { t = 0; return false; } continue; }
            var t0 = (mn - oa) / da;
            var t1 = (mx - oa) / da;
            if (t0 > t1) (t0, t1) = (t1, t0);
            near = Math.Max(near, t0);
            far  = Math.Min(far, t1);
            if (near > far) { t = 0; return false; }
        }
        t = near;
        return true;
    }

    // 三角形と光線 (Möller–Trumbore。裏も当てる)
    private static float rayTriangles(Vector3 o, Vector3 d, Shape shape, float best)
    {
        var p = shape.Positions;
        var idx = shape.Indices;
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            int a = (int)idx[i] * 3, b = (int)idx[i + 1] * 3, c = (int)idx[i + 2] * 3;
            var v0 = new Vector3(p[a], p[a + 1], p[a + 2]);
            var e1 = new Vector3(p[b], p[b + 1], p[b + 2]) - v0;
            var e2 = new Vector3(p[c], p[c + 1], p[c + 2]) - v0;
            var h = Vector3.Cross(d, e2);
            var det = Vector3.Dot(e1, h);
            if (MathF.Abs(det) < 1e-12f) continue;
            var inv = 1.0f / det;
            var s = o - v0;
            var u = Vector3.Dot(s, h) * inv;
            if (u < 0.0f || u > 1.0f) continue;
            var q = Vector3.Cross(s, e1);
            var v = Vector3.Dot(d, q) * inv;
            if (v < 0.0f || u + v > 1.0f) continue;
            var t = Vector3.Dot(e2, q) * inv;
            if (t > 0.0f && t < best) best = t;
        }
        return best;
    }
}

internal enum GizmoMode { Translate, Rotate, Scale }

internal sealed class Gizmo
{
    public GizmoMode Mode = GizmoMode.Translate;
    public bool  Local;                 // 軸をこの物の向きに合わせる (拡大縮小はいつもローカル)
    public bool  Snap;                  // Ctrl を押している間もスナップ
    public float TranslateSnap = 0.5f, RotateSnap = 15.0f, ScaleSnap = 0.1f;

    public bool Dragging => _axis >= 0;
    public bool Hovered  => _hover >= 0;

    private const float Pick = 7.0f;    // ハンドルに当たる距離 (ピクセル)
    private int _hover = -1, _axis = -1;   // 0..2 = X Y Z、3 = 真ん中 (移動 : 画面の面 / 拡大縮小 : 全体)
    private Vector3 _startWorldPosition, _startScale, _grabPoint, _dragAxis;   // _dragAxis : 掴んだ時の軸 (回すと軸も回るので)
    private Quaternion _startWorldRotation;
    private float _startT;
    private Vector2 _startMouse;

    private static readonly uint[] Colors = { 0xFF4040E8, 0xFF40D040, 0xFFE87040, 0xFFE0E0E0 };   // ABGR : 赤 / 緑 / 青 / 白
    private const uint HoverColor = 0xFF20E0FF;

    // 1 フレーム分 : 描いて、ドラッグしていれば新しい Transform (ローカル) を返す
    public bool Update(SceneCamera camera, ImDrawListPtr draw, World world, Entity entity, bool mouseInView, out Transform result)
    {
        result = world.Get<Transform>(entity);
        var worldMatrix = Hierarchy.GetWorldMatrix(world, entity);
        Matrix4x4.Decompose(worldMatrix, out _, out var worldRotation, out var pivot);
        if (!camera.Project(pivot, out var center)) { _axis = -1; return false; }
        var size = camera.WorldSize(pivot, 0.12f);
        var axisRotation = Mode == GizmoMode.Scale || Local ? worldRotation : Quaternion.Identity;
        Span<Vector3> axes = stackalloc Vector3[3];
        axes[0] = Vector3.Transform(Vector3.UnitX, axisRotation);
        axes[1] = Vector3.Transform(Vector3.UnitY, axisRotation);
        axes[2] = Vector3.Transform(Vector3.UnitZ, axisRotation);

        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        var snap = Snap || io.KeyCtrl;

        // ---------- 当たり (ドラッグしていない時) ----------
        if (!Dragging)
        {
            _hover = -1;
            if (mouseInView)
            {
                var best = Pick;
                for (int a = 0; a < 3; a++)
                {
                    float distance;
                    if (Mode == GizmoMode.Rotate) distance = ringDistance(camera, pivot, axes[a], size, mouse);
                    else if (camera.Project(pivot + axes[a] * size, out var tip)) distance = segmentDistance(mouse, center, tip);
                    else continue;
                    if (distance < best) { best = distance; _hover = a; }
                }
                if (Mode != GizmoMode.Rotate && Vector2.Distance(mouse, center) < Pick + 2.0f) _hover = 3;
            }
            if (_hover >= 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _axis = _hover;
                _startWorldPosition = pivot;
                _startWorldRotation = worldRotation;
                _startScale = result.Scale;
                _startMouse = mouse;
                if (_axis < 3) _dragAxis = axes[_axis];
                var ray = camera.RayDirection(mouse);
                if (_axis < 3) _startT = axisParameter(pivot, axes[_axis], camera.Position, ray);
                else _grabPoint = planeHit(pivot, camera.Forward, camera.Position, ray);
            }
        }

        // ---------- ドラッグ ----------
        var changed = false;
        if (Dragging)
        {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                _axis = -1;
            }
            else
            {
                var ray = camera.RayDirection(mouse);
                Matrix4x4 parentMatrix = Matrix4x4.Identity;
                var hasParent = !result.Parent.IsNull && world.IsAlive(result.Parent);
                if (hasParent) parentMatrix = Hierarchy.GetWorldMatrix(world, result.Parent);
                switch (Mode)
                {
                case GizmoMode.Translate:
                {
                    Vector3 delta;
                    if (_axis < 3)
                    {
                        var t = axisParameter(_startWorldPosition, axes[_axis], camera.Position, ray) - _startT;
                        if (snap) t = MathF.Round(t / TranslateSnap) * TranslateSnap;
                        delta = axes[_axis] * t;
                    }
                    else
                    {
                        delta = planeHit(_startWorldPosition, camera.Forward, camera.Position, ray) - _grabPoint;
                        if (snap) delta = new Vector3(MathF.Round(delta.X / TranslateSnap), MathF.Round(delta.Y / TranslateSnap), MathF.Round(delta.Z / TranslateSnap)) * TranslateSnap;
                    }
                    var position = _startWorldPosition + delta;
                    if (hasParent && Matrix4x4.Invert(parentMatrix, out var inverse)) position = Vector3.Transform(position, inverse);
                    result.Position = position;
                    break;
                }
                case GizmoMode.Rotate:
                {
                    // 画面の上で、中心の周りを回った角度 (軸がこちらを向いているかで向きを変える)
                    var a0 = MathF.Atan2(_startMouse.Y - center.Y, _startMouse.X - center.X);
                    var a1 = MathF.Atan2(mouse.Y - center.Y, mouse.X - center.X);
                    var angle = a1 - a0;
                    if (Vector3.Dot(_dragAxis, camera.Forward) > 0.0f) angle = -angle;
                    if (snap) angle = MathF.Round(angle * MathEx.Rad2Deg / RotateSnap) * RotateSnap * MathEx.Deg2Rad;
                    var rotation = Quaternion.Normalize(Quaternion.Concatenate(_startWorldRotation, Quaternion.CreateFromAxisAngle(_dragAxis, angle)));
                    if (hasParent)
                    {
                        Matrix4x4.Decompose(parentMatrix, out _, out var parentRotation, out _);
                        rotation = Quaternion.Normalize(Quaternion.Concatenate(rotation, Quaternion.Inverse(parentRotation)));
                    }
                    result.Rotation = rotation;
                    break;
                }
                case GizmoMode.Scale:
                {
                    var scale = _startScale;
                    if (_axis < 3)
                    {
                        var factor = Math.Max(axisParameter(_startWorldPosition, axes[_axis], camera.Position, ray) / Math.Max(_startT, 1e-4f), 0.01f);
                        if (_axis == 0) scale.X *= factor; else if (_axis == 1) scale.Y *= factor; else scale.Z *= factor;
                    }
                    else
                    {
                        scale *= Math.Max(1.0f + (mouse.X - _startMouse.X) / 100.0f, 0.01f);
                    }
                    if (snap) scale = new Vector3(snapScale(scale.X), snapScale(scale.Y), snapScale(scale.Z));
                    result.Scale = scale;
                    break;
                }
                }
                changed = true;
            }
        }

        // ---------- 描く ----------
        var active = Dragging ? _axis : _hover;
        for (int a = 0; a < 3; a++)
        {
            var color = a == active ? HoverColor : Colors[a];
            if (Mode == GizmoMode.Rotate)
            {
                drawRing(draw, camera, pivot, axes[a], size, color);
                continue;
            }
            if (!camera.Project(pivot + axes[a] * size, out var tip)) continue;
            draw.AddLine(center, tip, color, 3.0f);
            if (Mode == GizmoMode.Translate)
            {
                var dir = Vector2.Normalize(tip - center + new Vector2(1e-4f));
                var side = new Vector2(-dir.Y, dir.X) * 5.0f;
                draw.AddTriangleFilled(tip + dir * 12.0f, tip + side, tip - side, color);
            }
            else
            {
                draw.AddRectFilled(tip - new Vector2(5), tip + new Vector2(5), color);
            }
        }
        if (Mode != GizmoMode.Rotate)
            draw.AddRect(center - new Vector2(5), center + new Vector2(5), active == 3 ? HoverColor : Colors[3], 0, ImDrawFlags.None, 2.0f);
        return changed;
    }

    private float snapScale(float v)
    {
        var snapped = MathF.Round(v / ScaleSnap) * ScaleSnap;
        return snapped != 0.0f ? snapped : v;
    }

    // 軸の直線 (p + a t) とマウスの光線の一番近い所の t
    private static float axisParameter(Vector3 p, Vector3 a, Vector3 origin, Vector3 ray)
    {
        var w0 = p - origin;
        float b = Vector3.Dot(a, ray), d = Vector3.Dot(a, w0), e = Vector3.Dot(ray, w0);
        var denominator = 1.0f - b * b;   // |a| = |ray| = 1
        if (MathF.Abs(denominator) < 1e-5f) return 0.0f;
        return (b * e - d) / denominator;
    }

    private static Vector3 planeHit(Vector3 point, Vector3 normal, Vector3 origin, Vector3 ray)
    {
        var denominator = Vector3.Dot(normal, ray);
        if (MathF.Abs(denominator) < 1e-6f) return point;
        return origin + ray * (Vector3.Dot(point - origin, normal) / denominator);
    }

    private static float segmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        var t = Math.Clamp(Vector2.Dot(p - a, ab) / Math.Max(ab.LengthSquared(), 1e-6f), 0.0f, 1.0f);
        return Vector2.Distance(p, a + ab * t);
    }

    private const int RingSegments = 48;

    private static void ringBasis(Vector3 axis, out Vector3 u, out Vector3 v)
    {
        u = Vector3.Normalize(Vector3.Cross(axis, MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        v = Vector3.Cross(axis, u);
    }

    private static float ringDistance(SceneCamera camera, Vector3 pivot, Vector3 axis, float radius, Vector2 mouse)
    {
        ringBasis(axis, out var u, out var v);
        var best = float.MaxValue;
        Vector2 previous = default;
        var hasPrevious = false;
        for (int i = 0; i <= RingSegments; i++)
        {
            var angle = i * MathF.Tau / RingSegments;
            var ok = camera.Project(pivot + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius, out var point);
            if (ok && hasPrevious) best = Math.Min(best, segmentDistance(mouse, previous, point));
            previous = point;
            hasPrevious = ok;
        }
        return best;
    }

    private static void drawRing(ImDrawListPtr draw, SceneCamera camera, Vector3 pivot, Vector3 axis, float radius, uint color)
    {
        ringBasis(axis, out var u, out var v);
        Vector2 previous = default;
        var hasPrevious = false;
        for (int i = 0; i <= RingSegments; i++)
        {
            var angle = i * MathF.Tau / RingSegments;
            var ok = camera.Project(pivot + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius, out var point);
            if (ok && hasPrevious) draw.AddLine(previous, point, color, 2.5f);
            previous = point;
            hasPrevious = ok;
        }
    }
}
