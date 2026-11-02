using System.Numerics;
using GameEngine.Native;

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

// レイの当たり
public readonly record struct RaycastHit(Entity Entity, Vector3 Point, Vector3 Normal, float Distance);

// 接触点 (法線は A → B。B を Normal の向きに Depth だけ動かすと離れる)
public readonly record struct Contact(Entity A, Entity B, Vector3 Position, Vector3 Normal, float Depth);

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
    // 確認用 : 接触点を黄色の印と法線の線で描く
    public bool ShowContacts { get; set; }

    // ---------- レイヤーの衝突の表 (Unity の Physics.IgnoreLayerCollision と同じ) ----------
    //   既定は全部のレイヤーどうしを調べる。Collider.CollideWith (物ごと) と両方で許された組だけ調べる
    //   C++ は木の節に「枝の中のレイヤー」をビットで持ち、当たりえない枝は箱を調べる前に飛ばす (速くなる)
    //   表を変えると次の Step で全部の組を調べ直す (外れた組は Exit になる)。頻繁に変えるものではない

    /// <summary>レイヤー a と b の物どうしを調べるか (a, b : 0〜31)</summary>
    public void SetLayerCollision(int a, int b, bool collide)
    {
        if (!Available || (uint)a > 31 || (uint)b > 31) return;
        PhysicsNativeMethods.Physics_SetLayerCollision((uint)a, (uint)b, collide);
    }

    public void IgnoreLayerCollision(int a, int b, bool ignore = true) => SetLayerCollision(a, b, !ignore);

    public bool GetLayerCollision(int a, int b) =>
        Available && (uint)a <= 31 && (uint)b <= 31 && (PhysicsNativeMethods.Physics_GetLayerCollisionMask((uint)a) & (1u << b)) != 0;

    // 比べる用 : 木の節のレイヤーのビットで枝を飛ばすか (既定 true)
    public void SetLayerPruning(bool enabled)
    {
        if (Available) PhysicsNativeMethods.Physics_SetOption(1, enabled ? 1u : 0u);
    }

    /// <summary>
    /// 衝突判定を並列に行う追加のスレッドの数 (0 = 呼んだスレッドだけ)。
    /// 既定は 論理コアの数 - 1 (3 本まで)。描画や他の処理とコアを取り合う時は減らす
    /// </summary>
    public void SetWorkerCount(int count)
    {
        if (Available) PhysicsNativeMethods.Physics_SetOption(2, (uint)Math.Clamp(count, 0, 63));
    }

    // 比べる用 : 多く動いた時に動く物の木を作り直すか (既定 true。false なら 1 つずつ入れ直す)
    public void SetTreeRebuild(bool enabled)
    {
        if (Available) PhysicsNativeMethods.Physics_SetOption(3, enabled ? 1u : 0u);
    }

    // 体のハンドル → エンティティ (CollisionSystem が設定する)
    internal Func<uint, Entity>? EntityOf { get; set; }

    // ---------- 問い合わせ (前のフレームの Step の後の位置で答える) ----------

    /// <summary>一番近い当たり (始点が形の中にある物には当たらない)</summary>
    public bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance = 1000.0f, uint layerMask = 0xFFFFFFFFu)
    {
        hit = default;
        if (!Available) return false;
        var ray = new PhysicsNativeMethods.Ray { Origin = origin, Direction = direction, MaxDistance = maxDistance, LayerMask = layerMask };
        if (!PhysicsNativeMethods.Physics_Raycast(ref ray, out var result)) return false;
        hit = new RaycastHit(EntityOf?.Invoke(result.Body) ?? default, result.Point, result.Normal, result.Distance);
        return true;
    }

    private uint[] _overlapBuffer = new uint[64];

    /// <summary>球と重なる物を results に入れる (results は空にしてから)。戻り値は数</summary>
    public unsafe int OverlapSphere(Vector3 center, float radius, List<Entity> results, uint layerMask = 0xFFFFFFFFu)
    {
        results.Clear();
        if (!Available) return 0;
        uint count;
        while (true)
        {
            fixed (uint* buffer = _overlapBuffer)
            {
                count = PhysicsNativeMethods.Physics_OverlapSphere(&center, radius, layerMask, buffer, (uint)_overlapBuffer.Length);
            }
            if (count <= _overlapBuffer.Length) break;
            _overlapBuffer = new uint[count * 2];   // 足りなければ大きくして聞き直す
        }
        for (int i = 0; i < count; i++) results.Add(EntityOf?.Invoke(_overlapBuffer[i]) ?? default);
        return (int)count;
    }

    /// <summary>回した箱と重なる物を results に入れる (results は空にしてから)。戻り値は数</summary>
    public unsafe int OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion rotation, List<Entity> results, uint layerMask = 0xFFFFFFFFu)
    {
        results.Clear();
        if (!Available) return 0;
        uint count;
        while (true)
        {
            fixed (uint* buffer = _overlapBuffer)
            {
                count = PhysicsNativeMethods.Physics_OverlapBox(&center, &halfExtents, &rotation, layerMask, buffer, (uint)_overlapBuffer.Length);
            }
            if (count <= _overlapBuffer.Length) break;
            _overlapBuffer = new uint[count * 2];
        }
        for (int i = 0; i < count; i++) results.Add(EntityOf?.Invoke(_overlapBuffer[i]) ?? default);
        return (int)count;
    }

    private PhysicsNativeMethods.ContactPoint[] _contactBuffer = new PhysicsNativeMethods.ContactPoint[64];

    /// <summary>今重なっている組の接触点を results に入れる (results は空にしてから)。戻り値は数</summary>
    public unsafe int GetContacts(List<Contact> results)
    {
        results.Clear();
        if (!Available) return 0;
        var count = (int)PhysicsNativeMethods.Physics_GetContacts(null, 0);
        if (count > _contactBuffer.Length) _contactBuffer = new PhysicsNativeMethods.ContactPoint[count * 2];
        fixed (PhysicsNativeMethods.ContactPoint* buffer = _contactBuffer)
        {
            count = (int)PhysicsNativeMethods.Physics_GetContacts(buffer, (uint)_contactBuffer.Length);
        }
        count = Math.Min(count, _contactBuffer.Length);
        for (int i = 0; i < count; i++)
        {
            ref var c = ref _contactBuffer[i];
            results.Add(new Contact(EntityOf?.Invoke(c.BodyA) ?? default, EntityOf?.Invoke(c.BodyB) ?? default, c.Position, c.Normal, c.Depth));
        }
        return count;
    }

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
    public float TreeMs        { get; internal set; }   // 動く物の木を直した時間 (BroadphaseMs に含む)
    public float SyncMs        { get; internal set; }   // 動いた体の位置を C++ が受け取った時間 (木を直す分)
    public float EventsMs      { get; internal set; }   // 重なりの記録とイベント
    public int   Workers       { get; internal set; }   // 並列に使ったスレッドの数
    public float SystemMs      { get; internal set; }   // C# の CollisionSystem 全体 (P/Invoke と C++ の時間を含む)

    // ---------- 剛体 (P1) ----------
    public float SolverMs           { get; internal set; }   // 最後の刻みで接触とジョイントを解いた時間
    public int   RigidBodies        { get; internal set; }
    public int   ContactConstraints { get; internal set; }   // 解いた接触の組
    public int   Joints             { get; internal set; }   // 働いているジョイント
    public int   FixedSteps         { get; internal set; }   // このフレームで進めた固定の刻み

    /// <summary>剛体の解き方の設定 (変えると次の刻みから効く)</summary>
    public PhysicsSolverSettings Solver { get; } = new();
}

