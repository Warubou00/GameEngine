using System.Numerics;
using System.Runtime.CompilerServices;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// CompoundCollider (コリジョンアセット。v1.9) を C++ の複合の体にする (CollisionSystem の続き)
//   1. 作る : 全部のアセットの部品を 1 回の Physics_CreateCompound でまとめて作る (1 つ目が代表)。
//      部品の体には持ち主ごとの group を付け、同じ持ち主の部品どうしは C++ が組にしない (形の判定もしない)
//   2. 動かす : 持ち主の Transform が動いた時だけ、代表のハンドルに 1 回送る (C++ が部品へ広げる)
//   3. 剛体 : Collider が無ければ、代表を _states (Collider と同じ表) に入れる。剛体 / ジョイント / 力の命令 /
//      Transform への書き戻しは Collider と同じ道を通る (C++ は全部の部品で 1 つの剛体)
//   4. 作り直す : アセットの中身 (Version) / 配列 / Static / Collider の有無が変わった時だけ
//   体の番号 → (エンティティ, 部品) は _bodyEntities / _bodyParts で引く (イベント / レイキャスト / 接触)
// ==========================================================================
internal sealed unsafe partial class CollisionSystem
{
    private const ColliderShape CompoundShape = (ColliderShape)100;   // _states の代表 (Collider ではない) の印

    private struct CompoundState
    {
        public bool    Active;
        public uint    Root;          // 代表のハンドル (部品が 1 つも無ければ InvalidHandle)
        public uint[]  Handles;       // 全部の部品のハンドル
        public int     Signature;     // アセットの参照と Version (変わったら作り直す)
        public CollisionAsset?[]? SentArray;
        public bool    IsStatic;
        public bool    OwnsBody;      // Collider が無い : 代表が _states に入っている (剛体になれる)
        public uint    GroupedBody;   // group を付けた同じエンティティの Collider の体
        public Matrix4x4 Sent;
    }

    private CompoundState[] _compoundStates = new CompoundState[16];
    private ColliderPart[]  _bodyParts = new ColliderPart[64];   // 体の番号 → 部品 (ふつうの Collider は既定値)
    private int[]           _partOverlaps = new int[64];         // 体の番号 → 重なっている相手の数 (部品の確認用の色)
    private PhysicsNativeMethods.PartDesc[] _partScratch = new PhysicsNativeMethods.PartDesc[16];
    private uint[] _handleScratch = new uint[16];
    // 消した体 → 持ち主 (消した後に C++ が返す「離れた」のイベントを、正しいエンティティ / 部品で知らせるため)
    // 以前 (v1.8 まで) は、同じフレームで体の番号が使い回されると相手が分からず、OnCollisionStay が呼ばれ続けることがあった
    private readonly Dictionary<uint, (Entity Entity, ColliderPart Part)> _retired = new();

    private static uint groupOf(Entity entity) => (uint)entity.Id + 1u;

    private void initializeCompounds(World world)
    {
        world.GetPool<CompoundCollider>().Removed += (entity, _) =>
        {
            if ((uint)entity.Id >= (uint)_compoundStates.Length) return;
            ref var cs = ref _compoundStates[entity.Id];
            if (cs.Active && cs.Root != PhysicsNativeMethods.InvalidHandle)
            {
                retireCompound(entity, ref cs);
                _pendingDestroy.Add(cs.Root);
                if (cs.OwnsBody && (uint)entity.Id < (uint)_states.Length && _states[entity.Id].Body == cs.Root)
                {
                    _states[entity.Id] = default;
                    if ((uint)entity.Id < (uint)_rigidStates.Length) _rigidStates[entity.Id] = default;
                }
            }
            cs = default;
        };
    }

    // ---------------------------------------------------------------------
    // 体の番号 → 持ち主
    // ---------------------------------------------------------------------

    private ColliderPart partOf(uint body)
    {
        var index = (int)(body & 0xFFFF);
        if (index < _bodyHandles.Length && _bodyHandles[index] == body) return index < _bodyParts.Length ? _bodyParts[index] : default;
        return _retired.TryGetValue(body, out var r) ? r.Part : default;
    }

