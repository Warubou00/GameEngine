using System.Numerics;
using GameEngine.Rendering;

namespace GameEngine;

// ==========================================================================
// カメラ (Transform と一緒に付ける。位置と向きは Transform から取る)
//   v1.5 : 複数のカメラ (Unity と同じ)。有効なカメラを Depth の小さい順に描き、画面の ViewportRect の範囲に重ねる (最大 8 台)
//     ClearFlags : Skybox (空) / SolidColor (BackgroundColor の 1 色) / DepthOnly (前のカメラの絵の上に、物だけを重ねる)
//     CullingMask : 写すレイヤー (エンティティのレイヤー = EntityInfo.Layer)
//     描き方 (Forward / Deferred / アンチエイリアス / SSAO / 露出など) もカメラごと
//     (以前は最初に見つかったカメラ 1 台だけで、描き方はシーンの RenderSettings に 1 つだった)
//   プロジェクト全体の描画の設定 (光の計算 / 環境光 / 空の画像 / 影 / パーティクル) は Project Settings の Rendering
// ==========================================================================
public enum CameraClearFlags : uint
{
    Skybox     = 0,   // 空 (Project Settings の Rendering の空の画像)
    SolidColor = 1,   // BackgroundColor の 1 色
    DepthOnly  = 2,   // 消さない : 前のカメラの絵の上に、写した物だけを重ねる (武器 / ミニマップの枠など)
}

[ComponentInfo(Category = "Camera")]
public struct Camera : IComponent
{
    public float FieldOfView  = 57.29578f;   // 縦の視野角 (度)。既定は以前の C++ と同じ 1.0 ラジアン
    public float NearClip     = 1.0f;
    public float FarClip      = 10000.0f;
    public bool  Orthographic = false;       // 平行投影 (高さ = 画面のピクセル数。幅は範囲の比)

    [Header("Output")]
    [Tooltip("Cameras are drawn from the smallest Depth to the largest (larger ones are drawn on top).")]
    public int     Depth = 0;
    [Tooltip("x, y, width, height on the screen (0 to 1. x, y = bottom left, like Unity).")]
    public Vector4 ViewportRect = new(0.0f, 0.0f, 1.0f, 1.0f);
    public CameraClearFlags ClearFlags = CameraClearFlags.Skybox;
    public Vector3 BackgroundColor = new(0.1f, 0.2f, 0.4f);   // SolidColor の色 (sRGB)
    [LayerMask] public uint CullingMask = 0xFFFFFFFFu;        // 写すレイヤー

    [Header("Rendering")]
    public RenderPath   RenderPath   = RenderPath.Forward;
    public AntiAliasing AntiAliasing = AntiAliasing.Fxaa;
    public SsaoValues   Ssao         = new();
    public FxaaValues   Fxaa         = new();
    [Range(0.05f, 8.0f)] public float Exposure = 1.0f;
    public Tonemapper Tonemapper = Tonemapper.ACES;
    [Range(1.0f, 3.0f)] public float Gamma = 2.2f;

    public Camera()
    {
    }
}

// ==========================================================================
// 自由に飛び回るカメラ (FreeCameraSystem が動かす)
//   右ボタンを押しながらマウス : 向き / WASD : 前後左右 / E Q : 上下 / Shift : 速く
//   ゲームパッド (1 台目) : 右スティック : 向き / 左スティック : 前後左右 / RT LT : 上下 / RB : 速く
// ==========================================================================
[ComponentInfo(DisplayName = "Free Camera", Category = "Camera")]
public struct FreeCamera : IComponent
{
    public float MoveSpeed       = 10.0f;    // 1 秒に進む距離
    public float FastMultiplier  = 5.0f;     // Shift を押している間の倍率
    public float LookSensitivity = 0.2f;     // マウス 1 ピクセルあたりの回転 (度)
    public float GamepadLookSpeed = 120.0f;  // ゲームパッドの右スティックを倒し切った時の回転 (度 / 秒)

    internal float Yaw;
    internal float Pitch;
    internal bool  Initialized;

    public FreeCamera()
    {
    }
}

// ==========================================================================
// 注視点の周りを回るカメラ (OrbitCameraSystem が動かす。カメラを動かし続けるテスト用)
// ==========================================================================
[ComponentInfo(DisplayName = "Orbit Camera", Category = "Camera")]
public struct OrbitCamera : IComponent
{
    public System.Numerics.Vector3 Target;
    public float Radius           = 20.0f;
    public float Height           = 8.0f;
    public float DegreesPerSecond = 30.0f;
    public float Angle;                      // 今の角度 (度)

    public OrbitCamera()
    {
    }
}
