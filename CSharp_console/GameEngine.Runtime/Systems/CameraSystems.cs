using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// このフレームに描くカメラ 1 台 (CameraSystem が Depth の順に並べる。v1.5)
internal struct CameraFrame
{
    public Entity  Entity;     // エディターのカメラの時は、設定を借りたシーンのカメラ (無ければ Entity.Null)
    public Camera  Camera;     // 部品の値 (Volume を混ぜた後の値は RenderSettingsSystem が Graphics へ)
    public Vector3 Position;   // ワールドの位置 (Volume の効きを決めるのに使う)
}

// ==========================================================================
// カメラを C++ へ送る (エンジン組み込み)
//   v1.5 : 有効な Camera + Transform を全部 (最大 8 台)、Depth の小さい順に (同じ Depth なら見つかった順)
//     (以前は最初に見つかったカメラ 1 台だけ)
//     範囲 (ViewportRect) が画面全体でないカメラは、範囲の縦横の比で投影を作る (Aspect)
//   エディターのカメラ (CameraOverride) がある時は 1 台だけ。描き方の設定は一番目のシーンのカメラの物を使う
//   前のフレームと同じなら送らない (Projection はリサイズ時に C++ 側で作り直される)
// ==========================================================================
internal sealed class CameraSystem : ISystem
{
    private readonly NativeMethods.CameraDesc[] _descs = new NativeMethods.CameraDesc[NativeMethods.MaxCameras];
    private readonly NativeMethods.CameraDesc[] _last  = new NativeMethods.CameraDesc[NativeMethods.MaxCameras];
    private int  _lastCount;
    private bool _sent;
    private readonly List<(int Depth, int Order)> _order = new();

    // このフレームに描くカメラ (Depth の順。0 番がメインのカメラ)
    public List<CameraFrame> Cameras { get; } = new();

    // メインのカメラの位置 (Volume / 音を聞く位置。ED6)。カメラが無ければ最後に送った位置
    public Vector3 Position => _last[0].Position;

    public void Update(World world) => Update(world, null, (0, 0));

    // cameraOverride : エディターのカメラ (あればシーンのカメラの代わりに使う。ED3)
    // screen : シーンを描く大きさ (範囲の比を求める)
    public void Update(World world, CameraOverride? cameraOverride, (int Width, int Height) screen)
    {
        Cameras.Clear();
        var cameras    = world.GetPool<Camera>();
        var transforms = world.GetPool<Transform>();
        var entities   = cameras.Entities;

        // 有効なカメラを Depth の順に
        _order.Clear();
        for (int i = 0; i < entities.Length; i++)
        {
            if (!transforms.Has(entities[i].Id) || !world.IsActive(entities[i])) continue;   // 無効なカメラは使わない (ED2)
            _order.Add((cameras.Components[i].Depth, i));
        }
        _order.Sort((a, b) => a.Depth != b.Depth ? a.Depth.CompareTo(b.Depth) : a.Order.CompareTo(b.Order));

        if (cameraOverride is { } editor)
        {
            // エディターのカメラ : 画面全体に 1 台。描き方は一番目のシーンのカメラ (無ければ既定の値)
            var settings = _order.Count > 0 ? cameras.Components[_order[0].Order] : new Camera();
            settings.ViewportRect = new Vector4(0.0f, 0.0f, 1.0f, 1.0f);
            settings.CullingMask  = 0xFFFFFFFFu;   // エディターでは全部のレイヤーを見せる
            if (settings.ClearFlags == CameraClearFlags.DepthOnly) settings.ClearFlags = CameraClearFlags.Skybox;
            Cameras.Add(new CameraFrame { Entity = _order.Count > 0 ? entities[_order[0].Order] : Entity.Null, Camera = settings, Position = editor.Position });
            _descs[0] = new NativeMethods.CameraDesc
            {
                Position     = editor.Position,
                Forward      = Vector3.Transform(Vector3.UnitZ, editor.Rotation),
                Up           = Vector3.Transform(Vector3.UnitY, editor.Rotation),
                FieldOfViewY = editor.FieldOfView * MathEx.Deg2Rad,
                NearZ        = editor.NearClip,
                FarZ         = editor.FarClip,
                CullingMask  = 0xFFFFFFFFu,
            };
            send(1);
            return;
        }

        var count = Math.Min(_order.Count, NativeMethods.MaxCameras);
        for (int c = 0; c < count; c++)
        {
            var index = _order[c].Order;
            ref var camera    = ref cameras.Components[index];
            ref var transform = ref transforms.Get(entities[index].Id);
            var position = transform.WorldPosition;   // 以前はローカル (Position)。親が無ければ同じ
            Cameras.Add(new CameraFrame { Entity = entities[index], Camera = camera, Position = position });
            _descs[c] = new NativeMethods.CameraDesc
            {
                Position     = position,
                Forward      = transform.Forward,
                Up           = transform.Up,
                FieldOfViewY = camera.FieldOfView * MathEx.Deg2Rad,
                NearZ        = camera.NearClip,
                FarZ         = camera.FarClip,
                Orthographic = camera.Orthographic ? 1u : 0u,
                Aspect       = AspectOf(camera.ViewportRect, screen),
                CullingMask  = camera.CullingMask,
            };
        }
        if (count > 0) send(count);
    }

