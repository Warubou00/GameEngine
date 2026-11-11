using System.Numerics;
using GameEngine.Rendering;

namespace GameEngine;

// ==========================================================================
// 描画の設定の値 (ED6 / v1.5)
//   v1.5 : シーンに置く RenderSettings の部品は無くした (Unity と同じく、描き方はカメラごと、全体の設定は Project Settings)
//     カメラ (Camera) : 描き方 (RenderPath) / アンチエイリアス / SSAO / FXAA / 露出 / トーンマップ / ガンマ / 背景
//     Project Settings の Rendering (RenderingProjectValues) : 光の計算 / 環境光 / 空の画像 / 影 / パーティクル
//   ここにはその値の型 (SsaoValues など) と、場所で効く上書き (Volume) を置く
// ==========================================================================
public enum RenderPath : uint
{
    Forward  = 0,
    Deferred = 1,
}

// Project Settings の Rendering (プロジェクトの全部のシーンで同じ値。v1.5)
public struct RenderingProjectValues
{
    public LightingModel LightingModel = LightingModel.PBR;
    public Vector3       AmbientColor  = new(0.25f, 0.27f, 0.30f);   // 環境光 (線形)

    // 空 (カメラの ClearFlags が Skybox の時)。正距円筒の画像 (png / jpg / bmp)。空ならエンジンの既定の空
    [AssetPath(AssetKind.Texture)] public string SkyboxTexture = "";
    [Range(0.0f, 8.0f)]   public float SkyboxIntensity = 1.0f;
    [Range(0.0f, 360.0f)] public float SkyboxRotation  = 0.0f;

    public ShadowValues   Shadows   = new();
    public ParticleValues Particles = new();

    public RenderingProjectValues()
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
//   カメラが範囲に入ると、Override... を付けた値をカメラ / Project Settings の値に混ぜる (入っている深さ = 重み)
//   露出 / 背景 / SSAO はカメラごと (そのカメラの位置で)。環境光 / 影の距離は全体の値なので、一番目のカメラの位置で
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
