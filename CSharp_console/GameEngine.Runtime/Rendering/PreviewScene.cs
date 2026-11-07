using System.Numerics;
using GameEngine.Native;

namespace GameEngine.Rendering;

// ==========================================================================
// プレビューの窓の世界 (AN5)
//   シーン (Engine.World) とは別の小さな World を持ち、その物を自分のレンダーターゲットへ描く
//   - 物の描画オブジェクトには視点の番号 (View。1〜) を付ける → シーンの描画には出ず、DrawPreview (この番号) だけに出る
//   - 骨のアニメーション (AN3) / GPU スキニング (AN4) / 親子の行列 は、シーンと同じシステムをこの World 用に別に持って動かす
//   - カメラは注視点の周りを回る (Yaw / Pitch / Distance)。左手系 / Y 上 (シーンと同じ)
//   - 線 : 床の格子 (Grid)、Line で足した線 (深度で隠れる / 一番上)。このフレームだけ (毎フレーム足す)
//   エンジンが毎フレーム Update (シーンの描画オブジェクトの後) と描画の命令の追加をする。使い終わったら Dispose
// ==========================================================================
public sealed class PreviewScene : IDisposable
{
    private readonly Engine _engine;
    private readonly TransformSystem _transforms = new();
    private readonly RenderSyncSystem _renderSync;
    private readonly AnimationSystem _animation = new();
    private readonly SkinningSystem _skinning = new();
    private readonly List<NativeMethods.DebugLineVertex> _depthLines = new();
    private readonly List<NativeMethods.DebugLineVertex> _overlayLines = new();
    private NativeMethods.DebugLineVertex[] _lineArray = new NativeMethods.DebugLineVertex[1024];
    private bool _disposed;