    // 範囲の縦横の比 (画面全体なら 0 = C++ が画面の比を使う。リサイズしても送り直さなくてよい)
    internal static float AspectOf(Vector4 rect, (int Width, int Height) screen)
    {
        var r = ClampRect(rect);
        if (IsFullRect(r) || screen.Width <= 0 || screen.Height <= 0) return 0.0f;
        return (r.Z * screen.Width) / Math.Max(r.W * screen.Height, 1.0f);
    }

    // 範囲を画面の中 (0〜1) に収める (幅 / 高さは最低 1%)
    internal static Vector4 ClampRect(Vector4 rect)
    {
        var x = Math.Clamp(rect.X, 0.0f, 0.99f);
        var y = Math.Clamp(rect.Y, 0.0f, 0.99f);
        var w = Math.Clamp(rect.Z, 0.01f, 1.0f - x);
        var h = Math.Clamp(rect.W, 0.01f, 1.0f - y);
        return new Vector4(x, y, w, h);
    }

    internal static bool IsFullRect(Vector4 r) => r.X <= 0.0f && r.Y <= 0.0f && r.Z >= 1.0f && r.W >= 1.0f;

    private unsafe void send(int count)
    {
        if (_sent && count == _lastCount)
        {
            var same = true;
            for (int i = 0; i < count && same; i++) same = _descs[i].Equals(_last[i]);
            if (same) return;
        }
        fixed (NativeMethods.CameraDesc* p = _descs) NativeMethods.Renderer_SetCameras(p, (uint)count);
        Array.Copy(_descs, _last, count);
        _lastCount = count;
        _sent = true;
    }
}

// ==========================================================================
// FreeCamera を動かす (右ボタン + マウスで向き、WASD / E Q で移動、Shift で速く。ゲームパッドでも動かせる)
// ==========================================================================
public sealed class FreeCameraSystem : ISystem
{
    public void Update(World world)
    {
        world.ForEach(static (Entity _, ref Transform transform, ref FreeCamera camera) =>
        {
            // 最初は今の向きから角度を求める (LookingAt で置いた向きを引き継ぐ)
            // CreateFromYawPitchRoll の前方向は (cos p sin y, -sin p, cos p cos y)
            if (!camera.Initialized)
            {
                var f = transform.Forward;
                camera.Yaw         = MathF.Atan2(f.X, f.Z) * MathEx.Rad2Deg;
                camera.Pitch       = -MathF.Asin(Math.Clamp(f.Y, -1.0f, 1.0f)) * MathEx.Rad2Deg;
                camera.Initialized = true;
            }

            if (Input.GetMouseButton(MouseButtons.Right))
            {
                var delta = Input.MouseDelta * camera.LookSensitivity;
                camera.Yaw  += delta.X;
                camera.Pitch = Math.Clamp(camera.Pitch + delta.Y, -89.0f, 89.0f);
            }
            // ゲームパッド (1 台目) : 右スティックで向き、左スティックで移動、RT / LT で上下、RB (R1) で速く
            var look = Gamepad.GetRightStick();
            if (look != Vector2.Zero)
            {
                var degrees = camera.GamepadLookSpeed * Time.DeltaTime;
                camera.Yaw  += look.X * degrees;
                camera.Pitch = Math.Clamp(camera.Pitch - look.Y * degrees, -89.0f, 89.0f);
            }
            transform.Rotation = Quaternion.CreateFromYawPitchRoll(camera.Yaw * MathEx.Deg2Rad, camera.Pitch * MathEx.Deg2Rad, 0.0f);

            var stick = Gamepad.GetLeftStick();
            var move  = new Vector3(stick.X, Gamepad.GetAxis(GamepadAxis.RightTrigger) - Gamepad.GetAxis(GamepadAxis.LeftTrigger), stick.Y);
            if (Input.GetKey(Keys.W)) move.Z += 1;
            if (Input.GetKey(Keys.S)) move.Z -= 1;
            if (Input.GetKey(Keys.D)) move.X += 1;
            if (Input.GetKey(Keys.A)) move.X -= 1;
            if (Input.GetKey(Keys.E)) move.Y += 1;
            if (Input.GetKey(Keys.Q)) move.Y -= 1;
            if (move != Vector3.Zero)
            {
                // キーとスティックを両方使った時に速くなりすぎないよう、長さは 1 まで
                // (以前はキーだけで、W + D の斜めは 1.41 倍の速さだった)
                if (move.LengthSquared() > 1.0f) move = Vector3.Normalize(move);
                var fast  = Input.GetKey(Keys.ShiftKey) || Gamepad.GetButton(GamepadButton.RightShoulder);
                var speed = camera.MoveSpeed * (fast ? camera.FastMultiplier : 1.0f) * Time.DeltaTime;
                transform.Position += (transform.Right * move.X + Vector3.UnitY * move.Y + transform.Forward * move.Z) * speed;
            }
        });
    }
}

// ==========================================================================
// OrbitCamera を動かす (注視点の周りを一定の速さで回り続ける)
// ==========================================================================
public sealed class OrbitCameraSystem : ISystem
{
    public void Update(World world)
    {
        world.ForEach(static (Entity _, ref Transform transform, ref OrbitCamera orbit) =>
        {
            orbit.Angle = (orbit.Angle + orbit.DegreesPerSecond * Time.DeltaTime) % 360.0f;
            var radians = orbit.Angle * MathEx.Deg2Rad;
            transform.Position = orbit.Target + new Vector3(MathF.Sin(radians) * orbit.Radius, orbit.Height, -MathF.Cos(radians) * orbit.Radius);
            transform.Rotation = MathEx.LookRotation(orbit.Target - transform.Position, Vector3.UnitY);
        });
    }
}