    private void registerBody(uint body, Entity entity, ColliderPart part)
    {
        var index = (int)(body & 0xFFFF);
        if (index >= _bodyEntities.Length)
        {
            Array.Resize(ref _bodyEntities, Math.Max(_bodyEntities.Length * 2, index + 1));
            Array.Resize(ref _bodyHandles,  _bodyEntities.Length);
        }
        if (index >= _bodyParts.Length)
        {
            Array.Resize(ref _bodyParts,    Math.Max(_bodyParts.Length * 2, index + 1));
            Array.Resize(ref _partOverlaps, _bodyParts.Length);
        }
        _bodyEntities[index] = entity;
        _bodyHandles[index]  = body;
        _bodyParts[index]    = part;
        _partOverlaps[index] = 0;
    }

    // 体を消す (消した後のイベントのために持ち主を覚えておく)。複合の代表なら部品も覚える (C++ は部品も消す)
    private void destroyBody(uint body)
    {
        var index = (int)(body & 0xFFFF);
        if (index < _bodyHandles.Length && _bodyHandles[index] == body)
        {
            var entity = _bodyEntities[index];
            _retired[body] = (entity, index < _bodyParts.Length ? _bodyParts[index] : default);
            if ((uint)entity.Id < (uint)_compoundStates.Length)
            {
                ref var cs = ref _compoundStates[entity.Id];
                if (cs.Active && cs.Root == body) retireCompound(entity, ref cs);
            }
        }
        PhysicsNativeMethods.Physics_DestroyBody(body);
    }

    private void retireCompound(Entity entity, ref CompoundState cs)
    {
        if (cs.Handles == null) return;
        foreach (var handle in cs.Handles)
        {
            var index = (int)(handle & 0xFFFF);
            if (index < _bodyHandles.Length && _bodyHandles[index] == handle) _retired[handle] = (entity, _bodyParts[index]);
        }
    }

    // ---------------------------------------------------------------------
    // 毎フレーム
    // ---------------------------------------------------------------------

    private void updateCompounds(World world, ComponentPool<Transform> transforms, ComponentPool<Static> statics,
                                 ComponentPool<Rigidbody> rigidbodies, bool hasRigidbodies, ref int batchCount)
    {
        var compounds = world.GetPool<CompoundCollider>();
        if (compounds.Count == 0) return;
        var colliders  = world.GetPool<Collider>();
        var entities   = compounds.Entities;
        var components = compounds.Components;
        for (int i = 0; i < components.Length; i++)
        {
            var entity = entities[i];
            var id = entity.Id;
            if (id >= _compoundStates.Length) Array.Resize(ref _compoundStates, Math.Max(_compoundStates.Length * 2, id + 1));
            ref var cs = ref _compoundStates[id];
            ref var compound = ref components[i];
            ref var transform = ref transforms.TryGet(id, out var hasTransform);

            if (!hasTransform || !compound.Enabled || !world.IsActive(entity))
            {
                if (cs.Active) destroyCompound(entity, ref cs);
                continue;
            }
            if (id >= _states.Length) Array.Resize(ref _states, Math.Max(_states.Length * 2, id + 1));

            var hasCollider = colliders.Has(id);
            var isStatic    = statics.Has(id);
            var signature   = signatureOf(compound.Assets);
            var ownsLost    = cs.OwnsBody && cs.Root != PhysicsNativeMethods.InvalidHandle && (!_states[id].Active || _states[id].Body != cs.Root);   // Collider を足した時など
            var rebuild = !cs.Active || cs.Signature != signature || !ReferenceEquals(cs.SentArray, compound.Assets) ||
                          cs.IsStatic != isStatic || cs.OwnsBody == hasCollider || ownsLost;
            if (rebuild)
            {
                if (cs.Active) destroyCompound(entity, ref cs);
                createCompound(entity, ref cs, ref compound, ref transform, isStatic, !hasCollider, signature, rigidbodies);
                continue;
            }
            if (cs.Root == PhysicsNativeMethods.InvalidHandle) continue;   // 部品が無い

            if (!cs.OwnsBody)
            {
                // 同じエンティティの Collider とは当たらない (Collider の体を作り直したら付け直す)
                ref var colliderState = ref _states[id];
                if (colliderState.Active && cs.GroupedBody != colliderState.Body)
                {
                    PhysicsNativeMethods.Physics_SetBodyGroup(colliderState.Body, groupOf(entity));
                    cs.GroupedBody = colliderState.Body;
                }
                if (!transform.Changed || transform.LocalToWorld == cs.Sent) continue;
                cs.Sent = transform.LocalToWorld;
            }
            else
            {
                ref var state = ref _states[id];
                if (state.Rigid != 0 || hasRigidbodies) syncRigidbody(entity, ref state, ref transform, rigidbodies);
                if (state.Rigid != 0)
                {
                    if (!userMovedRigidbody(id, ref state, ref transform)) continue;
                }
                else if (!transform.Changed || transform.LocalToWorld == state.Sent) continue;
                state.Sent = transform.LocalToWorld;
            }
            if (batchCount == _batch.Length) Array.Resize(ref _batch, _batch.Length * 2);
            _batch[batchCount++] = new PhysicsNativeMethods.BodyTransform
            {
                Handle = cs.Root, Position = transform.WorldPosition, Rotation = transform.WorldRotation, Scale = transform.WorldScale,
            };
        }
    }

