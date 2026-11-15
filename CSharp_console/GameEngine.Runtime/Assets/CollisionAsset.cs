using System.Numerics;
using System.Text;
using System.Text.Json;

namespace GameEngine;

// コリジョンアセットのノードの形 (v1.9)
public enum CollisionNodeShape
{
    Empty   = 0,   // 形なし (子をまとめるだけ)
    Sphere  = 1,   // Size.X = 半径
    Box     = 2,   // Size = 半分の大きさ
    Capsule = 3,   // Size.X = 半径 / Size.Y = 円柱の半分の高さ (ノードのローカルの Y)
}

// ==========================================================================
// コリジョンアセットのノード (v1.9)。親子を作れる (位置 / 回転は親から見た値)
//   Id : ゲームの OnCollisionEnter などで「どのコライダーか」を見分ける番号 (アセットの中で重ならないこと。名前も付けられる)
// ==========================================================================
public sealed class CollisionNode
{
    public int                Id;
    public string             Name = "Collider";
    public CollisionNode?     Parent;
    public CollisionNodeShape Shape = CollisionNodeShape.Box;
    public Vector3            Position;                        // 親から見た位置
    public Quaternion         Rotation = Quaternion.Identity;  // 親から見た向き
    public Vector3            Size = new(0.5f, 0.5f, 0.5f);
    public bool               IsTrigger;
    public int                Layer;
    public uint               CollideWith = 0xFFFFFFFFu;
    public float              Friction    = 0.5f;
    public float              Restitution = 0.0f;

    public CollisionNode Clone() => (CollisionNode)MemberwiseClone();

    /// <summary>node が自分か、自分の子孫か (親を変える時に輪にならないように)</summary>
    public bool IsSelfOrAncestorOf(CollisionNode? node)
    {
        for (var n = node; n != null; n = n.Parent) if (n == this) return true;
        return false;
    }

    public override string ToString() => $"{Name} (ID {Id}, {Shape})";
}

// コリジョンアセットを平らにした部品 (形のあるノードだけ。位置 / 回転は持ち主 (エンティティ) から見た値)
public readonly record struct CollisionPart(int NodeIndex, ColliderShape Shape, Vector3 Center, Quaternion Rotation, Vector3 Size,
                                            bool IsTrigger, int Layer, uint CollideWith, float Friction, float Restitution);

// ==========================================================================
// コリジョンアセット (.collision.json。v1.9)。Box / Capsule / Sphere を組み合わせた、モデルの複雑な当たり判定
//   CompoundCollider (エンジンのコンポーネント) に複数のアセットを入れて使う。同じ持ち主の中の部品どうしは当たらない
//   OnCollisionEnter などの Collision.SelfPart / OtherPart で、どのアセットのどのコライダー (ID) が当たったかが分かる
//   読む時に親子をたどって「持ち主から見た位置 / 回転」の平らな配列 (Parts) を作っておく (毎フレームは木をたどらない)
//   中身を変えたら MarkChanged (Version が上がり、使っている CompoundCollider は次のフレームで体を作り直す)
// ==========================================================================
public sealed class CollisionAsset
{
    public const string Extension = ".collision.json";
    public const string Format    = "TsumugiCollision";
    public const int    FormatVersion = 1;

    /// <summary>アセットのパス ("Assets/Collisions/Robot.collision.json")</summary>
    public string Path { get; internal set; }
    public List<CollisionNode> Nodes { get; } = new();
    /// <summary>エディターのプレビューで一緒に出すモデル (ゲームでは使わない)</summary>
    public string? PreviewModel { get; set; }
    /// <summary>中身を変えた回数 (CompoundCollider が作り直しを決めるのに使う)</summary>
    public int Version { get; private set; } = 1;

    private CollisionPart[] _parts = Array.Empty<CollisionPart>();
    private int _partsVersion;

    public CollisionAsset(string path) { Path = path; }

    public CollisionAsset(string path, string json) : this(path) { SetJson(json); }

    /// <summary>中身を変えた (部品の表を作り直す / 使っている物を作り直す)</summary>
    public void MarkChanged() => Version++;

    /// <summary>形のあるノードを、持ち主から見た位置 / 回転にした表 (Nodes の順)</summary>
    public ReadOnlySpan<CollisionPart> Parts
    {
        get
        {
            if (_partsVersion != Version) rebuildParts();
            return _parts;
        }
    }

    public CollisionNode? FindById(int id)
    {
        foreach (var node in Nodes) if (node.Id == id) return node;
        return null;
    }

    public IEnumerable<CollisionNode> ChildrenOf(CollisionNode? parent) => Nodes.Where(n => n.Parent == parent);

