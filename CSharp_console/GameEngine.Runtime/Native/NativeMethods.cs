using System.Numerics;
using System.Runtime.InteropServices;

namespace GameEngine.Native;

// ==========================================================================
// Renderer.dll (C++) の関数の宣言
//   C# から呼ぶ C++ の入口はここだけ (C++ 側は Renderer/Source/rendererDll.cpp)
//   - モデル・描画オブジェクトはハンドル (uint) でやり取りする。ポインタは受け取らない
//   - 1 フレームに何度も使う操作は配列でまとめて渡す (P/Invoke は 1 回 数十 ns かかるため)
//   - 構造体は blittable (int / float / IntPtr だけ) にして、コピー無しで渡す
//   エンジンの利用者は直接使わない (Engine / AssetManager / System 経由で使う)
// ==========================================================================
internal static unsafe class NativeMethods
{
    private const string Dll = "Renderer.dll";

    // C++ の Renderer::INVALID_HANDLE と同じ
    public const uint InvalidHandle = 0xFFFFFFFFu;

    // C++ の WindowInfo と同じ並び
    [StructLayout(LayoutKind.Sequential)]
    public struct WindowDesc
    {
        public IntPtr HWnd;
        public uint   Width;
        public uint   Height;
        public uint   EnableVSync;
    }

    // C++ の RHI::InitializeResult と同じ並び (1 = 成功)
    [StructLayout(LayoutKind.Sequential)]
    public struct InitializeResult
    {
        public int Device;
        public int CommandContext;
        public int Buffer;
        public int Texture;
        public int Pipeline;

        public readonly bool AllSucceeded =>
            Device == 1 && CommandContext == 1 && Buffer == 1 && Texture == 1 && Pipeline == 1;
    }

    // C++ の Renderer::CameraDesc と同じ並び (float ×12 + uint)。左手系 (+Z が奥)
    [StructLayout(LayoutKind.Sequential)]
    public struct CameraDesc : IEquatable<CameraDesc>
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Up;
        public float   FieldOfViewY;   // ラジアン
        public float   NearZ;
        public float   FarZ;
        public uint    Orthographic;

