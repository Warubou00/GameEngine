using System.Numerics;

namespace GameEngine.Editor;

// ==========================================================================
// エディターの確認 (v1.9) : コリジョンアセット (.collision.json) / CompoundCollider / コリジョンの窓
// ==========================================================================
internal sealed partial class EditorTest
{
    // 体 (持ち主から見た位置) : Body (箱, ID 1) の子に Head (球, ID 2)、Head の子に Visor (箱, ID 3, 回してある)、
    // Body の子に Arms (形なし, ID 10) とその子 Hand (カプセル, ID 4)、Sensor (トリガーの球, ID 5)
    private static CollisionAsset makeTestAsset(string path)
    {
        var asset = new CollisionAsset(path);
        var body   = new CollisionNode { Id = 1, Name = "Body", Shape = CollisionNodeShape.Box, Position = new Vector3(0, 1, 0), Size = new Vector3(0.3f, 0.5f, 0.2f) };
        var head   = new CollisionNode { Id = 2, Name = "Head", Shape = CollisionNodeShape.Sphere, Parent = body, Position = new Vector3(0, 0.7f, 0), Size = new Vector3(0.25f, 0, 0) };
        var visor  = new CollisionNode { Id = 3, Name = "Visor", Shape = CollisionNodeShape.Box, Parent = head, Position = new Vector3(0, 0, 0.2f),
                                         Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2), Size = new Vector3(0.05f, 0.05f, 0.15f) };
        var arms   = new CollisionNode { Id = 10, Name = "Arms", Shape = CollisionNodeShape.Empty, Parent = body, Position = new Vector3(0.45f, 0.3f, 0),
                                         Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2) };
        var hand   = new CollisionNode { Id = 4, Name = "Hand", Shape = CollisionNodeShape.Capsule, Parent = arms, Position = new Vector3(0, -0.3f, 0), Size = new Vector3(0.08f, 0.2f, 0) };
        var sensor = new CollisionNode { Id = 5, Name = "Sensor", Shape = CollisionNodeShape.Sphere, Parent = body, Position = new Vector3(0, 0, -0.6f), Size = new Vector3(0.2f, 0, 0), IsTrigger = true };
        asset.Nodes.AddRange(new[] { body, head, visor, arms, hand, sensor });
        asset.MarkChanged();
        return asset;
    }

    private IEnumerable<int> collisionAssets()
    {
        var database = _app.Database;
        var engine = _app.Engine;

        // ---------- アセット : 親子の位置 / JSON の往復 ----------
        var asset = makeTestAsset("Assets/_CollisionTest/Test.collision.json");
        var parts = asset.Parts.ToArray();
        var headPart  = parts.First(p => asset.Nodes[p.NodeIndex].Id == 2);
        var visorPart = parts.First(p => asset.Nodes[p.NodeIndex].Id == 3);
        var handPart  = parts.First(p => asset.Nodes[p.NodeIndex].Id == 4);
        // Arms は Z 周りに 90 度 : 子の (0, -0.3, 0) は持ち主から見て (+0.3, 0, 0) ずれる
        check(parts.Length == 5 && Vector3.Distance(headPart.Center, new Vector3(0, 1.7f, 0)) < 1e-4f &&
              Vector3.Distance(visorPart.Center, new Vector3(0, 1.7f, 0.2f)) < 1e-4f &&
              Vector3.Distance(handPart.Center, new Vector3(0.75f, 1.3f, 0)) < 1e-4f &&
              MathF.Abs(Vector3.Transform(Vector3.UnitY, handPart.Rotation).X + 1) < 1e-4f,
              $"collision asset : children are placed in the parent's space ({parts.Length} colliders, hand at {handPart.Center.X:F2}, {handPart.Center.Y:F2})");
        var json = asset.ToJson();
        var copy = new CollisionAsset("copy", json);
        var copyParts = copy.Parts.ToArray();
        check(copy.Nodes.Count == 6 && copy.Nodes[4].Parent == copy.Nodes[3] && copy.Nodes[5].IsTrigger && copy.ToJson() == json &&
              copyParts.Zip(parts).All(z => Vector3.Distance(z.First.Center, z.Second.Center) < 1e-5f),
              "collision asset : JSON keeps nodes / parents / IDs / triggers (save -> load -> same file)");
        var versionBefore = asset.Version;
        asset.Nodes[1].Size = new Vector3(0.3f, 0, 0);
        asset.MarkChanged();
        check(asset.Version > versionBefore && asset.Parts.ToArray().First(p => asset.Nodes[p.NodeIndex].Id == 2).Size.X == 0.3f,
              "MarkChanged raises the version and the parts are rebuilt");
        asset.Nodes[1].Size = new Vector3(0.25f, 0, 0);
        asset.MarkChanged();
        check(asset.DuplicateIds().Count == 0 && asset.NextId() == 6, "unique IDs / the next free ID");

        // ---------- Project : 作る / 種類 / 窓で開く ----------
        var folder = "Assets/_CollisionTest";
        Directory.CreateDirectory(database.FullPath(folder));
        database.Refresh();
        _app.Project.CreateFolderForTest(folder);   // 今のフォルダにする
        var created = _app.Project.CreateCollisionAsset("Robot");
        yield return 0;
        check(created == $"{folder}/Robot.collision.json" && database.Find(created)?.Kind == AssetKind.Collision &&
              AssetDatabase.SplitName("Robot.collision.json", false).Extension == ".collision.json",
              $"Project > Create > Collision Asset makes a .collision.json ({created})");
        var editor = _app.CollisionEditor;
        check(created != null && _app.OpenCollision(created) && editor.IsOpen && editor.Asset?.Nodes.Count == 1 && editor.Preview != null,
              "double-click opens the collision editor window with its own preview world");
        for (int i = 0; i < 3; i++) yield return 0;
        var robot = editor.Asset!;
        var root = robot.Nodes[0];
        var head = editor.AddNode(CollisionNodeShape.Sphere, root)!;
        var visor = editor.AddNode(CollisionNodeShape.Box, head)!;
        var other = editor.AddNode(CollisionNodeShape.Capsule, null)!;
        check(head.Parent == root && visor.Parent == head && other.Parent == null && head.Id == 2 && visor.Id == 3 && other.Id == 4 &&
              robot.Nodes.IndexOf(visor) == robot.Nodes.IndexOf(head) + 1,
              "Add makes a child of the selected node with a new ID (shown under its parent)");
        // 親子を選んだ時の色 : 選んだ物は黄色 / 子孫は橙色 / 他は緑
        editor.Selected = head;
        yield return 0;
        yield return 0;
        check(editor.ColorOf(head) == CollisionEditor.SelectedColor && editor.ColorOf(visor) == CollisionEditor.DescendantColor &&
              editor.ColorOf(root) == CollisionEditor.NormalColor && editor.ColorOf(other) == CollisionEditor.NormalColor &&
              editor.HighlightedCount == 2 && editor.LineCount > 40,
              $"the selected node and its descendants are drawn in highlight colors ({editor.HighlightedCount} highlighted, {editor.LineCount} lines)");
        // 親を変えても、持ち主から見た位置は同じ
        head.Position = new Vector3(0.1f, 0.6f, 0);
        root.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f);
        editor.Changed();
        var before = robot.OwnerPoseOf(visor);
        check(editor.SetParent(visor, other) && visor.Parent == other && Vector3.Distance(robot.OwnerPoseOf(visor).Position, before.Position) < 1e-4f &&
              MathF.Abs(Quaternion.Dot(robot.OwnerPoseOf(visor).Rotation, before.Rotation)) > 0.9999f && !editor.SetParent(other, visor),
              "re-parenting keeps the collider where it is (and refuses to make a loop)");
        editor.Selected = other;
        var dup = editor.DuplicateSelected();
        check(dup != null && dup.Id != other.Id && robot.DuplicateIds().Count == 0 && robot.Nodes.Count(n => n.Parent == dup) == 1,
              $"duplicate copies the subtree with new IDs ({robot.Nodes.Count} nodes)");
        var nodesBefore = robot.Nodes.Count;
        editor.Undo();
        check(robot.Nodes.Count == nodesBefore - 2, $"undo ({nodesBefore} -> {robot.Nodes.Count} nodes)");
        editor.Redo();
        check(robot.Nodes.Count == nodesBefore, "redo");
        // プロジェクトのモデルを一緒に出す (モデルがあれば)
        var modelEntry = database.Entries.Where(e => e.Kind is AssetKind.Model or AssetKind.FbxModel).OrderBy(e => e.Path).FirstOrDefault();
        if (modelEntry != null)
        {
            editor.SelectModel(modelEntry.Path);
            for (int i = 0; i < 3; i++) yield return 0;
            check(robot.PreviewModel == modelEntry.Path && editor.Preview!.World.IsAlive(editor.ModelEntity) && editor.Preview.World.Has<MeshRenderer>(editor.ModelEntity),
                  $"a project model is shown with the colliders ({modelEntry.Path})");
        }
        // クリックで選ぶ (光線) : 根の箱を通る (元に戻す / やり直すとノードは作り直されるので ID で引き直す)
        root = robot.FindById(1)!;
        var rootCenter = robot.OwnerPoseOf(root).Position;
        check(editor.Pick(rootCenter + new Vector3(0, 0, -5), Vector3.UnitZ) == root, "clicking a collider in the preview selects it");
        editor.Flush();
        var saved = File.ReadAllText(database.FullPath(created!));
        check(editor.SaveCount > 0 && saved == robot.ToJson() && saved.Contains("\"previewModel\"") == (modelEntry != null),
              $"the editor saves the asset (with the preview model) ({editor.SaveCount} saves)");
        editor.Close();
        yield return 0;

        // ---------- 再生 : CompoundCollider の部品ごとのイベント / 自分の部品どうしは当たらない ----------
        var probes = new PartProbeSystem();
        _app.TogglePlay();
        yield return 0;
        engine.AddSystem(probes);
        var physics = engine.Physics;
        var basePos = new Vector3(40, 80, 40);   // 見本のシーンから離れた所
        var bodiesBefore = physics.Bodies;
        var robotEntity = World.CreateEntity("CompoundProbe");
        World.Add(robotEntity, new Transform(basePos));
        World.Add(robotEntity, new CompoundCollider(asset));
        World.Add(robotEntity, new PartProbe());
        for (int i = 0; i < 3; i++) yield return 0;
        // 部品どうしは重なっている (Visor は Head の中) が、自分どうしのイベントは来ない
        var p0 = World.Get<PartProbe>(robotEntity);
        check(physics.Bodies - bodiesBefore == 5 && p0.Enter == 0 && p0.TriggerEnter == 0 && p0.SelfHits == 0,
              $"a compound collider makes one body per collider ({physics.Bodies - bodiesBefore}) and its own colliders never collide with each other");
        // 頭の高さに箱を置く → 自分の Head (2) と Visor (3) に当たる
        var block = World.CreateEntity("PartBlock");
        World.Add(block, new Transform(basePos + new Vector3(0, 1.7f, 0.3f)));
        World.Add(block, Collider.Box(new Vector3(0.1f)));
        World.Add(block, new PartProbe());
        for (int i = 0; i < 4; i++) yield return 0;
        var p1 = World.Get<PartProbe>(robotEntity);
        var b1 = World.Get<PartProbe>(block);
        check(p1.Enter == 2 && p1.SelfIdMask == ((1u << 2) | (1u << 3)) && p1.OtherIdMask == 0 && p1.LastAssetIndex == 0 && p1.SelfHits == 0 && p1.HadContact,
              $"OnCollisionEnter gets which collider of the asset was hit (self IDs 0b{Convert.ToString(p1.SelfIdMask, 2)}, other id mask {p1.OtherIdMask}, {p1.LastSelfName})");
        check(b1.Enter == 2 && b1.OtherIdMask == ((1u << 2) | (1u << 3)) && b1.OtherWasPart && b1.SelfIdMask == 0,
              $"the other side gets OtherPart.Id (a plain Collider has Id -1 : mask 0b{Convert.ToString(b1.OtherIdMask, 2)})");
        // 離す → 部品ごとに Exit / トリガーの部品 (Sensor 5) は OnTrigger*
        { ref var t = ref World.Get<Transform>(block); t.Position = basePos + new Vector3(0, 1, -0.6f); t.MarkDirty(); }
        for (int i = 0; i < 4; i++) yield return 0;
        var p2 = World.Get<PartProbe>(robotEntity);
        check(p2.Exit == 2 && p2.TriggerEnter >= 1 && (p2.TriggerIdMask & (1u << 5)) != 0 && p2.SelfHits == 0,
              $"Exit per collider, and the trigger collider calls OnTriggerEnter with its ID ({p2.Exit} exits, trigger mask 0b{Convert.ToString(p2.TriggerIdMask, 2)})");
        // レイキャストも部品が分かる
        var rayHit = physics.Raycast(basePos + new Vector3(0, 1.7f, 3), -Vector3.UnitZ, out var hit, 10);
        check(rayHit && hit.Entity == robotEntity && hit.Part.Id is 2 or 3, $"Raycast tells which collider it hit (id {hit.Part.Id} {hit.Part.Name})");
        // アセットを直す → 使っている物は作り直す (部品が 1 つ増える)
        var bodiesNow = physics.Bodies;
        asset.Nodes.Add(new CollisionNode { Id = 6, Name = "Tail", Shape = CollisionNodeShape.Box, Position = new Vector3(0, 0.5f, -0.5f), Size = new Vector3(0.05f) });
        asset.MarkChanged();
        for (int i = 0; i < 2; i++) yield return 0;
        check(physics.Bodies == bodiesNow + 1, $"editing the asset rebuilds the compound collider ({bodiesNow} -> {physics.Bodies} bodies)");
        // 2 つ目のアセット : AssetIndex
        var second = new CollisionAsset("Assets/_CollisionTest/Second.collision.json");
        second.Nodes.Add(new CollisionNode { Id = 7, Name = "Extra", Shape = CollisionNodeShape.Box, Position = new Vector3(2, 0, 0), Size = new Vector3(0.2f) });
        second.MarkChanged();
        { ref var cc = ref World.Get<CompoundCollider>(robotEntity); cc.Assets = new CollisionAsset?[] { asset, second }; }
        { ref var pr = ref World.Get<PartProbe>(robotEntity); pr = default; }
        { ref var t = ref World.Get<Transform>(block); t.Position = basePos + new Vector3(2, 0, 0); t.MarkDirty(); }
        for (int i = 0; i < 4; i++) yield return 0;
        var p3 = World.Get<PartProbe>(robotEntity);
        check(p3.Enter == 1 && p3.LastAssetIndex == 1 && p3.SelfIdMask == (1u << 7), $"several assets : SelfPart.AssetIndex tells which asset ({p3.LastAssetIndex})");
        // 消す → 体が全部消える
        World.DestroyEntity(robotEntity);
        World.DestroyEntity(block);
        for (int i = 0; i < 3; i++) yield return 0;
        check(physics.Bodies == bodiesBefore, $"removing the entity removes all its collider bodies ({physics.Bodies} / {bodiesBefore})");

        // ---------- 剛体 : 全部の部品で 1 つの剛体 (重心は部品から) ----------
        var dumbbell = new CollisionAsset("Assets/_CollisionTest/Dumbbell.collision.json");
        // 重なった 2 つの箱 (自分どうしで押し合えば飛び散る) + 片側に大きい箱 (重心がそちらへ寄る)
        dumbbell.Nodes.Add(new CollisionNode { Id = 1, Name = "Left",  Shape = CollisionNodeShape.Box, Position = new Vector3(-0.4f, 0, 0), Size = new Vector3(0.5f, 0.25f, 0.25f) });
        dumbbell.Nodes.Add(new CollisionNode { Id = 2, Name = "Right", Shape = CollisionNodeShape.Box, Position = new Vector3(0.3f, 0, 0), Size = new Vector3(0.25f, 0.25f, 0.25f) });
        dumbbell.MarkChanged();
        var ground = World.CreateEntity("CompoundGround");
        World.Add(ground, new Transform(new Vector3(-40, 79.5f, 40)));
        World.Add(ground, Collider.Box(new Vector3(4, 0.5f, 4)));
        World.Add(ground, new Static());
        var falling = World.CreateEntity("CompoundRigid");
        World.Add(falling, new Transform(new Vector3(-40, 81.5f, 40)));
        World.Add(falling, new CompoundCollider(dumbbell));
        World.Add(falling, Rigidbody.Dynamic(2.0f));
        var settled = false;
        for (int i = 0; i < 400 && !settled; i++)
        {
            yield return 0;
            if (i > 60) settled = World.Get<Rigidbody>(falling).IsSleeping;
        }
        var ft = World.Get<Transform>(falling);
        var rb = World.Get<Rigidbody>(falling);
        // 体積の比 : 左 0.25 / 右 0.125 → 重心の X = (-0.4 * 0.25 + 0.3 * 0.125) / 0.375 = -0.1667
        var comLocal = Vector3.Transform(rb.WorldCenterOfMass - ft.WorldPosition, Quaternion.Inverse(ft.WorldRotation));
        var up = Vector3.Transform(Vector3.UnitY, ft.WorldRotation);
        check(MathF.Abs(ft.WorldPosition.Y - 80.25f) < 0.05f && up.Y > 0.999f && MathF.Abs(ft.WorldPosition.X + 40) < 0.05f,
              $"a compound rigidbody falls and rests on its colliders without pushing itself apart (y {ft.WorldPosition.Y:F3}, up {up.Y:F4}, {(settled ? "asleep" : "awake")})");
        check(MathF.Abs(comLocal.X + 0.16667f) < 0.01f && MathF.Abs(comLocal.Y) < 0.01f,
              $"the center of mass comes from the colliders' volumes ({comLocal.X:F3}, {comLocal.Y:F3})");
        // 傾けて落とす : 片側 (重い方) が先に着いて回る (1 つの剛体として回転する)
        { ref var t = ref World.Get<Transform>(falling); t.Position = new Vector3(-40, 82, 40); t.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.4f); t.MarkDirty(); }
        { ref var r = ref World.Get<Rigidbody>(falling); r.WakeUp(); }
        settled = false;
        for (int i = 0; i < 500 && !settled; i++)
        {
            yield return 0;
            if (i > 60) settled = World.Get<Rigidbody>(falling).IsSleeping;
        }
        ft = World.Get<Transform>(falling);
        up = Vector3.Transform(Vector3.UnitY, ft.WorldRotation);
        check(up.Y > 0.999f && MathF.Abs(ft.WorldPosition.Y - 80.25f) < 0.05f,
              $"a tilted compound rigidbody rotates as one body and lies flat (up {up.Y:F4}, y {ft.WorldPosition.Y:F3})");
        World.DestroyEntity(falling);
        World.DestroyEntity(ground);
        engine.RemoveSystems(new object[] { probes });
        _app.TogglePlay();
        yield return 0;

        // ---------- シーンの保存 / 読み込み : アセットはパスで ----------
        var saveEntity = World.CreateEntity("CompoundSave");
        World.Add(saveEntity, new Transform(Vector3.Zero));
        var robotAsset = engine.Assets.LoadCollisionAsset(created!);
        World.Add(saveEntity, new CompoundCollider(robotAsset));
        var sceneJson = SceneSerializer.Save(World, new[] { saveEntity });
        World.DestroyEntity(saveEntity);
        var loaded = SceneSerializer.Load(World, sceneJson, engine.Assets);
        var loadedEntity = loaded.FirstOrDefault();
        check(sceneJson.Contains(created!) && World.IsAlive(loadedEntity) && World.Has<CompoundCollider>(loadedEntity) &&
              ReferenceEquals(World.Get<CompoundCollider>(loadedEntity).Assets[0], robotAsset),
              "a scene saves CompoundCollider.Assets as paths and loads the same asset");
        foreach (var e in loaded) World.DestroyEntity(e);
        yield return 0;

        // ---------- 速さ : 動く 200 体 x 8 部品 (CompoundCollider) と、同じ数の Collider 1600 個 ----------
        foreach (var step in compoundBench()) yield return step;

        // 片付け
        Directory.Delete(database.FullPath(folder), true);
        File.Delete(database.FullPath(folder) + ".meta");
        database.Refresh();
        yield return 0;
    }

    // 部品の数が同じ時の CollisionSystem の時間を比べる (確認用。結果は表に出すだけ)
    private IEnumerable<int> compoundBench()
    {
        var engine = _app.Engine;
        var physics = engine.Physics;
        var bench = new CollisionAsset("bench");
        for (int i = 0; i < 8; i++)
            bench.Nodes.Add(new CollisionNode { Id = i + 1, Name = $"P{i}", Shape = i % 2 == 0 ? CollisionNodeShape.Box : CollisionNodeShape.Capsule,
                                                Position = new Vector3((i % 4) * 0.3f, (i / 4) * 0.4f, 0), Size = new Vector3(0.12f, 0.12f, 0.12f) });
        bench.MarkChanged();
        const int Count = 200;
        float measure(List<Entity> movers, int frameIndex)
        {
            for (int i = 0; i < movers.Count; i++)
            {
                ref var t = ref World.Get<Transform>(movers[i]);
                t.Position = new Vector3(200 + (i % 20) * 2.0f + MathF.Sin(frameIndex * 0.1f + i) * 0.3f, 100, 200 + (i / 20) * 2.0f);
                t.MarkDirty();
            }
            return physics.SystemMs;
        }
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var compound in new[] { true, false })
            {
                var movers = new List<Entity>();
                var extra = new List<Entity>();
                for (int i = 0; i < Count; i++)
                {
                    var e = World.CreateEntity($"Bench{i}");
                    World.Add(e, new Transform(new Vector3(200 + (i % 20) * 2.0f, 100, 200 + (i / 20) * 2.0f)));
                    if (compound) World.Add(e, new CompoundCollider(bench));
                    else
                    {
                        // 同じ 8 個の形を、子のエンティティの Collider で (以前の作り方)
                        for (int k = 0; k < 8; k++)
                        {
                            var child = World.CreateEntity($"Bench{i}_{k}");
                            var part = bench.Parts[k];
                            World.Add(child, new Transform(part.Center));
                            World.Add(child, part.Shape == ColliderShape.Box ? Collider.Box(part.Size) : Collider.Capsule(part.Size.X, part.Size.Y));
                            Hierarchy.SetParent(World, child, e, keepWorld: false);
                            extra.Add(child);
                        }
                    }
                    movers.Add(e);
                }
                for (int i = 0; i < 5; i++) yield return 0;
                var total = 0.0f;
                const int Frames = 60;
                for (int f = 0; f < Frames; f++)
                {
                    measure(movers, f);
                    yield return 0;
                    total += physics.SystemMs;
                }
                Console.WriteLine($"[Test] bench pass {pass} : {(compound ? "CompoundCollider" : "child Colliders ")} {Count} x 8 moving : " +
                                  $"CollisionSystem {total / Frames:F3} ms / frame ({physics.Bodies} bodies)");
                if (pass == 1 && compound) _benchCompoundMs = total / Frames;
                if (pass == 1 && !compound) _benchChildMs = total / Frames;
                foreach (var e in extra) World.DestroyEntity(e);
                foreach (var e in movers) World.DestroyEntity(e);
                for (int i = 0; i < 3; i++) yield return 0;
            }
        }
        check(_benchCompoundMs > 0 && _benchCompoundMs <= _benchChildMs * 1.1f,
              $"a compound collider is not slower than the same shapes as child Colliders ({_benchCompoundMs:F3} ms vs {_benchChildMs:F3} ms)");
    }

    private float _benchCompoundMs, _benchChildMs;
}
