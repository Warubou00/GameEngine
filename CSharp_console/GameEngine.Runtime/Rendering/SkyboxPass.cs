using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using GameEngine.Native;

namespace GameEngine.Rendering;

// ==========================================================================
// 空 (Skybox。v1.4)。描画パイプラインが、不透明の物を描く前に HDR へ全画面で描く (物はこの上に描かれる)
//   画像 : 正距円筒 (横 = 方位 360 度 / 縦 = 上から下へ 180 度) の png / jpg / bmp
//          GraphicsSettings.Skybox.Texture (アセットのパス)。空なら、エンジンに埋め込んだ既定の空 (Resources/DefaultSky.png。make_sky.py で作った物)
//   読んだ画像は GPU のテクスチャにして覚える (同じ画像は 1 回だけ読む)。読めなければ既定の空 (理由は Console に 1 回)
//   ※以前は背景が 1 色 (BackgroundColor) だけだった。Skybox を切ると今まで通り BackgroundColor
// ==========================================================================
public sealed class SkyboxPass
{
    private FullscreenShader? _shader;
    private readonly Dictionary<string, uint> _textures = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, RenderTexture> _wrapped = new();   // テクスチャ → 全画面の入力 (毎フレーム作らない)
    private const string DefaultKey = "(default)";

    /// <summary>今の設定で空を今の描画先へ描く (色の描画先だけを付けておくこと)。戻り値 = 描いたか</summary>
    public bool Draw(RenderCommandBuffer cmd, GraphicsSettings settings)
    {
        var sky = settings.Skybox;
        if (!sky.Enabled || settings.ClearFlags != CameraClearFlags.Skybox) return false;   // v1.5 : 空はカメラの ClearFlags が Skybox の時だけ
        var texture = textureOf(sky.Texture);
        if (texture == NativeMethods.InvalidHandle) return false;
        _shader ??= FullscreenShader.Load(ShaderPaths.Skybox, "PSSkybox");
        DrawCount++;
        if (!_wrapped.TryGetValue(texture, out var input)) _wrapped[texture] = input = RenderTexture.WrapTexture(texture);
        cmd.DrawFullscreen(_shader, [input], [sky.Intensity, sky.Rotation * MathF.PI / 180.0f]);
        return true;
    }

    /// <summary>空を描いた回数 (確認用)</summary>
    public int DrawCount { get; private set; }

    /// <summary>空の画像の GPU のテクスチャの番号 (確認用。読めなければ既定の空)</summary>
    public uint TextureOf(string? path) => textureOf(path);

    private uint textureOf(string? path)
    {
        var key = string.IsNullOrWhiteSpace(path) ? DefaultKey : path.Trim();
        if (_textures.TryGetValue(key, out var cached)) return cached;
        if (key != DefaultKey && _failed.Contains(key)) return textureOf(null);
        try
        {
            using var bitmap = key == DefaultKey ? loadDefault() : new Bitmap(AssetDatabase.Current?.FullPath(key) ?? key);
            var index = upload(bitmap);
            _textures[key] = index;
            return index;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or ExternalException or InvalidOperationException)
        {
            if (key == DefaultKey) { _textures[key] = NativeMethods.InvalidHandle; return NativeMethods.InvalidHandle; }
            _failed.Add(key);
            Console.WriteLine($"[Skybox] Cannot read {key} : {ex.Message} (using the default sky)");
            return textureOf(null);
        }
    }

    // エンジンに埋め込んだ既定の空
    private static Bitmap loadDefault()
    {
        using var stream = typeof(SkyboxPass).Assembly.GetManifestResourceStream("Sky/DefaultSky.png")
                           ?? throw new InvalidOperationException("The default sky is not in GameEngine.Runtime");
        return new Bitmap(stream);
    }

    // 画像 → RGBA8 → GPU (UI のテクスチャと同じ作り方。番号はシェーダーの g_Textures の番号)
    private static unsafe uint upload(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                var row = (byte*)data.Scan0 + y * data.Stride;
                for (int x = 0; x < width; x++)
                {
                    var o = (y * width + x) * 4;
                    rgba[o]     = row[x * 4 + 2];   // BGRA → RGBA
                    rgba[o + 1] = row[x * 4 + 1];
                    rgba[o + 2] = row[x * 4];
                    rgba[o + 3] = 255;
                }
            }
            fixed (byte* p = rgba) return NativeMethods.Renderer_CreateUITexture((uint)width, (uint)height, p);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}

/// <summary>空 (Skybox) の設定 (v1.4。RenderSettings の Skybox から書かれる)</summary>
public sealed class SkyboxSettings
{
    public bool    Enabled   { get; set; } = true;   // 既定で空を出す (切ると BackgroundColor の 1 色)
    public string? Texture   { get; set; }           // 正距円筒の画像のアセットのパス。null / 空 = 既定の空
    public float   Intensity { get; set; } = 1.0f;   // 明るさ (倍率)
    public float   Rotation  { get; set; }           // 方位の回転 (度)
}