    private static int signatureOf(CollisionAsset?[]? assets)
    {
        var hash = new HashCode();
        if (assets == null) return 0;
        hash.Add(assets.Length);
        foreach (var asset in assets)
        {
            hash.Add(asset == null ? 0 : RuntimeHelpers.GetHashCode(asset));
            hash.Add(asset?.Version ?? 0);
        }
        return hash.ToHashCode();
    }

    private void createCompound(Entity entity, ref CompoundState cs, ref CompoundCollider compound, ref Transform transform, bool isStatic,
                                bool ownsBody, int signature, ComponentPool<Rigidbody> rigidbodies)
    {
        cs = new CompoundState
        {
            Active = true, Root = PhysicsNativeMethods.InvalidHandle, Handles = Array.Empty<uint>(), Signature = signature,
            SentArray = compound.Assets, IsStatic = isStatic, OwnsBody = ownsBody, Sent = transform.LocalToWorld,
        };

        // 部品を並べる (アセットの順 → ノードの順)
        var count = 0;
        var assets = compound.Assets ?? Array.Empty<CollisionAsset?>();
        var position = transform.WorldPosition;
        var rotation = transform.WorldRotation;
        var scale    = transform.WorldScale;
        for (int a = 0; a < assets.Length; a++)
        {
            var asset = assets[a];
            if (asset == null) continue;
            foreach (ref readonly var part in asset.Parts)
            {
                if (count == _partScratch.Length) Array.Resize(ref _partScratch, _partScratch.Length * 2);
                _partScratch[count++] = new PhysicsNativeMethods.PartDesc
                {
                    Body = new PhysicsNativeMethods.BodyDesc
                    {
                        Shape = (uint)part.Shape, Layer = (uint)part.Layer, CollideMask = part.CollideWith, IsStatic = isStatic ? 1u : 0u,
                        Center = part.Center, Size = part.Size, Position = position, Rotation = rotation, Scale = scale,
                        Friction = part.Friction, Restitution = part.Restitution, Flags = part.IsTrigger ? 1u : 0u,
                    },
                    LocalRotation = part.Rotation,
                };
            }
        }
        if (count == 0) return;   // 部品が無い (アセットが空 / 全部 Empty)
        if (_handleScratch.Length < count) _handleScratch = new uint[_partScratch.Length];

        fixed (PhysicsNativeMethods.PartDesc* parts = _partScratch)
        fixed (uint* handles = _handleScratch)
        {
            PhysicsNativeMethods.Physics_CreateCompound(parts, (uint)count, groupOf(entity), handles);
        }

        // 体の番号 → (エンティティ, 部品)
        var list = new uint[count];
        var k = 0;
        for (int a = 0; a < assets.Length; a++)
        {
            var asset = assets[a];
            if (asset == null) continue;
            foreach (ref readonly var part in asset.Parts)
            {
                var handle = _handleScratch[k];
                list[k++] = handle;
                if (handle == PhysicsNativeMethods.InvalidHandle) continue;
                var node = asset.Nodes[part.NodeIndex];
                registerBody(handle, entity, new ColliderPart(asset, a, part.NodeIndex, node.Id, node.Name, part.IsTrigger));
                if (cs.Root == PhysicsNativeMethods.InvalidHandle) cs.Root = handle;
            }
        }
        cs.Handles = list;
        if (cs.Root == PhysicsNativeMethods.InvalidHandle) return;

        if (ownsBody)
        {
            // Collider と同じ表に入れる (剛体 / ジョイント / 書き戻しを同じ道で)
            ref var state = ref _states[entity.Id];
            state = new BodyState
            {
                Body = cs.Root, Shape = CompoundShape, IsStatic = isStatic, Sent = transform.LocalToWorld, Active = true,
            };
            if ((uint)entity.Id < (uint)_rigidStates.Length) _rigidStates[entity.Id] = default;
            syncRigidbody(entity, ref state, ref transform, rigidbodies);
        }
        else if (_states[entity.Id].Active)
        {
            PhysicsNativeMethods.Physics_SetBodyGroup(_states[entity.Id].Body, groupOf(entity));
            cs.GroupedBody = _states[entity.Id].Body;
        }
    }

