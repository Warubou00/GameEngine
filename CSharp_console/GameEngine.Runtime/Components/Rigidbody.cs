using System.Numerics;

namespace GameEngine;

// 剛体の動き方 (C++ の Physics::MotionType と同じ番号)
public enum RigidbodyType : uint
{
    Dynamic   = 1,   // 重力 / 力 / 接触で動く
    Kinematic = 2,   // 自分では動かない。Velocity / MovePosition / Transform で動かし、他の物を押す (押されない)
}

// 軸の固定 (ワールドの軸。Unity の RigidbodyConstraints と同じ)
[Flags]
public enum RigidbodyConstraints : uint
{
    None            = 0,
    FreezePositionX = 1 << 0,
    FreezePositionY = 1 << 1,
    FreezePositionZ = 1 << 2,
    FreezeRotationX = 1 << 3,
    FreezeRotationY = 1 << 4,
    FreezeRotationZ = 1 << 5,
    FreezePosition  = FreezePositionX | FreezePositionY | FreezePositionZ,
    FreezeRotation  = FreezeRotationX | FreezeRotationY | FreezeRotationZ,
    FreezeAll       = FreezePosition | FreezeRotation,
}

// 力のかけ方 (Unity の ForceMode と同じ)
public enum ForceMode
{
    Force,           // 力 (N)。次の固定の刻みの間かかる (質量が大きいほど動きにくい)
    Acceleration,    // 加速度 (m/s²)。質量に関係なく同じように動く
    Impulse,         // 力積 (N·s)。すぐに速度が変わる (質量が大きいほど変わりにくい)
    VelocityChange,  // 速度の変化 (m/s)。質量に関係なく、すぐに速度が変わる
}

// ==========================================================================
// 剛体 (Transform + Collider と一緒に付ける)。Physics.dll が固定の時間刻み (Time.FixedDeltaTime) で動かす
//   - 位置 / 回転は Physics が Transform に書く (Interpolate なら、描画のフレームに合わせて前後の刻みの間を補間)
//   - Transform を自分で書き換えると、その位置へ瞬間移動する (Kinematic なら、次の刻みでそこへ動いて他の物を押す)
//   - 質量の分布 (慣性) は Collider の形と大きさから求める。重心は Collider の中心
//   - 力 / 速度は FixedUpdate (IFixedSystem) で触るのがおすすめ (Update でかけた力は、次の 1 刻みだけかかる)
//   - 摩擦 / 反発は Collider に持つ (動かない地面にも付けられるように)
//   - 止まった物は眠る (P2) : 触れ合っている物のまとまり (島) が全部止まると、島ごと計算しなくなる。
//     動いている物が触れる / 力や速度を与える / Transform を動かす / 支えが消える と島ごと起きる
//     IsSleeping で分かる。CanSleep = false で眠らない (いつも計算する)
//   使い方 : ref var body = ref world.Get<Rigidbody>(entity); body.AddForce(new Vector3(0, 10, 0));
// ==========================================================================
public struct Rigidbody
{
    public RigidbodyType Type = RigidbodyType.Dynamic;
    public float Mass           = 1.0f;    // kg
    public float LinearDamping  = 0.0f;    // 速度の減衰 (1 秒あたり。空気抵抗の代わり)
    public float AngularDamping = 0.05f;   // 回転の減衰 (転がる球もいつかは止まる)
    public float GravityScale   = 1.0f;    // 重力の倍率 (0 で無重力)
    public float InertiaScale   = 1.0f;    // 慣性の倍率 (大きいほど回りにくい)
    public RigidbodyConstraints Constraints = RigidbodyConstraints.None;
    public bool  Interpolate    = true;    // 描画のフレームに合わせて、前後の刻みの間の位置を出す (カクつかない)
    public bool  CanSleep       = true;    // 止まったら眠ってよいか (P2)

    // ---------- 今の状態 (Physics が書く) ----------
    internal Vector3    _velocity;
    internal Vector3    _angularVelocity;
    internal Vector3    _position;        // 最後の刻みの位置 (補間していない)
    internal Quaternion _rotation;
    internal Vector3    _centerOfMass;
    internal bool       _sleeping;

    // ---------- 次の刻みへの命令 (CollisionSystem が送って消す) ----------
    internal Vector3    _force, _torque, _impulse, _angularImpulse, _angularVelocityChange;
    internal bool       _setVelocity, _setAngularVelocity, _hasTarget, _hasCommand, _wake, _sleep;
    internal Vector3    _targetPosition;
    internal Quaternion _targetRotation;
    internal bool       _targetRotationSet;

