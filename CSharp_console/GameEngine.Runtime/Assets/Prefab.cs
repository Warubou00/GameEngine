using System.Numerics;
using System.Text;
using System.Text.Json;

namespace GameEngine;

// ==========================================================================
// プレハブ (ED8)。エンティティの塊 (親と子) をアセットにした物 (「名前.prefab.json」。中身はシーンのファイルと同じ形。最初の物が根)
//   ゲームのコードから : engine.Assets.LoadPrefab("Assets/Prefabs/Enemy.prefab.json").Instantiate(world, position)
//   置いた物には PrefabLink が付く (どのプレハブのどの物から作ったか / 自分で変えた値)。
//   エディターはプレハブが変わると置いた物を作り直す (自分で変えた値はそのまま。ED8 の PrefabSync)
// ==========================================================================
public sealed class Prefab
{
    public string Path { get; internal set; }       // アセットのパス ("Assets/...")。名前を変えると変わる (ED10)
    public Guid   AssetId { get; }                  // アセットの Id (.meta の guid。PrefabLink.Prefab に入れる)
    public string Json { get; private set; } = "";
    public IReadOnlyList<Guid> EntityIds => _ids;   // ファイルの中のエンティティの Id (ファイルの順。0 番が根)
    public Guid RootId => _ids.Count > 0 ? _ids[0] : Guid.Empty;
    public string RootName { get; private set; } = "";

    private readonly AssetManager? _assets;
    private List<Guid> _ids = new();

    internal Prefab(string path, Guid assetId, string json, AssetManager? assets)
    {
        Path    = path;
        AssetId = assetId;
        _assets = assets;
        SetJson(json);
    }

    // 中身を差し替える (ファイルが変わった時 / エディターの Apply)
    public void SetJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var ids = new List<Guid>();
        var rootName = "";
        if (document.RootElement.TryGetProperty("entities", out var entities))
        {
            foreach (var e in entities.EnumerateArray())
            {
                ids.Add(e.TryGetProperty("id", out var id) && Guid.TryParse(id.GetString(), out var g) ? g : Guid.NewGuid());
                if (ids.Count == 1 && e.TryGetProperty("name", out var name)) rootName = name.GetString() ?? "";
            }
        }
        Json = json;
        _ids = ids;
        RootName = rootName;
    }

    /// <summary>
    /// 置く : 新しい Id のエンティティを作り、全部に PrefabLink を付ける。根は position に (parent があればその子に。position はワールド)
    /// 戻り値 = 根
    /// </summary>
    public Entity Instantiate(World world, Vector3 position, Entity parent = default)
    {
        var created = SceneSerializer.Load(world, Json, _assets, freshIds: true);
        if (created.Count == 0) return Entity.Null;
        for (int i = 0; i < created.Count && i < _ids.Count; i++)
            world.Set(created[i], new PrefabLink { Prefab = AssetId, Source = _ids[i], Overrides = Array.Empty<string>() });
        var root = created[0];
        if (!world.Has<Transform>(root)) world.Add(root, new Transform());
        ref var transform = ref world.Get<Transform>(root);
        if (world.IsAlive(parent) && world.Has<Transform>(parent) && Matrix4x4.Invert(Hierarchy.GetWorldMatrix(world, parent), out var inverse))
        {
            transform.Parent   = parent;
            transform.Position = Vector3.Transform(position, inverse);
        }
        else
        {
            transform.Parent   = Entity.Null;
            transform.Position = position;
        }
        transform.MarkDirty();
        return root;
    }

    public void Save(string file) => File.WriteAllText(file, Json, new UTF8Encoding(false));
}

// ==========================================================================
// プレハブから置いた物の印 (ED8)。プレハブの全部の物に付く
//   Prefab : プレハブのアセットの Id / Source : プレハブの中のどの物から作ったか (プレハブの中の Id)
//   Overrides : 自分で変えた値 ("型の名前.フィールド" の並び)。プレハブが変わっても、これに入っている値は変えない
//   根の Transform の Position / Rotation / Parent は、いつも自分の値 (置いた場所)
// ==========================================================================
[ComponentInfo(Hidden = true)]
public struct PrefabLink : IComponent
{
    public Guid     Prefab;
    public Guid     Source;
    public string[] Overrides;

    public readonly bool IsOverridden(string key) => Overrides != null && Array.IndexOf(Overrides, key) >= 0;
}