    private void destroyCompound(Entity entity, ref CompoundState cs)
    {
        ref var state = ref _states[entity.Id];
        // Collider だけ残る : group を外して他の物と同じに戻す
        if (!cs.OwnsBody && cs.GroupedBody != 0 && state.Active && state.Body == cs.GroupedBody)
            PhysicsNativeMethods.Physics_SetBodyGroup(cs.GroupedBody, 0);
        if (cs.Root != PhysicsNativeMethods.InvalidHandle)
        {
            retireCompound(entity, ref cs);
            PhysicsNativeMethods.Physics_DestroyBody(cs.Root);   // 部品も消える (もう消えていれば何もしない)
            if (cs.OwnsBody && state.Active && state.Body == cs.Root)
            {
                state = default;
                if ((uint)entity.Id < (uint)_rigidStates.Length) _rigidStates[entity.Id] = default;
            }
        }
        cs = default;
    }

    // ---------------------------------------------------------------------
    // 確認用 : 部品を線で描く (重なっている部品は赤)
    // ---------------------------------------------------------------------

    private void drawCompounds(World world, uint freeColor, uint overlapColor, uint staticColor, uint sleepColor, uint triggerColor)
    {
        var compounds = world.GetPool<CompoundCollider>();
        if (compounds.Count == 0) return;
        var transforms = world.GetPool<Transform>();
        var entities   = compounds.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            if ((uint)entity.Id >= (uint)_compoundStates.Length || !world.IsActive(entity)) continue;
            ref var cs = ref _compoundStates[entity.Id];
            if (!cs.Active || cs.Handles == null || cs.Handles.Length == 0) continue;
            ref var transform = ref transforms.TryGet(entity.Id, out var found);
            if (!found) continue;
            var sleeping = cs.OwnsBody && (uint)entity.Id < (uint)_rigidStates.Length && _states[entity.Id].Rigid != 0 && _rigidStates[entity.Id].Sleeping;
            foreach (var handle in cs.Handles)
            {
                var index = (int)(handle & 0xFFFF);
                if (handle == PhysicsNativeMethods.InvalidHandle || index >= _bodyParts.Length || _bodyHandles[index] != handle) continue;
                var part = _bodyParts[index];
                if (part.Asset == null) continue;
                var parts = part.Asset.Parts;
                CollisionPart? data = null;
                foreach (ref readonly var p in parts) if (p.NodeIndex == part.NodeIndex) { data = p; break; }
                if (data is not { } d) continue;
                var color = sleeping ? sleepColor : _partOverlaps[index] > 0 ? overlapColor : d.IsTrigger ? triggerColor : cs.IsStatic ? staticColor : freeColor;
                CollisionDraw.Part(d, transform.WorldPosition, transform.WorldRotation, transform.WorldScale, color);
            }
        }
    }
}

