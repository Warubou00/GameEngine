using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// ECS の MeshRenderer / Transform を C++ の描画オブジェクトへ反映する (エンジン組み込み。最後に動く)
//   1. まだ C++ 側に無い MeshRenderer は、同じモデルごとにまとめて作る (1 モデル 1 回の呼び出し)
//   2. 行列が変わった物 (と、作ったばかりの物) の行列を配列に集めて、1 回の呼び出しで送る
//   3. 外された / 削除された MeshRenderer の描画オブジェクトは、まとめて削除する
//   止まっているシーンでは、毎フレーム C++ を呼ばない (P/Invoke は 0 回)
// ==========================================================================
internal sealed unsafe class RenderSyncSystem : ISystem
{
    private readonly World _world;
    private readonly TransformSystem _transforms;
    private int _knownRenderers;   // 前のフレームの MeshRenderer の数 (増えていなければ「作る物」を探さない)
    private int _knownActivity = -1;   // 前のフレームの World.ActivityVersion (変われば、無効になった物を消し、有効になった物を作る。ED2)

    // 毎フレームの割り当てを避けるため、配列は使い回す (足りなくなった時だけ大きくする)
    private uint[]      _handles  = new uint[1024];
    private Matrix4x4[] _matrices = new Matrix4x4[1024];
    private uint[]      _created  = new uint[1024];
    private readonly List<uint>                      _pendingDestroy = new();
    private readonly Dictionary<uint, List<int>>     _pendingCreate  = new();   // モデル → 詰めた配列の位置

    // AN5 : 作った描画オブジェクトを出す視点 (0 = シーン / 1〜 = プレビューの窓。窓の世界の物はシーンに描かない)
    private readonly uint _view;

    public RenderSyncSystem(World world, TransformSystem transforms, uint view = 0)
    {
        _world      = world;
        _transforms = transforms;
        _view       = view;
        // MeshRenderer が外れたら (エンティティの削除も含む)、C++ の描画オブジェクトも消す
        world.GetPool<MeshRenderer>().Removed += (_, renderer) =>
        {
            if (renderer.Created) _pendingDestroy.Add(renderer.Handle);
            _knownRenderers--;
        };
    }

    private uint[] _materialSlots = new uint[16];

    // v1.5 : マテリアル (サブメッシュごと) / 影の印 / レイヤーを C++ へ (変わった物だけ。C# の中で比べるだけなので P/Invoke は変わった時だけ)
    //   以前の Material (全部のサブメッシュに 1 つ) は、ここで Materials へ移す
    private void syncObjectSettings(World world, ReadOnlySpan<Entity> entities, Span<MeshRenderer> components)
    {
        var infos = world.GetPool<EntityInfo>();
        for (int i = 0; i < components.Length; i++)
        {
            ref var renderer = ref components[i];
            if (renderer.Material != null && (renderer.Materials == null || renderer.Materials.Length == 0) && renderer.Model.IsValid)
            {
                renderer.Materials = Enumerable.Repeat<Material?>(renderer.Material, Math.Max(1, renderer.Model.SubMeshCount)).ToArray();
                renderer.Material = null;
            }
            if (!renderer.Created) continue;

            // 以前の 1 つのマテリアル (C++ の全部のサブメッシュ) を外す (Materials に移したので)
            if (renderer.AppliedMaterialPlusOne != 0)
            {
                NativeMethods.Renderer_SetRenderObjectMaterial(renderer.Handle, NativeMethods.InvalidHandle);
                renderer.AppliedMaterialPlusOne = 0;
            }

            // サブメッシュごとのマテリアル
            var materials = renderer.Materials;
            var hash = MeshRenderer.materialsHash(materials);
            if (hash != renderer.AppliedMaterialsHash)
            {
                var count = materials?.Length ?? 0;
                if (_materialSlots.Length < count) _materialSlots = new uint[Math.Max(count, _materialSlots.Length * 2)];
                for (int s = 0; s < count; s++) _materialSlots[s] = materials![s] is { IsValid: true } m ? m.Handle : NativeMethods.InvalidHandle;
                fixed (uint* slots = _materialSlots) NativeMethods.Renderer_SetRenderObjectMaterials(renderer.Handle, slots, (uint)count);
                renderer.AppliedMaterialsHash = hash;
            }

            // 影の印とレイヤー (既定 : 影を落とす / 受ける / レイヤー 0)
            var layer = infos.Has(entities[i].Id) ? Math.Clamp(infos.Get(entities[i].Id).Layer, 0, 31) : 0;
            var flags = (renderer.CastShadows ? 0u : 1u) | (renderer.ReceiveShadows ? 0u : 2u);
            var wanted = (flags | ((uint)layer << 8)) + 1;
            if (wanted == renderer.AppliedFlagsPlusOne || (renderer.AppliedFlagsPlusOne == 0 && wanted == 1)) continue;
            NativeMethods.Renderer_SetRenderObjectFlags(renderer.Handle, flags, 1u << layer);
            renderer.AppliedFlagsPlusOne = wanted;
        }
    }

