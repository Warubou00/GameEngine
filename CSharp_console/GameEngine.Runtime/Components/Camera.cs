namespace GameEngine;

// ==========================================================================
// カメラ (Transform と一緒に付ける。位置と向きは Transform から取る)
//   今は最初に見つかったカメラ 1 台で描く (複数のカメラ / 画面分割は描画パスを C# で組む段階で)
// ==========================================================================
public struct Camera
{
    public float FieldOfView  = 57.29578f;   // 縦の視野角 (度)。既定は以前の C++ と同じ 1.0 ラジアン
    public float NearClip     = 1.0f;
    public float FarClip      = 10000.0f;
    public bool  Orthographic = false;       // 平行投影 (幅 / 高さ = 画面のピクセル数)

    public Camera()
    {
    }
}

// ==========================================================================
// 自由に飛び回るカメラ (FreeCameraSystem が動かす)
//   右ボタンを押しながらマウス : 向き / WASD : 前後左右 / E Q : 上下 / Shift : 速く
// ==========================================================================
public struct FreeCamera
{
    public float MoveSpeed       = 10.0f;    // 1 秒に進む距離
    public float FastMultiplier  = 5.0f;     // Shift を押している間の倍率
    public float LookSensitivity = 0.2f;     // マウス 1 ピクセルあたりの回転 (度)

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
public struct OrbitCamera
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
