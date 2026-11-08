using System.Numerics;

namespace GameEngine.Editor;

// ==========================================================================
// 確認用 (EditorTest の v1.2) : Behaviour<T> の GetComponent と衝突のメソッドを数える
//   ゲームの DLL ではなくエディターの中に置く ([GameSystem] を付けない : 確認の間だけ engine.AddSystem で足す)
// ==========================================================================
internal struct CollisionProbe : IComponent
{
    public int Updates;
    public int Enter, Stay, Exit;
    public int TriggerEnter, TriggerStay, TriggerExit;
    public bool HadContact;
    public Vector3 Normal;          // 最後の Enter / Stay の法線 (相手 → 自分)
    public bool OtherIsProbe;       // collision.HasComponent<CollisionProbe>() (相手のコンポーネント)
    public float SeenX;             // Update で GetComponent<Transform>() から読んだ位置
    public bool DestroyOnEnter;     // Enter で自分を Destroy する
}

internal sealed class CollisionProbeSystem : Behaviour<CollisionProbe>
{
    protected override void Update(ref CollisionProbe data)
    {
        data.Updates++;
        data.SeenX = GetComponent<Transform>().Position.X;
    }

    protected override void OnCollisionEnter(ref CollisionProbe data, in Collision collision)
    {
        data.Enter++;
        data.OtherIsProbe = collision.HasComponent<CollisionProbe>();
        if (collision.TryGetContact(out _, out var normal)) { data.HadContact = true; data.Normal = normal; }
        if (data.DestroyOnEnter) Destroy(Entity);
    }

    protected override void OnCollisionStay(ref CollisionProbe data, in Collision collision)
    {
        data.Stay++;
        if (collision.TryGetContact(out _, out var normal)) { data.HadContact = true; data.Normal = normal; }
    }

    protected override void OnCollisionExit(ref CollisionProbe data, in Collision collision) => data.Exit++;
    protected override void OnTriggerEnter(ref CollisionProbe data, in Collision other) => data.TriggerEnter++;
    protected override void OnTriggerStay(ref CollisionProbe data, in Collision other) => data.TriggerStay++;
    protected override void OnTriggerExit(ref CollisionProbe data, in Collision other) => data.TriggerExit++;
}

// 速さの確認用 : Update だけ (GetComponent を 2 回)
internal struct BenchData : IComponent { public float Sum; }

internal sealed class BenchBehaviour : Behaviour<BenchData>
{
    protected override void Update(ref BenchData data)
    {
        ref var transform = ref Transform;
        transform.Position += new Vector3(0.001f, 0, 0);
        data.Sum += GetComponent<Transform>().Position.X;
    }
}

// 確認用 (EditorTest の v1.4) : Awake / Start / Update / FixedUpdate / LateUpdate の順番と回数
internal struct LifecycleProbe : IComponent
{
    public int Awake, Start, Updates, Fixed, Late;
    public int OrderErrors;   // 順番が違った回数 (Start の前の Update など)
}

internal sealed class LifecycleProbeSystem : Behaviour<LifecycleProbe>
{
    public readonly List<string> Log = new();   // 最初の呼び出しの順 ("Awake" / "Start")

    protected override void Awake(ref LifecycleProbe d) { if (d.Start > 0 || d.Updates > 0) d.OrderErrors++; d.Awake++; Log.Add("Awake"); }
    protected override void Start(ref LifecycleProbe d) { if (d.Awake == 0 || d.Updates > 0) d.OrderErrors++; d.Start++; Log.Add("Start"); }
    protected override void Update(ref LifecycleProbe d) { if (d.Start == 0) d.OrderErrors++; d.Updates++; }
    protected override void FixedUpdate(ref LifecycleProbe d) { if (d.Start == 0) d.OrderErrors++; d.Fixed++; }

    // 全部の Update の後。ここで動かした Transform は、このフレームの行列に入る
    protected override void LateUpdate(ref LifecycleProbe d)
    {
        if (d.Late >= d.Updates) d.OrderErrors++;
        d.Late++;
        ref var transform = ref Transform;
        transform.Position = new Vector3(transform.Position.X, 100.0f + d.Late, transform.Position.Z);
    }
}
