// ==============================================================
// particleSim.shader
//
// GPU パーティクルの発生 / 更新 / 生死の管理 / ソート (コンピュートシェーダー)
//   CPU (C# の ParticleSystem) は毎フレーム、エミッター (置き場所・出す数・動かし方) を送るだけで、
//   パーティクル 1 個 1 個のデータは GPU の中にしか無い。
//   参考 : CAPCOM「MH:W | GPU Particle」(GCC2018)。設計は ClaudLog/Engine_GpuParticles_Design_2026-11-03.txt
//
//   バッファ
//     g_ParticlesIn  : このフレームに動かすパーティクル [容量]。先頭から「入力の数」個 = 前のフレームの生き残り + このフレームに出した物
//     g_ParticlesOut : 生き残ったパーティクルを先頭から詰めて書く [容量]。次のフレームの入力になる (CPU が 2 本を入れ替える)
//                      描画もこれを読む
//     g_KeyList      : 生き残った物のうち画面に映る物の (ソートのキー, g_ParticlesOut の位置)。ソートしない時は描画がこれを読む
//                      (以前は生き残った物全部。画面の外の物も頂点シェーダーまで進んでいた)
//     g_SortedList   : g_KeyList を奥から順に並べた物 (ソートした時は描画がこれを読む)
//     g_DrawData     : 描画用 32byte (g_ParticlesOut と同じ位置。映る物だけ書く)。頂点シェーダーはこれだけを読む (資料の Build Primitive に当たる)
//     g_Counters     : 数 (COUNTER_*)
//     g_Args         : 更新の DispatchIndirect の引数 (+0) / 描画の DrawInstanced の引数 (+16)
//   (以前は本体 1 本 + 空きの番号の列 + (キー, 番号) の列 2 本で、更新は番号を経由して本体を読み書きしていた。
//    死んだ物の番号を使い回すうちに番号がばらばらになり、64byte をランダムな場所から読み書きしていた。
//    RTX 2070 / 100 万個 : 番号が順に並んでいる起動直後 0.86 ms → 使い回した後 1.65 ms。
//    今は入力を先頭から順に読み、出力へ順に詰めて書く (資料の Particle Binary の詰め直しに近い)。メモリは 1 個 84 → 144byte)
//
//   毎フレームの流れ (CommandContext::updateParticles)
//     CSParticleBegin (1 スレッド) → CSParticleEmit → CSParticleSimulate (DispatchIndirect) → CSParticleFinish (1 スレッド)
//     → (ソートあり) バイトニックソート / 計数ソート
//
//   資料 (MH:W) との違い
//     - 生死は Atomic で数える (資料はソートで分けて Atomic を 0 にしている)。Atomic は Wave (32 スレッド) ごとに
//       1 回にまとめる (SM6 の Wave 命令。FXC の時は 1 スレッド 1 回)
//     - 発生は Atomic 無し : 出す数を Begin で 1 回だけ決め、スレッド i は入力の末尾の決まった場所に書く
//
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ==============================================================

// RHI::ParticleEmitterData と同じ並び (160byte)
struct Emitter
{
    float3 Position;
    uint   SpawnCount;      // このフレームに出す数
    float3 PrevPosition;    // 前のフレームの位置 (この間に散らして出す)
    uint   SpawnOffset;     // 前のエミッターまでの SpawnCount の合計 (発生のスレッド → エミッター)
    float3 Direction;       // 出す向き (正規化済み)
    float  ConeAngle;       // 向きのばらつき (円錐の半角)
    float  SpeedMin;
    float  SpeedMax;
    float  LifeMin;
    float  LifeMax;
    float3 Gravity;
    float  Drag;
    float4 ColorStart;
    float4 ColorEnd;
    float  SizeStart;
    float  SizeEnd;
    uint   Shape;           // 下位 8bit : 出す場所の形 / 8〜15bit : 見た目の形
    float  ShapeRadius;
    uint   Flags;           // EMITTER_*
    float  Restitution;
    float  AngularSpeed;
    uint   Seed;
    float  Turbulence;
    float  StretchScale;
    float2 Padding;
};

// パーティクル 1 個 (64byte。シェーダーの中で使う形。バッファの並びは下の PARTICLE_LOAD / PARTICLE_STORE)
struct Particle
{
    float3 Position;
    float  Age;              // 生まれてからの時間 (秒)
    float3 Velocity;
    float  Life;             // 寿命 (秒)。Age >= Life で死ぬ
    uint   Emitter;          // 出したエミッターの番号 (動かし方を読む)
    float  Size;             // 描画用 : 今の大きさ (更新で決める)
    uint2  Color;            // 描画用 : 今の色 (half4 を詰めた物)
    float  Rotation;         // 描画用 : 回転 (ラジアン)
    float  AngularVelocity;  // 回転の速さ。速度で伸ばす物 (EMITTER_STRETCH) は伸ばす長さ (秒) を入れる
    uint   Seed;
    uint   Flags;            // 下位 8bit : エミッターの Flags の写し / 8〜15bit : 見た目の形
};

static const uint EMITTER_ADDITIVE = 1;
static const uint EMITTER_COLLIDE  = 2;
static const uint EMITTER_STRETCH  = 4;

static const uint SHAPE_POINT  = 0;
static const uint SHAPE_SPHERE = 1;
static const uint SHAPE_SHELL  = 2;
static const uint SHAPE_BOX    = 3;
static const uint SHAPE_DISC   = 4;

