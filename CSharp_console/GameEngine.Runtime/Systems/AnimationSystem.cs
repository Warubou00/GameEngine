using System.Diagnostics;
using System.Numerics;
using GameEngine.Fbx;

namespace GameEngine;

// ==========================================================================
// アニメーションの System (AN3。エンジンが持つ。登録した System の後、Transform の行列の前)
//   1. 骨を探す : Animator のエンティティの子孫から、Skeleton の骨と同じ名前の物 (EntityInfo.Name)。
//      Skeleton / Clip が読み直された時、骨のエンティティが消えた時に探し直す
//   2. 時間を進める (再生中で Playing の時だけ。Speed 倍、Loop なら戻る)
//   3. Clip の列がある骨に、その時間の 位置 / 回転 / 大きさ を書く (変わった時だけ。行列は TransformSystem が作る)
//   ShowBones : 骨を線で描く (親の骨 → 子の骨。行列ができた後に DrawBones で)
//   CPU で動かすのは骨のエンティティの Transform だけ。メッシュを曲げる (スキン) のは AN4 (GPU)
// ==========================================================================
internal sealed class AnimationSystem
{
    public int AnimatorCount { get; private set; }
    public int BonesWritten { get; private set; }
    public double LastMilliseconds { get; private set; }

    public void Update(World world, bool advance, float deltaTime)
    {
        var start = Stopwatch.GetTimestamp();
        AnimatorCount = 0;
        BonesWritten = 0;
        var pool = world.GetPool<Animator>();
        if (pool.Count == 0) { LastMilliseconds = 0; return; }
        foreach (var entity in pool.Entities.ToArray())
        {
            ref var animator = ref world.Get<Animator>(entity);
            if (animator.Skeleton is not { } skeleton) continue;
            AnimatorCount++;
            if (needsBinding(world, ref animator)) bind(world, entity, ref animator);
            if (animator.Clip is not { } clip || clip.FrameCount == 0 || animator.Tracks == null || animator.Bones == null) continue;
            // 時間
            if (advance && animator.Playing)
            {
                animator.Time += deltaTime * animator.Speed;
                if (animator.Loop && clip.Duration > 0)
                {
                    animator.Time %= clip.Duration;
                    if (animator.Time < 0) animator.Time += clip.Duration;
                }
                else animator.Time = Math.Clamp(animator.Time, 0, clip.Duration);
            }
            var time = Math.Clamp(animator.Time, 0, clip.Duration);
            // 骨に書く
            for (int b = 0; b < animator.Bones.Length; b++)
            {
                var track = animator.Tracks[b];
                var bone = animator.Bones[b];
                if (track < 0 || bone == Entity.Null || !world.IsAlive(bone) || !world.Has<Transform>(bone)) continue;
                clip.Sample(track, time, out var position, out var rotation, out var scale);
                var info = clip.Tracks[track];
                ref var transform = ref world.Get<Transform>(bone);
                var changed = false;
                if (info.HasTranslation && transform.Position != position) { transform.Position = position; changed = true; }
                if (info.HasRotation && transform.Rotation != rotation) { transform.Rotation = rotation; changed = true; }
                if (info.HasScale && transform.Scale != scale) { transform.Scale = scale; changed = true; }
                if (!changed) continue;
                transform.MarkDirty();
                BonesWritten++;
            }
        }
        LastMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static bool needsBinding(World world, ref Animator animator)
    {
        if (animator.Bones == null || animator.BoundSkeleton != animator.Skeleton || animator.BoundSkeletonVersion != animator.Skeleton!.Version) return true;
        if (animator.BoundClip != animator.Clip || (animator.Clip != null && animator.BoundClipVersion != animator.Clip.Version)) return true;
        foreach (var bone in animator.Bones) if (bone != Entity.Null && !world.IsAlive(bone)) return true;
        return false;
    }

    // 子孫から骨の名前のエンティティを探す (幅優先。同じ名前は近い方)
    private static void bind(World world, Entity root, ref Animator animator)
    {
        var skeleton = animator.Skeleton!;
        var byName = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var queue = new Queue<Entity>(Hierarchy.GetChildren(world, root));
        var guard = 0;
        while (queue.Count > 0 && guard++ < 100000)
        {
            var e = queue.Dequeue();
            if (world.Has<EntityInfo>(e)) byName.TryAdd(world.Get<EntityInfo>(e).Name, e);
            foreach (var child in Hierarchy.GetChildren(world, e)) queue.Enqueue(child);
        }
        animator.Bones = skeleton.Bones.Select(b => byName.TryGetValue(b.Name, out var e) ? e : Entity.Null).ToArray();
        var clip = animator.Clip;
        animator.Tracks = skeleton.Bones.Select(b => clip?.TrackIndex(b.Name) ?? -1).ToArray();
        animator.BoundSkeleton = skeleton;
        animator.BoundSkeletonVersion = skeleton.Version;
        animator.BoundClip = animator.Clip;
        animator.BoundClipVersion = animator.Clip?.Version ?? 0;
    }

    /// <summary>ShowBones の Animator の骨を線で描く (行列ができた後に呼ぶ)</summary>
    public void DrawBones(World world)
    {
        var pool = world.GetPool<Animator>();
        if (pool.Count == 0) return;
        foreach (var entity in pool.Entities)
        {
            ref var animator = ref world.Get<Animator>(entity);
            if (!animator.ShowBones || animator.Bones == null || animator.Skeleton == null) continue;
            var bones = animator.Skeleton.Bones;
            for (int b = 0; b < animator.Bones.Length && b < bones.Count; b++)
            {
                var e = animator.Bones[b];
                if (e == Entity.Null || !world.IsAlive(e)) continue;
                var p = Hierarchy.GetWorldMatrix(world, e).Translation;
                var parent = bones[b].Parent;
                var color = bones[b].IsBone ? DebugDraw.Color(1.0f, 0.85f, 0.2f) : DebugDraw.Color(0.6f, 0.6f, 0.6f);
                if (parent >= 0 && animator.Bones[parent] is var pe && pe != Entity.Null && world.IsAlive(pe))
                    DebugDraw.Line(Hierarchy.GetWorldMatrix(world, pe).Translation, p, color);
                DebugDraw.WireSphere(p, 0.02f, color);
            }
        }
    }
}

// ==========================================================================
// 骨のエンティティを作る (AN3)。root の子に、Skeleton の骨を親子のとおりに作る (名前 = 骨の名前、Transform = 休みの姿勢)
//   modelPath (xxx.mod.fbx) があれば、その中のメッシュのノードを部品 (xxx.mod.fbx#ノード) にして、FBX で親だった骨の子に付ける
//   (骨に無い親のメッシュは root の子。スキンのあるメッシュは root の子に、休みの姿勢のまま = AN4 で曲げる)
//   戻り値 = 作ったエンティティ (親から順)
// ==========================================================================
public static class AnimationRig
{
    public static List<Entity> Build(World world, AssetManager assets, Entity root, Skeleton skeleton, string? modelPath = null)
    {
        var created = new List<Entity>();
        var boneEntities = new Entity[skeleton.Bones.Count];
        for (int i = 0; i < skeleton.Bones.Count; i++)
        {
            var bone = skeleton.Bones[i];
            var entity = world.CreateEntity(bone.Name);
            world.Add(entity, new Transform { Position = bone.Position, Rotation = bone.Rotation, Scale = bone.Scale, Parent = bone.Parent >= 0 ? boneEntities[bone.Parent] : root });
            boneEntities[i] = entity;
            created.Add(entity);
        }
        if (modelPath == null || FbxAsset.TypeOf(modelPath) != FbxAssetType.Model) return created;
        var fbx = assets.LoadFbx(modelPath);
        if (fbx.Scene is not { } scene || !fbx.IsValid) return created;
        var space = FbxAnimationImporter.SpaceMatrix(scene);
        Matrix4x4.Invert(space, out var spaceInverse);
        var globals = new Dictionary<long, Matrix4x4>();
        foreach (var node in scene.OfKind("Model", "Mesh"))
        {
            var geometry = node.Children.Select(c => c.Child).FirstOrDefault(c => c.Kind == "Geometry");
            if (geometry == null) continue;
            var skinned = geometry.Children.Any(c => c.Child.Kind == "Deformer" && c.Child.SubKind == "Skin");
            // FBX で一番近い、骨の名前の親
            FbxObject? owner = null;
            if (!skinned)
                for (var cur = node.Parents.Select(p => p.Parent).FirstOrDefault(p => p.Kind == "Model"); cur != null && owner == null;
                     cur = cur.Parents.Select(p => p.Parent).FirstOrDefault(p => p.Kind == "Model"))
                    if (skeleton.IndexOf(cur.Name) >= 0) owner = cur;
            var parentEntity = owner != null ? boneEntities[skeleton.IndexOf(owner.Name)] : root;
            var relative = FbxTransforms.Global(node, globals);
            if (owner != null && Matrix4x4.Invert(FbxTransforms.Global(owner, globals), out var ownerInverse)) relative *= ownerInverse;
            FbxAnimationImporter.Decompose(FbxAnimationImporter.ToEngine(relative, space, spaceInverse), out var position, out var rotation, out var scale);
            Model model;
            try { model = assets.LoadModel($"{modelPath}#{node.Name}"); }
            catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException) { Console.WriteLine($"[Animation] {ex.Message}"); continue; }
            var part = world.CreateEntity(node.Name);
            world.Add(part, new Transform { Position = position, Rotation = rotation, Scale = scale, Parent = parentEntity });
            world.Add(part, new MeshRenderer(model));
            if (skinned) world.Add(part, new SkinnedMesh { Root = root });   // AN4 : 骨で曲げる (GPU)
            created.Add(part);
        }
        return created;
    }
}
