using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

internal enum SpriteRequest : byte { Play, Stop, Pause, Resume, SetTime }

// ==========================================================================
// スプライトのアニメーション (v2.4。エンジン組み込み)
//   ゲームの Update の後に時間を進め、今のコマを同じ物の SpriteRenderer に書く。
//   コマに入った時のイベント / Once の終わりは、Behaviour<T> の OnSpriteEvent / OnSpriteFinished へ送る (この後すぐ)
//   編集中 (再生していない) は時間を進めず、最初のコマを出す (PreviewInEditor = true なら編集中も動かす。イベントは送らない)
//   Play / Stop などはコンポーネントのコピーから呼ばれても効くように、Owner に宛てて積み、ここで本物に書く (AudioSource と同じ)
// ==========================================================================
internal sealed class SpriteAnimationSystem
{
    private static readonly List<(Entity Entity, SpriteRequest Request, SpriteAnimation? Animation, float Time)> _requests = new();
    private readonly List<(Entity Entity, string? Name)> _events = new();   // Name = null は終わり

    /// <summary>編集中もアニメーションを動かす (エディターの設定)</summary>
    public static bool PreviewInEditor { get; set; }

    /// <summary>前のフレームで送ったイベントの数 (確認用)</summary>
    public int EventsSent { get; private set; }
    public int AnimatorCount { get; private set; }

    internal static void Post(Entity owner, SpriteRequest request, SpriteAnimation? animation = null, float time = 0)
    {
        if (!owner.IsNull) lock (_requests) _requests.Add((owner, request, animation, time));
    }

    /// <summary>前のフレームの時間 (ms。計測用)</summary>
    public double LastMilliseconds { get; private set; }

    public void Update(World world, bool playing, float deltaTime)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try { update(world, playing, deltaTime); }
        finally { LastMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }

    private void update(World world, bool playing, float deltaTime)
    {
        var animators = world.GetPool<SpriteAnimator>();
        var renderers = world.GetPool<SpriteRenderer>();
        applyRequests(world, animators);
        _events.Clear();
        var entities = animators.Entities;
        var components = animators.Components;
        AnimatorCount = components.Length;
        bool advance = playing || PreviewInEditor;
        for (int i = 0; i < components.Length; i++)
        {
            ref var a = ref components[i];
            var entity = entities[i];
            a.Owner = entity;
            if (!world.IsActive(entity)) continue;
            var animation = a.Animation;
            if (animation != a.Applied || (animation != null && animation.Version != a.AnimationVersion))
            {
                // 違うアニメーションになった / 中身が変わった : 最初から (イベントは今のコマから)
                if (animation != a.Applied) { a.Time = 0; a.Step = -1; }
                a.Applied = animation;
                a.AnimationVersion = animation?.Version ?? -1;
            }
            if (animation == null || animation.FrameCount == 0) { a.Frame = 0; continue; }

            if (!playing)
            {
                // 編集中 : 再生ボタンで最初から始まるように、再生の状態は持たない
                a.Started = false;
                if (!PreviewInEditor) { a.Playing = false; a.Time = 0; a.Step = -1; }
                else { a.Playing = true; a.Time += deltaTime * Math.Max(0, a.Speed); }
            }
            else
            {
                if (!a.Started) { a.Started = true; if (a.PlayOnStart) { a.Playing = true; a.Paused = false; a.Time = 0; a.Step = -1; } }
                if (a.Playing && !a.Paused) a.Time += deltaTime * Math.Max(0, a.Speed);
            }

            var loop = animation.LoopFor(a.Loop);
            var sample = animation.Sample(a.Time, loop);
            a.Frame = sample.Frame;
            if (playing && a.Playing)
            {
                // 前のフレームから今までに入ったコマのイベント (速い / 重いフレームで飛ばしたコマの分も。多すぎる時は最後の 64 個)
                long from = a.Step < 0 ? sample.Step : a.Step + 1;
                if (sample.Step - from >= 64) from = sample.Step - 63;
                for (long s = from; s <= sample.Step; s++)
                {
                    var name = animation.Frames[animation.FrameOfStep(s, loop)].Event;
                    if (!string.IsNullOrEmpty(name)) _events.Add((entity, name));
                }
                a.Step = sample.Step;
                if (sample.Finished)
                {
                    a.Playing = false;
                    _events.Add((entity, null));
                }
            }

            if (renderers.Has(entity.Id))
            {
                ref var r = ref renderers.Get(entity.Id);
                r.AnimatedFrame = animation.TextureOf(a.Frame);
                r.AnimatedPivot = animation.Pivot;
                r.AnimatedPixelsPerUnit = animation.PixelsPerUnit;
                r.AnimatedStamp = Time.FrameCount;
                r.AnimatedHidden = playing && a.HideWhenStopped && !a.Playing;
            }
        }
        EventsSent = _events.Count;
    }