static const uint FRAME_COLLISION = 1;

// g_Counters の場所 (byte)
static const uint COUNTER_IN        = 0;    // 入力の数 (Begin で出す数を足す。Finish の後は生き残った数)
static const uint COUNTER_OUT       = 4;    // 出力の数 (Simulate が Atomic で数える)
static const uint COUNTER_FREE      = 8;    // 空きの数 (容量 - 生きている数。プロファイラー用)
static const uint COUNTER_SPAWNED   = 12;   // このフレームに出した数
static const uint COUNTER_EMIT_BASE = 16;   // 出した物を入れる入力の位置 (前のフレームの生き残りの数)
static const uint COUNTER_VISIBLE   = 20;   // 生き残った物のうち画面に映る数 (= 描く数 / 並べる数。Simulate が Atomic で数える)
// (以前は 20 : 出す物が使う空きリストの位置 もあった)
// g_Args の場所 (byte)
static const uint ARGS_DISPATCH = 0;        // D3D12_DISPATCH_ARGUMENTS (更新)
static const uint ARGS_DRAW     = 16;       // D3D12_DRAW_INDEXED_ARGUMENTS (描画。20byte。以前は D3D12_DRAW_ARGUMENTS)

static const uint SIMULATE_GROUP = 256;     // Pipeline::PARTICLE_THREAD_GROUP
static const uint SORT_GROUP     = 2048;    // Pipeline::PARTICLE_SORT_GROUP (1024 スレッドで 2048 個。以前は 512 スレッドで 1024 個)

static const float PI = 3.14159265f;

// ==============================================================
// リソース (Pipeline::ParticleRootParameterIndex と対応)
// ==============================================================

// [PARTICLE_PARAM_FRAME] : フレームの定数 (hlslTemplate.shader の FrameData の先頭と同じ並び。使う所まで)
cbuffer FrameData : register(b0)
{
    matrix g_ViewMatrix;
    matrix g_ProjectionMatrix;
    matrix g_InvViewProj;
    float3 g_CameraPosition;
    uint   g_LightCount;
    float3 g_Ambient;
    uint   g_LightingModel;
    float2 g_ScreenSize;
    float2 g_InvScreenSize;
};

// [PARTICLE_PARAM_CONSTANTS] : ルート定数 16 個 (CommandContext_Particles.cpp の ParticleConstants と同じ並び)
cbuffer ParticleConstants : register(b1)
{
    float g_DeltaTime;
    uint  g_EmitterCount;
    uint  g_SpawnRequested;      // このフレームに出す数の合計 (CPU が頼んだ数。容量が足りなければ減る)
    uint  g_Capacity;
    uint  g_FrameSeed;
    uint  g_SimFlags;            // FRAME_*
    float g_CollisionThickness;
    float g_Time;
    uint  g_SortK;               // ソート : 並べる列の長さ (バイトニックの段)
    uint  g_SortJ;               // ソート : 比べる 2 個の距離
    uint  g_SortLevels;          // ソート : CSParticleSortStep でまとめて行う段の数 (1〜4)
    uint  g_ConstantsPadding0;
    float4 g_ConstantsPadding1;
};

StructuredBuffer<Emitter>      g_Emitters   : register(t0);
Texture2D<float>               g_SceneDepth : register(t1);
RWStructuredBuffer<uint4>      g_ParticlesIn  : register(u0);   // 16byte × 4 枚 (PARTICLE_LOAD / PARTICLE_STORE)
RWStructuredBuffer<uint4>      g_ParticlesOut : register(u1);
RWStructuredBuffer<uint2>      g_SortedList   : register(u2);
RWStructuredBuffer<uint2>      g_KeyList      : register(u3);
RWByteAddressBuffer            g_Counters   : register(u4);
RWByteAddressBuffer            g_Args       : register(u5);
RWStructuredBuffer<uint>       g_SortBins   : register(u6);   // 計数ソート : 区間ごとの数 → 書く位置
RWStructuredBuffer<uint4>      g_DrawData   : register(u7);   // 描画用 32byte × 容量 (uint4 × 2。DRAW_DATA_* と particleDraw.shader)

// ==============================================================
// パーティクル本体の並び : 16byte × 4 枚 (SoA)。i 番目の物の k 枚目 = [k × 容量 + i]
//   0 枚目 (Position, Age) / 1 枚目 (Velocity, Life) / 2 枚目 (Emitter, Size, Color) / 3 枚目 (Rotation, AngularVelocity, Seed, Flags)
//   1 命令で Wave の全スレッドが隣どうしの 16byte を読み書きする (32 スレッドで連続した 512byte)
//   (以前は 64byte の構造体の配列で、項目ごとに 4〜12byte ずつ、隣のスレッドと 64byte 離れた所を読み書きしていた。
//    RTX 2070 / 100 万個 : 計算を全部外しても更新が 1.17 ms かかり、帯域が出ていなかった)
// ==============================================================
#define PARTICLE_LOAD(buffer, i, capacity, p)                                                              \
    {                                                                                                      \
        const uint4 plane0 = buffer[(i)];                                                                  \
        const uint4 plane1 = buffer[(capacity) + (i)];                                                     \
        const uint4 plane2 = buffer[(capacity) * 2 + (i)];                                                 \
        const uint4 plane3 = buffer[(capacity) * 3 + (i)];                                                 \
        p.Position = asfloat(plane0.xyz); p.Age  = asfloat(plane0.w);                                      \
        p.Velocity = asfloat(plane1.xyz); p.Life = asfloat(plane1.w);                                      \
        p.Emitter  = plane2.x; p.Size = asfloat(plane2.y); p.Color = plane2.zw;                            \
        p.Rotation = asfloat(plane3.x); p.AngularVelocity = asfloat(plane3.y); p.Seed = plane3.z; p.Flags = plane3.w; \
    }
