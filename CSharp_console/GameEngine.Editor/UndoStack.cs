namespace GameEngine.Editor;

// ==========================================================================
// 元に戻す / やり直す (ED4)
//   変更を「前の値 → 後の値」で記録する。値は ED1 のシリアライズ (JSON) で持つので、配列などの参照の値もコピーになる
//   エンティティは Entity (番号) ではなく EntityInfo.Id (GUID) で覚える
//   (消して戻すと番号が変わるので。参照している側の値も GUID で書いてあるので、戻した後の新しい番号に繋がる)
//   - ComponentEdit : コンポーネント 1 つの値 / 足す / 外す (EntityInfo の名前 / 有効もこれ)
//   - EntitiesEdit  : エンティティを作る / 消す / 複製 (子を含めた JSON)
//   - CompoundEdit  : いくつかをまとめて 1 回の操作にする
//   ドラッグ中の連続した変更 (インスペクターの数値 / ギズモ) は、同じ相手の変更を 1 つにまとめる (Record の mergeKey)
// ==========================================================================
internal abstract class EditCommand
{
    public abstract string Name { get; }
    public int Revision;   // まとめた (After を差し替えた) 回数 (保存した時と同じ状態かを見る)
    public abstract void Undo(World world, AssetManager assets);
    public abstract void Redo(World world, AssetManager assets);
}

internal static class EditorWorld
{
    // コンポーネントを書く (Transform は行列を作り直させる / EntityInfo は有効・無効も反映する)
    public static void WriteComponent(World world, Entity entity, Type type, object boxed)
    {
        world.GetPool(type).SetBoxed(entity, boxed);
        if (type == typeof(Transform)) world.Get<Transform>(entity).MarkDirty();
        else if (type == typeof(EntityInfo)) world.SetActive(entity, ((EntityInfo)boxed).Active);
    }

    // 子と孫を含めた一覧 (親が先)
    public static List<Entity> WithDescendants(World world, Entity root)
    {
        var result = new List<Entity> { root };
        for (int i = 0; i < result.Count; i++) result.AddRange(Hierarchy.GetChildren(world, result[i]));
        return result;
    }

    public static Guid IdOf(World world, Entity entity)
    {
        if (!world.Has<EntityInfo>(entity)) world.Add(entity, new EntityInfo($"Entity {entity.Id}"));
        ref var info = ref world.Get<EntityInfo>(entity);
        if (info.Id == Guid.Empty) info.Id = Guid.NewGuid();
        return info.Id;
    }
}

// コンポーネント 1 つ。Before / After が null = 持っていない
internal sealed class ComponentEdit : EditCommand
{
    public readonly Guid   Entity;
    public readonly Type   Type;
    public readonly string? Before;
    public string?         After;

    public ComponentEdit(Guid entity, Type type, string? before, string? after)
    {
        Entity = entity; Type = type; Before = before; After = after;
    }

    public override string Name =>
        Before == null ? $"Add {Type.Name}" : After == null ? $"Remove {Type.Name}" : $"Edit {Type.Name}";

    public override void Undo(World world, AssetManager assets) => apply(world, assets, Before);
    public override void Redo(World world, AssetManager assets) => apply(world, assets, After);

    private void apply(World world, AssetManager assets, string? json)
    {
        var entity = SceneSerializer.FindEntity(world, Entity);
        if (!world.IsAlive(entity)) return;
        var pool = world.GetPool(Type);
        if (json == null)
        {
            pool.Remove(entity.Id);
            return;
        }
        // 今の値に保存する値だけを書き戻す (C++ のハンドルなど、保存しない値はそのまま)
        var boxed = SceneSerializer.LoadComponent(world, Type, json, pool.Has(entity.Id) ? pool.GetBoxed(entity.Id) : null, assets);
        EditorWorld.WriteComponent(world, entity, Type, boxed);
    }
}

