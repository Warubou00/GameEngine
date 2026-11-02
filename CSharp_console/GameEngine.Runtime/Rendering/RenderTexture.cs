using System.Numerics;
using GameEngine.Native;

namespace GameEngine.Rendering;

// ==========================================================================
// レンダーターゲットの形式 (C++ の RHI::RenderTargetFormat と同じ番号)
// ==========================================================================
public enum RenderTextureFormat : uint
{
    RGBA8      = 0,   // 色 (0〜1)
    RGBA8_SRGB = 1,   // 色 (書く時に sRGB へ、読む時に線形へハードウェアが変換。アルベドなど)
    RGBA16F    = 2,   // HDR の色 / 法線
    RG16F      = 3,
    R16F       = 4,
    R32F       = 5,
    R8         = 6,   // 1 成分 (遮蔽など)
    RGB10A2    = 7,
    R11G11B10F = 8,   // HDR の色 (アルファ無し。RGBA16F の半分の大きさ)
    Depth32    = 9,   // 深度だけ (シャドーマップ)。SetRenderTargets の depth に指定し、後で読む (MSAA は不可)
}

// ==========================================================================
// レンダーターゲット (描画パスの描き先 / 後のパスで読むテクスチャ)
//   Create(format, scale) : 画面の大きさ × scale。ウィンドウのサイズが変わると C++ が自動で作り直す
//   BackBuffer / SceneDepth : 画面と、シーンの深度 (GPU カリングのオクルージョンもこれを使う)。消せない
//   sampleCount > 1 (MSAA) : 描画先にした後、全画面のシェーダーで読む時に C++ が 1 サンプルへ自動で解決する。
//                            SceneDepth と一緒に描画先にすると、深度も同じサンプル数になる (C++ が別に持つ)
//   以前は描画先がバックバッファ 1 枚に決まっていて、C# からは作れなかった
// ==========================================================================
public sealed class RenderTexture : IDisposable
{
    public static RenderTexture BackBuffer { get; } = new(0xFFFFFFFEu, owned: false, RenderTextureFormat.RGBA8, 1.0f);
    public static RenderTexture SceneDepth { get; } = new(0xFFFFFFFDu, owned: false, RenderTextureFormat.R32F, 1.0f);

    private readonly bool _owned;

    internal uint Handle { get; private set; }
    public RenderTextureFormat Format { get; }
    public float Scale { get; }   // 0 なら大きさ固定
    public Vector4 ClearColor { get; }
    public int SampleCount { get; }   // MSAA のサンプル数 (頼んだ数。GPU が対応していなければ C++ が下げる)
    public bool IsValid => Handle != NativeMethods.InvalidHandle;

    private RenderTexture(uint handle, bool owned, RenderTextureFormat format, float scale, Vector4 clearColor = default, int sampleCount = 1)
    {
        Handle      = handle;
        _owned      = owned;
        Format      = format;
        Scale       = scale;
        ClearColor  = clearColor;
        SampleCount = sampleCount;
    }

    /// <summary>画面の大きさに合わせたレンダーターゲットを作る</summary>
    /// <param name="clearColor">最適化クリア値 (RenderCommandBuffer.ClearColor で同じ色を使うと速い)</param>
    /// <param name="sampleCount">MSAA のサンプル数 (1 なら MSAA なし)</param>
    public static RenderTexture Create(RenderTextureFormat format, float scale = 1.0f, Vector4 clearColor = default, int sampleCount = 1)
    {
        var samples = Math.Max(1, sampleCount);
        var desc = new NativeMethods.RenderTargetDesc { Format = (uint)format, Scale = scale, ClearColor = clearColor, SampleCount = (uint)samples };
        return CreateNative(ref desc, format, scale, clearColor, samples);
    }

    /// <summary>大きさ固定のレンダーターゲットを作る (リサイズしても変わらない)</summary>
    public static RenderTexture CreateFixed(RenderTextureFormat format, uint width, uint height, Vector4 clearColor = default)
    {
        var desc = new NativeMethods.RenderTargetDesc { Format = (uint)format, Scale = 0.0f, Width = width, Height = height, ClearColor = clearColor, SampleCount = 1 };
        return CreateNative(ref desc, format, 0.0f, clearColor);
    }

    private static RenderTexture CreateNative(ref NativeMethods.RenderTargetDesc desc, RenderTextureFormat format, float scale, Vector4 clearColor, int sampleCount = 1)
    {
        var handle = NativeMethods.Renderer_CreateRenderTarget(ref desc);
        if (handle == NativeMethods.InvalidHandle)
        {
            throw new InvalidOperationException($"Failed to create render texture ({format}). See the log above.");
        }
        return new RenderTexture(handle, owned: true, format, scale, clearColor, sampleCount);
    }

    // 消すと GPU の完了を待つ (描画パイプラインを切り替える時だけ使う想定)
    public void Dispose()
    {
        if (!_owned || !IsValid) return;
        NativeMethods.Renderer_DestroyRenderTarget(Handle);
        Handle = NativeMethods.InvalidHandle;
    }
}

// ==========================================================================
// 全画面に描くシェーダー (ライティング / ポストエフェクト)
//   ファイルの VSFullscreen と、entry のピクセルシェーダーを使う。同じものを 2 回読んでも同じハンドルになる
// ==========================================================================
public sealed class FullscreenShader
{
    internal uint Handle { get; }
    public string Path  { get; }
    public string Entry { get; }

    private FullscreenShader(uint handle, string path, string entry)
    {
        Handle = handle;
        Path   = path;
        Entry  = entry;
    }

    /// <param name="path">シェーダーのファイル (実行ファイルからの相対パス。例 : "../Shader/postProcess.shader")</param>
    public static FullscreenShader Load(string path, string entry)
    {
        var handle = NativeMethods.Renderer_LoadFullscreenShader(path, entry);
        if (handle == NativeMethods.InvalidHandle)
        {
            throw new InvalidOperationException($"Failed to load fullscreen shader {path} : {entry}. See the log above.");
        }
        return new FullscreenShader(handle, path, entry);
    }
}
