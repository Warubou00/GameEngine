using System.Collections;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace GameEngine;

// ==========================================================================
// シーンの保存 / 読み込み (JSON。ED1)
//   {
//     "format": "GameEngineScene", "version": 1,
//     "entities": [
//       { "id": "GUID", "name": "...", "active": true,
//         "components": [ { "type": "GameEngine.Transform", "Position": [0, 1, 0], ... }, ... ] }
//     ]
//   }
//   - コンポーネントは ComponentRegistry の「保存するフィールド」だけを書く (C++ のハンドルなどは書かない)
//   - 読み込みは「型の既定の値 (コンストラクターの値)」から始めて、ファイルにある値だけ上書きする
//     (後でフィールドを足しても、古いファイルを読める)
//   - エンティティへの参照 (Entity) は相手の Id (GUID)、アセット (Model / Sound / CollisionMesh) はパスで書く
//     (アセットの GUID は ED5 の Assets フォルダで)
//   - 値の書き方は型ごとに足せる (RegisterConverter。ユーザーの型も)
// ==========================================================================
public static class SceneSerializer
{
    public const string Format  = "GameEngineScene";
    public const int    Version = 1;

    // 書く時 : エンティティ → Id
    public sealed class WriteContext
    {
        internal readonly Dictionary<Entity, Guid> Ids = new();
        public World World { get; }
        internal WriteContext(World world) => World = world;
        // 表に無い物は World から引く (コンポーネント 1 つだけを書く時に、全部の物の表を作らないため。ED4)
        public Guid? IdOf(Entity entity)
        {
            if (Ids.TryGetValue(entity, out var id)) return id;
            if (!World.IsAlive(entity)) return null;
            if (!World.Has<EntityInfo>(entity)) World.Add(entity, new EntityInfo($"Entity {entity.Id}"));
            ref var info = ref World.Get<EntityInfo>(entity);
            if (info.Id == Guid.Empty) { info.Id = Guid.NewGuid(); World.Reindex(entity); }   // v1.18 : Id で引く索引も直す
            return Ids[entity] = info.Id;
        }
    }

    // 読む時 : Id → エンティティ / アセット
    public sealed class ReadContext
    {
        internal readonly Dictionary<Guid, Entity> Entities = new();
        internal Dictionary<Guid, Entity>? Existing;   // ファイルに無い Id は、World に今ある物から探す (一部だけを書いた JSON。ED4)
        public World         World  { get; }
        public AssetManager? Assets { get; }
        public List<string>  Warnings { get; } = new();
        internal ReadContext(World world, AssetManager? assets) { World = world; Assets = assets; }
        public Entity EntityOf(Guid id) =>
            Entities.TryGetValue(id, out var e) ? e : Existing != null && Existing.TryGetValue(id, out var found) && World.IsAlive(found) ? found : Entity.Null;
    }

    private sealed record Converter(Action<Utf8JsonWriter, object, WriteContext> Write, Func<JsonElement, ReadContext, object?> Read);
    private static readonly Dictionary<Type, Converter> _converters = new();

    /// <summary>型 T の書き方 / 読み方を足す (ユーザーのアセットの型など)</summary>
    public static void RegisterConverter<T>(Action<Utf8JsonWriter, T, WriteContext> write, Func<JsonElement, ReadContext, T> read)
    {
        lock (_converters) _converters[typeof(T)] = new Converter((w, v, c) => write(w, (T)v, c), (e, c) => read(e, c));
    }

