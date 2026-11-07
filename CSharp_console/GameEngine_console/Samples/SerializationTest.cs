using System.Collections;
using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// 保存 / 読み込みのテスト (--serialize-test。ED1)
//   1. 全部のコンポーネントの型 (IComponent) で、既定と違う値を入れたエンティティを作る (エンティティ / アセットの参照も)
//   2. JSON に保存 → 新しい World に読み込み → もう一度保存。2 つの JSON が同じ / 全部のフィールドが元の値と同じ
//   3. 見本のシーン (起動時に作った World) も同じように行き来させる
//   結果をコンソールに出して終わる (失敗があれば終了コード 1)
// ==========================================================================
internal static class SerializationTest
{
    private static int _pass, _fail;

    public static int Run(Engine engine)
    {
        var assets = engine.Assets;
        var box    = assets.LoadModel("Assets/Models/obj/box.obj");
        var mesh   = assets.LoadCollisionMesh(box);
        Sound? sound = null;
        try { sound = assets.LoadSound(@"C:\Windows\Media\chimes.wav"); } catch (Exception e) { Console.WriteLine($"[Test] (no sound : {e.Message})"); }

        // ---------- 1. 全部の型 ----------
        var world = new World();
        var types = ComponentRegistry.KnownComponents(rescan: true).ToList();
        var target = world.CreateEntity("Reference target");   // エンティティの参照の相手
        var generator = new ValueGenerator(target, box, mesh, sound);
        foreach (var type in types)
        {
            if (type.Type == typeof(EntityInfo)) continue;
            var entity = world.CreateEntity($"Test {type.Type.Name}");
            var boxed = type.CreateDefault();
            foreach (var field in type.Fields) field.SetValue(boxed, generator.Different(field.FieldType, field.GetValue(boxed)));
            world.GetPool(type.Type).SetBoxed(entity, boxed);
        }
        Console.WriteLine($"[Test] component types : {types.Count} ({string.Join(", ", types.Select(t => t.Type.Name))})");
        roundTrip("all component types", world, assets, compareFields: true);

        // ---------- 2. 見本のシーン ----------
        roundTrip("sample scene", engine.World, assets, compareFields: true);

        Console.WriteLine($"[Test] serialization : pass {_pass} / fail {_fail}");
        return _fail == 0 ? 0 : 1;
    }

    private static void roundTrip(string label, World source, AssetManager assets, bool compareFields)
    {
        var json1 = SceneSerializer.Save(source);
        var loaded = new World();
        var warnings = new List<string>();
        SceneSerializer.Load(loaded, json1, assets, warnings);
        var json2 = SceneSerializer.Save(loaded);
        foreach (var w in warnings) Console.WriteLine($"[Test]   warning : {w}");
        check($"{label} : JSON is identical after load + save ({json1.Length} chars, {source.EntityCount} entities)", json1 == json2 && warnings.Count == 0,
              json1 == json2 ? "" : firstDifference(json1, json2));
        if (!compareFields) return;

        // フィールドごとに元の値と比べる (エンティティは Id で対応させる)
        var byId = new Dictionary<Guid, Entity>();
        foreach (var e in loaded.Entities) byId[loaded.Get<EntityInfo>(e).Id] = e;
        var idOf = new Func<World, Entity, Guid?>((w, e) => w.IsAlive(e) && w.Has<EntityInfo>(e) ? w.Get<EntityInfo>(e).Id : null);
        int fields = 0, mismatches = 0;
        foreach (var entity in source.Entities)
        {
            var id = source.Get<EntityInfo>(entity).Id;
            if (!byId.TryGetValue(id, out var other)) { mismatches++; Console.WriteLine($"[Test]   missing entity {id}"); continue; }
            foreach (var pool in source.Pools)
            {
                if (!pool.Has(entity.Id)) continue;
                var type = ComponentRegistry.Get(pool.ComponentType);
                if (type.DontSave) continue;
                var otherPool = loaded.GetPool(pool.ComponentType);
                if (!otherPool.Has(other.Id)) { mismatches++; Console.WriteLine($"[Test]   missing {type.Type.Name} on {id}"); continue; }
                var a = pool.GetBoxed(entity.Id);
                var b = otherPool.GetBoxed(other.Id);
                foreach (var field in type.Fields)
                {
                    fields++;
                    if (sameValue(field.GetValue(a), field.GetValue(b), source, loaded, idOf)) continue;
                    mismatches++;
                    Console.WriteLine($"[Test]   {type.Type.Name}.{field.Name} : {field.GetValue(a)} != {field.GetValue(b)}");
                }
            }
        }
        check($"{label} : every field has the same value ({fields} fields)", mismatches == 0, $"{mismatches} mismatches");
    }

