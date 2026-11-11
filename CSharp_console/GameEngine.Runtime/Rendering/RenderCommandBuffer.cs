using System.Numerics;
using GameEngine.Native;

namespace GameEngine.Rendering;

// シーンを描く時のシェーダー (C++ の RHI::ScenePass と同じ番号)
public enum ScenePass : uint
{
    Forward = 0,   // ライティングまで 1 回で描く (HDR の色)
    GBuffer = 1,   // ディファード用 : アルベド / 法線 / 金属・粗さ (3 枚に描く)
    ForwardSplitAmbient = 2,   // Forward + SSAO 用 : 1 枚目にライトの光、2 枚目に環境光 (後で 環境光 × 遮蔽 を足す)
    Shadow  = 3,   // シャドーマップ : 深度だけ (DrawScene の view でライトの視点を選ぶ。半透明はアルファテスト)
}

// シーンのどの物を描くか (C++ の RHI::RenderQueue と同じ番号)
public enum RenderQueue : uint
{
    Opaque      = 0,   // 不透明 (GPU カリング / オクルージョンはここで行う)
    Transparent = 1,   // 半透明 (Opaque の後に描くこと。G-Buffer には描けない)
}

// 全画面を描く時の重ね方
public enum FullscreenBlend : uint
{
    None     = 0,   // 上書き
    Additive = 1,   // 足す
    Alpha    = 2,   // アルファブレンド
    Premultiplied = 3,   // 乗算済みアルファ (色 + 描いてある色 × (1 - α))
}

// ==========================================================================
// 描画パスの命令の列 (Unity の CommandBuffer に当たる)
//   RenderPipeline.Render が毎フレーム積み、エンジンがまとめて 1 回の P/Invoke で C++ に渡す
//   (命令ごとに C++ を呼ぶと P/Invoke の分だけ遅いので、配列にためる。毎フレーム確保もしない)
// ==========================================================================
public sealed unsafe class RenderCommandBuffer
{
    private NativeMethods.RenderCommand[] _commands = new NativeMethods.RenderCommand[32];

    public int Count { get; private set; }

    internal void Clear() => Count = 0;

    private ref NativeMethods.RenderCommand Add(uint type)
    {
        if (Count == _commands.Length) Array.Resize(ref _commands, _commands.Length * 2);
        ref var command = ref _commands[Count++];
        command = default;
        command.Type  = type;
        command.Depth = NativeMethods.InvalidHandle;
        return ref command;
    }

    // ---------- 描画先 ----------

    /// <summary>描画先を 1 枚にする (depth : 深度。null なら深度なし)</summary>
    public void SetRenderTarget(RenderTexture color, RenderTexture? depth = null)
    {
        ref var command = ref Add(0);
        command.ColorCount = 1;
        command.Colors[0]  = color.Handle;
        command.Depth      = depth?.Handle ?? NativeMethods.InvalidHandle;
    }

    /// <summary>描画先を複数にする (最大 4 枚。G-Buffer など)</summary>
    /// <param name="depthSlice">depth が配列 (カスケードのシャドーマップ) の時、何枚目に描くか</param>
    public void SetRenderTargets(ReadOnlySpan<RenderTexture> colors, RenderTexture? depth = null, int depthSlice = 0)
    {
        if (colors.Length > NativeMethods.MaxPassColorTargets) throw new ArgumentException($"Up to {NativeMethods.MaxPassColorTargets} color targets.");
        ref var command = ref Add(0);
        command.ColorCount = (uint)colors.Length;
        for (int i = 0; i < colors.Length; i++) command.Colors[i] = colors[i].Handle;
        command.Depth = depth?.Handle ?? NativeMethods.InvalidHandle;
        command.Arg0  = (uint)Math.Max(0, depthSlice);
    }

    /// <summary>描画先の色を全部消す</summary>
    public void ClearColor(Vector4 color)
    {
        ref var command = ref Add(1);
        command.ClearColor = color;
    }

    /// <summary>描画先の深度を消す</summary>
    public void ClearDepth(float depth = 1.0f)
    {
        ref var command = ref Add(2);
        command.ClearDepth = depth;
    }

    // ---------- 描画 ----------

