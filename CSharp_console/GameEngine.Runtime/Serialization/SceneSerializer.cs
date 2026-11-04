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
        public Guid? IdOf(Entity entity) => Ids.TryGetValue(entity, out var id) ? id : null;
    }

    // 読む時 : Id → エンティティ / アセット
    public sealed class ReadContext
    {
        internal readonly Dictionary<Guid, Entity> Entities = new();
        public World         World  { get; }
        public AssetManager? Assets { get; }
        public List<string>  Warnings { get; } = new();
        internal ReadContext(World world, AssetManager? assets) { World = world; Assets = assets; }
        public Entity EntityOf(Guid id) => Entities.TryGetValue(id, out var e) ? e : Entity.Null;
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

        // アセット : パス (読む時は AssetManager で読む。同じパスは 1 回だけ読まれる)
        RegisterConverter<Model>(
            (w, v, _) => writeAsset(w, v.IsValid ? v.Path : null),
            (e, c) => readAsset(e, c, path => c.Assets!.LoadModel(path), default(Model)));
        RegisterConverter<Sound>(
            (w, v, _) => writeAsset(w, v.IsValid ? v.Path : null),
            (e, c) => readAsset(e, c, path => c.Assets!.LoadSound(path), default(Sound)));
        RegisterConverter<CollisionMesh?>(
            (w, v, _) => writeAsset(w, v?.Path, "model"),
            (e, c) => readAsset<CollisionMesh?>(e, c, path => c.Assets!.LoadCollisionMesh(c.Assets.LoadModel(path)), null, "model"));
    }

    // ---------------------------------------------------------------------
    // 保存
    // ---------------------------------------------------------------------

    /// <summary>World の全部のエンティティを JSON にする (EntityInfo が無い物には付ける)</summary>
    public static string Save(World world)
    {
        var context = new WriteContext(world);
        var entities = world.Entities.ToList();
        var infos = world.GetPool<EntityInfo>();
        foreach (var entity in entities)
        {
            if (!infos.Has(entity.Id)) world.Add(entity, new EntityInfo($"Entity {entity.Id}"));
            ref var info = ref infos.Get(entity.Id);
            if (info.Id == Guid.Empty) info.Id = Guid.NewGuid();
            context.Ids[entity] = info.Id;
        }

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
    public static List<Entity> Load(World world, string json, AssetManager? assets, List<string>? warnings = null)
    {
        var context = new ReadContext(world, assets);
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;
        if (root.TryGetProperty("format", out var format) && format.GetString() != Format) throw new InvalidDataException($"Not a scene file (format {format.GetString()}).");
        var created = new List<Entity>();
        if (!root.TryGetProperty("entities", out var entities)) return created;

        // 1. エンティティを全部作る (参照が後の物を指していても引けるように)
        var existing = new HashSet<Guid>();
        foreach (var e in world.Entities) if (world.Has<EntityInfo>(e)) existing.Add(world.Get<EntityInfo>(e).Id);
        foreach (var element in entities.EnumerateArray())
        {
            var fileId = element.TryGetProperty("id", out var idElement) && Guid.TryParse(idElement.GetString(), out var parsed) ? parsed : Guid.NewGuid();
            var info = new EntityInfo
            {
                Id     = existing.Contains(fileId) ? Guid.NewGuid() : fileId,   // 同じシーンを 2 回読んだ時は新しい Id
                Name   = element.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                Active = !element.TryGetProperty("active", out var active) || active.GetBoolean(),
            };
            var entity = world.CreateEntity();
            world.Add(entity, info);
            context.Entities[fileId] = entity;
            created.Add(entity);
        }

        // 2. コンポーネント
        var index = 0;
        foreach (var element in entities.EnumerateArray())
        {
            var entity = created[index++];
            if (!element.TryGetProperty("components", out var components)) continue;
            foreach (var componentElement in components.EnumerateArray())
            {
                var typeName = componentElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                var type = ComponentRegistry.Find(typeName);
                if (type == null)
                {
                    context.Warnings.Add($"Unknown component type '{typeName}' on {world.Get<EntityInfo>(entity).Name} (skipped)");
                    continue;
                }
                var boxed = type.CreateDefault();
                foreach (var field in type.Fields)
                {
                    if (!componentElement.TryGetProperty(field.Name, out var value)) continue;   // 無い値は既定のまま
                    try
                    {
                        field.SetValue(boxed, readValue(value, field.FieldType, context));
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        context.Warnings.Add($"{type.Type.Name}.{field.Name} : {ex.Message}");
                    }
                }
                world.GetPool(type.Type).SetBoxed(entity, boxed);
            }
        }
        warnings?.AddRange(context.Warnings);
        return created;
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

    private static void writeAsset(Utf8JsonWriter w, string? path, string key = "path")
    {
        if (path == null) { w.WriteNullValue(); return; }
        w.WriteStartObject();
        w.WriteString(key, path);
        w.WriteEndObject();
    }

    private static T readAsset<T>(JsonElement e, ReadContext context, Func<string, T> load, T fallback, string key = "path")
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(key, out var p) || p.GetString() is not { } path) return fallback;
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
