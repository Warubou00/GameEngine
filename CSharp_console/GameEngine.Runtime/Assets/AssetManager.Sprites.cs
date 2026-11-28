using GameEngine.Imaging;
using GameEngine.Imaging.Codecs;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// スプライト (v2.4) : アニメーションのアセット (.spriteanim.json) と、コマ / 画像の GPU のテクスチャ
//   画像は GameEngine.Imaging で読む (PNG / JPEG / BMP / TGA。自前)。コマごとに切り出して 1 枚ずつテクスチャにする
//   テクスチャは UI のテクスチャ (Renderer_CreateUITexture)。番号はマテリアル (g_Textures) と UI の両方でそのまま使える
//   同じ画像の同じ四角のコマは 1 枚を共有する。作り直す時の古いテクスチャは Renderer_ReleaseUITexture
//   (GPU が使い終わってから消える。このフレームの描画が読んでいても大丈夫)
// ==========================================================================
public sealed partial class AssetManager
{
    private readonly Dictionary<string, SpriteAnimation> _spriteAnimations = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SpriteFrameTexture> _spriteImages = new(StringComparer.OrdinalIgnoreCase);   // SpriteRenderer.Sprite / UImage の画像 (全体)
    private readonly Dictionary<SpriteAnimation, List<uint>> _spriteAnimationTextures = new(ReferenceEqualityComparer.Instance);

    /// <summary>今あるスプライトのテクスチャの数 (確認用 : 漏れていないか)</summary>
    public int SpriteTextureCount => _spriteImages.Values.Count(t => t.IsValid) + _spriteAnimationTextures.Values.Sum(l => l.Count);

    /// <summary>スプライトのアニメーションを読む。同じパスは同じインスタンス</summary>
    public SpriteAnimation LoadSpriteAnimation(string path)
    {
        (path, var file) = resolve(path);
        if (_spriteAnimations.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(file)) throw new FileNotFoundException($"Failed to load sprite animation: {path}", path);
        var asset = new SpriteAnimation(path, File.ReadAllText(file, System.Text.Encoding.UTF8)) { Owner = this };
        _spriteAnimations.Add(path, asset);
        return asset;
    }