    private static void applyRequests(World world, ComponentPool<SpriteAnimator> animators)
    {
        lock (_requests)
        {
            foreach (var (entity, request, animation, time) in _requests)
            {
                if (!world.IsAlive(entity) || !animators.Has(entity.Id)) continue;
                ref var a = ref animators.Get(entity.Id);
                switch (request)
                {
                    case SpriteRequest.Play:
                        if (animation != null) a.Animation = animation;
                        a.Playing = true; a.Paused = false; a.Started = true; a.Time = 0; a.Step = -1;
                        break;
                    case SpriteRequest.Stop: a.Playing = false; a.Paused = false; a.Started = true; a.Time = 0; a.Step = -1; break;
                    case SpriteRequest.Pause: if (a.Playing) a.Paused = true; break;
                    case SpriteRequest.Resume: a.Paused = false; break;
                    case SpriteRequest.SetTime: a.Time = time; a.Step = -1; a.Started = true; break;
                }
            }
            _requests.Clear();
        }
    }

    /// <summary>イベントを Behaviour&lt;T&gt; へ送る (ゲームのコード)</summary>
    public void Dispatch(World world, IReadOnlyList<ISystem> systems)
    {
        if (_events.Count == 0) return;
        foreach (var system in systems)
        {
            if (system is not ISpriteEventReceiver receiver || !receiver.WantsSpriteEvents) continue;
            foreach (var (entity, name) in _events)
                if (world.IsAlive(entity)) receiver.ReceiveSprite(world, entity, name);
            receiver.AfterSpriteEvents(world);
        }
    }
}

/// <summary>スプライトのイベントを受け取る物 (Behaviour<T>)</summary>
internal interface ISpriteEventReceiver
{
    bool WantsSpriteEvents { get; }
    void ReceiveSprite(World world, Entity entity, string? name);
    void AfterSpriteEvents(World world);
}

// ==========================================================================
// SpriteRenderer を C++ の描画オブジェクトへ (v2.4。エンジン組み込み。カメラの後 / RenderSyncSystem の前)
//   板 = Quad のモデル (-0.5〜0.5、表は -Z)。行列 = 大きさ × 中心のずれ × (Transform の行列 / ビルボード)
//   マテリアル = (テクスチャ, 色, 影) ごとの共有 (半透明 + 両面)。コマが変わったら付けるマテリアルを替えるだけ
//   行列は変わった時だけ送る (前に送った物と比べる)。外された / 無効になった物は描画オブジェクトを消す
// ==========================================================================
internal sealed unsafe class SpriteRenderSystem
{
    private readonly World _world;
    private readonly AssetManager _assets;
    private Model _quad;
    private readonly Dictionary<(uint Texture, Vector4 Color), Material> _materials = new();
    private readonly Dictionary<Material, int> _materialUsers = new(ReferenceEqualityComparer.Instance);
    private readonly List<uint> _pendingDestroy = new();
    private uint[] _handles = new uint[256];
    private Matrix4x4[] _matrices = new Matrix4x4[256];

    public int SpriteCount { get; private set; }
    public int MatricesSent { get; private set; }
    public int MaterialCount => _materials.Count;