// ==========================================================================
// コリジョンアセットの部品を線で描く (v1.9。CollisionSystem の確認用の表示とエディターのプレビューで同じ形にする)
//   C++ (physicsWorld.cpp の updateShape) と同じ計算 : 中心 = 位置 + 回転 × (部品の中心 × 大きさ)、
//   形の向き = 持ち主の向き × 部品の向き、形の大きさ = 部品の軸の向きの大きさ
// ==========================================================================
public static class CollisionDraw
{
    /// <summary>部品のワールドの形 (中心 / 向き / 箱の半分の大きさ・球の半径 (X)・カプセルの半径 (X) と半分の高さ (Y))</summary>
    public static (Vector3 Center, Quaternion Rotation, Vector3 Size) WorldShape(in CollisionPart part, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        var s = Vector3.Abs(scale);
        var center = position + Vector3.Transform(part.Center * scale, rotation);
        var worldRotation = Quaternion.Normalize(Quaternion.Concatenate(part.Rotation, rotation));
        var k = s;
        if (part.Rotation != Quaternion.Identity)
        {
            k = new Vector3((Vector3.Transform(Vector3.UnitX, part.Rotation) * s).Length(),
                            (Vector3.Transform(Vector3.UnitY, part.Rotation) * s).Length(),
                            (Vector3.Transform(Vector3.UnitZ, part.Rotation) * s).Length());
        }
        var size = part.Shape switch
        {
            ColliderShape.Sphere  => new Vector3(part.Size.X * MathF.Max(MathF.Max(s.X, s.Y), s.Z), 0, 0),
            ColliderShape.Capsule => new Vector3(part.Size.X * MathF.Max(k.X, k.Z), part.Size.Y * k.Y, 0),
            _                     => part.Size * k,
        };
        return (center, worldRotation, size);
    }

    // v2.2 : レイヤーの色 (黄金比で色相を回す。0 番は灰色に近い色)
    public static Vector3 LayerHue(int layer)
    {
        if (layer <= 0) return new Vector3(0.62f, 0.62f, 0.64f);
        var h = (layer * 0.618034f) % 1.0f;
        var r = Math.Clamp(MathF.Abs(h * 6 - 3) - 1, 0, 1);
        var g = Math.Clamp(2 - MathF.Abs(h * 6 - 2), 0, 1);
        var b = Math.Clamp(2 - MathF.Abs(h * 6 - 4), 0, 1);
        return new Vector3(0.25f + r * 0.7f, 0.25f + g * 0.7f, 0.25f + b * 0.7f);
    }

    /// <summary>v2.2 : 部品を DebugDraw の塗りつぶしの三角形で描く (エディターの CollisionMap)</summary>
    public static void SolidPart(in CollisionPart part, Vector3 position, Quaternion rotation, Vector3 scale, uint color)
    {
        var (center, worldRotation, size) = WorldShape(part, position, rotation, scale);
        switch (part.Shape)
        {
        case ColliderShape.Sphere:  DebugDraw.SolidSphere(center, size.X, color); break;
        case ColliderShape.Capsule: DebugDraw.SolidCapsule(center, worldRotation, size.X, size.Y, color); break;
        default:                    DebugDraw.SolidBox(center, worldRotation, size, color); break;
        }
    }

    /// <summary>部品を DebugDraw の線で描く</summary>
    public static void Part(in CollisionPart part, Vector3 position, Quaternion rotation, Vector3 scale, uint color)
    {
        var (center, worldRotation, size) = WorldShape(part, position, rotation, scale);
        switch (part.Shape)
        {
        case ColliderShape.Sphere:  DebugDraw.WireSphere(center, size.X, color); break;
        case ColliderShape.Capsule: DebugDraw.WireCapsule(center, worldRotation, size.X, size.Y, color); break;
        default:                    DebugDraw.WireBox(center, worldRotation, size, color); break;
        }
    }
}
