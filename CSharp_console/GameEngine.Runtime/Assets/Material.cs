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
    // 自分のシェーダー (SH2)。.hlsl のアセットのパス (null = シーンの共通のシェーダー)。付けると色 / テクスチャ / 粗さなどは使わず、シェーダーが色を決める
    public string? Shader;
    // シェーダーのパラメーターの値 (名前 → float / Vector4 / テクスチャのパス string)。無い物はシェーダーに書いた既定の値
    public Dictionary<string, object> ShaderValues { get; } = new(StringComparer.Ordinal);
    /// <summary>今使っているシェーダー (Apply の後。読めない / コンパイルできない時は Error を見る)</summary>
    public ShaderAsset? ShaderAsset => _shader;

    internal uint Handle { get; private set; } = NativeMethods.InvalidHandle;
    public bool IsValid => Handle != NativeMethods.InvalidHandle;

    private readonly AssetManager? _assets;
    private uint    _textureHandle;      // C++ のテクスチャ (0 = 白)
    private string? _loadedTexture;      // _textureHandle を読んだパス
    private bool    _disposed;
    private ShaderAsset? _shader;        // SH2
    private bool    _shaderApplied;      // C++ のマテリアルに自分のシェーダーを付けているか
    private readonly Dictionary<string, (string Path, uint Handle)> _paramTextures = new(StringComparer.Ordinal);   // パラメーターの 2D テクスチャ

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
        applyShader();
    }

    // 自分のシェーダー (SH2) : パラメーターを float4 の並びにして、シェーダーと一緒に C++ のマテリアルに付ける
    private unsafe void applyShader()
    {
        _shader = null;
        if (!string.IsNullOrEmpty(Shader) && _assets != null)
        {
            try { _shader = _assets.LoadShader(Shader); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine($"[Material] {ex.Message} ({Path})"); }
        }
        if (_shader is not { IsValid: true } shader)
        {
            if (_shaderApplied) NativeMethods.Renderer_SetMaterialShader(Handle, 0, null, 0);
            _shaderApplied = false;
            return;
        }
        var data = shader.Pack(ShaderValues, textureFor);
        fixed (float* p = data) NativeMethods.Renderer_SetMaterialShader(Handle, shader.Id, p, (uint)(data.Length / 4));
        _shaderApplied = true;
    }

    // パラメーターのテクスチャ : 2D はマテリアルごとに読む (white = 0 = 白) / 3D は AssetManager が作って共有する
    private uint textureFor(ShaderParam param, string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Equals("white", StringComparison.OrdinalIgnoreCase)) { releaseParamTexture(param.Name); return 0; }
        if (param.Type == ShaderParamType.Texture3D) return _assets?.LoadTexture3D(path) ?? 0;
        if (_paramTextures.TryGetValue(param.Name, out var loaded) && loaded.Path == path) return loaded.Handle;
        releaseParamTexture(param.Name);
        var file = _assets?.Database?.ToAssetPath(path) is { } assetPath ? _assets.Database.FullPath(assetPath) : path;
        var handle = NativeMethods.Renderer_LoadTexture(file);
        if (handle == 0) Console.WriteLine($"[Material] Texture not loaded : {path} ({Path} / {param.Name})");
        _paramTextures[param.Name] = (path, handle);
        return handle;
    }

    private void releaseParamTexture(string name)
    {
        if (!_paramTextures.Remove(name, out var old)) return;
        if (old.Handle != 0) NativeMethods.Renderer_ReleaseTexture(old.Handle);
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
        foreach (var name in _paramTextures.Keys.ToList()) releaseParamTexture(name);   // SH2
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
            // 自分のシェーダー (SH2) : シェーダーの Id とパス / パラメーターの値 (テクスチャはアセットなら Id も)
            if (!string.IsNullOrEmpty(Shader))
            {
                w.WriteStartObject("shader");
                if (AssetDatabase.Current?.Find(Shader) is { } shaderEntry) w.WriteString("guid", shaderEntry.Guid.ToString());
                w.WriteString("path", Shader);
                w.WriteEndObject();
                w.WriteStartObject("values");
                foreach (var (name, value) in ShaderValues.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    switch (value)
                    {
                    case float f: w.WriteNumber(name, f); break;
                    case Vector4 v:
                        w.WriteStartArray(name);
                        w.WriteNumberValue(v.X); w.WriteNumberValue(v.Y); w.WriteNumberValue(v.Z); w.WriteNumberValue(v.W);
                        w.WriteEndArray();
                        break;
                    case string s when AssetDatabase.Current?.Find(s) is { } entry:
                        w.WriteStartObject(name);
                        w.WriteString("guid", entry.Guid.ToString());
                        w.WriteString("path", s);
                        w.WriteEndObject();
                        break;
                    case string s: w.WriteString(name, s); break;   // white / builtin:...
                    }
                }
                w.WriteEndObject();
            }
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
        // SH2 : 自分のシェーダー (無ければ共通のシェーダー)
        Shader = null;
        if (root.TryGetProperty("shader", out var sh) && sh.ValueKind == JsonValueKind.Object)
        {
            Shader = sh.TryGetProperty("path", out var sp) ? sp.GetString() : null;
            if (sh.TryGetProperty("guid", out var sg) && Guid.TryParse(sg.GetString(), out var shaderGuid) && AssetDatabase.Current?.Find(shaderGuid) is { } shaderEntry) Shader = shaderEntry.Path;
        }
        ShaderValues.Clear();
        if (root.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in values.EnumerateObject())
            {
                var v = property.Value;
                switch (v.ValueKind)
                {
                case JsonValueKind.Number: ShaderValues[property.Name] = v.GetSingle(); break;
                case JsonValueKind.Array:
                    var n = new float[4];
                    for (int i = 0; i < Math.Min(4, v.GetArrayLength()); i++) n[i] = v[i].GetSingle();
                    ShaderValues[property.Name] = new Vector4(n[0], n[1], n[2], n[3]);
                    break;
                case JsonValueKind.String: ShaderValues[property.Name] = v.GetString() ?? ""; break;
                case JsonValueKind.Object:
                    var texture = v.TryGetProperty("path", out var tp) ? tp.GetString() : null;
                    if (v.TryGetProperty("guid", out var tg) && Guid.TryParse(tg.GetString(), out var textureGuid) && AssetDatabase.Current?.Find(textureGuid) is { } textureEntry) texture = textureEntry.Path;
                    if (texture != null) ShaderValues[property.Name] = texture;
                    break;
                }
            }
        }
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