#define PARTICLE_STORE(buffer, i, capacity, p)                                                             \
    {                                                                                                      \
        buffer[(i)]                  = uint4(asuint(p.Position), asuint(p.Age));                          \
        buffer[(capacity) + (i)]     = uint4(asuint(p.Velocity), asuint(p.Life));                         \
        buffer[(capacity) * 2 + (i)] = uint4(p.Emitter, asuint(p.Size), p.Color);                         \
        buffer[(capacity) * 3 + (i)] = uint4(asuint(p.Rotation), asuint(p.AngularVelocity), p.Seed, p.Flags); \
    }

// ==============================================================
// 乱数 (PCG hash。スレッドごとに違う種から 0〜1 を順に取り出す)
// ==============================================================
uint pcgHash(uint v)
{
    const uint state = v * 747796405u + 2891336453u;
    const uint word  = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
    return (word >> 22u) ^ word;
}

float random01(inout uint seed)
{
    seed = pcgHash(seed);
    return (seed & 0x00FFFFFFu) * (1.0f / 16777216.0f);
}

// 向き n に垂直な 2 本 (Duff et al. "Building an Orthonormal Basis, Revisited")
void makeBasis(float3 n, out float3 t, out float3 b)
{
    const float s = n.z >= 0.0f ? 1.0f : -1.0f;
    const float a = -1.0f / (s + n.z);
    const float c = n.x * n.y * a;
    t = float3(1.0f + s * n.x * n.x * a, s * c, -s * n.x);
    b = float3(c, s + n.y * n.y * a, -n.y);
}

// 向き dir を中心に半角 angle の円錐の中の向き (angle = π なら全方向)
float3 randomInCone(float3 dir, float angle, inout uint seed)
{
    const float cosTheta = lerp(1.0f, cos(min(angle, PI)), random01(seed));
    const float sinTheta = sqrt(saturate(1.0f - cosTheta * cosTheta));
    const float phi      = 2.0f * PI * random01(seed);
    float3 t, b;
    makeBasis(dir, t, b);
    return t * (sinTheta * cos(phi)) + b * (sinTheta * sin(phi)) + dir * cosTheta;
}

float3 randomUnitVector(inout uint seed)
{
    const float z   = random01(seed) * 2.0f - 1.0f;
    const float phi = 2.0f * PI * random01(seed);
    const float r   = sqrt(saturate(1.0f - z * z));
    return float3(r * cos(phi), r * sin(phi), z);
}

// 出す場所 (エミッターからのずれ)
float3 spawnOffset(Emitter e, inout uint seed)
{
    const uint  shape  = e.Shape & 0xFFu;
    const float radius = e.ShapeRadius;
    if (shape == SHAPE_SPHERE) return randomUnitVector(seed) * (radius * pow(random01(seed), 1.0f / 3.0f));   // 体積で均一
    if (shape == SHAPE_SHELL)  return randomUnitVector(seed) * radius;
    if (shape == SHAPE_BOX)    return (float3(random01(seed), random01(seed), random01(seed)) * 2.0f - 1.0f) * radius;
    if (shape == SHAPE_DISC)
    {
        float3 t, b;
        makeBasis(e.Direction, t, b);
        const float r   = radius * sqrt(random01(seed));   // 面積で均一
        const float phi = 2.0f * PI * random01(seed);
        return t * (r * cos(phi)) + b * (r * sin(phi));
    }
    return 0.0f.xxx;
}

uint2 packColor(float4 c)
{
    return uint2(f32tof16(c.x) | (f32tof16(c.y) << 16), f32tof16(c.z) | (f32tof16(c.w) << 16));
}

// ==============================================================
// 数を数えて場所を取る (追加する列の末尾を 1 個ずつ確保する)
//   SM6 : Wave (32 / 64 スレッド) の中で「追加する物」を数え、代表の 1 スレッドだけが Atomic で場所をまとめて取る
//         (Atomic の回数が 1/32 になる。資料の「Atomic を 0 に」の狙い = 並列性を落とさない、をこれで保つ)
//   FXC (SM5) : Wave 命令が無いので 1 スレッド 1 回
//   ※ Wave 命令の前に return しないこと (全スレッドがここに来る前提)
// ==============================================================
uint appendSlot(uint counter, bool append)
{
#if defined(__SHADER_TARGET_MAJOR) && __SHADER_TARGET_MAJOR >= 6
    const uint count = WaveActiveCountBits(append);
    uint base = 0;
    if (count > 0 && WaveIsFirstLane()) g_Counters.InterlockedAdd(counter, count, base);
    base = WaveReadLaneFirst(base);
    return base + WavePrefixCountBits(append);
#else
    uint base = 0;
    if (append) g_Counters.InterlockedAdd(counter, 1, base);
    return base;
#endif
}

