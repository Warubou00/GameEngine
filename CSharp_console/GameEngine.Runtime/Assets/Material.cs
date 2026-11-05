using System.Numerics;
using System.Text;
using System.Text.Json;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// マテリアルのアセット (ED5)。「名前.material.json」に色 / テクスチャ / 粗さ・金属 / 半透明 / 両面を書く
//   MeshRenderer.Material に入れると、モデルに付いているマテリアルの代わりに使う (全部のサブメッシュ)
//   同じ Material を何個の物に付けても、C++ のマテリアルは 1 つ (値を変えると全部が変わる)
//   値を変えたら Apply で C++ へ送り、Save でファイルに書く (エディターは両方を自動で行う)
// ==========================================================================
public sealed class Material : IDisposable
{
    public const string Format = "GameEngineMaterial";

    // 生きているマテリアルの数 (0 なら RenderSyncSystem は見回らない)
    public static int LiveCount { get; private set; }

    public string  Path { get; internal set; }         // アセットのパス ("Assets/..."。コードで作った物は "")。名前を変えると変わる (ED10)
    public Vector4 Color       = Vector4.One;          // テクスチャ × この色 (A は半透明の時の不透明度)
    public string? Texture;                            // テクスチャのアセットのパス (null = 白)
    public float   Metallic;                           // 0 = 非金属 / 1 = 金属
    public float   Roughness   = 0.6f;                 // 0 = つるつる / 1 = ざらざら
    public bool    Transparent;                        // 半透明として描く (不透明な物の後に、ブレンドして)
    public bool    DoubleSided;                        // 裏面も描く

    internal uint Handle { get; private set; } = NativeMethods.InvalidHandle;
    public bool IsValid => Handle != NativeMethods.InvalidHandle;

    private readonly AssetManager? _assets;
    private uint    _textureHandle;      // C++ のテクスチャ (0 = 白)
    private string? _loadedTexture;      // _textureHandle を読んだパス
    private bool    _disposed;

    // コードで作るマテリアル (ファイルに書かない。Apply で C++ へ送る。要らなくなったら Dispose)
    public Material() : this("", null) { }

    internal Material(string path, AssetManager? assets)
    {
        Path    = path;
        _assets = assets;
        LiveCount++;
    }

    // 値を C++ へ送る (テクスチャが変わっていれば読み直す)。最初の 1 回で C++ のマテリアルを作る
    public void Apply()
    {
        if (Texture != _loadedTexture) loadTexture();
        var desc = new NativeMethods.MaterialDesc
        {
            Color     = Color,
            Metallic  = Math.Clamp(Metallic, 0.0f, 1.0f),
            Roughness = Math.Clamp(Roughness, 0.02f, 1.0f),
            Texture   = _textureHandle,
            Flags     = (Transparent ? 1u : 0u) | (DoubleSided ? 2u : 0u),
        };
        if (!IsValid) Handle = NativeMethods.Renderer_CreateMaterial(desc);
        else          NativeMethods.Renderer_UpdateMaterial(Handle, desc);
    }

    // テクスチャのファイルが変わった時 (エディター) : 読み直して送る
    public void ReloadTexture()
    {
        _loadedTexture = null;
        Apply();
    }

    private void loadTexture()
    {
        if (_textureHandle != 0) NativeMethods.Renderer_ReleaseTexture(_textureHandle);
        _textureHandle = 0;
        _loadedTexture = Texture;
        if (string.IsNullOrEmpty(Texture)) return;
        var file = _assets?.Database?.ToAssetPath(Texture) is { } assetPath ? _assets.Database.FullPath(assetPath) : Texture;
        _textureHandle = NativeMethods.Renderer_LoadTexture(file);
        if (_textureHandle == 0) Console.WriteLine($"[Material] Texture not loaded : {Texture} ({Path})");
    }

    // C++ のマテリアルとテクスチャを返す (この後は使えない。2 回呼んでもよい)
    public void Dispose()
    {
        if (_textureHandle != 0) NativeMethods.Renderer_ReleaseTexture(_textureHandle);
        if (IsValid) NativeMethods.Renderer_DestroyMaterial(Handle);
        _textureHandle = 0;
        _loadedTexture = null;
        if (_disposed) return;
        _disposed = true;
        Handle = NativeMethods.InvalidHandle;
        LiveCount--;
    }

    // ---------------------------------------------------------------------
    // ファイル (JSON)
    // ---------------------------------------------------------------------

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", 1);
            w.WriteStartArray("color");
            w.WriteNumberValue(Color.X); w.WriteNumberValue(Color.Y); w.WriteNumberValue(Color.Z); w.WriteNumberValue(Color.W);
            w.WriteEndArray();
            if (string.IsNullOrEmpty(Texture)) w.WriteNull("texture");
            else
            {
                w.WriteStartObject("texture");
                if (AssetDatabase.Current?.Find(Texture) is { } entry) w.WriteString("guid", entry.Guid.ToString());
                w.WriteString("path", Texture);
                w.WriteEndObject();
            }
            w.WriteNumber("metallic", Metallic);
            w.WriteNumber("roughness", Roughness);
            w.WriteBoolean("transparent", Transparent);
            w.WriteBoolean("doubleSided", DoubleSided);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // JSON の値を読む (無い値は今のまま)。テクスチャは Id を優先する (動かしたファイルも見つかる)
    public void FromJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var root = document.RootElement;
        if (root.TryGetProperty("format", out var format) && format.GetString() != Format) throw new InvalidDataException($"Not a material file : {Path}");
        if (root.TryGetProperty("color", out var c) && c.ValueKind == JsonValueKind.Array && c.GetArrayLength() >= 3)
            Color = new Vector4(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle(), c.GetArrayLength() > 3 ? c[3].GetSingle() : 1.0f);
        if (root.TryGetProperty("texture", out var t))
        {
            string? texture = null;
            if (t.ValueKind == JsonValueKind.Object)
            {
                texture = t.TryGetProperty("path", out var p) ? p.GetString() : null;
                if (t.TryGetProperty("guid", out var g) && Guid.TryParse(g.GetString(), out var guid) && AssetDatabase.Current?.Find(guid) is { } entry) texture = entry.Path;
            }
            Texture = texture;
        }
        if (root.TryGetProperty("metallic", out var m))     Metallic    = m.GetSingle();
        if (root.TryGetProperty("roughness", out var r))    Roughness   = r.GetSingle();
        if (root.TryGetProperty("transparent", out var tr)) Transparent = tr.GetBoolean();
        if (root.TryGetProperty("doubleSided", out var ds)) DoubleSided = ds.GetBoolean();
    }

    // ファイルに書く (アセットのパスが無い物は書けない)
    public void Save()
    {
        if (string.IsNullOrEmpty(Path) || _assets?.Database is not { } database) return;
        var file = database.FullPath(Path);
        _assets.NoteOwnWrite(file);
        File.WriteAllText(file, ToJson(), new UTF8Encoding(false));
    }

    public override string ToString() => $"Material({Path})";
}