    public SpriteRenderSystem(World world, AssetManager assets)
    {
        _world = world;
        _assets = assets;
        world.GetPool<SpriteRenderer>().Removed += (_, r) =>
        {
            if (r.Created) _pendingDestroy.Add(r.Handle);
            if (r.AppliedMaterial != 0) releaseMaterial(r.AppliedMaterial - 1);
        };
    }

    /// <summary>前のフレームの時間 (ms。計測用)</summary>
    public double LastMilliseconds { get; private set; }

    public void Update(World world, Vector3 cameraForward, Vector3 cameraUp)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try { update(world, cameraForward, cameraUp); }
        finally { LastMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds; }
    }

    private void update(World world, Vector3 cameraForward, Vector3 cameraUp)
    {
        flushDestroy();
        var renderers = world.GetPool<SpriteRenderer>();
        var transforms = world.GetPool<Transform>();
        var infos = world.GetPool<EntityInfo>();
        var entities = renderers.Entities;
        var components = renderers.Components;
        SpriteCount = components.Length;
        MatricesSent = 0;
        if (components.Length == 0) return;
        if (!_quad.IsValid) _quad = _assets.LoadPrimitive(PrimitiveShape.Quad);

        int count = 0;
        long frame = Time.FrameCount;
        for (int i = 0; i < components.Length; i++)
        {
            ref var r = ref components[i];
            var entity = entities[i];
            ref var transform = ref transforms.TryGet(entity.Id, out var hasTransform);
            // 出す画像 : このフレームに SpriteAnimator が書いたコマ、無ければ Sprite
            var texture = r.AnimatedStamp == frame && r.AnimatedFrame.IsValid ? r.AnimatedFrame : _assets.LoadSpriteTexture(r.Sprite);
            var animated = r.AnimatedStamp == frame && r.AnimatedFrame.IsValid;
            var hidden = r.AnimatedStamp == frame && r.AnimatedHidden;
            if (hidden || !hasTransform || !transform.HasMatrix || !world.IsActive(entity) || !texture.IsValid || r.Color.W <= 0f)
            {
                destroy(ref r);
                r.CurrentTexture = SpriteFrameTexture.None;
                continue;
            }
            if (!r.Created)
            {
                uint handle;
                NativeMethods.Renderer_CreateRenderObjects(_quad.Handle, 1, &handle);
                if (handle == NativeMethods.InvalidHandle) continue;
                r.Handle = handle;
                r.Created = true;
                r.AppliedMaterial = 0;
                r.AppliedFlagsPlusOne = 0;
                r.HasSentMatrix = false;
            }

            // マテリアル (テクスチャと色ごとに共有)
            if (!(_materials.TryGetValue((texture.Texture, r.Color), out var current) && current.Handle + 1 == r.AppliedMaterial))
            {
                var material = acquireMaterial(texture.Texture, r.Color);
                NativeMethods.Renderer_SetRenderObjectMaterial(r.Handle, material.Handle);
                if (r.AppliedMaterial != 0) releaseMaterial(r.AppliedMaterial - 1);
                r.AppliedMaterial = material.Handle + 1;
            }
            r.CurrentTexture = texture.Texture;

            // 影の印とレイヤー (影を受けるのはいつも。影を落とすのは CastShadows)
            var layer = infos.Has(entity.Id) ? Math.Clamp(infos.Get(entity.Id).Layer, 0, 31) : 0;
            var flags = r.CastShadows ? 0u : 1u;
            var wanted = (flags | ((uint)layer << 8)) + 1;
            if (wanted != r.AppliedFlagsPlusOne)
            {
                NativeMethods.Renderer_SetRenderObjectFlags(r.Handle, flags, 1u << layer);
                r.AppliedFlagsPlusOne = wanted;
            }

            // 行列
            var ppu = Math.Max(animated ? r.AnimatedPixelsPerUnit : r.PixelsPerUnit, 0.001f);
            var pivot = animated ? r.AnimatedPivot : r.Pivot;
            var size = r.Size.X > 0 && r.Size.Y > 0 ? r.Size : new Vector2(texture.Width, texture.Height) / ppu;
            r.CurrentSize = size;
            var local = Matrix4x4.CreateTranslation(0.5f - pivot.X, 0.5f - pivot.Y, 0) *
                        Matrix4x4.CreateScale(size.X * (r.FlipX ? -1 : 1), size.Y * (r.FlipY ? -1 : 1), 1);
            Matrix4x4 world4;
            if (r.Billboard == SpriteBillboard.None) world4 = local * transform.LocalToWorld;
            else
            {
                Matrix4x4.Decompose(transform.LocalToWorld, out var scale, out _, out var position);
                var forward = cameraForward;
                var up = cameraUp;
                if (r.Billboard == SpriteBillboard.Vertical) { forward = new Vector3(forward.X, 0, forward.Z); up = Vector3.UnitY; }
                if (forward.LengthSquared() < 1e-8f) forward = Vector3.UnitZ;
                var rotation = MathEx.LookRotation(Vector3.Normalize(forward), up);
                world4 = local * Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
            }
            if (r.HasSentMatrix && r.SentMatrix == world4) continue;
            r.SentMatrix = world4;
            r.HasSentMatrix = true;
            if (count == _handles.Length) { Array.Resize(ref _handles, count * 2); Array.Resize(ref _matrices, count * 2); }
            _handles[count] = r.Handle;
            _matrices[count] = world4;
            count++;
        }
        if (count > 0)
        {
            fixed (uint* h = _handles)
            fixed (Matrix4x4* m = _matrices)
                NativeMethods.Renderer_SetRenderObjectTransforms(h, m, (uint)count);
        }
        MatricesSent = count;
        flushDestroy();
    }

    private void destroy(ref SpriteRenderer r)
    {
        if (r.Created) _pendingDestroy.Add(r.Handle);
        if (r.AppliedMaterial != 0) releaseMaterial(r.AppliedMaterial - 1);
        r.Created = false;
        r.AppliedMaterial = 0;
        r.HasSentMatrix = false;
    }

    private Material acquireMaterial(uint texture, Vector4 color)
    {
        if (!_materials.TryGetValue((texture, color), out var material))
        {
            material = new Material { Color = color, Transparent = true, DoubleSided = true, Roughness = 1f };
            material.TextureHandleOverride = texture;
            material.Apply();
            _materials[(texture, color)] = material;
            _byHandle[material.Handle] = ((texture, color), material);
            _materialUsers[material] = 0;
        }
        else if (_materialUsers[material] == 0) _unused = Math.Max(0, _unused - 1);   // 使っていなかった物をまた使う
        _materialUsers[material]++;
        return material;
    }

    // 使う物が無くなっても、すぐには返さない (C++ のマテリアルを返すと GPU を待つので。色を毎フレーム変える時など)。
    // 使っていない物が MaxUnused を越えたら、使っていない物をまとめて返す
    private const int MaxUnused = 256;
    private int _unused;

    private void releaseMaterial(uint handle)
    {
        if (!_byHandle.TryGetValue(handle, out var entry)) return;
        if (--_materialUsers[entry.Material] > 0) return;
        if (++_unused <= MaxUnused) return;
        foreach (var (key, material) in _materials.ToList())
        {
            if (_materialUsers[material] > 0) continue;
            _materials.Remove(key);
            _materialUsers.Remove(material);
            _byHandle.Remove(material.Handle);
            _retired.Add(material);
        }
        _unused = 0;
    }

    private readonly Dictionary<uint, ((uint, Vector4) Key, Material Material)> _byHandle = new();
    private readonly List<Material> _retired = new();

    private void flushDestroy()
    {
        if (_pendingDestroy.Count > 0)
        {
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pendingDestroy);
            fixed (uint* handles = span) NativeMethods.Renderer_DestroyRenderObjects(handles, (uint)span.Length);
            _pendingDestroy.Clear();
        }
        if (_retired.Count > 0)
        {
            foreach (var m in _retired) m.Dispose();
            _retired.Clear();
        }
    }
}