// ==============================================================
// CSParticleInit : 数を 0 にする (バッファを作った時だけ。1 スレッド)
//   (以前は空きリストに全部の番号を積んでいた。今は入力の先頭から詰めるので空きリストが要らない)
// ==============================================================
[numthreads(1, 1, 1)]
void CSParticleInit()
{
    g_Counters.Store4(0,  uint4(0, 0, g_Capacity, 0));
    g_Counters.Store4(16, uint4(0, 0, 0, 0));
    g_Args.Store4(ARGS_DISPATCH, uint4(0, 1, 1, 0));
    g_Args.Store4(ARGS_DRAW,     uint4(6, 0, 0, 0));
    g_Args.Store(ARGS_DRAW + 16, 0);
}

// ==============================================================
// CSParticleBegin : このフレームに出す数を決める (1 スレッド)
//   出す数 = min(CPU が頼んだ数, 容量 - 生きている数)。入力の末尾に続けて書く (Atomic 無し)
//   入力は「前のフレームの生き残り + 出す物」になる → 更新の Dispatch の引数
//   (資料の Begin Update : 前のフレームの総数 + 発生予定の数 = 仮の総数)
// ==============================================================
[numthreads(1, 1, 1)]
void CSParticleBegin()
{
    const uint aliveIn = g_Counters.Load(COUNTER_IN);
    const uint spawn   = min(g_SpawnRequested, g_Capacity - aliveIn);
    g_Counters.Store(COUNTER_IN,        aliveIn + spawn);
    g_Counters.Store(COUNTER_OUT,       0);
    g_Counters.Store(COUNTER_VISIBLE,   0);
    g_Counters.Store(COUNTER_SPAWNED,   spawn);
    g_Counters.Store(COUNTER_EMIT_BASE, aliveIn);
    g_Args.Store3(ARGS_DISPATCH, uint3((aliveIn + spawn + SIMULATE_GROUP - 1) / SIMULATE_GROUP, 1, 1));
}

// ==============================================================
// CSParticleEmit : 出す (1 スレッド 1 個)。Dispatch(CPU が頼んだ数 / 64)
//   スレッドの番号 → エミッター : SpawnOffset (前のエミッターまでの出す数の合計) を二分探索
//   (資料の Spawn / Initialize Particles。エミッターごとに Dispatch しない)
// ==============================================================
[numthreads(64, 1, 1)]
void CSParticleEmit(uint3 id : SV_DispatchThreadID)
{
    const uint spawned = g_Counters.Load(COUNTER_SPAWNED);
    if (id.x >= spawned) return;

    // SpawnOffset <= id.x の最後のエミッター (出す数が 0 のエミッターは次と同じ値なので選ばれない)
    uint lo = 0, hi = g_EmitterCount;
    while (hi - lo > 1)
    {
        const uint mid = (lo + hi) / 2;
        if (g_Emitters[mid].SpawnOffset <= id.x) lo = mid; else hi = mid;
    }
    const Emitter e = g_Emitters[lo];

    uint seed = pcgHash(id.x ^ pcgHash(g_FrameSeed ^ e.Seed));
    Particle p;
    // 動いているエミッターは、前のフレームの位置との間に散らして出す (速く動かしても点々にならない)
    const float3 origin = lerp(e.PrevPosition, e.Position, random01(seed));
    p.Position = origin + spawnOffset(e, seed);
    p.Velocity = randomInCone(e.Direction, e.ConeAngle, seed) * lerp(e.SpeedMin, e.SpeedMax, random01(seed));
    p.Life     = max(lerp(e.LifeMin, e.LifeMax, random01(seed)), 1e-3f);
    // フレームの中で生まれた時刻を散らす (同じフレームに出た物が層になって並ばないように)。少し先まで進めておく
    p.Age      = random01(seed) * g_DeltaTime;
    p.Position += p.Velocity * p.Age;
    p.Emitter  = lo;
    p.Size     = e.SizeStart;
    p.Color    = packColor(e.ColorStart);
    p.Rotation = random01(seed) * 2.0f * PI;
    p.AngularVelocity = (e.Flags & EMITTER_STRETCH) ? e.StretchScale : (random01(seed) * 2.0f - 1.0f) * e.AngularSpeed;
    p.Seed     = seed;
    p.Flags    = (e.Flags & 0xFFu) | (e.Shape & 0xFF00u);

    PARTICLE_STORE(g_ParticlesIn, g_Counters.Load(COUNTER_EMIT_BASE) + id.x, g_Capacity, p);   // 入力の末尾へ (前のフレームの生き残りの後ろ)
}

// ==============================================================
// 衝突 : シーンの深度 (前に描いた不透明な物) で跳ね返る
//   パーティクルを画面に投影し、深度の面より奥 (CollisionThickness まで) に入っていたら当たったとみなす。
//   面の法線は深度の隣のピクセルから作る。画面の外 / 面の厚みより奥 (物の裏) は当たらない
// ==============================================================
float3 depthToWorld(int2 pixel, float depth)
{
    const float2 ndc   = float2((pixel.x + 0.5f) * g_InvScreenSize.x * 2.0f - 1.0f, 1.0f - (pixel.y + 0.5f) * g_InvScreenSize.y * 2.0f);
    const float4 world = mul(float4(ndc, depth, 1.0f), g_InvViewProj);
    return world.xyz / world.w;
}

