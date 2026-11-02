namespace GameEngine;

// ==========================================================================
// エンティティ (ECS の「物」。中身は番号と世代だけ)
//   データはコンポーネント (struct) として World が型ごとに持ち、処理は System が書く。
//   削除された番号は使い回すので、世代 (Version) で「古い番号を持ち続けていないか」を見分ける
// ==========================================================================
public readonly struct Entity : IEquatable<Entity>
{
    public readonly int Id;
    public readonly int Version;   // 1 から始まる (0 = Entity.Null)

    internal Entity(int id, int version)
    {
        Id      = id;
        Version = version;
    }

    // どの World にも存在しないエンティティ
    public static Entity Null => default;
    public bool IsNull => Version == 0;

    public bool Equals(Entity other) => Id == other.Id && Version == other.Version;
    public override bool Equals(object? obj) => obj is Entity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Id, Version);
    public static bool operator ==(Entity a, Entity b) => a.Equals(b);
    public static bool operator !=(Entity a, Entity b) => !a.Equals(b);
    public override string ToString() => IsNull ? "Entity(Null)" : $"Entity({Id}:{Version})";
}