    /// <summary>まだ使われていない一番小さい ID (1 から)</summary>
    public int NextId()
    {
        var used = new HashSet<int>(Nodes.Select(n => n.Id));
        var id = 1;
        while (used.Contains(id)) id++;
        return id;
    }

    /// <summary>重なっている ID (エディターが赤で出す)</summary>
    public HashSet<int> DuplicateIds()
    {
        var seen = new HashSet<int>();
        var dup  = new HashSet<int>();
        foreach (var node in Nodes) if (!seen.Add(node.Id)) dup.Add(node.Id);
        return dup;
    }

    /// <summary>ノードの、持ち主から見た位置 / 回転 (親をたどって掛ける)</summary>
    public (Vector3 Position, Quaternion Rotation) OwnerPoseOf(CollisionNode node)
    {
        var position = node.Position;
        var rotation = node.Rotation;
        var guard = 0;
        for (var parent = node.Parent; parent != null && guard < 1024; parent = parent.Parent, guard++)
        {
            position = parent.Position + Vector3.Transform(position, parent.Rotation);
            rotation = Quaternion.Normalize(Quaternion.Concatenate(rotation, parent.Rotation));   // 先に子、次に親
        }
        return (position, rotation);
    }

    private void rebuildParts()
    {
        var parts = new List<CollisionPart>(Nodes.Count);
        for (int i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            if (node.Shape == CollisionNodeShape.Empty) continue;
            var (position, rotation) = OwnerPoseOf(node);
            var shape = node.Shape switch
            {
                CollisionNodeShape.Sphere  => ColliderShape.Sphere,
                CollisionNodeShape.Capsule => ColliderShape.Capsule,
                _                          => ColliderShape.Box,
            };
            parts.Add(new CollisionPart(i, shape, position, rotation, Vector3.Max(node.Size, Vector3.Zero), node.IsTrigger,
                                        Math.Clamp(node.Layer, 0, 31), node.CollideWith, node.Friction, node.Restitution));
        }
        _parts = parts.ToArray();
        _partsVersion = Version;
    }

    // ---------------------------------------------------------------------
    // JSON
    // ---------------------------------------------------------------------

    /// <summary>JSON から読み直す (ファイルが変わった時)。前のノードは消える</summary>
    public void SetJson(string json)
    {
        Nodes.Clear();
        PreviewModel = null;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("previewModel", out var preview) && preview.ValueKind == JsonValueKind.String) PreviewModel = preview.GetString();
        var parents = new List<int>();
        if (root.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in nodes.EnumerateArray())
            {
                var node = new CollisionNode
                {
                    Id          = e.TryGetProperty("id", out var id) ? id.GetInt32() : 0,
                    Name        = e.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Shape       = e.TryGetProperty("shape", out var shape) && Enum.TryParse<CollisionNodeShape>(shape.GetString(), true, out var s) ? s : CollisionNodeShape.Box,
                    Position    = readVector3(e, "position", Vector3.Zero),
                    Rotation    = readQuaternion(e, "rotation"),
                    Size        = readVector3(e, "size", new Vector3(0.5f)),
                    IsTrigger   = e.TryGetProperty("isTrigger", out var trigger) && trigger.GetBoolean(),
                    Layer       = e.TryGetProperty("layer", out var layer) ? layer.GetInt32() : 0,
                    CollideWith = e.TryGetProperty("collideWith", out var mask) ? mask.GetUInt32() : 0xFFFFFFFFu,
                    Friction    = e.TryGetProperty("friction", out var friction) ? friction.GetSingle() : 0.5f,
                    Restitution = e.TryGetProperty("restitution", out var restitution) ? restitution.GetSingle() : 0.0f,
                };
                Nodes.Add(node);
                parents.Add(e.TryGetProperty("parent", out var parent) ? parent.GetInt32() : -1);
            }
        }
        for (int i = 0; i < Nodes.Count; i++)
        {
            var p = parents[i];
            if (p < 0 || p >= Nodes.Count || p == i) continue;
            if (Nodes[i].IsSelfOrAncestorOf(Nodes[p])) continue;   // 壊れたファイルで輪にならないように
            Nodes[i].Parent = Nodes[p];
        }
        MarkChanged();
    }

    public string ToJson()
    {
        var index = new Dictionary<CollisionNode, int>();
        for (int i = 0; i < Nodes.Count; i++) index[Nodes[i]] = i;
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", FormatVersion);
            if (!string.IsNullOrEmpty(PreviewModel)) w.WriteString("previewModel", PreviewModel);
            w.WriteStartArray("nodes");
            foreach (var node in Nodes)
            {
                w.WriteStartObject();
                w.WriteNumber("id", node.Id);
                w.WriteString("name", node.Name);
                w.WriteNumber("parent", node.Parent != null && index.TryGetValue(node.Parent, out var p) ? p : -1);
                w.WriteString("shape", node.Shape.ToString());
                writeFloats(w, "position", node.Position.X, node.Position.Y, node.Position.Z);
                writeFloats(w, "rotation", node.Rotation.X, node.Rotation.Y, node.Rotation.Z, node.Rotation.W);
                writeFloats(w, "size", node.Size.X, node.Size.Y, node.Size.Z);
                w.WriteBoolean("isTrigger", node.IsTrigger);
                w.WriteNumber("layer", node.Layer);
                w.WriteNumber("collideWith", node.CollideWith);
                w.WriteNumber("friction", node.Friction);
                w.WriteNumber("restitution", node.Restitution);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>新しいアセットの中身 (箱 1 つ)</summary>
    public static string DefaultJson()
    {
        var asset = new CollisionAsset("");
        asset.Nodes.Add(new CollisionNode { Id = 1, Name = "Root", Shape = CollisionNodeShape.Box, Position = new Vector3(0, 0.5f, 0) });
        return asset.ToJson();
    }

    private static void writeFloats(Utf8JsonWriter w, string name, params float[] values)
    {
        w.WriteStartArray(name);
        foreach (var v in values) w.WriteNumberValue(float.IsFinite(v) ? v : 0.0f);
        w.WriteEndArray();
    }

    private static Vector3 readVector3(JsonElement e, string name, Vector3 fallback)
    {
        if (!e.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() < 3) return fallback;
        return new Vector3(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle());
    }

    private static Quaternion readQuaternion(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var a) || a.ValueKind != JsonValueKind.Array || a.GetArrayLength() < 4) return Quaternion.Identity;
        var q = new Quaternion(a[0].GetSingle(), a[1].GetSingle(), a[2].GetSingle(), a[3].GetSingle());
        // ほぼ長さ 1 ならそのまま (保存 → 読み込みで値が変わらないように)。手で書いた値などはここで直す
        var lengthSq = q.LengthSquared();
        if (lengthSq < 1e-12f) return Quaternion.Identity;
        return MathF.Abs(lengthSq - 1.0f) > 1e-5f ? Quaternion.Normalize(q) : q;
    }

    public override string ToString() => $"CollisionAsset({Path}, {Nodes.Count} nodes)";
}

