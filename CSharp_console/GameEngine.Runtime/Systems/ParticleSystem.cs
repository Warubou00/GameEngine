using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// GPU パーティクルの設定 (Graphics.Particles)
// ==========================================================================
public sealed class ParticleSettings
{
    // 使うか (false なら描画パスに UpdateParticles / DrawParticles を積まない)
    public bool  Enabled            { get; set; } = true;
    // 同時に生きていられる最大数 (2 の累乗に切り上げ。最大 2^21)。変えると GPU の完了を待って作り直す (生きている物は消える)
    // 1 個 約 144byte (本体 64byte × 2 本 + 並べる列。262144 個で約 36MB。以前は 84byte)
    public int   Capacity           { get; set; } = 1 << 18;
    // 奥から手前へ並べ替えて描く (半透明の前後が正しくなる。加算だけなら要らない)
    public bool  Sorting            { get; set; } = true;
    // シーンの深度で跳ね返る (ParticleEmitter.Collide の物だけ)
    public bool  Collision          { get; set; } = true;
    // 深度の面の後ろ、どこまでを「当たった」とみなすか (m。薄い物を突き抜けにくくするなら大きく)
    public float CollisionThickness { get; set; } = 0.5f;
    // 面に近づくと薄くする距離 (m。ソフトパーティクル。0 で切る)
    public float SoftDistance       { get; set; } = 0.3f;
    // 半分の解像度のバッファに描いて重ねる (画面を覆う大きな煙の塗りつぶしが 1/4 になる。細かい粒は少しぼやける)
    public bool  HalfResolution     { get; set; } = HALF_RESOLUTION_DEFAULT;
    internal const bool HALF_RESOLUTION_DEFAULT = true;

    // 計測値 (GPU の値は 2 フレーム前)。毎フレーム ParticleSystem が読む
    public int   AliveCount   { get; internal set; }
    public int   VisibleCount { get; internal set; }      // 画面に映った数 (描いた数)
    public int   EmitterCount { get; internal set; }
    public int   AppliedCapacity { get; internal set; }   // C++ が実際に作った容量 (2 の累乗)
    public float GpuUpdateMs  { get; internal set; }      // 発生 + 更新 + ソート
    public float GpuSortMs    { get; internal set; }
    public float GpuDrawMs    { get; internal set; }
    public bool  StatsValid   { get; internal set; }
}

// ==========================================================================
// GPU パーティクルのエミッターを集めて C++ へ送る (エンジン組み込み。TransformSystem の後に動く)
//   毎フレーム : エミッターごとに「このフレームに出す数」を決め (Rate × 経過時間 の端数は持ち越す + Burst)、
//                全エミッターを 1 回の P/Invoke でまとめて送る。パーティクルそのものは GPU にしか無い
//   エミッターの番号 (スロット) : パーティクルは生まれたエミッターの番号で動かし方を読むので、
//                エミッターを外しても、出た物が全部消える (寿命の最大 + 少し) まではその番号を使い回さない
//                (資料 MH:W p.87 : 削除したエミッターの領域を配下のパーティクルが読んで壊れる、の対策)
//   容量は最初のエミッターが現れた時に確保する (使わないシーンでは VRAM を使わない)
// ==========================================================================
internal sealed unsafe class ParticleSystem : ISystem
{
    private const float RetireMargin = 0.25f;   // 外したエミッターの番号を使い回すまでの余裕 (秒。GPU は 2 フレーム遅れ)

    private enum SlotState : byte { Free, Active, Retiring }

    private readonly ParticleSettings _settings;
    private readonly NativeMethods.ParticleEmitterData[] _data = new NativeMethods.ParticleEmitterData[NativeMethods.MaxParticleEmitters];
    private readonly SlotState[] _states = new SlotState[NativeMethods.MaxParticleEmitters];
    private readonly float[]     _retireTimers = new float[NativeMethods.MaxParticleEmitters];
    private readonly Entity[]    _owners = new Entity[NativeMethods.MaxParticleEmitters];   // その番号を使っているエンティティ
    private readonly Stack<int>  _freeSlots = new();
    private int  _slotCount;       // 使ったことのある一番大きい番号 + 1 (送る数)
    private int  _activeCount;
    private int  _appliedCapacity = -1;
    private bool _warned;
    private uint _seed = 0x1234567u;

