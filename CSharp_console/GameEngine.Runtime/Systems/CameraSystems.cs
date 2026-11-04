using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// カメラを C++ へ送る (エンジン組み込み)。最初に見つかった Camera + Transform を使う
// 前のフレームと同じなら送らない (Projection はリサイズ時に C++ 側で作り直される)
// ==========================================================================
internal sealed class CameraSystem : ISystem
{
    private NativeMethods.CameraDesc _last;
    private bool _sent;

    public void Update(World world)
    {
        var cameras    = world.GetPool<Camera>();
        var transforms = world.GetPool<Transform>();
        var entities   = cameras.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            ref var transform = ref transforms.TryGet(entities[i].Id, out var found);
            if (!found || !world.IsActive(entities[i])) continue;   // 無効なカメラは使わない (ED2)
            ref var camera = ref cameras.Components[i];

            var desc = new NativeMethods.CameraDesc
            {
                Position     = transform.WorldPosition,   // 以前はローカル (Position)。親が無ければ同じ
                Forward      = transform.Forward,
                Up           = transform.Up,
                FieldOfViewY = camera.FieldOfView * MathEx.Deg2Rad,
                NearZ        = camera.NearClip,
                FarZ         = camera.FarClip,
                Orthographic = camera.Orthographic ? 1u : 0u,
            };
            if (!_sent || !desc.Equals(_last))
            {
                NativeMethods.Renderer_SetCamera(ref desc);
                _last = desc;
                _sent = true;
            }
            return;   // 今は 1 台だけ
        }
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