// ==========================================================================
// 剛体の解き方 (engine.Physics.Solver)。既定は Box2D v3 に近い値
// ==========================================================================
public sealed class PhysicsSolverSettings
{
    private Vector3 _gravity = new(0.0f, -9.81f, 0.0f);
    private int   _subSteps = 4, _iterations = 1, _relaxIterations = 1;
    private float _contactHertz = 30.0f, _contactDampingRatio = 10.0f, _contactPushMaxVelocity = 3.0f;
    private float _restitutionThreshold = 1.0f, _speculativeDistance = 0.02f;
    private float _jointHertz = 60.0f, _jointDampingRatio = 2.0f, _maxLinearSpeed = 400.0f;

    internal bool Dirty { get; set; } = true;

    public Vector3 Gravity { get => _gravity; set { _gravity = value; Dirty = true; } }
    /// <summary>1 刻みを何回に分けて解くか (多いほど積み重ねや鎖が安定。重くなる)</summary>
    public int   SubSteps               { get => _subSteps;               set { _subSteps = Math.Clamp(value, 1, 64); Dirty = true; } }
    public int   Iterations             { get => _iterations;             set { _iterations = Math.Clamp(value, 1, 64); Dirty = true; } }
    public int   RelaxIterations        { get => _relaxIterations;        set { _relaxIterations = Math.Clamp(value, 0, 64); Dirty = true; } }
    /// <summary>めり込みを戻す速さ (振動数)。大きいほど硬いが、刻みの 1/4 までしか効かない</summary>
    public float ContactHertz           { get => _contactHertz;           set { _contactHertz = MathF.Max(0.0f, value); Dirty = true; } }
    public float ContactDampingRatio    { get => _contactDampingRatio;    set { _contactDampingRatio = MathF.Max(0.0f, value); Dirty = true; } }
    /// <summary>めり込みを戻す速度の上限 (m/s。大きいと重なった物が勢いよく飛ぶ)</summary>
    public float ContactPushMaxVelocity { get => _contactPushMaxVelocity; set { _contactPushMaxVelocity = MathF.Max(0.0f, value); Dirty = true; } }
    /// <summary>この速さ (m/s) より遅くぶつかった時は跳ねない (止まっている物が震えないように)</summary>
    public float RestitutionThreshold   { get => _restitutionThreshold;   set { _restitutionThreshold = MathF.Max(0.0f, value); Dirty = true; } }
    /// <summary>離れていても、この距離までは接触として扱う (m。すり抜けと着地の震えを減らす)</summary>
    public float SpeculativeDistance    { get => _speculativeDistance;    set { _speculativeDistance = MathF.Max(0.0f, value); Dirty = true; } }
    public float JointHertz             { get => _jointHertz;             set { _jointHertz = MathF.Max(0.0f, value); Dirty = true; } }
    public float JointDampingRatio      { get => _jointDampingRatio;      set { _jointDampingRatio = MathF.Max(0.0f, value); Dirty = true; } }
    public float MaxLinearSpeed         { get => _maxLinearSpeed;         set { _maxLinearSpeed = MathF.Max(0.1f, value); Dirty = true; } }

    internal PhysicsNativeMethods.SolverSettings ToNative() => new()
    {
        Gravity = _gravity, SubSteps = (uint)_subSteps, ContactHertz = _contactHertz, ContactDampingRatio = _contactDampingRatio,
        ContactPushMaxVelocity = _contactPushMaxVelocity, RestitutionThreshold = _restitutionThreshold, SpeculativeDistance = _speculativeDistance,
        JointHertz = _jointHertz, JointDampingRatio = _jointDampingRatio, MaxLinearSpeed = _maxLinearSpeed,
        Iterations = (uint)_iterations, RelaxIterations = (uint)_relaxIterations,
    };
}
