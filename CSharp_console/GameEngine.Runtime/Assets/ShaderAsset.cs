using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// 自分のシェーダー (SH2)。Assets の「名前.hlsl」。マテリアルの Shader に付けると、そのマテリアルの物をこのシェーダーで描く
//
//   書き方 :
//     // @queue opaque | transparent          (既定 opaque。transparent は不透明の後に遠い順に描く)
//     // @blend opaque | alpha | additive | premultiplied   (既定 : opaque。transparent なら alpha)
//     // @cull back | front | none            (既定 back)
//     // @zwrite on | off                     (既定 : opaque は on / transparent は off)
//     // @param float     Density   1.0  range(0, 4)
//     // @param color     Tint      (1, 0.5, 0.2, 1)
//     // @param vector    Wind      (1, 0, 0, 0)
//     // @param texture   Noise     white          (2D。white / black / アセットのパス)
//     // @param texture3d Volume    builtin:ValueNoise3D:64   (3D。エンジンが作るノイズ)
//     float4 Surface(SurfaceInput IN)
//     {
//         return float4(Tint.rgb * Density, 1);   // 色 (線形の HDR) と不透明度
//     }
//
//   使える物 (このファイルの前にエンジンが書く) :
//     IN.WorldPosition / LocalPosition / Normal (カメラ側を向いたワールドの法線) / ViewDirection (カメラ → 点) /
//     CameraPosition / UV / Color (頂点の色) / ScreenUV / Time (秒) / LocalToWorld / WorldToLocal (行ベクトル : mul(float4(p, 1), M))
//     パラメーターは名前でそのまま (float / float4 の値、テクスチャは Texture2D / Texture3D として Noise.Sample(g_Sampler, uv))
//     Lit(albedo, normal, position, metallic, roughness) : シーンと同じライティング (ライト / 環境光 / 影)
//     ToLinear(color) : sRGB → 線形 / シーンのシェーダーの関数 / g_Sampler (線形・繰り返し) / g_LinearClamp
//   コンパイルのエラーは「ファイル:行」で Console に出る (書いたファイルの行番号)
// ==========================================================================
public enum ShaderParamType { Float, Color, Vector, Texture, Texture3D }
public enum ShaderBlend { Opaque = 0, Alpha = 1, Additive = 2, Premultiplied = 3 }
public enum ShaderCull { Back = 0, Front = 1, None = 2 }

public sealed record ShaderParam(string Name, ShaderParamType Type, int Slot, Vector4 DefaultValue, string? DefaultTexture, Vector2? Range)
{
    public bool IsTexture => Type is ShaderParamType.Texture or ShaderParamType.Texture3D;
}

public sealed class ShaderAsset : IDisposable
{
    public string Path { get; internal set; }
    public IReadOnlyList<ShaderParam> Parameters { get; private set; } = Array.Empty<ShaderParam>();
    public ShaderBlend Blend { get; private set; }
    public ShaderCull  Cull  { get; private set; }
    public bool DepthWrite  { get; private set; } = true;
    public bool Transparent { get; private set; }

    /// <summary>最後のコンパイルのエラー (成功なら null)。前に成功していれば、その時のシェーダーで描き続ける</summary>
    public string? Error { get; private set; }
    /// <summary>コンパイルに成功するたびに増える (パラメーターが変わるとマテリアルが送り直す)</summary>
    public int Version { get; private set; }
    /// <summary>エンジンが作った HLSL (確認用)</summary>
    public string GeneratedSource { get; private set; } = "";

    internal uint Id { get; private set; }
    public bool IsValid => Id != 0;
    private bool _disposed;

    internal ShaderAsset(string path) => Path = path;

    // ---------------------------------------------------------------------
    // 読む / コンパイル
    // ---------------------------------------------------------------------

