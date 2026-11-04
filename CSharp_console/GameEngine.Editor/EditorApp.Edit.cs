namespace GameEngine.Editor;

// ==========================================================================
// シーンを変える操作と、再生 / 停止 (ED4)。変える操作はすべて UndoStack に積む
// ==========================================================================
internal sealed partial class EditorApp
{
    public UndoStack UndoHistory => _undo;
    public Engine Engine => _engine;
    public string? PlaySnapshot => _playSnapshot;
    public bool IsPlaying => _playing;
    public bool IsPaused  => _paused;
    public bool IsDirty   => _undo.State != _savedState;   // 保存していない変更があるか

    public void Undo()
    {
        if (_playing) return;
        _undo.Undo(World, _engine.Assets);
        reselect();
    }

    public void Redo()
    {
        if (_playing) return;
        _undo.Redo(World, _engine.Assets);
        reselect();
    }

    // 空のエンティティ (Transform だけ) を作る。parent があればその子に
    public Entity CreateEntity(Entity parent)
    {
        var entity = World.CreateEntity("Entity");
        World.Add(entity, new Transform { Parent = World.IsAlive(parent) && World.Has<Transform>(parent) ? parent : Entity.Null });
        Selected = entity;
        _undo.Record(new EntitiesEdit("Create Entity", SceneSerializer.Save(World, new[] { entity }), new List<Guid> { EditorWorld.IdOf(World, entity) }, create: true));
        return entity;
    }

    // 選んだ物を子ごと消す
    public void DeleteSelected()
    {
        if (!World.IsAlive(_selected)) return;
        var entities = EditorWorld.WithDescendants(World, _selected);
        var json = SceneSerializer.Save(World, entities);
        var ids = entities.Select(e => EditorWorld.IdOf(World, e)).ToList();
        var references = findReferences(entities);
        Hierarchy.DestroyWithChildren(World, _selected);
        _undo.Record(new EntitiesEdit($"Delete {ids.Count} entities", json, ids, create: false, references));
        _selected = Entity.Null;   // Id は覚えたまま (元に戻すと、戻った物がまた選ばれる)
    }

    // 選んだ物を子ごと複製する (外の物への参照はそのまま、中の物どうしの参照は複製した物どうしに繋がる)
    public Entity Duplicate()
    {
        if (!World.IsAlive(_selected)) return Entity.Null;
        var source = EditorWorld.WithDescendants(World, _selected);
        var created = SceneSerializer.Load(World, SceneSerializer.Save(World, source), _engine.Assets);
        if (created.Count == 0) return Entity.Null;
        ref var info = ref World.Get<EntityInfo>(created[0]);
        info.Name = $"{info.Name} (copy)";
        var ids = created.Select(e => EditorWorld.IdOf(World, e)).ToList();
        _undo.Record(new EntitiesEdit("Duplicate", SceneSerializer.Save(World, created), ids, create: true));
        Selected = created[0];
        return created[0];
    }

    // 親を変える (ワールドの位置は変えない)。Transform が無ければ付ける
    public bool SetParent(Entity child, Entity parent)
    {
        if (!World.IsAlive(child)) return false;
        if (World.IsAlive(parent) && (parent == child || Hierarchy.IsAncestor(World, child, parent)))
        {
            Console.WriteLine("[Editor] Cannot make a cycle in the hierarchy.");
            return false;
        }
        var edit = new CompoundEdit("Set Parent");
        ensureTransform(child, edit);
        if (World.IsAlive(parent)) ensureTransform(parent, edit);
        var id = EditorWorld.IdOf(World, child);
        var before = SceneSerializer.SaveComponent(World, child, typeof(Transform));
        if (!Hierarchy.SetParent(World, child, parent)) return false;
        World.Get<Transform>(child).MarkDirty();
        edit.Commands.Add(new ComponentEdit(id, typeof(Transform), before, SceneSerializer.SaveComponent(World, child, typeof(Transform))));
        _undo.Record(edit);
        return true;
    }

    private void ensureTransform(Entity entity, CompoundEdit edit)
    {
        if (World.Has<Transform>(entity)) return;
        World.Add(entity, new Transform());
        edit.Commands.Add(new ComponentEdit(EditorWorld.IdOf(World, entity), typeof(Transform), null, SceneSerializer.SaveComponent(World, entity, typeof(Transform))));
    }

    // 消す物を参照している、外のコンポーネント (Joint の相手など)。消す前の値を覚えておき、戻した時に繋ぎ直す
    private List<ComponentEdit> findReferences(List<Entity> removed)
    {
        var set = new HashSet<Entity>(removed);
        var result = new List<ComponentEdit>();
        foreach (var pool in World.Pools.ToList())
        {
            var fields = ComponentRegistry.Get(pool.ComponentType).Fields.Where(f => f.FieldType == typeof(Entity)).ToList();
            if (fields.Count == 0) continue;
            foreach (var entity in World.Entities)
            {
                if (set.Contains(entity) || !pool.Has(entity.Id)) continue;
                var boxed = pool.GetBoxed(entity.Id);
                if (!fields.Any(f => f.GetValue(boxed) is Entity target && set.Contains(target))) continue;
                result.Add(new ComponentEdit(EditorWorld.IdOf(World, entity), pool.ComponentType, SceneSerializer.SaveComponent(World, entity, pool.ComponentType), null));
            }
        }
        return result;
    }

    // ---------------------------------------------------------------------
    // 再生 / 停止
    //   再生 : 今のシーンを JSON で覚えて、ゲームの System / 物理 / 音を動かす (元に戻すの記録は止める)
    //   停止 : シーンを消して、覚えた JSON から作り直す (アセットは AssetManager が覚えているので読み直さない)
    // ---------------------------------------------------------------------

    public void TogglePlay()
    {
        if (_playing) stop(); else play();
    }

    public void TogglePause()
    {
        if (!_playing) return;
        _paused = !_paused;
        _engine.IsPlaying = !_paused;
    }

    public void Step()
    {
        if (!_playing) return;
        _paused = true;
        _engine.IsPlaying = false;
        _stepRequest = true;
    }

    private void play()
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        _playSnapshot = SceneSerializer.Save(World);
        _playing  = true;
        _paused   = false;
        _gameView = true;
        _undo.Enabled = false;
        _engine.IsPlaying = true;
        Console.WriteLine($"[Editor] Play (scene snapshot {_playSnapshot.Length / 1024} KB, {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms)");
    }

    private void stop()
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        _engine.IsPlaying = false;
        _playing  = false;
        _paused   = false;
        _stepRequest = false;
        _stepping = false;
        _gameView = false;
        World.Clear();
        var warnings = new List<string>();
        SceneSerializer.Load(World, _playSnapshot!, _engine.Assets, warnings);
        foreach (var warning in warnings) Console.WriteLine($"[Scene] {warning}");
        _playSnapshot = null;
        _undo.Enabled = true;
        reselect();
        Console.WriteLine($"[Editor] Stop (scene restored, {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms)");
    }
}