    public void Update(World world)
    {
        flushDestroy();

        var renderers  = world.GetPool<MeshRenderer>();
        var transforms = world.GetPool<Transform>();
        var entities   = renderers.Entities;
        var components = renderers.Components;

        // マテリアル / 影の印 / レイヤー (v1.5) : 変わった物を探して C++ へ送る
        //   (以前はマテリアルのアセットがある時だけ見回った。印とレイヤーも見るので毎フレーム。C# の中で比べるだけ)
        syncObjectSettings(world, entities, components);

        // 何も増えず、どの行列も変わっていなければ何もしない (止まっているシーンでは全部の物を見て回らない)
        // ※MeshRenderer を外して付け直すと数が同じになるので、外された時も _knownRenderers を減らしている
        TransformsSentLastFrame = 0;
        var activityChanged = world.ActivityVersion != _knownActivity;
        if (components.Length <= _knownRenderers && _transforms.ChangedCount == 0 && !activityChanged) return;

        // 0. 無効になった物の描画オブジェクトを消す (有効に戻ると 1 で作り直す。ED2)
        if (activityChanged)
        {
            for (int i = 0; i < components.Length; i++)
            {
                ref var renderer = ref components[i];
                if (!renderer.Created || world.IsActive(entities[i])) continue;
                _pendingDestroy.Add(renderer.Handle);
                renderer.Created = false;
            }
            flushDestroy();
            _knownActivity = world.ActivityVersion;
        }

        // 1. C++ 側に描画オブジェクトを作る (モデルごとにまとめて)
        foreach (var list in _pendingCreate.Values) list.Clear();
        var anyCreate = false;
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i].Created || !components[i].Model.IsValid || !world.IsActive(entities[i])) continue;
            var model = components[i].Model.Handle;
            if (!_pendingCreate.TryGetValue(model, out var list)) _pendingCreate[model] = list = new List<int>();
            list.Add(i);
            anyCreate = true;
        }
        if (anyCreate)
        {
            foreach (var (model, list) in _pendingCreate)
            {
                if (list.Count == 0) continue;
                ensureCapacity(ref _created, list.Count);
                fixed (uint* created = _created)
                {
                    NativeMethods.Renderer_CreateRenderObjects(model, (uint)list.Count, created);
                    if (_view != 0) NativeMethods.Renderer_SetObjectsView(created, (uint)list.Count, _view);   // AN5 (作った同じフレームのうちに)
                }
                for (int n = 0; n < list.Count; n++)
                {
                    if (_created[n] == NativeMethods.InvalidHandle) continue;   // 上限 (C++ のログに出る)
                    ref var renderer = ref components[list[n]];
                    renderer.Handle  = _created[n];
                    renderer.Created = true;
                    renderer.AppliedMaterialPlusOne = 0;   // 新しい描画オブジェクトはモデルのマテリアル (次のフレームで syncObjectSettings が付ける)
                    renderer.AppliedMaterialsHash   = 0;
                    renderer.AppliedFlagsPlusOne    = 0;
                    // 作ったばかりの物は、行列が変わっていなくても送る
                    if (transforms.Has(entities[list[n]].Id)) transforms.Get(entities[list[n]].Id).Changed = true;
                }
            }
        }

        // 2. 行列が変わった物を集めて、まとめて送る
        var count = 0;
        for (int i = 0; i < components.Length; i++)
        {
            ref var renderer = ref components[i];
            if (!renderer.Created) continue;
            ref var transform = ref transforms.TryGet(entities[i].Id, out var found);
            if (!found || !transform.Changed || !transform.HasMatrix) continue;

            if (count == _handles.Length)
            {
                Array.Resize(ref _handles,  count * 2);
                Array.Resize(ref _matrices, count * 2);
            }
            _handles[count]  = renderer.Handle;
            _matrices[count] = transform.LocalToWorld;
            count++;
            transform.Changed = false;   // 送ったので下ろす (Static の物はここでしか下ろされない)
        }
        if (count > 0)
        {
            fixed (uint* handles = _handles)
            fixed (Matrix4x4* matrices = _matrices)
            {
                NativeMethods.Renderer_SetRenderObjectTransforms(handles, matrices, (uint)count);
            }
        }
        TransformsSentLastFrame = count;
        _knownRenderers = components.Length;
    }

    // プロファイラー用 : このフレームで C++ へ送った行列の数
    public int TransformsSentLastFrame { get; private set; }

    private void flushDestroy()
    {
        if (_pendingDestroy.Count == 0) return;
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pendingDestroy);
        fixed (uint* handles = span)
        {
            NativeMethods.Renderer_DestroyRenderObjects(handles, (uint)span.Length);
        }
        _pendingDestroy.Clear();
    }

    private static void ensureCapacity(ref uint[] array, int count)
    {
        if (array.Length < count) Array.Resize(ref array, Math.Max(count, array.Length * 2));
    }
}
