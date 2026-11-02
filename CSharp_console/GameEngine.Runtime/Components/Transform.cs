using System.Numerics;

namespace GameEngine;

// ==========================================================================
// 位置・回転・大きさ (左手系 : +X 右 / +Y 上 / +Z 奥。DirectX と同じ)
//   値を書き換えると、TransformSystem が次のフレームで行列を作り直し、
//   描画オブジェクト (MeshRenderer) なら C++ へまとめて送られる。
//   動かない物には Static も付けると、最初の 1 回しか計算しない (大量の背景向け)
// ==========================================================================
public struct Transform
{
    public Vector3    Position;
    public Quaternion Rotation;
    public Vector3    Scale;

    // TransformSystem が作る行列 (スケール → 回転 → 移動)。読むのは自由、書くのはエンジンだけ
    internal Matrix4x4 LocalToWorld;
    internal bool      HasMatrix;   // 一度でも計算したか
    internal bool      Changed;     // このフレームで行列が変わったか (描画へ送る印)

    public Transform()
    {
        Rotation = Quaternion.Identity;
        Scale    = Vector3.One;
    }

    public Transform(Vector3 position) : this()
    {
        Position = position;
    }

    // position から target を見る向きにする
    public static Transform LookingAt(Vector3 position, Vector3 target) =>
        new(position) { Rotation = MathEx.LookRotation(target - position, Vector3.UnitY) };

    // 最後に計算した行列 (Static なら作った時のもの)
    public readonly Matrix4x4 Matrix => LocalToWorld;

    public readonly Vector3 Forward => Vector3.Transform(Vector3.UnitZ, Rotation);
    public readonly Vector3 Right   => Vector3.Transform(Vector3.UnitX, Rotation);
    public readonly Vector3 Up      => Vector3.Transform(Vector3.UnitY, Rotation);

    public void LookAt(Vector3 target) => Rotation = MathEx.LookRotation(target - Position, Vector3.UnitY);

    // Static を付けた物を動かした時に呼ぶ (次のフレームで行列を作り直して送る)
    public void MarkDirty() => HasMatrix = false;
}

// 動かない物の印 (中身は無い)。TransformSystem は最初の 1 回だけ行列を計算する
public struct Static
{
}

// 計算の補助
public static class MathEx
{
    public const float Deg2Rad = MathF.PI / 180.0f;
    public const float Rad2Deg = 180.0f / MathF.PI;

    // forward の向きを向く回転 (左手系。up は上の目安)
    public static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {
        if (forward.LengthSquared() < 1e-12f) return Quaternion.Identity;
        var f = Vector3.Normalize(forward);
        var r = Vector3.Cross(up, f);
        if (r.LengthSquared() < 1e-12f) r = Vector3.Cross(Vector3.UnitZ, f);   // 真上 / 真下を見る時
        r = Vector3.Normalize(r);
        var u = Vector3.Cross(f, r);

        // 行ベクトルの行列 (X 軸 → r, Y 軸 → u, Z 軸 → f)
        var m = new Matrix4x4(
            r.X, r.Y, r.Z, 0,
            u.X, u.Y, u.Z, 0,
            f.X, f.Y, f.Z, 0,
            0,   0,   0,   1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }
}