    public Rigidbody()
    {
        _rotation       = Quaternion.Identity;
        _targetRotation = Quaternion.Identity;
    }

    public static Rigidbody Dynamic(float mass = 1.0f) => new() { Type = RigidbodyType.Dynamic, Mass = mass };
    public static Rigidbody Kinematic() => new() { Type = RigidbodyType.Kinematic };

    /// <summary>重心の速度 (m/s)。書くと次の刻みでその速度にする</summary>
    public Vector3 Velocity
    {
        readonly get => _velocity;
        set { _velocity = value; _setVelocity = true; _hasCommand = true; }
    }

    /// <summary>角速度 (rad/s、ワールドの軸)。書くと次の刻みでその角速度にする</summary>
    public Vector3 AngularVelocity
    {
        readonly get => _angularVelocity;
        set { _angularVelocity = value; _setAngularVelocity = true; _angularVelocityChange = Vector3.Zero; _hasCommand = true; }
    }

    /// <summary>最後の固定の刻みの位置 (Transform は補間した位置なので少し違う)</summary>
    public readonly Vector3    Position          => _position;
    public readonly Quaternion Rotation          => _rotation;
    public readonly Vector3    WorldCenterOfMass => _centerOfMass;
    /// <summary>眠っているか (止まっていて計算していない。P2)</summary>
    public readonly bool       IsSleeping        => _sleeping;

    /// <summary>起こす (触れ合っている島ごと)。力 / 速度を与えた時は自動で起きる</summary>
    public void WakeUp()
    {
        _wake = true; _sleep = false; _hasCommand = true;
    }

    /// <summary>今すぐ眠らせる (この体だけ。動いている物が触れれば起きる)</summary>
    public void Sleep()
    {
        _sleep = true; _wake = false; _hasCommand = true;
    }

    // ---------- 力 ----------

    public void AddForce(Vector3 force, ForceMode mode = ForceMode.Force)
    {
        switch (mode)
        {
        case ForceMode.Force:          _force   += force;        break;
        case ForceMode.Acceleration:   _force   += force * Mass; break;
        case ForceMode.Impulse:        _impulse += force;        break;
        case ForceMode.VelocityChange: _impulse += force * Mass; break;
        }
        _hasCommand = true;
    }

    /// <summary>回す力 (ワールドの軸)。Acceleration / VelocityChange は慣性に関係なく角速度を変える</summary>
    public void AddTorque(Vector3 torque, ForceMode mode = ForceMode.Force)
    {
        switch (mode)
        {
        case ForceMode.Force:          _torque                += torque;                    break;
        case ForceMode.Impulse:        _angularImpulse        += torque;                    break;
        case ForceMode.Acceleration:   _angularVelocityChange += torque * Time.FixedDeltaTime; break;
        case ForceMode.VelocityChange: _angularVelocityChange += torque;                    break;
        }
        _hasCommand = true;
    }

    /// <summary>ワールドの点 position に力をかける (重心から外れていれば回る)</summary>
    public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode = ForceMode.Force)
    {
        var scaled = mode is ForceMode.Acceleration or ForceMode.VelocityChange ? force * Mass : force;
        var torque = Vector3.Cross(position - _centerOfMass, scaled);
        var impulse = mode is ForceMode.Impulse or ForceMode.VelocityChange;
        AddForce(scaled, impulse ? ForceMode.Impulse : ForceMode.Force);
        AddTorque(torque, impulse ? ForceMode.Impulse : ForceMode.Force);
    }

    // ---------- キネマティック ----------

    /// <summary>Kinematic : 次の刻みでこの位置へ動く (途中の物を押す。着いたら止まる)</summary>
    public void MovePosition(Vector3 position)
    {
        _targetPosition = position;
        if (!_targetRotationSet) _targetRotation = _rotation;
        _hasTarget  = true;
        _hasCommand = true;
    }

    /// <summary>Kinematic : 次の刻みでこの向きへ回る</summary>
    public void MoveRotation(Quaternion rotation)
    {
        if (!_hasTarget) _targetPosition = _position;
        _targetRotation    = rotation;
        _targetRotationSet = true;
        _hasTarget  = true;
        _hasCommand = true;
    }

    internal void ClearCommands()
    {
        _force = _torque = _impulse = _angularImpulse = _angularVelocityChange = Vector3.Zero;
        _setVelocity = _setAngularVelocity = _hasTarget = _hasCommand = _targetRotationSet = _wake = _sleep = false;
    }
}