void collideWithDepth(inout Particle p, float restitution)
{
    const float4 viewPos = mul(float4(p.Position, 1.0f), g_ViewMatrix);
    if (viewPos.z <= 0.0f) return;
    const float4 clip = mul(viewPos, g_ProjectionMatrix);
    const float2 ndc  = clip.xy / clip.w;
    if (any(abs(ndc) >= 1.0f)) return;

    uint width, height;
    g_SceneDepth.GetDimensions(width, height);
    const int2 pixel = int2(float2(ndc.x * 0.5f + 0.5f, 0.5f - ndc.y * 0.5f) * float2(width, height));
    const float depth = g_SceneDepth.Load(int3(pixel, 0));
    if (depth >= 1.0f) return;   // 背景

    const float3 surface = depthToWorld(pixel, depth);
    const float  surfaceZ = mul(float4(surface, 1.0f), g_ViewMatrix).z;
    if (viewPos.z < surfaceZ || viewPos.z > surfaceZ + g_CollisionThickness) return;

    // 法線 : 右と下のピクセルの位置との外積 (画面の端は反対側を使う)
    const int2 dx = int2(pixel.x + 1 < (int)width  ? 1 : -1, 0);
    const int2 dy = int2(0, pixel.y + 1 < (int)height ? 1 : -1);
    const float3 px = depthToWorld(pixel + dx, g_SceneDepth.Load(int3(pixel + dx, 0))) - surface;
    const float3 py = depthToWorld(pixel + dy, g_SceneDepth.Load(int3(pixel + dy, 0))) - surface;
    float3 n = cross(py * dy.y, px * dx.x);
    const float lengthSq = dot(n, n);
    if (lengthSq < 1e-12f) return;
    n *= rsqrt(lengthSq);
    if (dot(n, g_CameraPosition - surface) < 0.0f) n = -n;   // カメラ側 (見えている面の表) へ向ける

    const float vn = dot(p.Velocity, n);
    if (vn < 0.0f)
    {
        // 法線の向きは反射 (跳ね返りの強さ)、面に沿う向きは少し弱める (摩擦)
        const float3 normalPart  = n * vn;
        const float3 tangentPart = p.Velocity - normalPart;
        p.Velocity = tangentPart * 0.9f - normalPart * restitution;
    }
    p.Position = surface + n * 0.01f;   // 面の上へ押し出す
}

// ==============================================================
// 描画用 32byte (particleDraw.shader の VSParticle が読む)
//   [0] : 位置 xyz / 大きさ
//   [1] : 色 (half4) / 回転 (ラジアン)。速度で伸ばす物は 回転 の代わりに「伸ばす向きと長さ」(速度 × 伸ばす秒) の xy (half2)
//         / 伸ばす向きと長さの z (half) | 形とフラグ (上位 16bit)
//   ソートした描画はばらばらの順に読むので、1 個分が 1 か所にまとまっている必要がある (本体は 16byte × 4 枚に分かれている)
//   (以前は頂点シェーダーが本体 (64byte) を読んでいた。本体を 4 枚に分けたら、ソートした描画が 100 万個で 1.18 → 2.35 ms に増えた)
// ==============================================================
void storeDrawData(uint slot, Particle p)
{
    const bool  stretch = (p.Flags & EMITTER_STRETCH) != 0;
    const float3 s      = p.Velocity * p.AngularVelocity;   // 伸ばす向きと長さ (速度 × 伸ばす秒。AngularVelocity = 伸ばす秒)
    const uint rotationOrStretch = stretch ? (f32tof16(s.x) | (f32tof16(s.y) << 16)) : asuint(p.Rotation);
    const uint stretchZAndFlags  = (stretch ? f32tof16(s.z) : 0u) | ((p.Flags & 0xFFFFu) << 16);
    g_DrawData[slot * 2]     = uint4(asuint(p.Position), asuint(p.Size));
    g_DrawData[slot * 2 + 1] = uint4(p.Color, rotationOrStretch, stretchZAndFlags);
}

// ==============================================================
// 画面に映るか (視錐台。手前の面と上下左右。奥の面は見ない)
//   四角の半径 = 大きさ (速度で伸ばす物は + 伸ばす長さ)。クリップ空間で、その半径の分だけ外側まで映るとみなす
//   (以前は全部を描いていた。RTX 2070 / 100 万個 : 全部を画面の外へ出しても頂点シェーダーと三角形の組み立てだけで 0.66 ms かかった)
// ==============================================================
bool isVisible(float4 viewPos, Particle p)
{
    float radius = p.Size;
    if ((p.Flags & EMITTER_STRETCH) != 0) radius += length(p.Velocity) * p.AngularVelocity;   // AngularVelocity = 伸ばす秒
    const float4 clip = mul(viewPos, g_ProjectionMatrix);
    // ビュー空間で radius ずらすと、クリップ空間の x / y は radius × 投影の倍率 だけ動く (透視投影 / 平行投影とも)
    const float2 margin = radius * float2(g_ProjectionMatrix[0][0], g_ProjectionMatrix[1][1]);
    return viewPos.z > -radius &&
           abs(clip.x) <= clip.w + margin.x &&
           abs(clip.y) <= clip.w + margin.y;
}