// ==========================================================================
// 衝突した部品 (v1.9)。Collision.SelfPart / OtherPart、RaycastHit.Part で受け取る
//   ふつうの Collider なら IsPart = false (Id = -1)
// ==========================================================================
public readonly struct ColliderPart : IEquatable<ColliderPart>
{
    private readonly CollisionAsset? _asset;
    private readonly int _assetIndex;
    private readonly int _nodeIndex;
    private readonly int _id;
    private readonly string? _name;
    private readonly bool _isTrigger;

    internal ColliderPart(CollisionAsset asset, int assetIndex, int nodeIndex, int id, string name, bool isTrigger)
    {
        _asset = asset; _assetIndex = assetIndex; _nodeIndex = nodeIndex; _id = id; _name = name; _isTrigger = isTrigger;
    }

    /// <summary>コリジョンアセットの部品か (false : ふつうの Collider)</summary>
    public bool IsPart => _asset != null;
    /// <summary>部品のアセット (ふつうの Collider は null)</summary>
    public CollisionAsset? Asset => _asset;
    /// <summary>CompoundCollider.Assets の何番目のアセットか (ふつうの Collider は -1)</summary>
    public int AssetIndex => _asset == null ? -1 : _assetIndex;
    /// <summary>エディターで付けたノードの ID (ふつうの Collider は -1)</summary>
    public int Id => _asset == null ? -1 : _id;
    /// <summary>ノードの名前 (ふつうの Collider は "")</summary>
    public string Name => _name ?? "";
    public bool IsTrigger => _isTrigger;
    internal int NodeIndex => _asset == null ? -1 : _nodeIndex;
    // 持ち主の中で部品を見分ける番号 (アセットの番号 + ノードの番号。ふつうの Collider は -1)
    internal int Code => _asset == null ? -1 : (_assetIndex << 16) | (_nodeIndex & 0xFFFF);

    public bool Equals(ColliderPart other) => Code == other.Code && ReferenceEquals(_asset, other._asset);
    public override bool Equals(object? obj) => obj is ColliderPart other && Equals(other);
    public override int GetHashCode() => Code;
    public static bool operator ==(ColliderPart a, ColliderPart b) => a.Equals(b);
    public static bool operator !=(ColliderPart a, ColliderPart b) => !a.Equals(b);
    public override string ToString() => IsPart ? $"{Name} (ID {Id}, asset {AssetIndex})" : "Collider";
}
