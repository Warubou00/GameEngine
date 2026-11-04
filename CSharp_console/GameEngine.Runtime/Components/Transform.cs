using System.Numerics;

namespace GameEngine;

// ==========================================================================
// 位置・回転・大きさ (左手系 : +X 右 / +Y 上 / +Z 奥。DirectX と同じ)
//   値を書き換えると、TransformSystem が次のフレームで行列を作り直し、
//   描画オブジェクト (MeshRenderer) なら C++ へまとめて送られる。
//   動かない物には Static も付けると、最初の 1 回しか計算しない (大量の背景向け)
//
//   親子 (ED2) : Parent を指定すると、Position / Rotation / Scale は親から見た値 (ローカル) になる
//     ワールドの値は WorldPosition / WorldRotation / WorldScale (TransformSystem が親から順に計算する)
//     親が無ければ、ローカル = ワールド (以前と同じ)。親子を変えるのは Hierarchy.SetParent (ワールドの位置を保てる)
//     描画 / 物理 / ライト / カメラ / 音 / パーティクルは、ワールドの値を使う
//     以前は親子が無く、Position などがそのままワールドの値だった
// ==========================================================================
public struct Transform : IComponent
{
    public Vector3    Position;
    public Quaternion Rotation;
    public Vector3    Scale;

    // TransformSystem が作るワールドの行列 (スケール → 回転 → 移動 → 親の行列)。読むのは自由、書くのはエンジンだけ
    internal Matrix4x4 LocalToWorld;

    // 親 (Entity.Null なら無し。保存すると親の Id になる)
    // ※毎フレーム全部の物で HasMatrix と一緒に見るので、同じキャッシュの行に並べる
    //   (Scale の後に置いた時は、動かない 1 万個の見回りが 0.07 → 0.10 ms になった)
    public Entity      Parent;
    internal bool      HasMatrix;   // 一度でも計算したか
    internal bool      Changed;     // このフレームで行列が変わったか (描画へ送る印)
    internal int        _stamp;           // 計算したフレーム (親を先に計算するため)
    // ※ワールドの位置 / 回転 / 大きさを別に持つと、1 万個を見回るだけで遅くなった (構造体が 40byte 大きくなる)。
    //   子のワールドの値は、ワールドの行列から取り出す (子は少ないので、その時だけ計算する)

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

    // ワールドの値 (親が無ければローカルの値そのもの。親がある時は最後に TransformSystem が計算したワールドの行列から)
    public readonly Vector3    WorldPosition => Parent.IsNull ? Position : LocalToWorld.Translation;
    public readonly Quaternion WorldRotation => Parent.IsNull ? Rotation : decompose().Rotation;
    public readonly Vector3    WorldScale    => Parent.IsNull ? Scale    : decompose().Scale;   // 親子で回転と拡大が混ざった歪みは無視した値

    private readonly (Quaternion Rotation, Vector3 Scale) decompose()
    {
        if (Matrix4x4.Decompose(LocalToWorld, out var scale, out var rotation, out _)) return (Quaternion.Normalize(rotation), scale);
        // 歪んでいて分けられない時 : 軸の長さを大きさに、正規化した軸から回転を作る
        var m = LocalToWorld;
        var sx = new Vector3(m.M11, m.M12, m.M13).Length();
        var sy = new Vector3(m.M21, m.M22, m.M23).Length();
        var sz = new Vector3(m.M31, m.M32, m.M33).Length();
        if (sx < 1e-12f || sy < 1e-12f || sz < 1e-12f) return (Quaternion.Identity, new Vector3(sx, sy, sz));
        var r = new Matrix4x4(m.M11 / sx, m.M12 / sx, m.M13 / sx, 0, m.M21 / sy, m.M22 / sy, m.M23 / sy, 0, m.M31 / sz, m.M32 / sz, m.M33 / sz, 0, 0, 0, 0, 1);
        return (Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(r)), new Vector3(sx, sy, sz));
    }

    // ワールドの向き (以前はローカルの回転から求めていた。親が無ければ同じ)
    public readonly Vector3 Forward => Vector3.Transform(Vector3.UnitZ, WorldRotation);
    public readonly Vector3 Right   => Vector3.Transform(Vector3.UnitX, WorldRotation);
    public readonly Vector3 Up      => Vector3.Transform(Vector3.UnitY, WorldRotation);

    public void LookAt(Vector3 target) => Rotation = MathEx.LookRotation(target - Position, Vector3.UnitY);

    // ローカルの行列 (スケール → 回転 → 移動)
    public readonly Matrix4x4 LocalMatrix => Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    // Static を付けた物を動かした時に呼ぶ (次のフレームで行列を作り直して送る)
    public void MarkDirty() => HasMatrix = false;

    // ワールドの位置 / 回転を直接決める (TransformSystem の後に位置を変える物 (剛体) 用)
    //   親があれば、親のワールドの値から見たローカルの値に直して Position / Rotation に入れる。
    //   行列もすぐ作り直し、変わったら描画へ送る印を立てる (以前はローカル = ワールドとして行列を作るだけだった)
    internal void SetWorldPose(Vector3 worldPosition, Quaternion worldRotation, in Transform parent, bool hasParent)
    {
        if (hasParent)
        {
            Matrix4x4.Invert(parent.LocalToWorld, out var inverse);
            Position = Vector3.Transform(worldPosition, inverse);
            Rotation = Quaternion.Normalize(Quaternion.Concatenate(worldRotation, Quaternion.Inverse(parent.WorldRotation)));
        }
        else
        {
            Position = worldPosition;
            Rotation = worldRotation;
        }
        var matrix = hasParent ? LocalMatrix * parent.LocalToWorld : LocalMatrix;
        if (HasMatrix && matrix == LocalToWorld) return;
        LocalToWorld = matrix;
        HasMatrix    = true;
        Changed      = true;
    }
}

// 動かない物の印 (中身は無い)。TransformSystem は最初の 1 回だけ行列を計算する
public struct Static : IComponent
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
