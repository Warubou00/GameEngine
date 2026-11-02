namespace GameEngine;

// 重なりを探す方法 (C++ の Physics::Broadphase と同じ番号)。比べる用。既定は Bvh
public enum Broadphase : uint
{
    Bvh   = 0,   // 動的 AABB ツリー (動かない物 / 動く物の 2 本)。動いた物だけ調べる
    Brute = 1,   // 総当たり
    Grid  = 2,   // 均一グリッド
}

public enum CollisionEventType : uint
{
    Enter = 0,   // 重なり始めた
    Exit  = 1,   // 離れた
}

// 重なりのイベント。A / B はどちらが先とは決まっていない
public readonly record struct CollisionEvent(Entity A, Entity B, CollisionEventType Type)
{
    // entity が関わっていれば、その相手を返す
    public bool Involves(Entity entity, out Entity other)
    {
        if (A == entity) { other = B; return true; }
        if (B == entity) { other = A; return true; }
        other = default;
        return false;
    }
}

// ==========================================================================
// 衝突判定の設定と結果 (engine.Physics)
//   Events : 前のフレームの Step のイベント (System の Update で読む。次のフレームで入れ替わる)
// ==========================================================================
public sealed class PhysicsSettings
{
    private Broadphase _broadphase = Broadphase.Bvh;

    internal PhysicsSettings(bool available)
    {
        Available = available;
    }

    // Physics.dll が読めたか (無ければ衝突判定はしない)
    public bool Available { get; }

    // 確認用 : コライダーを線で描く (重なっている物は赤、それ以外は緑、動かない物は灰色)
    public bool ShowColliders { get; set; }

    public Broadphase Broadphase
    {
        get => _broadphase;
        set { _broadphase = value; BroadphaseDirty = true; }
    }
    internal bool BroadphaseDirty { get; set; }

    internal readonly List<CollisionEvent> EventList = new();
    public IReadOnlyList<CollisionEvent> Events => EventList;

    // 前の Step の計測 (プロファイラーにも出る)
    public int   Bodies        { get; internal set; }
    public int   MovedBodies   { get; internal set; }
    public int   Candidates    { get; internal set; }
    public int   Overlaps      { get; internal set; }
    public float BroadphaseMs  { get; internal set; }
    public float NarrowphaseMs { get; internal set; }
    public float StepMs        { get; internal set; }
}
