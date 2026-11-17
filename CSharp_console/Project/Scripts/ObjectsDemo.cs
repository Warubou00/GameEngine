using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// GameObject / 物とアセットの参照 (v1.18 - v1.20) の見本 (シーン : Assets/Scenes/objects_demo.scene.json)
//   砲台 (Turret) が一番近い敵 (Enemy) を狙い、プレハブ (Bullet) の弾を撃つ。弾が当たると敵の Hp が減り、0 で消える
//   - Turret のフィールド : Target (ComponentRef<Enemy>) / Muzzle (GameObject) / Bullet (Prefab) / Next (SceneReference)
//     Inspector で Hierarchy から物を、Project からプレハブとシーンを落として入れる
//   - 探す : FindWithTag("Player") / FindObjectsOfType<Enemy>() / Find("Turret/Muzzle")
//   N : Next のシーンへ切り替える
// ==========================================================================
public struct Enemy : IComponent
{
    public float Hp = 3;
    [Tooltip("Up / down speed (radians per second)")]
    public float BobSpeed = 2;
    [HideInInspector] public float Phase;

    public Enemy()
    {
    }
}

public struct Turret : IComponent
{
    [Tooltip("The enemy to shoot (empty = the nearest one)")]
    public ComponentRef<Enemy> Target;
    [Tooltip("Bullets start here")]
    public GameObject Muzzle;
    public Prefab? Bullet;
    [Tooltip("Scene to load with N")]
    public SceneReference Next;
    public float FireInterval = 0.5f;
    [HideInInspector] public float Cooldown;

    public Turret()
    {
    }
}

public struct Bullet : IComponent
{
    public float Speed = 8;
    public float Life = 2;
    public ComponentRef<Enemy> Target;

    public Bullet()
    {
    }
}

[GameSystem]
public sealed class TurretSystem : Behaviour<Turret>
{
    protected override void Start(ref Turret turret)
    {
        var player = FindWithTag("Player");
        if (!turret.Muzzle) turret.Muzzle = Find("Turret/Muzzle");   // 欄が空ならパスで探す
        Console.WriteLine($"[ObjectsDemo] ready : player={player.Name} muzzle={turret.Muzzle.Name} target={(turret.Target ? turret.Target.GameObject.Name : "none")} " +
                          $"enemies={FindObjectsOfType<Enemy>().Count} bullet={turret.Bullet?.Path ?? "none"} next={turret.Next.Name}");
    }

    protected override void Update(ref Turret turret)
    {
        if (Input.GetKeyDown(Keys.N) && turret.Next) { Scenes.LoadScene(turret.Next); return; }

        // 狙う敵が居なくなったら、一番近い物を探す
        if (!turret.Target)
        {
            var position = Transform.WorldPosition;
            var nearest = GameObject.None;
            var best = float.MaxValue;
            foreach (var enemy in FindObjectsOfType<Enemy>())
            {
                var d = Vector3.DistanceSquared(enemy.Transform.WorldPosition, position);
                if (d < best) { best = d; nearest = enemy; }
            }
            turret.Target = nearest.GetComponentRef<Enemy>();
            if (!turret.Target) return;   // 全部倒した
        }

        // 敵の方を向く (Y 軸だけ)
        var toTarget = turret.Target.GameObject.Transform.WorldPosition - Transform.WorldPosition;
        Transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan2(toTarget.X, toTarget.Z));

        // 撃つ
        turret.Cooldown -= Time.DeltaTime;
        if (turret.Cooldown > 0 || turret.Bullet == null || !turret.Muzzle) return;
        turret.Cooldown = turret.FireInterval;
        var shot = turret.Bullet.Instantiate(World, turret.Muzzle.Transform.WorldPosition);
        World.Get<Bullet>(shot).Target = turret.Target;
    }
}

[GameSystem]
public sealed class BulletSystem : Behaviour<Bullet>
{
    protected override void Update(ref Bullet bullet)
    {
        bullet.Life -= Time.DeltaTime;
        if (bullet.Life <= 0 || !bullet.Target) { Destroy(Entity); return; }
        ref var transform = ref Transform;
        var to = bullet.Target.GameObject.Transform.WorldPosition - transform.Position;
        var step = bullet.Speed * Time.DeltaTime;
        if (to.Length() <= step + 0.3f)
        {
            ref var enemy = ref bullet.Target.Value;
            enemy.Hp -= 1;
            if (enemy.Hp <= 0) Destroy(bullet.Target.Entity);
            Destroy(Entity);
            return;
        }
        transform.Position += Vector3.Normalize(to) * step;
    }
}

[GameSystem]
public sealed class EnemySystem : Behaviour<Enemy>
{
    protected override void Update(ref Enemy enemy)
    {
        enemy.Phase += enemy.BobSpeed * Time.DeltaTime;
        ref var transform = ref Transform;
        transform.Position = transform.Position with { Y = 1.0f + 0.5f * MathF.Sin(enemy.Phase) };
    }
}