        public readonly bool Equals(CameraDesc o) =>
            Position == o.Position && Forward == o.Forward && Up == o.Up &&
            FieldOfViewY == o.FieldOfViewY && NearZ == o.NearZ && FarZ == o.FarZ && Orthographic == o.Orthographic;
        public override readonly bool Equals(object? obj) => obj is CameraDesc o && Equals(o);
        public override readonly int GetHashCode() => HashCode.Combine(Position, Forward, Up, FieldOfViewY, NearZ, FarZ, Orthographic);
    }

    // ---------- 描画パス (C++ の RHI::RenderTargetDesc / RenderCommand と同じ並び) ----------
    public const int MaxPassColorTargets = 4;
    public const int MaxPassInputs       = 8;
    public const int MaxPassParams       = 8;

    [StructLayout(LayoutKind.Sequential)]
    public struct RenderTargetDesc
    {
        public uint    Format;       // RenderTextureFormat
        public float   Scale;        // 画面に対する倍率 (0 なら Width / Height)
        public uint    Width;
        public uint    Height;
        public Vector4 ClearColor;
        public uint    SampleCount;  // MSAA のサンプル数 (1 / 2 / 4 / 8)
        public uint    ArraySize;    // 配列の枚数 (Depth32 だけ。カスケードのシャドーマップ)
    }

    // 120byte (C++ 側は static_assert で確かめている)
    [StructLayout(LayoutKind.Sequential)]
    public struct RenderCommand
    {
        public uint         Type;          // RHI::RenderCommandType
        public uint         ColorCount;
        public fixed uint   Colors[MaxPassColorTargets];
        public uint         Depth;
        public uint         Arg0;
        public uint         Arg1;
        public Vector4      ClearColor;
        public float        ClearDepth;
        public fixed uint   Inputs[MaxPassInputs];
        public fixed float  Params[MaxPassParams];
    }

    // C++ の RHI::LightData と同じ並び (48byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct LightData
    {
        public Vector3 Position;
        public float   Range;
        public Vector3 Direction;
        public uint    Type;        // 0 : 平行光源 / 1 : 点光源
        public Vector3 Color;
        public float   Intensity;
    }

    // C++ の RHI::LightingSettings と同じ並び (16byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct LightingSettings
    {
        public uint    Model;       // LightingModel
        public Vector3 Ambient;
    }

    // C++ の RHI::ShadowSettings と同じ並び (48byte。以前は 32byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct ShadowSettings
    {
        public uint  Enabled;
        public uint  LightIndex;     // SetLights で送ったライトの番号
        public uint  ShadowMap;      // Depth32 のレンダーターゲット
        public uint  CascadeCount;
        public float Distance;
        public float NormalBias;
        public float Softness;
        public float SplitLambda;
        public float CascadeBlend;
        public uint  Flags;          // 1 : 全カスケードを囲む箱で 1 回だけカリング / 2 : 段ごとに色を付ける
        public uint  FrozenMask;     // このフレームは描き直さないカスケード (ビット)
        public uint  Padding;

        // 値の比較 (Equals は object を受け取るので箱詰めが起きる。毎フレーム比べるので自分で比べる)
        public readonly bool SameAs(in ShadowSettings o) =>
            Enabled == o.Enabled && LightIndex == o.LightIndex && ShadowMap == o.ShadowMap && CascadeCount == o.CascadeCount &&
            Distance == o.Distance && NormalBias == o.NormalBias && Softness == o.Softness && SplitLambda == o.SplitLambda &&
            CascadeBlend == o.CascadeBlend && Flags == o.Flags && FrozenMask == o.FrozenMask;
    }

    // C++ の RHI::DebugLineVertex と同じ並び (16byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct DebugLineVertex
    {
        public Vector3 Position;
        public uint    Color;   // R8G8B8A8
    }

    // ---------- GPU パーティクル (C++ の RHI::ParticleEmitterData / ParticleFrameSettings / ParticleStats と同じ並び) ----------
    public const int MaxParticleEmitters = 1024;      // C++ の RHI::MAX_PARTICLE_EMITTERS
    public const int MaxParticleCapacity = 1 << 21;   // C++ の RHI::MAX_PARTICLE_CAPACITY

    // 160byte
    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleEmitterData
    {
        public Vector3 Position;
        public uint    SpawnCount;      // このフレームに出す数
        public Vector3 PrevPosition;    // 前のフレームの位置 (この間に散らして出す)
        public uint    SpawnOffset;     // C++ が書く
        public Vector3 Direction;       // 出す向き (正規化済み)
        public float   ConeAngle;       // ラジアン (円錐の半角)
        public float   SpeedMin;
        public float   SpeedMax;
        public float   LifeMin;
        public float   LifeMax;
        public Vector3 Gravity;
        public float   Drag;
        public Vector4 ColorStart;
        public Vector4 ColorEnd;
        public float   SizeStart;
        public float   SizeEnd;
        public uint    Shape;           // 下位 8bit : ParticleShape / 8〜15bit : ParticleSprite
        public float   ShapeRadius;
        public uint    Flags;           // 1 : 加算 / 2 : 衝突 / 4 : 速度で伸ばす
        public float   Restitution;
        public float   AngularSpeed;    // ラジアン / 秒
        public uint    Seed;
        public float   Turbulence;
        public float   StretchScale;
        public Vector2 Padding;
    }

    // 32byte
    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleFrameSettings
    {
        public float DeltaTime;
        public uint  Flags;               // 1 : 衝突 / 2 : ソート
        public float CollisionThickness;
        public float SoftDistance;
        public float Time;
        public uint  Padding0;
        public uint  Padding1;
        public uint  Padding2;
    }

    // 36byte (以前は 32byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct ParticleStats
    {
        public uint  Capacity;
        public uint  Alive;
        public uint  Free;
        public uint  Emitters;
        public float UpdateMs;
        public float SortMs;
        public float DrawMs;
        public uint  Valid;
        public uint  Visible;    // 画面に映った数 (描いた数)
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetParticleCapacity(uint capacity);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetParticleEmitters(ParticleEmitterData* emitters, uint count, ref ParticleFrameSettings settings);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_GetParticleStats(out ParticleStats stats);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LogCallback([MarshalAs(UnmanagedType.LPStr)] string message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WLogCallback([MarshalAs(UnmanagedType.LPWStr)] string message);

    // ---------- 初期化 / 終了 / 画面 ----------

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetLogCallback(LogCallback callback, WLogCallback wcallback);

    // C++ 側はポインタで受けるので ref で渡す (以前は値渡しで、x64 の呼び出し規約で偶然動いていた : 2-12)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_Initialize(out InitializeResult result, ref WindowDesc window);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_CleanUp();

    // C++ の bool は 1 バイトなので I1 で受ける (既定の 4 バイト BOOL だと上位バイトのゴミを拾う)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Renderer_Resize(uint width, uint height);

    // commands : 描画パイプラインが積んだ命令 (以前は引数が無く、シーンをバックバッファに描くだけだった)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_Render(RenderCommand* commands, uint commandCount);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_WaitForGpu();

    // ---------- モデル / 描画オブジェクト ----------

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_LoadModel([MarshalAs(UnmanagedType.LPWStr)] string filePath, out uint subMeshCount);

    // 頂点 (56 byte) とインデックスの配列からモデルを作る (SH1 : プリミティブ)。name はモデルの名前 ("builtin:Cube" など)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_CreateMesh(Primitives.NativeVertex* vertices, uint vertexCount, uint* indices, uint indexCount,
                                                  [MarshalAs(UnmanagedType.LPWStr)] string name);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_CreateRenderObjects(uint model, uint count, uint* outHandles);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_DestroyRenderObjects(uint* handles, uint count);

    // matrices : Matrix4x4 (16 float) × count。System.Numerics と DirectXMath は同じ並び (行優先・行ベクトル)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetRenderObjectTransforms(uint* handles, Matrix4x4* matrices, uint count);

    // ---------- カメラ / 設定 / プロファイラー ----------

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetCamera(ref CameraDesc camera);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetIndirectDraw([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetInstancing([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetGpuCulling([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetOcclusionCulling([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_ReportProfilerValue([MarshalAs(UnmanagedType.LPStr)] string name, double value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetProfilerInfo([MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.LPStr)] string text);

    // ---------- 描画パス / ライト ----------

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_CreateRenderTarget(ref RenderTargetDesc desc);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_DestroyRenderTarget(uint handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_GetMaxSampleCount();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_LoadFullscreenShader([MarshalAs(UnmanagedType.LPWStr)] string filePath, [MarshalAs(UnmanagedType.LPStr)] string entry);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetLights(LightData* lights, uint count);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetLightingSettings(ref LightingSettings settings);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetShadowSettings(ref ShadowSettings settings);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetDebugLines(DebugLineVertex* vertices, uint count);

    // ---------- エディター (ED3) ----------
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Renderer_SetRenderSize(uint width, uint height);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetUIDrawData(Rendering.UIVertex* vertices, uint vertexCount, ushort* indices, uint indexCount,
                                                     Rendering.UIDrawCommand* commands, uint commandCount);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_CreateUITexture(uint width, uint height, byte* rgba);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetProfilerVisible([MarshalAs(UnmanagedType.I1)] bool visible);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Renderer_IsProfilerVisible();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Renderer_GetModelGeometry(uint model, float* positions, uint maxVertices, uint* indices, uint maxIndices,
                                                        out uint vertexCount, out uint indexCount);

    // ---------- ED5 : アセットの読み直し / 解放 / マテリアル ----------

    // C++ の RendererMaterialDesc と同じ並び (32 byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct MaterialDesc
    {
        public Vector4 Color;
        public float   Metallic;
        public float   Roughness;
        public uint    Texture;   // Renderer_LoadTexture の番号 (0 = 白)
        public uint    Flags;     // bit0 : 半透明 / bit1 : 両面
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Renderer_ReloadModel(uint model, out uint subMeshCount);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Renderer_UnloadModel(uint model);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_ReloadModelsUsingTexture([MarshalAs(UnmanagedType.LPWStr)] string filePath);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Renderer_ReloadShaders();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_LoadTexture([MarshalAs(UnmanagedType.LPWStr)] string filePath);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_ReleaseTexture(uint texture);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_CreateMaterial(in MaterialDesc desc);

    // ---------- SH2 : 自分のシェーダー / 3D テクスチャ ----------

    // C++ の RHI::CustomShaderSettings と同じ並び (16 byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct CustomShaderSettings
    {
        public uint Blend;         // 0 不透明 / 1 アルファ / 2 加算 / 3 乗算済み
        public uint Cull;          // 0 裏 / 1 表 / 2 なし
        public uint DepthWrite;
        public uint Transparent;   // 1 : 半透明の列
        public uint ReadDepth;     // 1 : シーンの深度を読む (SH3)
        public uint Padding0, Padding1, Padding2;
    }

    // source は UTF-8 の 0 終わり。error は 0 終わりの UTF-8 が入る
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_CreateCustomShader(byte* source, in CustomShaderSettings settings, byte* error, uint errorCapacity);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Renderer_UpdateCustomShader(uint shader, byte* source, in CustomShaderSettings settings, byte* error, uint errorCapacity);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_DestroyCustomShader(uint shader);

    // params : float4 × paramCount (shader = 0 で外す)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetMaterialShader(uint material, uint shader, float* parameters, uint paramCount);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_CreateTexture3D(uint width, uint height, uint depth, uint channels, byte* data);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_GetCustomDrawCount();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_UpdateMaterial(uint material, in MaterialDesc desc);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_DestroyMaterial(uint material);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetRenderObjectMaterial(uint renderObject, uint material);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern ulong Renderer_GetMeshBytesUsed();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_GetLoadedModelCount();
}