    private static readonly Regex Directive = new(@"^\s*//\s*@(\w+)\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex ParamLine = new(@"^(float|color|vector|texture|texture2d|texture3d)\s+([A-Za-z_]\w*)\s*(.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex RangeText = new(@"range\s*\(\s*([-+0-9.eE]+)\s*,\s*([-+0-9.eE]+)\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal) { "Surface", "SurfaceInput", "Lit", "ToLinear", "IN", "Time" };

    /// <summary>ソースを読んでコンパイルする。戻り値 = 成功したか (失敗しても前の物があればそれで描く)</summary>
    public bool Compile(string text)
    {
        var parsed = parse(text, out var parseError);
        if (parseError != null) return fail(parseError);
        GeneratedSource = generate(parsed, text);
        var settings = new NativeMethods.CustomShaderSettings
        {
            Blend = (uint)parsed.Blend, Cull = (uint)parsed.Cull, DepthWrite = parsed.DepthWrite ? 1u : 0u, Transparent = parsed.Transparent ? 1u : 0u,
        };
        var source = Encoding.UTF8.GetBytes(GeneratedSource + "\0");
        var error = new byte[8192];
        bool ok;
        unsafe
        {
            fixed (byte* s = source)
            fixed (byte* e = error)
            {
                if (Id == 0)
                {
                    Id = NativeMethods.Renderer_CreateCustomShader(s, settings, e, (uint)error.Length);
                    ok = Id != 0;
                }
                else ok = NativeMethods.Renderer_UpdateCustomShader(Id, s, settings, e, (uint)error.Length);
            }
        }
        if (!ok)
        {
            var length = Array.IndexOf(error, (byte)0);
            return fail(Encoding.UTF8.GetString(error, 0, length < 0 ? error.Length : length).Trim());
        }
        Parameters  = parsed.Parameters;
        Blend       = parsed.Blend;
        Cull        = parsed.Cull;
        DepthWrite  = parsed.DepthWrite;
        Transparent = parsed.Transparent;
        Error = null;
        Version++;
        return true;
    }

    private bool fail(string error)
    {
        Error = error;
        Console.WriteLine($"[Shader] {Path} : compile failed{(IsValid ? " (the previous version is kept)" : "")}");
        foreach (var line in error.Split('\n').Where(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)).Take(10)) Console.WriteLine($"  {line.Trim()}");
        return false;
    }

    private sealed class Parsed
    {
        public List<ShaderParam> Parameters = new();
        public ShaderBlend Blend = ShaderBlend.Opaque;
        public ShaderCull Cull = ShaderCull.Back;
        public bool DepthWrite = true;
        public bool Transparent;
    }

    private Parsed parse(string text, out string? error)
    {
        error = null;
        var parsed = new Parsed();
        bool? zwrite = null;
        bool blendSet = false;
        var lineNumber = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            lineNumber++;
            var match = Directive.Match(raw);
            if (!match.Success) continue;
            var key = match.Groups[1].Value.ToLowerInvariant();
            var value = match.Groups[2].Value.Trim();
            string where() => $"{Path}({lineNumber})";
            switch (key)
            {
            case "queue":
                if (value.Equals("transparent", StringComparison.OrdinalIgnoreCase)) parsed.Transparent = true;
                else if (!value.Equals("opaque", StringComparison.OrdinalIgnoreCase)) { error = $"{where()}: @queue must be opaque or transparent"; return parsed; }
                break;
            case "blend":
                if (!Enum.TryParse<ShaderBlend>(value, true, out var blend)) { error = $"{where()}: @blend must be opaque / alpha / additive / premultiplied"; return parsed; }
                parsed.Blend = blend;
                blendSet = true;
                break;
            case "cull":
                if (!Enum.TryParse<ShaderCull>(value, true, out var cull)) { error = $"{where()}: @cull must be back / front / none"; return parsed; }
                parsed.Cull = cull;
                break;
            case "zwrite":
                zwrite = value.Equals("on", StringComparison.OrdinalIgnoreCase) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
                break;
            case "param":
                var p = ParamLine.Match(value);
                if (!p.Success) { error = $"{where()}: @param <float|color|vector|texture|texture3d> <Name> [default] [range(min, max)]"; return parsed; }
                var name = p.Groups[2].Value;
                if (Reserved.Contains(name) || parsed.Parameters.Any(x => x.Name == name)) { error = $"{where()}: parameter name '{name}' is reserved or used twice"; return parsed; }
                if (parsed.Parameters.Count >= 64) { error = $"{where()}: up to 64 parameters"; return parsed; }
                var type = p.Groups[1].Value.ToLowerInvariant() switch
                {
                    "float" => ShaderParamType.Float,
                    "color" => ShaderParamType.Color,
                    "vector" => ShaderParamType.Vector,
                    "texture3d" => ShaderParamType.Texture3D,
                    _ => ShaderParamType.Texture,
                };
                var rest = p.Groups[3].Value;
                Vector2? range = null;
                var r = RangeText.Match(rest);
                if (r.Success)
                {
                    range = new Vector2(float.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(r.Groups[2].Value, CultureInfo.InvariantCulture));
                    rest = rest.Remove(r.Index, r.Length);
                }
                rest = rest.Trim();
                var defaultValue = type switch
                {
                    ShaderParamType.Color => Vector4.One,
                    ShaderParamType.Float => Vector4.Zero,
                    _ => Vector4.Zero,
                };
                string? defaultTexture = null;
                if (type is ShaderParamType.Texture or ShaderParamType.Texture3D) defaultTexture = rest.Length > 0 ? rest : (type == ShaderParamType.Texture ? "white" : null);
                else if (rest.Length > 0 && !TryParseVector(rest, out defaultValue)) { error = $"{where()}: cannot read the default value '{rest}'"; return parsed; }
                if (type == ShaderParamType.Color && rest.Length > 0 && rest.Count(c => c == ',') == 2) defaultValue.W = 1;   // (r, g, b) なら不透明
                parsed.Parameters.Add(new ShaderParam(name, type, parsed.Parameters.Count, defaultValue, defaultTexture, range));
                break;
            }
        }
        if (parsed.Transparent && !blendSet) parsed.Blend = ShaderBlend.Alpha;
        parsed.DepthWrite = zwrite ?? !parsed.Transparent;
        if (!Regex.IsMatch(text, @"\bSurface\s*\(\s*SurfaceInput\b")) error = $"{Path}: define float4 Surface(SurfaceInput IN)";
        return parsed;
    }

    // "1.5" / "(1, 2, 3, 4)" / "1 2 3" → Vector4 (足りない所は 0)
    public static bool TryParseVector(string text, out Vector4 value)
    {
        value = Vector4.Zero;
        var parts = text.Trim().TrimStart('(').TrimEnd(')').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 4) return false;
        var v = new float[4];
        for (int i = 0; i < parts.Length; i++)
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
        value = new Vector4(v[0], v[1], v[2], v[3]);
        return true;
    }