// ==============================================================
// CSParticleSimulate : 動かす / 生死を分ける (1 スレッド 1 個)。DispatchIndirect (引数は Begin が書いた)
//   入力を先頭から順に読み、生きていれば出力へ詰めて書く (死んだ物は書かないだけ = 消える)
//   (資料の Update Particle + Terminate Particles + Build Primitive (色 / 大きさ) をまとめた物)
// ==============================================================
[numthreads(SIMULATE_GROUP, 1, 1)]
void CSParticleSimulate(uint3 id : SV_DispatchThreadID)
{
    const uint count = g_Counters.Load(COUNTER_IN);
    const bool valid = id.x < count;
    bool  alive   = false;
    bool  visible = false;
    float key     = 0.0f;
    Particle p = (Particle)0;

    if (valid)
    {
        PARTICLE_LOAD(g_ParticlesIn, id.x, g_Capacity, p);
        p.Age += g_DeltaTime;
        alive = p.Age < p.Life;
        if (alive)
        {
            const Emitter e = g_Emitters[p.Emitter];
            const float dt = g_DeltaTime;

            // 速度 : 重力 → 乱れ → 空気抵抗 (指数で弱める。dt が大きくても速度の向きが反転しない)
            p.Velocity += e.Gravity * dt;
            if (e.Turbulence > 0.0f)
            {
                // 位置と時刻で揺れる滑らかな場 (sin の重ね合わせ。パーティクルごとに位相をずらす)
                const float phase = (p.Seed & 0xFFFFu) * (1.0f / 65536.0f) * 6.2831853f;
                const float3 q = p.Position * 0.9f + g_Time * float3(0.7f, 1.1f, 0.9f);
                const float3 field = float3(sin(q.y * 1.7f + q.z * 0.6f + phase),
                                            sin(q.z * 1.3f + q.x * 0.8f + phase * 1.3f),
                                            sin(q.x * 1.5f + q.y * 0.7f + phase * 0.7f));
                p.Velocity += field * (e.Turbulence * dt);
            }
            p.Velocity *= exp(-e.Drag * dt);
            p.Position += p.Velocity * dt;
            if ((p.Flags & EMITTER_STRETCH) == 0) p.Rotation += p.AngularVelocity * dt;

            if ((g_SimFlags & FRAME_COLLISION) != 0 && (p.Flags & EMITTER_COLLIDE) != 0)
            {
                collideWithDepth(p, e.Restitution);
            }

            // 描画用 : 年齢に合わせて色と大きさを変える
            const float t = saturate(p.Age / p.Life);
            p.Size  = lerp(e.SizeStart, e.SizeEnd, t);
            p.Color = packColor(lerp(e.ColorStart, e.ColorEnd, t));

            // ソートのキー : カメラからの奥行き (正の float はビットのまま uint で比べても大小が同じ)。
            // 0 は「生存数より後ろの空き」に使うので、生きている物は必ず 0 より大きくする
            const float4 viewPos = mul(float4(p.Position, 1.0f), g_ViewMatrix);
            key = max(viewPos.z, 1e-6f);
            visible = isVisible(viewPos, p);
        }
    }

    // 生き残り → 出力の末尾へ詰める (Wave でまとめて 1 回の Atomic。同じ Wave の物は隣どうしに並ぶ)
    const uint slot = appendSlot(COUNTER_OUT, alive);
    if (alive) PARTICLE_STORE(g_ParticlesOut, slot, g_Capacity, p);
    // 画面に映る物だけ → 描く列 (キー, 位置) と描画用 32byte (画面の外の物は頂点シェーダーまで進ませない)
    const uint visibleSlot = appendSlot(COUNTER_VISIBLE, visible);
    if (visible)
    {
        storeDrawData(slot, p);
        g_KeyList[visibleSlot] = uint2(asuint(key), slot);
    }
}

// ==============================================================
// CSParticleFinish : 描画の引数 (1 スレッド)
//   DrawIndexedInstanced(インデックス 6 個 = 四角 1 枚 (頂点 4 個), 映る数)。次のフレームの入力の数 = 生き残った数
//   (以前は DrawInstanced(頂点 6 個, 生き残った数)。インデックスで頂点 2 個を使い回すので、頂点シェーダーは 6 → 4 回)
//   (資料の Build Emitter Draw Args。ここでは全パーティクルを 1 回で描く)
// ==============================================================
[numthreads(1, 1, 1)]
void CSParticleFinish()
{
    const uint alive = g_Counters.Load(COUNTER_OUT);
    g_Counters.Store(COUNTER_IN,   alive);
    g_Counters.Store(COUNTER_OUT,  0);
    g_Counters.Store(COUNTER_FREE, g_Capacity - alive);
    g_Args.Store4(ARGS_DRAW, uint4(6, g_Counters.Load(COUNTER_VISIBLE), 0, 0));   // + 16 : StartInstanceLocation = 0 (Init で書いた)
}

