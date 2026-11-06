using System.Diagnostics;
using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// GPU スキニングの準備 (AN4。エンジンが持つ。描画オブジェクトを作った後 = RenderSyncSystem の後)
//   SkinnedMesh + MeshRenderer の物ごとに :
//     1. C++ の描画オブジェクトを「曲げる」にする (描画オブジェクトが作り直されたらもう一度)
//     2. 骨を探す (Root の子孫から、モデルの骨の名前のエンティティ)
//     3. 骨の行列 k = 休みの姿勢の逆行列 k × 骨の世界の行列 × メッシュの世界の逆行列 (= メッシュの空間で曲げた形)。
//        行ベクトルの行列の 3 列を 12 個の float で。最後に「動かない骨」(単位行列)
//     4. 境界球 : 骨の位置 (メッシュの空間) を中心に、骨から一番遠い頂点までの距離 (読み込む時に計算) を足した球を全部含む球
//   全部を 1 回の P/Invoke で送る (C++ は姿勢が変わった物だけ GPU で曲げる)
// ==========================================================================
internal sealed class SkinningSystem
{
    public int ObjectCount { get; private set; }
    public int MatrixCount { get; private set; }
    public double LastMilliseconds { get; private set; }

    private uint[] _objects = new uint[64];
    private uint[] _boneCounts = new uint[64];
    private float[] _matrices = new float[64 * 12];
    private float[] _spheres = new float[64 * 4];
    private readonly List<uint> _enable = new();

    public unsafe void Update(World world, AssetManager assets)
    {
        var start = Stopwatch.GetTimestamp();
        ObjectCount = 0;
        MatrixCount = 0;
        var pool = world.GetPool<SkinnedMesh>();
        if (pool.Count == 0) { LastMilliseconds = 0; return; }
        _enable.Clear();
        int objects = 0, matrices = 0;
        foreach (var entity in pool.Entities.ToArray())
        {
            if (!world.Has<MeshRenderer>(entity)) continue;
            var renderer = world.Get<MeshRenderer>(entity);
            if (!renderer.IsCreated) continue;
            if (assets.GetSkin(renderer.Model) is not { } skin) continue;
            ref var mesh = ref world.Get<SkinnedMesh>(entity);
            // 1. 描画オブジェクトを曲げるにする (作り直された / モデルが変わった時も)
            if (mesh.SkinnedHandle != renderer.Handle + 1 || mesh.BoundSkin != skin || mesh.BoundVersion != skin.Version)
            {
                _enable.Add(renderer.Handle);
                mesh.SkinnedHandle = renderer.Handle + 1;
            }
            // 2. 骨
            var root = mesh.Root != Entity.Null && world.IsAlive(mesh.Root) ? mesh.Root : findAnimator(world, entity);
            if (mesh.Bones == null || mesh.BoundSkin != skin || mesh.BoundVersion != skin.Version || mesh.BoundRoot != root || mesh.Bones.Any(b => b != Entity.Null && !world.IsAlive(b)))
            {
                mesh.Bones = bind(world, root, skin);
                mesh.BoundSkin = skin;
                mesh.BoundVersion = skin.Version;
                mesh.BoundRoot = root;
            }
            // 3. 骨の行列 (メッシュの空間)
            var count = skin.MatrixCount;
            ensure(objects + 1, matrices + count);
            Matrix4x4.Invert(Hierarchy.GetWorldMatrix(world, entity), out var meshInverse);
            var sphereMin = new Vector3(float.MaxValue);
            var sphereMax = new Vector3(float.MinValue);
            var points = new List<(Vector3 Point, float Radius)>();
            for (int b = 0; b < count; b++)
            {
                var m = Matrix4x4.Identity;
                if (b < skin.BoneNames.Count && mesh.Bones[b] is var bone && bone != Entity.Null)
                {
                    var boneToMesh = Hierarchy.GetWorldMatrix(world, bone) * meshInverse;
                    m = skin.InverseBind[b] * boneToMesh;
                    points.Add((boneToMesh.Translation, skin.BoneRadius[b]));
                }
                var o = (matrices + b) * 12;
                _matrices[o] = m.M11; _matrices[o + 1] = m.M21; _matrices[o + 2] = m.M31; _matrices[o + 3] = m.M41;
                _matrices[o + 4] = m.M12; _matrices[o + 5] = m.M22; _matrices[o + 6] = m.M32; _matrices[o + 7] = m.M42;
                _matrices[o + 8] = m.M13; _matrices[o + 9] = m.M23; _matrices[o + 10] = m.M33; _matrices[o + 11] = m.M43;
            }
            // 4. 境界球
            var center = Vector3.Zero;
            var radius = 0.0f;
            if (points.Count > 0)
            {
                foreach (var (p, _) in points) { sphereMin = Vector3.Min(sphereMin, p); sphereMax = Vector3.Max(sphereMax, p); }
                center = (sphereMin + sphereMax) * 0.5f;
                foreach (var (p, r) in points) radius = MathF.Max(radius, Vector3.Distance(p, center) + r);
            }
            _objects[objects] = renderer.Handle;
            _boneCounts[objects] = (uint)count;
            _spheres[objects * 4] = center.X; _spheres[objects * 4 + 1] = center.Y; _spheres[objects * 4 + 2] = center.Z; _spheres[objects * 4 + 3] = MathF.Max(radius, 0.01f);
            objects++;
            matrices += count;
        }
        if (_enable.Count > 0)
        {
            var enable = _enable.ToArray();
            fixed (uint* e = enable) NativeMethods.Renderer_SetObjectsSkinned(e, (uint)enable.Length, true);
        }
        if (objects > 0)
        {
            fixed (uint* o = _objects)
            fixed (uint* c = _boneCounts)
            fixed (float* m = _matrices)
            fixed (float* s = _spheres)
                NativeMethods.Renderer_SetSkinPoses(o, (uint)objects, m, c, s);
        }
        ObjectCount = objects;
        MatrixCount = matrices;
        LastMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private void ensure(int objects, int matrices)
    {
        if (_objects.Length < objects)
        {
            var size = Math.Max(objects, _objects.Length * 2);
            Array.Resize(ref _objects, size);
            Array.Resize(ref _boneCounts, size);
            Array.Resize(ref _spheres, size * 4);
        }
        if (_matrices.Length < matrices * 12) Array.Resize(ref _matrices, Math.Max(matrices * 12, _matrices.Length * 2));
    }

    // 親をたどって最初の Animator の物 (無ければ一番上の親)
    private static Entity findAnimator(World world, Entity entity)
    {
        var current = entity;
        var top = entity;
        for (int guard = 0; guard < 256 && current != Entity.Null && world.IsAlive(current); guard++)
        {
            if (world.Has<Animator>(current)) return current;
            top = current;
            current = world.Has<Transform>(current) ? world.Get<Transform>(current).Parent : Entity.Null;
        }
        return top;
    }

    // root の子孫から骨の名前のエンティティ (幅優先)
    private static Entity[] bind(World world, Entity root, ModelSkin skin)
    {
        var byName = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var queue = new Queue<Entity>();
        queue.Enqueue(root);
        var guard = 0;
        while (queue.Count > 0 && guard++ < 100000)
        {
            var e = queue.Dequeue();
            if (world.Has<EntityInfo>(e)) byName.TryAdd(world.Get<EntityInfo>(e).Name, e);
            foreach (var child in Hierarchy.GetChildren(world, e)) queue.Enqueue(child);
        }
        return skin.BoneNames.Select(n => byName.TryGetValue(n, out var e) ? e : Entity.Null).ToArray();
    }
}