    // シーンのシェーダーの後ろに付ける HLSL : 入力 / パラメーター / 便利な関数 / ユーザーのコード / エントリー (PSCustom)
    private string generate(Parsed parsed, string user)
    {
        var b = new StringBuilder();
        b.AppendLine("#line 1 \"(engine)\"");
        b.AppendLine("struct SurfaceInput");
        b.AppendLine("{");
        b.AppendLine("    float3   WorldPosition;");
        b.AppendLine("    float3   LocalPosition;");
        b.AppendLine("    float3   Normal;          // カメラ側を向いたワールドの法線");
        b.AppendLine("    float3   ViewDirection;   // カメラ → この点 (長さ 1)");
        b.AppendLine("    float3   CameraPosition;");
        b.AppendLine("    float2   UV;");
        b.AppendLine("    float4   Color;           // 頂点の色");
        b.AppendLine("    float2   ScreenUV;");
        b.AppendLine("    float    Time;");
        b.AppendLine("    float4x4 LocalToWorld;    // mul(float4(p, 1), LocalToWorld)");
        b.AppendLine("    float4x4 WorldToLocal;");
        b.AppendLine("};");
        b.AppendLine("static uint _shaderParamBase;");
        b.AppendLine("float4 _shaderParam(uint i) { return g_CustomData[_shaderParamBase + i]; }");
        foreach (var p in parsed.Parameters)
        {
            switch (p.Type)
            {
            case ShaderParamType.Float:     b.AppendLine($"static float  {p.Name};"); break;
            case ShaderParamType.Texture:   b.AppendLine($"#define {p.Name} (g_Textures[asuint(_shaderParam({p.Slot}).x)])"); break;
            case ShaderParamType.Texture3D: b.AppendLine($"#define {p.Name} (g_Textures3D[asuint(_shaderParam({p.Slot}).x)])"); break;
            default:                        b.AppendLine($"static float4 {p.Name};"); break;
            }
        }
        b.AppendLine("float3 ToLinear(float3 c) { return toLinear(c); }");
        b.AppendLine("float3 Lit(float3 albedo, float3 normal, float3 position, float metallic, float roughness)");
        b.AppendLine("{");
        b.AppendLine("    return shade(albedo, normalize(normal), normalize(g_CameraPosition - position), position, metallic, roughness, 1.0f);");
        b.AppendLine("}");
        b.AppendLine("float4x4 _inverse(float4x4 m)");
        b.AppendLine("{");
        b.AppendLine("    float n11 = m[0][0], n12 = m[1][0], n13 = m[2][0], n14 = m[3][0];");
        b.AppendLine("    float n21 = m[0][1], n22 = m[1][1], n23 = m[2][1], n24 = m[3][1];");
        b.AppendLine("    float n31 = m[0][2], n32 = m[1][2], n33 = m[2][2], n34 = m[3][2];");
        b.AppendLine("    float n41 = m[0][3], n42 = m[1][3], n43 = m[2][3], n44 = m[3][3];");
        b.AppendLine("    float t11 = n23 * n34 * n42 - n24 * n33 * n42 + n24 * n32 * n43 - n22 * n34 * n43 - n23 * n32 * n44 + n22 * n33 * n44;");
        b.AppendLine("    float t12 = n14 * n33 * n42 - n13 * n34 * n42 - n14 * n32 * n43 + n12 * n34 * n43 + n13 * n32 * n44 - n12 * n33 * n44;");
        b.AppendLine("    float t13 = n13 * n24 * n42 - n14 * n23 * n42 + n14 * n22 * n43 - n12 * n24 * n43 - n13 * n22 * n44 + n12 * n23 * n44;");
        b.AppendLine("    float t14 = n14 * n23 * n32 - n13 * n24 * n32 - n14 * n22 * n33 + n12 * n24 * n33 + n13 * n22 * n34 - n12 * n23 * n34;");
        b.AppendLine("    float det = n11 * t11 + n21 * t12 + n31 * t13 + n41 * t14;");
        b.AppendLine("    float idet = 1.0f / det;");
        b.AppendLine("    float4x4 r;");
        b.AppendLine("    r[0][0] = t11 * idet;");
        b.AppendLine("    r[0][1] = (n24 * n33 * n41 - n23 * n34 * n41 - n24 * n31 * n43 + n21 * n34 * n43 + n23 * n31 * n44 - n21 * n33 * n44) * idet;");
        b.AppendLine("    r[0][2] = (n22 * n34 * n41 - n24 * n32 * n41 + n24 * n31 * n42 - n21 * n34 * n42 - n22 * n31 * n44 + n21 * n32 * n44) * idet;");
        b.AppendLine("    r[0][3] = (n23 * n32 * n41 - n22 * n33 * n41 - n23 * n31 * n42 + n21 * n33 * n42 + n22 * n31 * n43 - n21 * n32 * n43) * idet;");
        b.AppendLine("    r[1][0] = t12 * idet;");
        b.AppendLine("    r[1][1] = (n13 * n34 * n41 - n14 * n33 * n41 + n14 * n31 * n43 - n11 * n34 * n43 - n13 * n31 * n44 + n11 * n33 * n44) * idet;");
        b.AppendLine("    r[1][2] = (n14 * n32 * n41 - n12 * n34 * n41 - n14 * n31 * n42 + n11 * n34 * n42 + n12 * n31 * n44 - n11 * n32 * n44) * idet;");
        b.AppendLine("    r[1][3] = (n12 * n33 * n41 - n13 * n32 * n41 + n13 * n31 * n42 - n11 * n33 * n42 - n12 * n31 * n43 + n11 * n32 * n43) * idet;");
        b.AppendLine("    r[2][0] = t13 * idet;");
        b.AppendLine("    r[2][1] = (n14 * n23 * n41 - n13 * n24 * n41 - n14 * n21 * n43 + n11 * n24 * n43 + n13 * n21 * n44 - n11 * n23 * n44) * idet;");
        b.AppendLine("    r[2][2] = (n12 * n24 * n41 - n14 * n22 * n41 + n14 * n21 * n42 - n11 * n24 * n42 - n12 * n21 * n44 + n11 * n22 * n44) * idet;");
        b.AppendLine("    r[2][3] = (n13 * n22 * n41 - n12 * n23 * n41 - n13 * n21 * n42 + n11 * n23 * n42 + n12 * n21 * n43 - n11 * n22 * n43) * idet;");
        b.AppendLine("    r[3][0] = t14 * idet;");
        b.AppendLine("    r[3][1] = (n13 * n24 * n31 - n14 * n23 * n31 + n14 * n21 * n33 - n11 * n24 * n33 - n13 * n21 * n34 + n11 * n23 * n34) * idet;");
        b.AppendLine("    r[3][2] = (n14 * n22 * n31 - n12 * n24 * n31 - n14 * n21 * n32 + n11 * n24 * n32 + n12 * n21 * n34 - n11 * n22 * n34) * idet;");
        b.AppendLine("    r[3][3] = (n12 * n23 * n31 - n13 * n22 * n31 + n13 * n21 * n32 - n11 * n23 * n32 - n12 * n21 * n33 + n11 * n22 * n33) * idet;");
        b.AppendLine("    return r;   // (n の並びを転置で読んでいるので、r がそのまま行ベクトルの形の逆行列)");
        b.AppendLine("}");
        // ユーザーのコード (エラーの行番号がファイルの行になるように)
        b.AppendLine($"#line 1 \"{Path.Replace("\\", "/").Replace("\"", "")}\"");
        b.AppendLine(user);
        b.AppendLine("#line 1 \"(engine entry)\"");
        b.AppendLine("float4 PSCustom(VSOutput input) : SV_TARGET");
        b.AppendLine("{");
        b.AppendLine("    _shaderParamBase = g_Inputs[0].x;");
        foreach (var p in parsed.Parameters)
        {
            if (p.Type == ShaderParamType.Float) b.AppendLine($"    {p.Name} = _shaderParam({p.Slot}).x;");
            else if (!p.IsTexture)               b.AppendLine($"    {p.Name} = _shaderParam({p.Slot});");
        }
        b.AppendLine("    SurfaceInput IN;");
        // ワールド行列 : g_CustomData の 4 個 (g_Objects と同じ並び = 列ごと。float4x4 の作り方は行ごとなので転置する)
        b.AppendLine("    const uint w = g_Inputs[0].y;");
        b.AppendLine("    IN.LocalToWorld   = transpose(float4x4(g_CustomData[w], g_CustomData[w + 1], g_CustomData[w + 2], g_CustomData[w + 3]));");
        b.AppendLine("    IN.WorldToLocal   = _inverse(IN.LocalToWorld);");
        b.AppendLine("    IN.WorldPosition  = worldPosition(input.Position);");
        b.AppendLine("    IN.LocalPosition  = mul(float4(IN.WorldPosition, 1.0f), IN.WorldToLocal).xyz;");
        b.AppendLine("    IN.CameraPosition = g_CameraPosition;");
        b.AppendLine("    IN.ViewDirection  = normalize(IN.WorldPosition - g_CameraPosition);");
        b.AppendLine("    float3 n = input.Normal;");
        b.AppendLine("    n = dot(n, n) > 1e-12f ? normalize(n) : -IN.ViewDirection;");
        b.AppendLine("    IN.Normal = dot(n, IN.ViewDirection) > 0.0f ? -n : n;");
        b.AppendLine("    IN.UV       = input.UV;");
        b.AppendLine("    IN.Color    = input.Color;");
        b.AppendLine("    IN.ScreenUV = input.Position.xy * g_InvScreenSize;");
        b.AppendLine("    IN.Time     = g_Params[0].x;");
        b.AppendLine("    return Surface(IN);");
        b.AppendLine("}");
        return b.ToString();
    }

    // ---------------------------------------------------------------------
    // マテリアルの値 → C++ に送る float4 の並び
    // ---------------------------------------------------------------------

    /// <summary>
    /// パラメーターの並びの値 (float4 × 数)。textureId はテクスチャのパラメーターの番号 (C++ のテクスチャの番号) を返す関数
    /// value は名前 → 値 (float / Vector4 / テクスチャのパス string)。無い物は既定の値
    /// </summary>
    internal float[] Pack(IReadOnlyDictionary<string, object> values, Func<ShaderParam, string?, uint> textureId)
    {
        var data = new float[Math.Max(1, Parameters.Count) * 4];
        foreach (var p in Parameters)
        {
            values.TryGetValue(p.Name, out var value);
            var o = p.Slot * 4;
            if (p.IsTexture)
            {
                var id = textureId(p, value as string ?? p.DefaultTexture);
                data[o] = BitConverter.Int32BitsToSingle((int)id);
                continue;
            }
            var v = value switch
            {
                float f => new Vector4(f, 0, 0, 0),
                double d => new Vector4((float)d, 0, 0, 0),
                Vector4 v4 => v4,
                Vector3 v3 => new Vector4(v3, 1),
                _ => p.DefaultValue,
            };
            data[o] = v.X; data[o + 1] = v.Y; data[o + 2] = v.Z; data[o + 3] = v.W;
        }
        return data;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Id != 0) NativeMethods.Renderer_DestroyCustomShader(Id);
        Id = 0;
    }

    public override string ToString() => $"ShaderAsset({Path}{(Error != null ? ", error" : "")})";
}
