using System.Numerics;
using GameEngine.Rendering;

namespace GameEngine;

// ==========================================================================
// 描画の設定 (ED6)。シーンに 1 つ置く (エディターの GameObject > Render Settings)。シーンと一緒に保存される
//   以前は engine.Graphics をコードで設定するだけだった (見本は起動の引数とキーで切り替えていた)
//   RenderSettingsSystem が値の変わった時だけ engine.Graphics へ書く (キーで変えた値は、この値を変えるまでそのまま)
//   シーンに無ければ engine.Graphics はコードの値のまま (前のシーンの物があった時は既定の値に戻す)
//   既定の値は engine.Graphics の既定の値と同じ (置いただけでは見た目は変わらない)
// ==========================================================================
public enum RenderPath : uint
{
    Forward  = 0,
    Deferred = 1,
}

[ComponentInfo(DisplayName = "Render Settings", Category = "Rendering")]
public struct RenderSettings : IComponent
{
    [Header("Pipeline")]
    public RenderPath    Path          = RenderPath.Forward;
    public LightingModel LightingModel = LightingModel.PBR;
    public AntiAliasing  AntiAliasing  = AntiAliasing.Fxaa;

    [Header("Color")]
    public Vector3    AmbientColor    = new(0.25f, 0.27f, 0.30f);   // 環境光 (線形)
    public Vector3    BackgroundColor = new(0.1f, 0.2f, 0.4f);      // 何も無い所の色 (sRGB)
    [Range(0.05f, 8.0f)] public float Exposure = 1.0f;
    public Tonemapper Tonemapper = Tonemapper.ACES;
    [Range(1.0f, 3.0f)] public float Gamma = 2.2f;

    public SsaoValues     Ssao     = new();
    public ShadowValues   Shadows  = new();
    public FxaaValues     Fxaa     = new();
    public ParticleValues Particles = new();

    public RenderSettings()
    {
    }
}

public struct SsaoValues
{
    public bool  Enabled = true;
    [Range(0.05f, 4.0f)]  public float Radius    = 0.6f;
    [Range(0.0f, 8.0f)]   public float Intensity = 1.5f;
    [Range(0.0f, 0.5f)]   public float Bias      = 0.02f;
    [Range(1, 32)]        public int   SampleCount     = 12;
    [Range(0.25f, 1.0f)]  public float ResolutionScale = 0.5f;
    public SsaoValues() { }
}

public struct ShadowValues
{
    public bool  Enabled = true;
    [Range(256, 8192)]   public int   Resolution   = 2048;
    [Range(1.0f, 500.0f)] public float Distance    = 60.0f;
    [Range(0.0f, 8.0f)]  public float NormalBias   = 1.0f;
    [Range(0.0f, 4.0f)]  public float Softness     = 1.0f;
    [Range(1, 4)]        public int   CascadeCount = 3;
    [Range(0.0f, 1.0f)]  public float SplitLambda  = 0.75f;
    [Range(0.0f, 0.5f)]  public float CascadeBlend = 0.1f;
    public ShadowValues() { }
}

public struct FxaaValues
{
    [Range(0.0f, 1.0f)]   public float Subpixel         = 0.75f;
    [Range(0.05f, 0.5f)]  public float EdgeThreshold    = 0.166f;
    [Range(0.0f, 0.1f)]   public float EdgeThresholdMin = 0.0833f;
    public FxaaValues() { }
}

public struct ParticleValues
{
    public bool  Enabled = true;
    [Tooltip("Max alive particles (rounded up to a power of 2). Changing it recreates the GPU buffers.")]
    public int   Capacity = 1 << 18;
    public bool  Sorting = true;
    public bool  Collision = true;
    [Range(0.0f, 4.0f)] public float CollisionThickness = 0.5f;
    [Range(0.0f, 2.0f)] public float SoftDistance = 0.3f;
    public bool  HalfResolution = ParticleSettings.HALF_RESOLUTION_DEFAULT;
    public ParticleValues() { }
}

// ==========================================================================
// 場所で効く描画の上書き (ED6。Unity の Volume / Post-process volume)
//   カメラが範囲に入ると、Override... を付けた値を RenderSettings の値に混ぜる (入っている深さ = 重み)
//   範囲 : Global (どこでも) / Box (Transform の位置と回転、Size × 拡大縮小) / Sphere (半径 Size.X × 拡大縮小の X)
//   BlendDistance : 範囲の外側のこの距離から、だんだん効き始める。Priority の小さい物から順に混ぜる (大きい物が勝つ)
// ==========================================================================
public enum VolumeShape : uint
{
    Global = 0,
    Box    = 1,
    Sphere = 2,
}

[ComponentInfo(DisplayName = "Volume", Category = "Rendering")]
public struct Volume : IComponent
{
    public VolumeShape Shape = VolumeShape.Box;
    public Vector3     Size  = new(10.0f, 10.0f, 10.0f);   // Box : 辺の長さ / Sphere : X が半径
    [Range(0.0f, 50.0f)] public float BlendDistance = 2.0f;
    [Range(0.0f, 1.0f)]  public float Weight        = 1.0f;
    public int         Priority;

    [Header("Overrides")]
    public bool    OverrideExposure;
    [Range(0.05f, 8.0f)] public float Exposure = 1.0f;
    public bool    OverrideAmbient;
    public Vector3 AmbientColor = new(0.25f, 0.27f, 0.30f);
    public bool    OverrideBackground;
    public Vector3 BackgroundColor = new(0.1f, 0.2f, 0.4f);
    public bool    OverrideSsao;
    [Range(0.0f, 8.0f)] public float SsaoIntensity = 1.5f;
    public bool    OverrideShadowDistance;
    [Range(1.0f, 500.0f)] public float ShadowDistance = 60.0f;

    public Volume()
    {
    }
}