    private static bool sameValue(object? a, object? b, World wa, World wb, Func<World, Entity, Guid?> idOf)
    {
        if (a is Entity ea && b is Entity eb) return idOf(wa, ea) == idOf(wb, eb);
        if (a is IList la && b is IList lb)
        {
            if (la.Count != lb.Count) return false;
            for (int i = 0; i < la.Count; i++) if (!sameValue(la[i], lb[i], wa, wb, idOf)) return false;
            return true;
        }
        if (a != null && b != null && a.GetType().IsValueType && !a.GetType().IsPrimitive && !a.GetType().IsEnum && a.GetType().Namespace != "System.Numerics" && a is not Guid)
        {
            // 入れ子の struct はフィールドごと
            foreach (var field in ComponentType.SerializedFields(a.GetType()))
                if (!sameValue(field.GetValue(a), field.GetValue(b), wa, wb, idOf)) return false;
            if (a is Model ma && b is Model mb) return ma.Path == mb.Path;
            if (a is Sound sa && b is Sound sb) return sa.Path == sb.Path;
            return true;
        }
        return Equals(a, b);
    }

    private static string firstDifference(string a, string b)
    {
        var i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        var start = Math.Max(0, i - 80);
        return $"differs at {i} : ...{a.Substring(start, Math.Min(160, a.Length - start))}... vs ...{b.Substring(start, Math.Min(160, b.Length - start))}...";
    }

    private static void check(string name, bool ok, string detail)
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"[Test] {(ok ? "ok  " : "FAIL")} {name}{(ok || detail == "" ? "" : " : " + detail)}");
    }

    // 既定と違う値を作る (型ごと)
    private sealed class ValueGenerator
    {
        private readonly Entity _target;
        private readonly Model _model;
        private readonly CollisionMesh _mesh;
        private readonly Sound? _sound;
        private int _counter;

        public ValueGenerator(Entity target, Model model, CollisionMesh mesh, Sound? sound)
        {
            _target = target; _model = model; _mesh = mesh; _sound = sound;
        }

        public object? Different(Type type, object? current)
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var value = Make(type, attempt);
                if (!Equals(value, current) || value == null) return value;
            }
            return current;
        }

        private object? Make(Type type, int attempt)
        {
            _counter++;
            var k = _counter + attempt;
            if (type == typeof(bool))    return attempt % 2 == 0;
            if (type == typeof(int))     return 7 + k;
            if (type == typeof(uint))    return (uint)(11 + k);
            if (type == typeof(float))   return 1.25f + k * 0.5f;
            if (type == typeof(double))  return 2.5 + k;
            if (type == typeof(string))  return $"text {k} (日本語)";
            if (type == typeof(Vector2)) return new Vector2(1.5f + k, -2.25f);
            if (type == typeof(Vector3)) return new Vector3(1.5f + k, -2.25f, 3.125f);
            if (type == typeof(Vector4)) return new Vector4(0.5f, 0.25f + k, 0.75f, 1.5f);
            if (type == typeof(Quaternion)) return Quaternion.Normalize(new Quaternion(0.1f * k, 0.2f, 0.3f, 0.9f));
            if (type == typeof(Guid))    return Guid.NewGuid();
            if (type == typeof(Entity))  return _target;
            if (type == typeof(Model))   return _model;
            if (type == typeof(CollisionMesh)) return _mesh;
            if (type == typeof(Sound))   return _sound ?? default(Sound);
            if (type.IsEnum)
            {
                var values = Enum.GetValues(type);
                return values.GetValue((k + 1) % values.Length);
            }
            if (type.IsArray)
            {
                var element = type.GetElementType()!;
                var array = Array.CreateInstance(element, 2);
                array.SetValue(Make(element, 0), 0);
                array.SetValue(Make(element, 1), 1);
                return array;
            }
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(type)!;
                list.Add(Make(type.GetGenericArguments()[0], 0));
                return list;
            }
            if (type.IsValueType)
            {
                var boxed = Activator.CreateInstance(type)!;
                foreach (var field in ComponentType.SerializedFields(type)) field.SetValue(boxed, Different(field.FieldType, field.GetValue(boxed)));
                return boxed;
            }
            return null;
        }
    }
}