    public ParticleSystem(World world, ParticleSettings settings)
    {
        _settings = settings;
        // 外した / エンティティを削除した : 新しく出さなくし、出た物が消えるまで番号を取っておく
        world.GetPool<ParticleEmitter>().Removed += (entity, emitter) => Retire(entity, emitter.SlotPlusOne - 1, emitter.LifeMax);
    }

    public void Update(World world)
    {
        var pool = world.GetPool<ParticleEmitter>();
        if (pool.Count == 0 && _activeCount == 0 && _slotCount == 0) return;   // 一度も使っていない

        // 容量 (最初に使う時 / 変えた時だけ。C++ が GPU の完了を待って作り直す)
        var capacity = _settings.Enabled ? Math.Clamp(_settings.Capacity, 0, NativeMethods.MaxParticleCapacity) : 0;
        if (capacity != _appliedCapacity)
        {
            NativeMethods.Renderer_SetParticleCapacity((uint)capacity);
            _appliedCapacity = capacity;
        }

        var dt = Time.DeltaTime;
        var transforms = world.GetPool<Transform>();
        var entities   = pool.Entities;
        var emitters   = pool.Components;
        for (int i = 0; i < emitters.Length; i++)
        {
            ref var emitter = ref emitters[i];
            // 外して付け直した (値を写して Add した) 物は、古い番号を持ったまま来る。持ち主が違えば新しい番号にする
            var owned = emitter.SlotPlusOne > 0 && _states[emitter.SlotPlusOne - 1] == SlotState.Active && _owners[emitter.SlotPlusOne - 1] == entities[i];
            if (!owned)
            {
                emitter.SlotPlusOne     = 0;
                emitter.HasLastPosition = false;
                var slot = AllocateSlot();
                if (slot < 0)
                {
                    if (!_warned) Console.WriteLine($"[ParticleSystem] Too many emitters. Only {NativeMethods.MaxParticleEmitters} are used.");
                    _warned = true;
                    continue;
                }
                emitter.SlotPlusOne = slot + 1;
                _owners[slot]       = entities[i];
            }

            ref var transform = ref transforms.TryGet(entities[i].Id, out var hasTransform);
            var position = hasTransform ? transform.Position : Vector3.Zero;
            var rotation = hasTransform ? transform.Rotation : Quaternion.Identity;
            var previous = emitter.HasLastPosition ? emitter.LastPosition : position;
            emitter.LastPosition    = position;
            emitter.HasLastPosition = true;

            // このフレームに出す数 : Rate × 経過時間 (端数は次へ持ち越す) + Burst
            uint spawn = 0;
            if (emitter.Emitting && emitter.Rate > 0.0f)
            {
                var exact = emitter.Rate * dt + emitter.SpawnRemainder;
                var whole = MathF.Floor(exact);
                emitter.SpawnRemainder = exact - whole;
                spawn = (uint)Math.Min(whole, (float)NativeMethods.MaxParticleCapacity);
            }
            if (emitter.Burst > 0)
            {
                spawn += (uint)emitter.Burst;
                emitter.Burst = 0;
            }

            var direction = Vector3.Transform(emitter.Direction, rotation);
            direction = direction.LengthSquared() > 1e-12f ? Vector3.Normalize(direction) : Vector3.UnitY;
            ref var data = ref _data[emitter.SlotPlusOne - 1];
            data = new NativeMethods.ParticleEmitterData
            {
                Position     = position,
                SpawnCount   = spawn,
                PrevPosition = previous,
                Direction    = direction,
                ConeAngle    = Math.Clamp(emitter.ConeAngle, 0.0f, 180.0f) * MathEx.Deg2Rad,
                SpeedMin     = emitter.SpeedMin,
                SpeedMax     = Math.Max(emitter.SpeedMin, emitter.SpeedMax),
                LifeMin      = Math.Max(0.001f, emitter.LifeMin),
                LifeMax      = Math.Max(Math.Max(0.001f, emitter.LifeMin), emitter.LifeMax),
                Gravity      = emitter.Gravity,
                Drag         = Math.Max(0.0f, emitter.Drag),
                ColorStart   = emitter.ColorStart,
                ColorEnd     = emitter.ColorEnd,
                SizeStart    = emitter.SizeStart,
                SizeEnd      = emitter.SizeEnd,
                Shape        = (uint)emitter.Shape | ((uint)emitter.Sprite << 8),
                ShapeRadius  = emitter.ShapeRadius,
                Flags        = (emitter.Blend == ParticleBlend.Additive ? 1u : 0u) | (emitter.Collide ? 2u : 0u) | (emitter.StretchScale > 0.0f ? 4u : 0u),
                Restitution  = Math.Clamp(emitter.Restitution, 0.0f, 1.0f),
                AngularSpeed = emitter.AngularSpeed * MathEx.Deg2Rad,
                Seed         = NextSeed(),
                Turbulence   = Math.Max(0.0f, emitter.Turbulence),
                StretchScale = Math.Max(0.0f, emitter.StretchScale),
            };
        }

        // 外したエミッター : 出た物が消えるまで (寿命の最大 + 余裕) 待ってから番号を返す
        for (int slot = 0; slot < _slotCount; slot++)
        {
            if (_states[slot] != SlotState.Retiring) continue;
            _retireTimers[slot] -= dt;
            if (_retireTimers[slot] > 0.0f) continue;
            _states[slot] = SlotState.Free;
            _freeSlots.Push(slot);
        }
        // 後ろの空いた番号は送らない
        while (_slotCount > 0 && _states[_slotCount - 1] == SlotState.Free) _slotCount--;

        var frame = new NativeMethods.ParticleFrameSettings
        {
            DeltaTime          = dt,
            Flags              = (_settings.Collision ? 1u : 0u) | (_settings.Sorting ? 2u : 0u),
            CollisionThickness = _settings.CollisionThickness,
            SoftDistance       = _settings.SoftDistance,
            Time               = (float)Time.TotalTime,
        };
        fixed (NativeMethods.ParticleEmitterData* data = _data)
        {
            NativeMethods.Renderer_SetParticleEmitters(data, (uint)_slotCount, ref frame);
        }

        NativeMethods.Renderer_GetParticleStats(out var stats);
        _settings.AliveCount      = (int)stats.Alive;
        _settings.VisibleCount    = (int)stats.Visible;
        _settings.EmitterCount    = pool.Count;
        _settings.AppliedCapacity = (int)stats.Capacity;
        _settings.GpuUpdateMs     = stats.UpdateMs;
        _settings.GpuSortMs       = stats.SortMs;
        _settings.GpuDrawMs       = stats.DrawMs;
        _settings.StatsValid      = stats.Valid != 0;
    }

    private int AllocateSlot()
    {
        int slot;
        if (_freeSlots.Count > 0) slot = _freeSlots.Pop();
        else if (_slotCount < NativeMethods.MaxParticleEmitters) slot = _slotCount;
        else return -1;
        // 一度 Free になって後ろが詰められた番号は、_freeSlots に残っていることがある (_slotCount を広げ直す)
        _slotCount = Math.Max(_slotCount, slot + 1);
        _states[slot] = SlotState.Active;
        _activeCount++;
        return slot;
    }

    private void Retire(Entity owner, int slot, float lifeMax)
    {
        if (slot < 0 || slot >= _states.Length || _states[slot] != SlotState.Active || _owners[slot] != owner) return;
        _states[slot]       = SlotState.Retiring;
        _owners[slot]       = Entity.Null;
        _retireTimers[slot] = Math.Max(0.001f, lifeMax) + RetireMargin;
        _data[slot].SpawnCount = 0;   // 新しく出さない (位置と動かし方はそのまま = 出た物はそのまま動く)
        _activeCount--;
    }

    private uint NextSeed()
    {
        _seed = _seed * 1664525u + 1013904223u;
        return _seed;
    }
}
