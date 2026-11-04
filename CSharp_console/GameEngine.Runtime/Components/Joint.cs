using System.Numerics;

namespace GameEngine;

// ジョイントの種類 (C++ の Physics::JointType と同じ番号)
public enum JointType : uint
{
    Distance = 0,   // 2 点の距離を保つ (棒 / ロープ / ばね)
    Ball     = 1,   // 2 点を合わせる (どの向きにも回る。肩の関節 / 鎖)
    Fixed    = 2,   // 2 つの体を固く繋ぐ (位置も向きも作った時のまま)
}

// ==========================================================================
// ジョイント (2 つの剛体、または剛体とワールドの点を繋ぐ)
//   - Body (既定 Entity.Null = このエンティティ) と ConnectedBody (Entity.Null = ワールド) を繋ぐ
//     Body を指定すれば、ジョイントだけのエンティティも作れる (1 つの体に何本でも繋げる)
//   - Anchor は Body のローカルの点 / ConnectedAnchor は ConnectedBody のローカルの点 (ワールドならワールドの点)
//   - 繋いだ 2 つの体どうしは押し戻さない (CollideConnected = true で押し戻す)
//   - BreakForce を超える力がかかると壊れる (IsBroken。もう働かない。値を変えると作り直す)
//   - C++ の Joint を継承すれば、新しい種類を足せる (joints.h)
// ==========================================================================
public struct Joint : IComponent
{
    public JointType Type;
    public Entity    Body;                // Entity.Null ならこのエンティティ
    public Entity    ConnectedBody;       // Entity.Null ならワールド
    public Vector3   Anchor;
    public Vector3   ConnectedAnchor;
    public float     Length;              // Distance : 保つ長さ (0 なら作った時の距離)
    public float     MinLength;           // Distance : 縮む限り (0 なら Length)
    public float     MaxLength;           // Distance : 伸びる限り (0 なら Length。ロープは MinLength を小さく)
    public float     SpringHertz;         // Distance : ばねの振動数 (0 なら硬い棒) / Ball・Fixed : 硬さ (0 なら既定)
    public float     SpringDamping;       // ばねの減衰比 (0.7 前後で揺れがすぐ収まる)
    public float     BreakForce;          // 壊れる力 (N。0 なら壊れない)
    public bool      CollideConnected;

    // ---------- 今の状態 (Physics が書く) ----------
    internal bool  _broken;
    internal float _force;
    internal float _length;

    public readonly bool  IsBroken      => _broken;
    public readonly float CurrentForce  => _force;    // 前の刻みでかかった力 (N)
    public readonly float CurrentLength => _length;   // 今の 2 点の距離

    /// <summary>硬い棒 (length = 0 なら作った時の距離)</summary>
    public static Joint Distance(Entity connected, Vector3 anchor, Vector3 connectedAnchor, float length = 0.0f) =>
        new() { Type = JointType.Distance, ConnectedBody = connected, Anchor = anchor, ConnectedAnchor = connectedAnchor, Length = length };

    /// <summary>ロープ (maxLength まで伸び、縮むのは自由)</summary>
    public static Joint Rope(Entity connected, Vector3 anchor, Vector3 connectedAnchor, float maxLength) =>
        new() { Type = JointType.Distance, ConnectedBody = connected, Anchor = anchor, ConnectedAnchor = connectedAnchor, Length = maxLength, MinLength = 0.001f, MaxLength = maxLength };

    /// <summary>ばね (restLength へ戻る。hertz : 振動数 / damping : 減衰比)</summary>
    public static Joint Spring(Entity connected, Vector3 anchor, Vector3 connectedAnchor, float restLength, float hertz, float damping = 0.7f) =>
        new()
        {
            Type = JointType.Distance, ConnectedBody = connected, Anchor = anchor, ConnectedAnchor = connectedAnchor,
            Length = restLength, MinLength = 0.001f, MaxLength = restLength * 100.0f + 100.0f, SpringHertz = hertz, SpringDamping = damping,
        };

    public static Joint Ball(Entity connected, Vector3 anchor, Vector3 connectedAnchor) =>
        new() { Type = JointType.Ball, ConnectedBody = connected, Anchor = anchor, ConnectedAnchor = connectedAnchor };

    public static Joint Fixed(Entity connected, Vector3 anchor, Vector3 connectedAnchor) =>
        new() { Type = JointType.Fixed, ConnectedBody = connected, Anchor = anchor, ConnectedAnchor = connectedAnchor };

    // 作り直す必要があるか (状態以外の値が同じか)
    internal readonly bool SameSettings(in Joint o) =>
        Type == o.Type && Body == o.Body && ConnectedBody == o.ConnectedBody && Anchor == o.Anchor && ConnectedAnchor == o.ConnectedAnchor &&
        Length == o.Length && MinLength == o.MinLength && MaxLength == o.MaxLength && SpringHertz == o.SpringHertz &&
        SpringDamping == o.SpringDamping && BreakForce == o.BreakForce && CollideConnected == o.CollideConnected;
}