// ==============================================================
// ソート : 生き残った物の (キー, 位置) の列を奥行きの大きい順 (奥 → 手前) に並べる
//   半透明は奥から順に重ねないと前後が崩れる (資料 p.69)。加算だけなら要らない (CPU が半透明の無い時は呼ばない)
//   g_KeyList から読み、g_SortedList へ並べる
//   並べる数の上限は CPU が決める : 2 フレーム前の生存数 + その後に出した数 (CPU が知っている)
//   (資料は全エミッターの最大数の合計から段数を決めている)
//   少ない時 (上限が 65536 以下) はバイトニックソート、多い時は計数ソート (このファイルの最後) を使う
//
// バイトニックソート
//   並べる数 N は上限以上の 2 の累乗。生存数より後ろは キー 0 (= 一番手前) として扱い、末尾に集める。
//   比べる向き : (i & k) == 0 なら大きい順
//     CSParticleSortLocal : g_KeyList から 2048 個ずつ読み、共有メモリの中で k = 2〜2048 を全部並べて g_SortedList へ (最初の Dispatch)
//     CSParticleSortStep  : k > 2048 の段で、距離 j >= 2048 の比較。1 スレッドが最大 16 個をレジスタに読み、
//                           距離 j, j/2, j/4, j/8 の 4 段をまとめて行う (列全体の読み書きが 1/4)
//     CSParticleSortMerge : k > 2048 の段の残り (距離 1024〜1) を共有メモリの中で
//   (以前は共有メモリで並べる長さが 1024、離れた比較は 1 段 1 回の Dispatch (2 個ずつ) だった。
//    65536 個 (2^16) で Dispatch 21 回 → 12 回。4096 個ずつも試したが、グループが大きすぎて少ない数で遅くなった)
// ==============================================================
static const uint SORT_THREADS    = 1024;                          // 1 グループのスレッド数
static const uint SORT_PER_THREAD = SORT_GROUP / SORT_THREADS;     // 共有メモリで 1 スレッドが持つ数 (2)
static const uint SORT_STEP_MAX   = 16;                            // CSParticleSortStep で 1 スレッドが読む最大の数 (= 4 段)

groupshared uint2 gs_Sort[SORT_GROUP];   // 2048 × 8byte = 16KB

// a と b を比べて、descending (大きい順) なら a.x >= b.x になるように入れ替える
void compareSwap(inout uint2 a, inout uint2 b, bool descending)
{
    if ((a.x < b.x) == descending)
    {
        const uint2 t = a;
        a = b;
        b = t;
    }
}

uint2 loadSortItem(uint index, uint count)
{
    // 生存数より後ろは キー 0 にする (前のフレームの古い値が残っているため)
    if (index >= count) return uint2(0, 0);
    return g_KeyList[index];
}

// 共有メモリの中で、距離 startJ (< 2048) から 1 まで比べる。1 段に 1024 組 = 1 スレッド 1 組
void sortInGroup(uint localIndex, uint groupBase, uint k, uint startJ)
{
    for (uint j = startJ; j > 0; j >>= 1)
    {
        GroupMemoryBarrierWithGroupSync();
        [unroll]
        for (uint p = 0; p < SORT_PER_THREAD / 2; ++p)
        {
            // 組の番号 q → (i, i + j)
            const uint q = localIndex + p * SORT_THREADS;
            const uint i = ((q & ~(j - 1)) << 1) | (q & (j - 1));
            uint2 a = gs_Sort[i];
            uint2 b = gs_Sort[i + j];
            compareSwap(a, b, ((groupBase + i) & k) == 0);
            gs_Sort[i]     = a;
            gs_Sort[i + j] = b;
        }
    }
    GroupMemoryBarrierWithGroupSync();
}

[numthreads(SORT_THREADS, 1, 1)]
void CSParticleSortLocal(uint3 groupId : SV_GroupID, uint3 localId : SV_GroupThreadID)
{
    const uint count = g_Counters.Load(COUNTER_VISIBLE);   // 描く数 (以前は COUNTER_IN = 生き残った数)
    const uint base  = groupId.x * SORT_GROUP;
    const uint t     = localId.x;
    [unroll]
    for (uint e = 0; e < SORT_PER_THREAD; ++e) gs_Sort[t + e * SORT_THREADS] = loadSortItem(base + t + e * SORT_THREADS, count);
    for (uint k = 2; k <= SORT_GROUP; k <<= 1)
    {
        sortInGroup(t, base, k, k >> 1);
    }
    [unroll]
    for (uint e2 = 0; e2 < SORT_PER_THREAD; ++e2) g_SortedList[base + t + e2 * SORT_THREADS] = gs_Sort[t + e2 * SORT_THREADS];
}

// 距離 g_SortJ から g_SortLevels 段 (1〜4) をまとめて比べる
//   段 j, j/2, ... s (s = j / 2^(段-1)) で入れ替わるのは、番号の s〜j のビットだけが違う 2^段 個の組の中だけ。
//   スレッドはその組を 1 つ担当し、組の中だけで全部の段を済ませてから書き戻す
//   向き ((i & k) == 0) は k のビットで決まり、k > j なので組の中で同じ
[numthreads(SIMULATE_GROUP, 1, 1)]
void CSParticleSortStep(uint3 id : SV_DispatchThreadID)
{
    const uint levels = g_SortLevels;
    const uint n      = 1u << levels;               // 1 スレッドが持つ数
    const uint s      = g_SortJ >> (levels - 1);    // 一番小さい距離
    // スレッドの番号の、s のビットの所に levels 個の 0 を差し込む = 組の先頭
    const uint base = ((id.x & ~(s - 1)) << levels) | (id.x & (s - 1));
    const bool descending = (base & g_SortK) == 0;

    uint2 v[SORT_STEP_MAX];
    [unroll]
    for (uint c = 0; c < SORT_STEP_MAX; ++c)
    {
        if (c < n) v[c] = g_SortedList[base + c * s];
    }
    [unroll]
    for (uint d = SORT_STEP_MAX / 2; d > 0; d >>= 1)   // 距離 j (= d × s) から s まで
    {
        if (d < n)
        {
            [unroll]
            for (uint c2 = 0; c2 < SORT_STEP_MAX; ++c2)
            {
                if (c2 < n && (c2 & d) == 0) compareSwap(v[c2], v[c2 + d], descending);
            }
        }
    }
    [unroll]
    for (uint c3 = 0; c3 < SORT_STEP_MAX; ++c3)
    {
        if (c3 < n) g_SortedList[base + c3 * s] = v[c3];
    }
}

