using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// UserData (v2.0。Unity の ScriptableObject / RE Engine の UserData に当たる) の見本
//   シーン : Assets/Scenes/userdata_demo.scene.json / 設定 : Assets/Samples/UserData/*.userdata.json
//   - EnemyType (敵の種類の設定) と WaveTable (出てくる順の表) を UserData で作る (Project の右クリック > Create > UserData > Samples)
//   - Spawner の WaveSpawner.Table に WaveTable を落とすと、表の順に敵を出す。敵は EnemyType の Body (プレハブ) から作り、
//     Hp / Speed / Scale は EnemyType の値。同じ EnemyType を使う敵は全部同じ設定 (1 つのファイルを直すと全部変わる)
//   - Inspector で .userdata.json を選んで値を変えると、すぐファイルに書かれる (再生中に変えた値は Stop で元に戻る)
//   - 書き出したゲームでは .userdata.bin (詰めて混ぜ、書き換えを確かめる形) になる。コードは今まで通り .userdata.json のパスで良い
// ==========================================================================

[UserData(Menu = "Samples/Enemy Type", FileName = "New Enemy Type")]
public sealed class EnemyType : UserData
{
    [Header("Look")]
    public string DisplayName = "Enemy";
    [Tooltip("The prefab to create for this enemy")]
    public Prefab? Body;
    public float Scale = 1;

    [Header("Stats")]
    [Range(1, 50)] public int Hp = 3;
    [Range(0, 10)] public float Speed = 1.5f;

    // Inspector で値を変えた後 : おかしな値を直す
    protected override void OnValidate()
    {
        if (Hp < 1) Hp = 1;
        if (Scale < 0.1f) Scale = 0.1f;
    }
}

// WaveTable の 1 行 ([Serializable] なクラスは UserData の中に値として書ける)
[Serializable]
public sealed class Wave
{
    public EnemyType? Enemy;
    public int Count = 3;
    [Tooltip("Seconds between enemies")]
    public float Interval = 0.5f;
    [Tooltip("Enemies appear at these points in turn (empty = the spawner)")]
    public List<Vector3> SpawnPoints = new();
}

[UserData(Menu = "Samples/Wave Table", FileName = "New Wave Table")]
public sealed class WaveTable : UserData
{
    public float StartDelay = 0.5f;
    [Tooltip("Seconds between waves")]
    public float WaveGap = 1.0f;
    public List<Wave> Waves = new();
    public bool Loop;
}

public struct WaveSpawner : IComponent
{
    public WaveTable? Table;
    [HideInInspector] public int WaveIndex;
    [HideInInspector] public int Spawned;
    [HideInInspector] public float Timer;
}

public struct Walker : IComponent
{
    public EnemyType? Type;
    public int Hp;
    public Vector3 Goal;
}

[GameSystem]
public sealed class WaveSpawnerSystem : Behaviour<WaveSpawner>
{
    protected override void Start(ref WaveSpawner spawner)
    {
        var table = spawner.Table;
        if (table == null) { Console.WriteLine("[UserDataDemo] no WaveTable"); return; }
        spawner.Timer = table.StartDelay;
        Console.WriteLine($"[UserDataDemo] ready : table={table.Name} waves={table.Waves.Count} " +
                          string.Join(" ", table.Waves.Select(w => $"{w.Enemy?.DisplayName ?? "none"}x{w.Count}(hp {w.Enemy?.Hp ?? 0}, speed {w.Enemy?.Speed ?? 0})")));
    }

    protected override void Update(ref WaveSpawner spawner)
    {
        var table = spawner.Table;
        if (table == null || table.Waves.Count == 0) return;
        if (spawner.WaveIndex >= table.Waves.Count)
        {
            if (!table.Loop) return;
            spawner.WaveIndex = 0;
        }
        spawner.Timer -= Time.DeltaTime;
        if (spawner.Timer > 0) return;

        var wave = table.Waves[spawner.WaveIndex];
        if (wave.Enemy?.Body is { } body)
        {
            var point = wave.SpawnPoints.Count > 0 ? wave.SpawnPoints[spawner.Spawned % wave.SpawnPoints.Count] : Transform.WorldPosition;
            var enemy = body.Instantiate(World, point);
            ref var transform = ref World.Get<Transform>(enemy);
            transform.Scale *= wave.Enemy.Scale;
            transform.MarkDirty();
            World.Add(enemy, new Walker { Type = wave.Enemy, Hp = wave.Enemy.Hp, Goal = Transform.WorldPosition });
        }
        spawner.Spawned++;
        spawner.Timer = wave.Interval;
        if (spawner.Spawned >= wave.Count)
        {
            spawner.WaveIndex++;
            spawner.Spawned = 0;
            spawner.Timer = table.WaveGap;
        }
    }
}

[GameSystem]
public sealed class WalkerSystem : Behaviour<Walker>
{
    protected override void Update(ref Walker walker)
    {
        if (walker.Type == null) return;
        ref var transform = ref Transform;
        var to = walker.Goal - transform.Position;
        to.Y = 0;
        var step = walker.Type.Speed * Time.DeltaTime;
        if (to.Length() <= step) { Destroy(Entity); return; }   // 着いたら消える
        transform.Position += Vector3.Normalize(to) * step;
    }
}