    /// <summary>この窓の世界 (シーンとは別)</summary>
    public World World { get; } = new();
    /// <summary>視点の番号 (1〜15)。描画オブジェクトに付く</summary>
    public int View { get; }
    public RenderTexture? Color { get; private set; }
    public RenderTexture? Depth { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    /// <summary>UI (ImGui.Image) に渡す番号。まだ大きさを決めていなければ 0xFFFFFFFF</summary>
    public uint TextureId { get; private set; } = NativeMethods.InvalidHandle;

    // ---------- カメラ ----------
    public Vector3 Target = new(0, 1, 0);
    public float Yaw = 30.0f;        // 度。Y の周り (0 = -Z の方から +Z を見る)
    public float Pitch = 20.0f;      // 度。上から見下ろすと正
    public float Distance = 5.0f;
    public float FieldOfView = 45.0f;
    public Vector4 Background = new(0.17f, 0.18f, 0.20f, 1.0f);

    // ---------- 見せ方 ----------
    public bool Shaded = true;
    public bool Wireframe;
    public Vector4 WireColor = new(0.05f, 0.05f, 0.05f, 1.0f);
    public bool Grid = true;
    public float GridSize = 10.0f;   // 床の格子の幅 (m)。1 m ごと
    /// <summary>光が進む向き (ワールド)</summary>
    public Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.4f, -0.8f, 0.45f));

    /// <summary>アニメーションの時間を進めるか (Animator.Playing の物だけ進む)</summary>
    public bool Advance = true;
    /// <summary>前のフレームで書いた骨の数 / 曲げた物の数 (確認用)</summary>
    public int BonesWritten => _animation.BonesWritten;
    public int SkinnedObjects => _skinning.ObjectCount;

    internal PreviewScene(Engine engine, int view)
    {
        _engine = engine;
        View = view;
        _renderSync = new RenderSyncSystem(World, _transforms, (uint)view);
    }

    /// <summary>描く大きさ (ピクセル)。変わった時だけレンダーターゲットを作り直す</summary>
    public void SetSize(int width, int height)
    {
        width = Math.Clamp(width, 1, 8192);
        height = Math.Clamp(height, 1, 8192);
        if (width == Width && height == Height && Color != null) return;
        _engine.Graphics.RetireTexture(Color);   // このフレームの UI が古い方を読んでいるかもしれないので、描いた後で消す
        _engine.Graphics.RetireTexture(Depth);
        Color = RenderTexture.CreateFixed(RenderTextureFormat.RGBA8, (uint)width, (uint)height, Background);
        Depth = RenderTexture.CreateFixed(RenderTextureFormat.Depth32, (uint)width, (uint)height);
        Width = width;
        Height = height;
        TextureId = Color.IsValid ? NativeMethods.Renderer_GetRenderTargetTexture(Color.Handle) : NativeMethods.InvalidHandle;
    }

    /// <summary>カメラの位置 (注視点の周りを回る)</summary>
    public Vector3 CameraPosition
    {
        get
        {
            var yaw = Yaw * MathF.PI / 180;
            var pitch = Pitch * MathF.PI / 180;
            var back = new Vector3(-MathF.Sin(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), -MathF.Cos(yaw) * MathF.Cos(pitch));
            return Target + back * Distance;
        }
    }

    /// <summary>ビュー × プロジェクション (行ベクトル。左手系 / 深度 0〜1)</summary>
    public Matrix4x4 ViewProjection
    {
        get
        {
            var eye = CameraPosition;
            var z = Vector3.Normalize(Target - eye);
            var x = Vector3.Cross(Vector3.UnitY, z);
            x = x.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(x);
            var y = Vector3.Cross(z, x);
            var view = new Matrix4x4(x.X, y.X, z.X, 0, x.Y, y.Y, z.Y, 0, x.Z, y.Z, z.Z, 0, -Vector3.Dot(x, eye), -Vector3.Dot(y, eye), -Vector3.Dot(z, eye), 1);
            var near = Math.Max(0.01f, Distance * 0.01f);
            var far = Math.Max(100.0f, Distance * 20.0f);
            var yScale = 1.0f / MathF.Tan(FieldOfView * MathF.PI / 360);
            var xScale = yScale / Math.Max(1e-3f, (float)Width / Math.Max(1, Height));
            var projection = new Matrix4x4(xScale, 0, 0, 0, 0, yScale, 0, 0, 0, 0, far / (far - near), 1, 0, 0, -near * far / (far - near), 0);
            return view * projection;
        }
    }

    /// <summary>ワールドの点 → 窓の中のピクセル (画面の外 / 後ろなら null)</summary>
    public Vector2? WorldToPixel(Vector3 point)
    {
        var clip = Vector4.Transform(new Vector4(point, 1), ViewProjection);
        if (clip.W <= 1e-5f) return null;
        return new Vector2((clip.X / clip.W * 0.5f + 0.5f) * Width, (0.5f - clip.Y / clip.W * 0.5f) * Height);
    }

    /// <summary>注視点と距離を、半径 radius の球が収まるように決める</summary>
    public void Frame(Vector3 center, float radius)
    {
        Target = center;
        Distance = Math.Max(0.1f, radius / MathF.Sin(FieldOfView * MathF.PI / 360) * 1.1f);
    }

    /// <summary>線を足す (このフレームだけ)。overlay = 物に隠れずに一番上に描く</summary>
    public void Line(Vector3 a, Vector3 b, uint color, bool overlay = false)
    {
        var list = overlay ? _overlayLines : _depthLines;
        if (list.Count >= DebugDraw.MaxLines) return;
        list.Add(new NativeMethods.DebugLineVertex { Position = a, Color = color });
        list.Add(new NativeMethods.DebugLineVertex { Position = b, Color = color });
    }

    /// <summary>世界の物を全部消す (描画オブジェクトも次の Update で消える)</summary>
    public void Clear() => World.Clear();

    // エンジンが毎フレーム呼ぶ (シーンの描画オブジェクトを送った後)
    internal void Update(float deltaTime)
    {
        _animation.Update(World, Advance, deltaTime);
        _transforms.Update(World);
        _renderSync.Update(World);
        _skinning.Update(World, _engine.Assets);
    }

    // エンジンが描画の命令の最後に足す (シーンの後。UI の前)
    internal unsafe void Submit(RenderCommandBuffer commands)
    {
        if (Color is not { IsValid: true } || Depth is not { IsValid: true }) { _depthLines.Clear(); _overlayLines.Clear(); return; }
        if (Grid)
        {
            var half = (int)MathF.Ceiling(GridSize * 0.5f);
            for (int i = -half; i <= half; i++)
            {
                var color = i == 0 ? DebugDraw.Color(0.55f, 0.55f, 0.6f) : DebugDraw.Color(0.32f, 0.33f, 0.36f);
                Line(new Vector3(i, 0, -half), new Vector3(i, 0, half), color);
                Line(new Vector3(-half, 0, i), new Vector3(half, 0, i), color);
            }
        }
        var depthCount = _depthLines.Count;
        var total = depthCount + _overlayLines.Count;
        if (_lineArray.Length < total) Array.Resize(ref _lineArray, Math.Max(total, _lineArray.Length * 2));
        _depthLines.CopyTo(_lineArray, 0);
        _overlayLines.CopyTo(_lineArray, depthCount);
        _depthLines.Clear();
        _overlayLines.Clear();
        var frame = new NativeMethods.PreviewFrame
        {
            ViewProjection = ViewProjection,
            LightDirection = LightDirection,
            Flags = (Shaded ? 1u : 0u) | (Wireframe ? 2u : 0u),
            CameraPosition = CameraPosition,
            WireColor = WireColor,
        };
        fixed (NativeMethods.DebugLineVertex* lines = _lineArray)
        {
            NativeMethods.Renderer_SetPreviewFrame((uint)View, &frame, lines, (uint)total, (uint)depthCount);
        }
        commands.SetRenderTarget(Color, Depth);
        commands.ClearColor(Background);
        commands.ClearDepth();
        commands.DrawPreview(View);
    }

    /// <summary>窓を閉じる : 世界の物 (描画オブジェクト / 曲げる場所) とレンダーターゲットを消す</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        World.Clear();
        _renderSync.Update(World);   // 消した描画オブジェクトを C++ からも消す
        _engine.Graphics.RetireTexture(Color);   // このフレームの UI がまだ読むかもしれない (描いた後で消す)
        _engine.Graphics.RetireTexture(Depth);
        Color = Depth = null;
        TextureId = NativeMethods.InvalidHandle;
        _engine.Graphics.RemovePreview(this);
    }
}