    /// <summary>シーン (MeshRenderer の付いた物) を今の描画先に描く</summary>
    /// <param name="view">視点 (0 = カメラ / 1〜 = 影のカスケード。C++ が行列を作る)</param>
    public void DrawScene(ScenePass pass, RenderQueue queue, int view = 0)
    {
        ref var command = ref Add(3);
        command.Arg0 = (uint)pass;
        command.Arg1 = (uint)queue;
        command.Inputs[0] = (uint)view;
    }

    /// <summary>全画面の三角形を描く</summary>
    /// <param name="inputs">シェーダーが読むテクスチャ (g_Inputs[0].x, .y, .z, .w, g_Inputs[1].x ... の順。最大 8)</param>
    /// <param name="parameters">シェーダーに渡す値 (g_Params[0].x ... の順。最大 8)</param>
    public void DrawFullscreen(FullscreenShader shader, ReadOnlySpan<RenderTexture> inputs, ReadOnlySpan<float> parameters,
                               FullscreenBlend blend = FullscreenBlend.None)
    {
        if (inputs.Length > NativeMethods.MaxPassInputs)     throw new ArgumentException($"Up to {NativeMethods.MaxPassInputs} inputs.");
        if (parameters.Length > NativeMethods.MaxPassParams) throw new ArgumentException($"Up to {NativeMethods.MaxPassParams} parameters.");
        ref var command = ref Add(4);
        command.Arg0 = shader.Handle;
        command.Arg1 = (uint)blend;
        for (int i = 0; i < NativeMethods.MaxPassInputs; i++) command.Inputs[i] = i < inputs.Length ? inputs[i].Handle : NativeMethods.InvalidHandle;
        for (int i = 0; i < parameters.Length; i++) command.Params[i] = parameters[i];
    }

    // ---------- GPU パーティクル ----------

    /// <summary>GPU パーティクルの発生 / 更新 (/ ソート)。不透明を描いた後に置く (衝突でシーンの深度を読む)。1 フレームに 1 回</summary>
    public void UpdateParticles() => Add(5);

    /// <summary>GPU パーティクルを今の描画先へ描く (色の描画先だけを付けておく。深度はシェーダーがシーンの深度を読む)</summary>
    public void DrawParticles() => Add(6);

    // ---------- 自分のシェーダー (SH2) ----------

    /// <summary>
    /// 自分のシェーダーのマテリアルの物を、今の描画先へ描く (queue : 不透明 / 半透明。半透明は遠い順)
    /// DrawScene は描かない (まとめて描く道から外れている)。色の描画先 1 枚目に描く (2 枚目からは書かない)
    /// </summary>
    /// <param name="readDepth">
    /// true : シーンの深度を読むシェーダー (@depth read。雲など) だけを描く (SH3)。深度は描画先から外れ、シェーダーがテクスチャとして読む
    /// false : それ以外 (深度のテストをする)
    /// </param>
    public void DrawCustom(RenderQueue queue, bool readDepth = false)
    {
        ref var command = ref Add(7);
        command.Arg0 = readDepth ? 1u : 0u;
        command.Arg1 = (uint)queue;
        command.Params[0] = (float)Time.TotalTime;   // シェーダーの SurfaceInput.Time (エディターの編集中も進む。パーティクルと同じ)
    }

    // ---------- プレビューの窓 (AN5) ----------

    /// <summary>視点 view (1〜) の物と線を今の描画先へ描く (色 RGBA8 1 枚 + 深度 Depth32 を SetRenderTarget で付けておく)</summary>
    public void DrawPreview(int view)
    {
        ref var command = ref Add(8);
        command.Arg0 = (uint)view;
    }

    /// <summary>v1.5 : この後の描画をカメラ camera (Graphics の今のフレームのカメラの番号) で行う (影のカスケード / カリングも)</summary>
    public void SetCamera(int camera)
    {
        ref var command = ref Add(9);
        command.Arg0 = (uint)camera;
    }

    /// <summary>v1.5 : 次の SetRenderTarget までの描画を、今の描画先の一部 (ピクセル) だけにする</summary>
    public void SetViewport(float x, float y, float width, float height)
    {
        ref var command = ref Add(10);
        command.Params[0] = x;
        command.Params[1] = y;
        command.Params[2] = width;
        command.Params[3] = height;
    }

    // 1 フレーム分を C++ へ渡して描く
    internal void Submit()
    {
        fixed (NativeMethods.RenderCommand* commands = _commands)
        {
            NativeMethods.Renderer_Render(commands, (uint)Count);
        }
    }
}