[numthreads(SORT_THREADS, 1, 1)]
void CSParticleSortMerge(uint3 groupId : SV_GroupID, uint3 localId : SV_GroupThreadID)
{
    const uint base = groupId.x * SORT_GROUP;
    const uint t    = localId.x;
    [unroll]
    for (uint e = 0; e < SORT_PER_THREAD; ++e) gs_Sort[t + e * SORT_THREADS] = g_SortedList[base + t + e * SORT_THREADS];
    sortInGroup(t, base, g_SortK, SORT_GROUP / 2);
    [unroll]
    for (uint e2 = 0; e2 < SORT_PER_THREAD; ++e2) g_SortedList[base + t + e2 * SORT_THREADS] = gs_Sort[t + e2 * SORT_THREADS];
}

// ==============================================================
// 計数ソート (奥行きの区間で並べる)
//   半透明を重ねるのに要るのは「奥行きのおおよその順」なので、奥行きを SORT_BUCKETS 個の区間に分けて並べる
//     1. CSParticleBinClear   : 区間の数を 0 に
//     2. CSParticleBinCount   : 生きている物ごとに区間の数を 1 増やす (Atomic)
//     3. CSParticleBinScan    : 区間の先頭の位置 = それより奥の区間の数の合計 (1 グループで 32768 個)
//     4. CSParticleBinScatter : 区間の位置を 1 増やして (Atomic)、取れた場所へ g_KeyList → g_SortedList に写す
//   列を 2 回読むだけ (バイトニックソートは長さ N の列を何十回も読み書きする)
//   (RTX 2070 : 100 万個 3.4 ms (最初のバイトニック) / 2.1 ms (段をまとめたバイトニック) → 0.34 ms、50 万個 0.89 → 0.25 ms)
//   区間 : 奥行き (正の float) のビットの上位を使う。1/16〜4096 m を 32768 区間 = 相対 2^-11 (約 0.05%。10 m 先で約 5 mm)
//          同じ区間の中の順番は決まらない (Atomic の順)。その外 (手前 / 奥すぎる) は端の区間にまとめる
// ==============================================================
static const uint SORT_BUCKETS     = 32768;        // Pipeline::PARTICLE_SORT_BUCKETS
static const uint SORT_KEY_NEAR    = 0x3D800000u;  // asuint(1.0 / 16)
static const uint SORT_KEY_FAR     = 0x45800000u;  // asuint(4096.0)
static const uint SORT_SCAN_THREADS = 1024;
static const uint SORT_BINS_PER_THREAD = SORT_BUCKETS / SORT_SCAN_THREADS;   // 32

// 奥の物ほど小さい番号 (先に描く)
uint sortBucket(uint key)
{
    const uint bucket = (clamp(key, SORT_KEY_NEAR, SORT_KEY_FAR - 1) - SORT_KEY_NEAR) >> 12;   // 手前ほど小さい
    return SORT_BUCKETS - 1 - bucket;
}

[numthreads(SIMULATE_GROUP, 1, 1)]
void CSParticleBinClear(uint3 id : SV_DispatchThreadID)
{
    if (id.x < SORT_BUCKETS) g_SortBins[id.x] = 0;
}

[numthreads(SIMULATE_GROUP, 1, 1)]
void CSParticleBinCount(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= g_Counters.Load(COUNTER_VISIBLE)) return;   // 描く数 (以前は COUNTER_IN = 生き残った数)
    InterlockedAdd(g_SortBins[sortBucket(g_KeyList[id.x].x)], 1);
}

groupshared uint gs_Scan[2][SORT_SCAN_THREADS];

[numthreads(SORT_SCAN_THREADS, 1, 1)]
void CSParticleBinScan(uint3 localId : SV_GroupThreadID)
{
    // スレッドごとに 32 区間の合計 → スレッドの間で累積 (Hillis-Steele) → 各区間の先頭の位置 (自分より前の合計)
    const uint t     = localId.x;
    const uint first = t * SORT_BINS_PER_THREAD;
    uint sum = 0;
    [unroll]
    for (uint i = 0; i < SORT_BINS_PER_THREAD; ++i) sum += g_SortBins[first + i];

    uint current = 0;
    gs_Scan[0][t] = sum;
    GroupMemoryBarrierWithGroupSync();
    for (uint offset = 1; offset < SORT_SCAN_THREADS; offset <<= 1)
    {
        const uint value = gs_Scan[current][t] + (t >= offset ? gs_Scan[current][t - offset] : 0);
        current ^= 1;
        gs_Scan[current][t] = value;
        GroupMemoryBarrierWithGroupSync();
    }
    uint position = gs_Scan[current][t] - sum;   // 自分より前のスレッドの合計
    [unroll]
    for (uint j = 0; j < SORT_BINS_PER_THREAD; ++j)
    {
        const uint count = g_SortBins[first + j];
        g_SortBins[first + j] = position;
        position += count;
    }
}

[numthreads(SIMULATE_GROUP, 1, 1)]
void CSParticleBinScatter(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= g_Counters.Load(COUNTER_VISIBLE)) return;
    const uint2 item = g_KeyList[id.x];
    uint slot;
    InterlockedAdd(g_SortBins[sortBucket(item.x)], 1, slot);
    g_SortedList[slot] = item;
}