    /// <summary>ファイルが変わった時 : 読み直す (同じインスタンスの中身を差し替える。テクスチャは次に使う時に作り直す)</summary>
    public bool ReloadSpriteAnimation(string path)
    {
        (path, var file) = resolve(path);
        if (!_spriteAnimations.TryGetValue(path, out var asset) || !File.Exists(file)) return false;
        try { asset.SetJson(File.ReadAllText(file, System.Text.Encoding.UTF8)); }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            Console.WriteLine($"[Assets] Sprite animation not reloaded : {path} ({ex.Message})");
            return false;
        }
        AssetReloaded?.Invoke(path);
        return true;
    }

    /// <summary>読み込み済みなら返す (エディターが保存した物を差し替えるため)</summary>
    public SpriteAnimation? FindLoadedSpriteAnimation(string path)
    {
        (path, _) = resolve(path);
        return _spriteAnimations.TryGetValue(path, out var asset) ? asset : null;
    }

    public IEnumerable<SpriteAnimation> LoadedSpriteAnimations => _spriteAnimations.Values;

    /// <summary>アセットをファイルに書く (エディター)。読み込み済みの物と同じパスなら、そのインスタンスを書く</summary>
    public void SaveSpriteAnimation(SpriteAnimation asset)
    {
        if (string.IsNullOrEmpty(asset.Path)) throw new InvalidOperationException("The sprite animation has no path.");
        var (_, file) = resolve(asset.Path);
        NoteOwnWrite(file);
        File.WriteAllText(file, asset.ToJson(), new System.Text.UTF8Encoding(false));
    }

    /// <summary>画像全体を 1 枚のスプライトのテクスチャにする (SpriteRenderer.Sprite / UImage。同じパスは共有)</summary>
    public SpriteFrameTexture LoadSpriteTexture(string? path)
    {
        if (string.IsNullOrEmpty(path)) return SpriteFrameTexture.Empty;
        (var key, var file) = resolve(path);
        if (_spriteImages.TryGetValue(key, out var cached)) return cached;
        var texture = SpriteFrameTexture.Empty;
        var image = decodeSpriteImage(key, file);
        if (image != null) texture = createSpriteTexture(image, 0, 0, image.Width, image.Height);
        _spriteImages[key] = texture;   // 読めなかった時も覚える (毎フレーム読みに行かない)
        return texture;
    }

    // 画像が変わった時 (ReloadTexture から) : その画像のスプライトのテクスチャを作り直す
    private int reloadSpriteImage(string path)
    {
        var count = 0;
        (var key, _) = resolve(path);
        if (_spriteImages.Remove(key, out var old))
        {
            if (old.IsValid) NativeMethods.Renderer_ReleaseUITexture(old.Texture);
            count++;
        }
        foreach (var animation in _spriteAnimations.Values)
        {
            if (!animation.Frames.Any(f => string.Equals(resolve(f.Image).Key, key, StringComparison.OrdinalIgnoreCase))) continue;
            animation.InvalidateTextures();   // 次に使う時にコマを作り直す
            count++;
        }
        return count;
    }

    /// <summary>アニメーションのコマのテクスチャを作る (SpriteAnimation.TextureOf が呼ぶ。前の物は返す)</summary>
    internal void CreateSpriteTextures(SpriteAnimation animation)
    {
        releaseAnimationTextures(animation);
        var textures = new SpriteFrameTexture[animation.Frames.Count];
        var created = new List<uint>();
        var shared = new Dictionary<(string, int, int, int, int), SpriteFrameTexture>();
        // 画像ごとに 1 回だけ読む
        foreach (var group in animation.Frames.Select((f, i) => (f, i)).GroupBy(p => p.f.Image, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrEmpty(group.Key)) continue;
            var (key, file) = resolve(group.Key);
            var image = decodeSpriteImage(key, file);
            if (image == null) continue;
            foreach (var (frame, i) in group)
            {
                int x = 0, y = 0, w = image.Width, h = image.Height;
                if (frame.HasRect)
                {
                    x = Math.Clamp(frame.X, 0, image.Width - 1);
                    y = Math.Clamp(frame.Y, 0, image.Height - 1);
                    w = Math.Clamp(frame.Width, 1, image.Width - x);
                    h = Math.Clamp(frame.Height, 1, image.Height - y);
                }
                if (!shared.TryGetValue((key, x, y, w, h), out var texture))
                {
                    texture = createSpriteTexture(image, x, y, w, h);
                    shared[(key, x, y, w, h)] = texture;
                    if (texture.IsValid) created.Add(texture.Texture);
                }
                textures[i] = texture;
            }
        }
        for (int i = 0; i < textures.Length; i++) if (textures[i].Width == 0) textures[i] = SpriteFrameTexture.Empty;
        animation.Textures = textures;
        animation.TexturesVersion = animation.Version;
        animation.TexturesKey = animation.FramesKey();
        _spriteAnimationTextures[animation] = created;
    }

    private void releaseAnimationTextures(SpriteAnimation animation)
    {
        if (!_spriteAnimationTextures.Remove(animation, out var list)) return;
        foreach (var texture in list) NativeMethods.Renderer_ReleaseUITexture(texture);
    }

    /// <summary>コードで作ったアニメーション (パス無し) のテクスチャを作れるようにする</summary>
    public SpriteAnimation Adopt(SpriteAnimation animation)
    {
        animation.Owner = this;
        return animation;
    }

    /// <summary>スプライトのテクスチャを全部返す (シーンを閉じる / 終了。使っている物は次に使う時に作り直す)</summary>
    public void ReleaseSpriteTextures()
    {
        foreach (var animation in _spriteAnimationTextures.Keys.ToList())
        {
            releaseAnimationTextures(animation);
            animation.Textures = null;
        }
        foreach (var texture in _spriteImages.Values) if (texture.IsValid) NativeMethods.Renderer_ReleaseUITexture(texture.Texture);
        _spriteImages.Clear();
    }

    private static ImageBuffer? decodeSpriteImage(string path, string file)
    {
        try
        {
            return ImageCodec.Load(file).Image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ImageFormatException or ArgumentException)
        {
            Console.WriteLine($"[Assets] Sprite image not loaded : {path} ({ex.Message})");
            return null;
        }
    }

    private static unsafe SpriteFrameTexture createSpriteTexture(ImageBuffer image, int x, int y, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var src = image.Pixels;
        for (int row = 0; row < height; row++)
        {
            int s = ((y + row) * image.Width + x) * 4, d = row * width * 4;
            for (int i = 0; i < width * 4; i++) rgba[d + i] = ImageBuffer.ToByte(src[s + i]);
        }
        uint id;
        fixed (byte* p = rgba) id = NativeMethods.Renderer_CreateUITexture((uint)width, (uint)height, p);
        return id == SpriteFrameTexture.None ? SpriteFrameTexture.Empty : new SpriteFrameTexture(id, width, height);
    }
}