// エンティティ (子を含む) を作る / 消す。Json は SceneSerializer.Save の一部分
internal sealed class EntitiesEdit : EditCommand
{
    private readonly string _json;
    private readonly List<Guid> _ids;
    private readonly bool _create;
    private readonly string _name;
    // 消した物を参照していた外のコンポーネント (戻した後に、新しい番号へ繋ぎ直す)
    private readonly List<ComponentEdit> _references;

    public EntitiesEdit(string name, string json, List<Guid> ids, bool create, List<ComponentEdit>? references = null)
    {
        _name = name; _json = json; _ids = ids; _create = create; _references = references ?? new();
    }

    public override string Name => _name;
    public override void Undo(World world, AssetManager assets) { if (_create) destroy(world); else restore(world, assets); }
    public override void Redo(World world, AssetManager assets) { if (_create) restore(world, assets); else destroy(world); }

    private void destroy(World world)
    {
        foreach (var id in _ids) world.DestroyEntity(SceneSerializer.FindEntity(world, id));
    }

    private void restore(World world, AssetManager assets)
    {
        SceneSerializer.Load(world, _json, assets);
        foreach (var reference in _references) reference.Undo(world, assets);   // Before = 消す前の値
    }
}

internal sealed class CompoundEdit : EditCommand
{
    private readonly string _name;
    public readonly List<EditCommand> Commands = new();
    public CompoundEdit(string name) => _name = name;
    public override string Name => _name;
    public override void Undo(World world, AssetManager assets) { for (int i = Commands.Count - 1; i >= 0; i--) Commands[i].Undo(world, assets); }
    public override void Redo(World world, AssetManager assets) { foreach (var command in Commands) command.Redo(world, assets); }
}

internal sealed class UndoStack
{
    private const int MaxCommands = 500;
    private readonly List<EditCommand> _undo = new();
    private readonly List<EditCommand> _redo = new();
    private object? _mergeKey;
    private bool    _mergeOpen;

    // 変わるたびに増える (画面の作り直しなどに使う)
    public int  Version { get; private set; }

    // 今の状態の印 (一番上の変更とそのまとめた回数)。保存した時の印と比べて「保存していない変更があるか」を出す
    // (以前は Version で比べていたので、元に戻して保存した時の状態に戻っても「変更あり」のままだった)
    public (EditCommand? Top, int Revision) State => _undo.Count > 0 ? (_undo[^1], _undo[^1].Revision) : (null, 0);
    public bool Enabled { get; set; } = true;   // 再生中は記録しない (止めると再生前に戻るので)

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Count > 0 ? _undo[^1].Name : null;
    public string? RedoName => _redo.Count > 0 ? _redo[^1].Name : null;
    public int Count => _undo.Count;

    // 実行済みの変更を積む。mergeKey が同じで、ドラッグが続いている間は 1 つにまとめる (ComponentEdit の After を差し替える)
    public void Record(EditCommand command, object? mergeKey = null)
    {
        if (!Enabled) return;
        Version++;
        if (mergeKey != null && _mergeOpen && Equals(mergeKey, _mergeKey) && _undo.Count > 0 &&
            _undo[^1] is ComponentEdit top && command is ComponentEdit edit && top.Type == edit.Type && top.Entity == edit.Entity)
        {
            top.After = edit.After;
            top.Revision++;
            return;
        }
        _undo.Add(command);
        if (_undo.Count > MaxCommands) _undo.RemoveAt(0);
        _redo.Clear();
        _mergeKey  = mergeKey;
        _mergeOpen = mergeKey != null;
    }

    // フレームの終わり。何も触っていなければ、次の変更は別の操作になる
    public void EndFrame(bool editing)
    {
        if (!editing) _mergeOpen = false;
    }

    public void Undo(World world, AssetManager assets)
    {
        if (_undo.Count == 0) return;
        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo(world, assets);
        _redo.Add(command);
        _mergeOpen = false;
        Version++;
    }

    public void Redo(World world, AssetManager assets)
    {
        if (_redo.Count == 0) return;
        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Redo(world, assets);
        _undo.Add(command);
        _mergeOpen = false;
        Version++;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _mergeOpen = false;
        Version++;
    }
}