    static SceneSerializer()
    {
        RegisterConverter<Vector2>((w, v, _) => writeFloats(w, v.X, v.Y), (e, _) => { var f = readFloats(e, 2); return new Vector2(f[0], f[1]); });
        RegisterConverter<Vector3>((w, v, _) => writeFloats(w, v.X, v.Y, v.Z), (e, _) => { var f = readFloats(e, 3); return new Vector3(f[0], f[1], f[2]); });
        RegisterConverter<Vector4>((w, v, _) => writeFloats(w, v.X, v.Y, v.Z, v.W), (e, _) => { var f = readFloats(e, 4); return new Vector4(f[0], f[1], f[2], f[3]); });
        RegisterConverter<Quaternion>((w, v, _) => writeFloats(w, v.X, v.Y, v.Z, v.W), (e, _) => { var f = readFloats(e, 4); return new Quaternion(f[0], f[1], f[2], f[3]); });
        RegisterConverter<Guid>((w, v, _) => w.WriteStringValue(v.ToString()), (e, _) => Guid.Parse(e.GetString()!));

        // エンティティ : 相手の Id (シーンの外の物 / 無い物は null)
        RegisterConverter<Entity>(
            (w, v, c) => { if (c.IdOf(v) is { } id) w.WriteStringValue(id.ToString()); else w.WriteNullValue(); },
            (e, c) => e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var id) ? c.EntityOf(id) : Entity.Null);
        // v1.18 : GameObject も Entity と同じく相手の Id (読む時は読み込み先の World の物)
        RegisterConverter<GameObject>(
            (w, v, c) => { if (v.IsValid && ReferenceEquals(v.World, c.World) && c.IdOf(v.Entity) is { } id) w.WriteStringValue(id.ToString()); else w.WriteNullValue(); },
            (e, c) => e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var id) ? new GameObject(c.World, c.EntityOf(id)) : GameObject.None);

        // v1.20 GO3 : プレハブ (.prefab.json) / シーンへの参照 (.scene.json。読み込みはしない。Id があれば今のパスを引く)
        RegisterConverter<Prefab?>(
            (w, v, _) => writeAsset(w, v?.Path),
            (e, c) => readAsset<Prefab?>(e, c, path => c.Assets!.LoadPrefab(path), null));
        RegisterConverter<SceneReference>(
            (w, v, _) => { if (!v.IsValid) w.WriteNullValue(); else { w.WriteStartObject(); if (v.AssetId != Guid.Empty) w.WriteString("guid", v.AssetId.ToString()); w.WriteString("path", v.Path); w.WriteEndObject(); } },
            (e, _) => e.ValueKind != JsonValueKind.Object ? default :
                new SceneReference(e.TryGetProperty("path", out var p) ? p.GetString() : null,
                                   e.TryGetProperty("guid", out var g) && Guid.TryParse(g.GetString(), out var guid) ? guid : Guid.Empty));

        // アセット : パス (読む時は AssetManager で読む。同じパスは 1 回だけ読まれる)
        RegisterConverter<Model>(
            (w, v, _) => writeAsset(w, v.IsValid ? v.Path : null),
            (e, c) => readAsset(e, c, path => c.Assets!.LoadModel(path), default(Model)));
        RegisterConverter<Sound>(
            (w, v, _) => writeAsset(w, v.IsValid ? v.Path : null),
            (e, c) => readAsset(e, c, path => c.Assets!.LoadSound(path), default(Sound)));
        RegisterConverter<Material?>(
            (w, v, _) => writeAsset(w, string.IsNullOrEmpty(v?.Path) ? null : v.Path),
            (e, c) => readAsset<Material?>(e, c, path => c.Assets!.LoadMaterial(path), null));
        RegisterConverter<CollisionMesh?>(
            (w, v, _) => writeAsset(w, v?.Path, "model"),
            (e, c) => readAsset<CollisionMesh?>(e, c, path => c.Assets!.LoadCollisionMesh(c.Assets.LoadModel(path)), null, "model"));
        // AN3 : スケルトン / アニメーション (FBX)
        RegisterConverter<Skeleton?>(
            (w, v, _) => writeAsset(w, v?.Path),
            (e, c) => readAsset<Skeleton?>(e, c, path => c.Assets!.LoadSkeleton(path), null));
        RegisterConverter<AnimationClip?>(
            (w, v, _) => writeAsset(w, v?.Path),
            (e, c) => readAsset<AnimationClip?>(e, c, path => c.Assets!.LoadAnimationClip(path), null));
        // v1.7 : UI の Widget (.widget.json)
        RegisterConverter<WidgetAsset?>(
            (w, v, _) => writeAsset(w, v?.Path),
            (e, c) => readAsset<WidgetAsset?>(e, c, path => c.Assets!.LoadWidget(path), null));
        // v1.9 : コリジョンアセット (.collision.json)
        RegisterConverter<CollisionAsset?>(
            (w, v, _) => writeAsset(w, v?.Path),
            (e, c) => readAsset<CollisionAsset?>(e, c, path => c.Assets!.LoadCollisionAsset(path), null));
    }

    // ---------------------------------------------------------------------
    // 保存
    // ---------------------------------------------------------------------

    /// <summary>World の全部のエンティティを JSON にする (EntityInfo が無い物には付ける)</summary>
    // v1.14 : メインシーンでない物 (サブシーンの中の物など) は書かない (それぞれのファイルにある物。以前は全部を書いていたので、サブシーンの中の物がメインシーンに重なって入った)
    public static string Save(World world) => Save(world, world.Entities.Where(e => !IsOutsideMainScene(world, e)).ToList());

    /// <summary>今のメインシーンでないシーン (サブシーン / 足したシーン / 永続) の物か (v1.14。メインシーンを保存する時に飛ばす)</summary>
    public static bool IsOutsideMainScene(World world, Entity entity) =>
        world.Has<EntityInfo>(entity) && (world.SceneFlags(world.Get<EntityInfo>(entity).SceneId) & World.SceneNotMain) != 0;

    /// <summary>
    /// 一部のエンティティだけを JSON にする (エディターの元に戻す / 複製。ED4)
    /// 外の物への参照も Id で書く (読む時に World に今ある物から探す)
    /// </summary>
    public static string Save(World world, IReadOnlyList<Entity> entities)
    {
        var context = assignIds(world);
        var infos = world.GetPool<EntityInfo>();

        // 型の名前の順 (ファイルの差分が見やすいように、毎回同じ順で書く)
        var pools = world.Pools
            .Where(p => p.ComponentType != typeof(EntityInfo))
            .Select(p => (Pool: p, Type: ComponentRegistry.Get(p.ComponentType)))
            .Where(p => !p.Type.DontSave)
            .OrderBy(p => p.Type.TypeName, StringComparer.Ordinal)
            .ToList();

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", Version);
            w.WriteStartArray("entities");
            foreach (var entity in entities)
            {
                var info = infos.Get(entity.Id);
                w.WriteStartObject();
                w.WriteString("id", info.Id.ToString());
                w.WriteString("name", info.Name ?? "");
                w.WriteBoolean("active", info.Active);
                if (info.Layer != 0) w.WriteNumber("layer", info.Layer);   // v1.5 : レイヤー (0 なら書かない。以前のファイルと同じ中身のまま)
                if (!string.IsNullOrEmpty(info.Tag)) w.WriteString("tag", info.Tag);   // v1.18 : タグ (Untagged なら書かない)
                w.WriteStartArray("components");
                foreach (var (pool, type) in pools)
                {
                    if (!pool.Has(entity.Id)) continue;
                    w.WriteStartObject();
                    w.WriteString("type", type.TypeName);
                    var boxed = pool.GetBoxed(entity.Id);
                    foreach (var field in type.Fields)
                    {
                        w.WritePropertyName(field.Name);
                        writeValue(w, field.FieldType, field.GetValue(boxed), context);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // 全部のエンティティに Id を付けて、エンティティ → Id の表を作る
    private static WriteContext assignIds(World world)
    {
        var context = new WriteContext(world);
        var infos = world.GetPool<EntityInfo>();
        foreach (var entity in world.Entities)
        {
            if (!infos.Has(entity.Id)) world.Add(entity, new EntityInfo($"Entity {entity.Id}"));
            ref var info = ref infos.Get(entity.Id);
            if (info.Id == Guid.Empty) { info.Id = Guid.NewGuid(); world.Reindex(entity); }   // v1.18 : Id で引く索引も直す
            context.Ids[entity] = info.Id;
        }
        return context;
    }

    /// <summary>Id (EntityInfo.Id) → 今のエンティティ (無ければ Entity.Null)</summary>
    //   v1.18 : World の索引で引く (以前は毎回全部の物を順に見ていた)。索引で見つからない時だけ、今まで通り順に見る
    public static Entity FindEntity(World world, Guid id)
    {
        var found = world.FindById(id);
        if (!found.IsNull) return found;
        var infos = world.GetPool<EntityInfo>();
        var entities = infos.Entities;
        var components = infos.Components;
        for (int i = 0; i < components.Length; i++) if (components[i].Id == id) return entities[i];
        return Entity.Null;
    }

    /// <summary>コンポーネント 1 つの保存する値を JSON にする (エディターの元に戻す。ED4)</summary>
    public static string SaveComponent(World world, Entity entity, Type componentType)
    {
        var context = new WriteContext(world);
        var type = ComponentRegistry.Get(componentType);
        var boxed = world.GetPool(componentType).GetBoxed(entity.Id);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            foreach (var field in type.Fields)
            {
                w.WritePropertyName(field.Name);
                writeValue(w, field.FieldType, field.GetValue(boxed), context);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// SaveComponent の JSON を、箱に入れたコンポーネントへ書き戻す (保存しないフィールド (C++ のハンドルなど) はそのまま残る)
    /// 箱が null なら型の既定の値から作る。戻り値 = 書いた箱
    /// </summary>
    public static object LoadComponent(World world, Type componentType, string json, object? boxed, AssetManager? assets)
    {
        var type = ComponentRegistry.Get(componentType);
        boxed ??= type.CreateDefault();
        var context = new ReadContext(world, assets) { Existing = existingIds(world) };
        using var document = JsonDocument.Parse(json);
        foreach (var field in type.Fields)
        {
            if (!document.RootElement.TryGetProperty(field.Name, out var value)) continue;
            field.SetValue(boxed, readValue(value, field.FieldType, context));
        }
        return boxed;
    }

    private static Dictionary<Guid, Entity> existingIds(World world)
    {
        var map = new Dictionary<Guid, Entity>();
        var infos = world.GetPool<EntityInfo>();
        var entities = infos.Entities;
        var components = infos.Components;
        for (int i = 0; i < components.Length; i++) map.TryAdd(components[i].Id, entities[i]);
        return map;
    }

    public static void SaveFile(World world, string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(path, Save(world), new UTF8Encoding(false));
    }

    // ---------------------------------------------------------------------
    // 読み込み
    // ---------------------------------------------------------------------

    /// <summary>
    /// JSON のエンティティを World に足す (今ある物は消さない)。戻り値は作ったエンティティ (ファイルの順)
    /// 知らない型 / 読めない値は飛ばして warnings に入れる (読み込みは止めない)
    /// </summary>
    // freshIds : 全部の物に新しい Id を付ける (プレハブを置く時。ED8)。中の参照は新しい物どうしに繋がる
    // v1.14 : 中身は SceneLoader (少しずつ進められる読み込み) を最後まで回す。非同期の読み込みも同じ道を通るので、作る物と順番は同じ
    public static List<Entity> Load(World world, string json, AssetManager? assets, List<string>? warnings = null, bool freshIds = false) =>
        Load(world, json, assets, warnings, freshIds, sceneId: 0);

    internal static List<Entity> Load(World world, string json, AssetManager? assets, List<string>? warnings, bool freshIds, int sceneId)
    {
        using var loader = new SceneLoader(world, json, assets, freshIds, sceneId);
        loader.Parse();
        while (loader.Step()) { }
        warnings?.AddRange(loader.Warnings);
        return loader.Created;
    }

    // ---------------------------------------------------------------------
    // 少しずつ進められる読み込み (v1.14)
    //   Parse : JSON を解析する (World に触らないので、別のスレッドで呼んでも良い)
    //   Step  : 1 回目で全部のエンティティを作り (参照が後の物を指していても引けるように)、その後は 1 回で 1 つの物のコンポーネントを入れる
    //           (アセットはここで読む = メインのスレッド)。戻り値 = まだ続きがあるか
    //   以前は Load の中で全部を一度に行ったので、途中で止めて次のフレームに続ける (ロード画面を動かしながら読む) ことができなかった
    // ---------------------------------------------------------------------
    internal sealed class SceneLoader : IDisposable
    {
        private readonly World _world;
        private readonly string _json;
        private readonly bool _freshIds;
        private readonly int _sceneId;
        private readonly ReadContext _context;
        private JsonDocument? _document;
        private JsonElement[] _elements = [];
        private int _next = -1;   // -1 = エンティティをまだ作っていない

        public List<Entity> Created { get; } = new();
        public List<string> Warnings => _context.Warnings;
        /// <summary>全部の手数 (エンティティを作る 1 + 物の数) と済んだ数</summary>
        public int Total => _elements.Length + 1;
        public int Done => _next < 0 ? 0 : _next + 1;
        /// <summary>全部の物を作り終えたか</summary>
        public bool IsFinished => _next >= 0 && _next >= _elements.Length;
        /// <summary>最後の Step でコンポーネントを入れた物 (遅い物を調べる用。v1.14 S2)</summary>
        public Entity LastEntity { get; private set; }

        public SceneLoader(World world, string json, AssetManager? assets, bool freshIds, int sceneId)
        {
            _world = world;
            _json = json;
            _freshIds = freshIds;
            _sceneId = sceneId;
            _context = new ReadContext(world, assets);
        }

        public void Parse()
        {
            if (_document != null) return;
            _document = JsonDocument.Parse(_json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = _document.RootElement;
            if (root.TryGetProperty("format", out var format) && format.GetString() != Format) throw new InvalidDataException($"Not a scene file (format {format.GetString()}).");
            _elements = root.TryGetProperty("entities", out var entities) ? entities.EnumerateArray().ToArray() : [];
        }

        public bool Step()
        {
            Parse();
            if (_next < 0)
            {
                createEntities();
                _next = 0;
                return _elements.Length > 0;
            }
            if (_next >= _elements.Length) return false;
            LastEntity = Created[_next];
            readComponents(_elements[_next], Created[_next]);
            _next++;
            return _next < _elements.Length;
        }

        // 1. エンティティを全部作る (参照が後の物を指していても引けるように)
        private void createEntities()
        {
            var existing = _context.Existing = existingIds(_world);
            foreach (var element in _elements)
            {
                var fileId = element.TryGetProperty("id", out var idElement) && Guid.TryParse(idElement.GetString(), out var parsed) ? parsed : Guid.NewGuid();
                var info = new EntityInfo
                {
                    Id     = _freshIds || (existing.ContainsKey(fileId) && _world.IsAlive(existing[fileId])) ? Guid.NewGuid() : fileId,   // 同じシーンを 2 回読んだ時は新しい Id
                    Name   = element.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    Active = !element.TryGetProperty("active", out var active) || active.GetBoolean(),
                    Layer  = element.TryGetProperty("layer", out var layer) && layer.TryGetInt32(out var layerValue) ? Math.Clamp(layerValue, 0, 31) : 0,   // v1.5
                    Tag    = element.TryGetProperty("tag", out var tag) && tag.ValueKind == JsonValueKind.String && tag.GetString() is { Length: > 0 } tagValue ? tagValue : null,   // v1.18
                    SceneId = _sceneId,   // v1.14 : どのシーンの物か
                };
                var entity = _world.CreateEntity();
                _world.Add(entity, info);
                // v1.14 : 無効なシーン (待機) の物は、コンポーネントを入れる前から無効 (読み込みの途中の物を描画 / 物理が拾わないように)
                if (!_world.IsSelfActive(info)) _world.SetActiveInHierarchy(entity.Id, false);
                _context.Entities[fileId] = entity;
                Created.Add(entity);
            }
        }

        // 2. コンポーネント
        private void readComponents(JsonElement element, Entity entity)
        {
            if (!element.TryGetProperty("components", out var components)) return;
            foreach (var componentElement in components.EnumerateArray())
            {
                var typeName = componentElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                var type = ComponentRegistry.Find(typeName);
                if (type == null)
                {
                    _context.Warnings.Add($"Unknown component type '{typeName}' on {_world.Get<EntityInfo>(entity).Name} (skipped)");
                    continue;
                }
                var boxed = type.CreateDefault();
                foreach (var field in type.Fields)
                {
                    if (!componentElement.TryGetProperty(field.Name, out var value)) continue;   // 無い値は既定のまま
                    try
                    {
                        field.SetValue(boxed, readValue(value, field.FieldType, _context));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        _context.Warnings.Add($"{type.Type.Name}.{field.Name} : {ex.Message}");
                    }
                }
                _world.GetPool(type.Type).SetBoxed(entity, boxed);
            }
        }

        /// <summary>
        /// 使うアセットのパス (v1.14 S2。コンポーネントの中の { "path": ... } / { "model": ... })。Parse の後、別のスレッドで呼んで良い
        /// 非同期の読み込みが、先にファイルを読んでおく (OS のキャッシュに載せる) のに使う
        /// </summary>
        public HashSet<string> CollectAssetPaths()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var element in _elements)
            {
                if (element.TryGetProperty("components", out var components)) collectPaths(components, result);
            }
            return result;
        }

        private static void collectPaths(JsonElement e, HashSet<string> result)
        {
            switch (e.ValueKind)
            {
            case JsonValueKind.Object:
                foreach (var property in e.EnumerateObject())
                {
                    if ((property.NameEquals("path") || property.NameEquals("model")) && property.Value.ValueKind == JsonValueKind.String &&
                        property.Value.GetString() is { Length: > 0 } path && !path.StartsWith("builtin:", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(path.IndexOf('#') is var hash and >= 0 ? path[..hash] : path);   // 部品 (xxx.mod.fbx#ノード) はファイルの所まで
                    }
                    else collectPaths(property.Value, result);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray()) collectPaths(item, result);
                break;
            }
        }

        public void Dispose() => _document?.Dispose();
    }

    public static List<Entity> LoadFile(World world, string path, AssetManager? assets, List<string>? warnings = null) =>
        Load(world, File.ReadAllText(path, Encoding.UTF8), assets, warnings);

    // ---------------------------------------------------------------------
    // 値
    // ---------------------------------------------------------------------

    private static void writeValue(Utf8JsonWriter w, Type type, object? value, WriteContext context)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying != null) type = underlying;
        if (value == null) { w.WriteNullValue(); return; }
        Converter? converter;
        lock (_converters) _converters.TryGetValue(type, out converter);
        if (converter != null) { converter.Write(w, value, context); return; }
        // v1.20 GO3 : ComponentRef<T> など (型引数ごとに書き方を登録しないで、相手の Id を書く)
        if (value is IEntityRef reference)
        {
            if (reference.World != null && ReferenceEquals(reference.World, context.World) && context.IdOf(reference.Entity) is { } refId) w.WriteStringValue(refId.ToString());
            else w.WriteNullValue();
            return;
        }

        switch (value)
        {
        case bool b:    w.WriteBooleanValue(b); return;
        case string s:  w.WriteStringValue(s); return;
        case float f:   writeFloat(w, f); return;
        case double d:  if (double.IsFinite(d)) w.WriteNumberValue(d); else w.WriteStringValue(d.ToString(System.Globalization.CultureInfo.InvariantCulture)); return;
        case Enum e:    w.WriteStringValue(e.ToString()); return;
        case byte or sbyte or short or ushort or int: w.WriteNumberValue(Convert.ToInt64(value)); return;
        case uint u:    w.WriteNumberValue(u); return;
        case long l:    w.WriteNumberValue(l); return;
        case ulong ul:  w.WriteNumberValue(ul); return;
        }

        if (value is IList list && elementType(type) is { } element)
        {
            w.WriteStartArray();
            foreach (var item in list) writeValue(w, element, item, context);
            w.WriteEndArray();
            return;
        }
        if (type.IsValueType)
        {
            // 入れ子の struct : 保存するフィールドを書く
            w.WriteStartObject();
            foreach (var field in ComponentType.SerializedFields(type))
            {
                w.WritePropertyName(field.Name);
                writeValue(w, field.FieldType, field.GetValue(value), context);
            }
            w.WriteEndObject();
            return;
        }
        w.WriteNullValue();   // 書き方の分からないクラス (RegisterConverter で足せる)
    }

    private static object? readValue(JsonElement e, Type type, ReadContext context)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (e.ValueKind == JsonValueKind.Null) return underlying != null || !type.IsValueType ? null : Activator.CreateInstance(type);
        if (underlying != null) type = underlying;
        Converter? converter;
        lock (_converters) _converters.TryGetValue(type, out converter);
        if (converter != null) return converter.Read(e, context);
        // v1.20 GO3 : ComponentRef<T> など (読み込み先の World の物。無い物は空の値)
        if (EntityRefs.IsEntityRef(type))
            return EntityRefs.Create(type, context.World, e.ValueKind == JsonValueKind.String && Guid.TryParse(e.GetString(), out var refGuid) ? context.EntityOf(refGuid) : Entity.Null);

        if (type == typeof(bool))   return e.GetBoolean();
        if (type == typeof(string)) return e.GetString();
        if (type == typeof(float))  return e.ValueKind == JsonValueKind.String ? float.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : e.GetSingle();
        if (type == typeof(double)) return e.ValueKind == JsonValueKind.String ? double.Parse(e.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : e.GetDouble();
        if (type.IsEnum)            return e.ValueKind == JsonValueKind.Number ? Enum.ToObject(type, e.GetInt64()) : Enum.Parse(type, e.GetString()!);
        if (type == typeof(int))    return e.GetInt32();
        if (type == typeof(uint))   return e.GetUInt32();
        if (type == typeof(long))   return e.GetInt64();
        if (type == typeof(ulong))  return e.GetUInt64();
        if (type == typeof(short))  return e.GetInt16();
        if (type == typeof(ushort)) return e.GetUInt16();
        if (type == typeof(byte))   return e.GetByte();
        if (type == typeof(sbyte))  return e.GetSByte();

        if (elementType(type) is { } element && e.ValueKind == JsonValueKind.Array)
        {
            var items = e.EnumerateArray().Select(item => readValue(item, element, context)).ToList();
            if (type.IsArray)
            {
                var array = Array.CreateInstance(element, items.Count);
                for (int i = 0; i < items.Count; i++) array.SetValue(items[i], i);
                return array;
            }
            var list = (IList)Activator.CreateInstance(type)!;
            foreach (var item in items) list.Add(item);
            return list;
        }
        if (type.IsValueType && e.ValueKind == JsonValueKind.Object)
        {
            var boxed = Activator.CreateInstance(type)!;
            foreach (var field in ComponentType.SerializedFields(type))
            {
                if (e.TryGetProperty(field.Name, out var value)) field.SetValue(boxed, readValue(value, field.FieldType, context));
            }
            return boxed;
        }
        throw new NotSupportedException($"Cannot read {type.Name} from {e.ValueKind}");
    }

    private static Type? elementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return type.GetGenericArguments()[0];
        return null;
    }

    // float : 有限なら数 (一番短く元に戻る書き方)、NaN / 無限大は文字列 (JSON の数では書けない)
    private static void writeFloat(Utf8JsonWriter w, float f)
    {
        if (float.IsFinite(f)) w.WriteNumberValue(f);
        else w.WriteStringValue(f.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void writeFloats(Utf8JsonWriter w, params float[] values)
    {
        w.WriteStartArray();
        foreach (var v in values) writeFloat(w, v);
        w.WriteEndArray();
    }

    private static float[] readFloats(JsonElement e, int count)
    {
        var result = new float[count];
        var i = 0;
        foreach (var item in e.EnumerateArray())
        {
            if (i >= count) break;
            result[i++] = item.ValueKind == JsonValueKind.String ? float.Parse(item.GetString()!, System.Globalization.CultureInfo.InvariantCulture) : item.GetSingle();
        }
        return result;
    }

    // アセット : { "guid": "...", "path": "Assets/..." } (ED5。プロジェクトの外の物はパスだけ)
    private static void writeAsset(Utf8JsonWriter w, string? path, string key = "path")
    {
        if (path == null) { w.WriteNullValue(); return; }
        w.WriteStartObject();
        // AN3 : 部品 (xxx.mod.fbx#ノード) は # の前のアセットの Id
        var hash = path.IndexOf('#');
        if (AssetDatabase.Current?.Find(hash >= 0 ? path[..hash] : path) is { } entry) w.WriteString("guid", entry.Guid.ToString());
        w.WriteString(key, path);
        w.WriteEndObject();
    }

    private static T readAsset<T>(JsonElement e, ReadContext context, Func<string, T> load, T fallback, string key = "path")
    {
        if (e.ValueKind != JsonValueKind.Object) return fallback;
        string? path = e.TryGetProperty(key, out var p) ? p.GetString() : null;
        // Id があれば Id を優先する (ファイルを動かした / 名前を変えた後も見つかる。ED5)
        if (e.TryGetProperty("guid", out var g) && Guid.TryParse(g.GetString(), out var guid) && AssetDatabase.Current?.Find(guid) is { } entry)
            path = entry.Path + (path != null && path.IndexOf('#') is var hash and >= 0 ? path[hash..] : "");   // AN3 : # の後 (部品) は残す
        if (path == null) return fallback;
        if (context.Assets == null)
        {
            context.Warnings.Add($"No AssetManager : '{path}' was not loaded");
            return fallback;
        }
        try
        {
            return load(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            context.Warnings.Add($"Asset '{path}' : {ex.Message}");
            return fallback;
        }
    }
}
