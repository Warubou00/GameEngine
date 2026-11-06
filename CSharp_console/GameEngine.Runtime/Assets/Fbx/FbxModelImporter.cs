using System.Diagnostics;
using System.Numerics;

namespace GameEngine.Fbx;

// ==========================================================================
// FBX のモデル (.mod.fbx) → エンジンの形 (AN2)
//   1. ノード (Model) の行列 : FBX の式 (下) と親の行列。メッシュはその Model の位置に置いた形 (全部をモデルの空間に焼き込む)
//   2. 軸と単位 : GlobalSettings の軸 (上 / 前 / 横と符号) と UnitScaleFactor (cm) → エンジン (左手系 / +Y 上 / +Z 奥 / m)
//   3. 層 (Layer) : 法線 / UV / 頂点の色 / マテリアル。並び方 (ByPolygonVertex / ByControlPoint / ByPolygon / AllSame) と
//      引き方 (Direct / IndexToDirect) を全部読む。法線が無ければ点ごとに面の法線を足して作る
//   4. 多角形 → 三角形 : 3 角はそのまま、4 角以上は耳を切る方法 (凹んだ多角形も)。面の向きは元のまま
//   5. 頂点 : 同じ (点 / 法線 / UV / 色) の角は 1 つにまとめる。サブメッシュはマテリアルごと (メッシュをまたいでまとめる)
//   6. マテリアル : 色 (DiffuseColor × DiffuseFactor) / 不透明度 / 鋭さ (Shininess → 粗さ) / 色のテクスチャ
//      (ファイルの場所 → FBX の隣 → 名前で Assets から探す。FBX に入っているテクスチャは Library に書き出して使う)
//
//   FBX のノードの式 (列ベクトル) : Global = Parent × T × Roff × Rp × Rpre × R × Rpost⁻¹ × Rp⁻¹ × Soff × Sp × S × Sp⁻¹
//   ここでは行ベクトル (System.Numerics) なので逆の順に掛ける。Rpre / Rpost は XYZ の順、R は RotationOrder の順
//   メッシュだけに掛かる Geometric (T / R / S) は子に伝わらない
//
//   面の向き : エンジンは cross(b - a, c - a) が外向きの法線の方 (左手系の画面で時計回り。Primitives と同じ)。FBX (右手系の反時計回り) も
//   数としては同じ関係だが、右手系 → 左手系の変換 (鏡) を通すと逆になるので、鏡の時は三角形の順を入れ替える
//   (負の大きさのノードも鏡。鏡が 2 回なら入れ替えない)
// ==========================================================================
public sealed class FbxMaterialData
{
    public string Name = "";
    public Vector4 Color = new(0.8f, 0.8f, 0.8f, 1.0f);
    public float Metallic;
    public float Roughness = 0.6f;
    public bool Transparent;
    /// <summary>色のテクスチャのファイル (フルパス。見つからなければ null)</summary>
    public string? TexturePath;
    /// <summary>FBX に書いてあったテクスチャの名前 (見つからなかった時の表示用)</summary>
    public string? TextureName;
    public bool TextureEmbedded;
}

public sealed class FbxSubMeshData
{
    public int Material;
    public int FirstIndex;
    public int IndexCount;
}

public sealed class FbxModelData
{
    public List<Vector3> Positions = new();
    public List<Vector3> Normals = new();
    public List<Vector2> UVs = new();
    public List<Vector4> Colors = new();
    public List<uint> Indices = new();
    public List<FbxSubMeshData> SubMeshes = new();
    public List<FbxMaterialData> Materials = new();
    /// <summary>頂点ごとの元 (メッシュの Id / 点の番号)。スキンの重みを引くのに使う (AN4)</summary>
    public List<(long Mesh, int ControlPoint)> Sources = new();
    public List<string> Warnings = new();
    public int MeshCount;
    public int TriangleCount => Indices.Count / 3;
    public Vector3 BoundsMin = new(float.MaxValue), BoundsMax = new(float.MinValue);
    public double ImportMilliseconds;
}

public static class FbxModelImporter
{
    /// <param name="fbxFile">FBX のファイル (テクスチャを隣から探す)</param>
    /// <param name="findTexture">名前でテクスチャを探す (Assets の中。無ければ null)</param>
    /// <param name="embeddedFolder">FBX に入っているテクスチャを書き出す所 (null なら一時フォルダ)</param>
    /// <param name="onlyNode">そのノード (Model の名前) のメッシュだけを、ノードの空間で作る (部品。AN3 : xxx.mod.fbx#ノード)。null なら全部を焼き込む</param>
    public static FbxModelData Import(FbxScene scene, string fbxFile, Func<string, string?>? findTexture = null, string? embeddedFolder = null, string? onlyNode = null)
    {
        var start = Stopwatch.GetTimestamp();
        var data = new FbxModelData();
        var axes = FbxTransforms.AxisMatrix(scene, out var axisDeterminant);
        var unit = (float)(scene.UnitScaleFactor * 0.01);   // cm → m
        var globals = new Dictionary<long, Matrix4x4>();
        var materialIndex = new Dictionary<long, int>();
        var perMaterial = new List<List<uint>>();
        var defaultMaterial = -1;

        int materialFor(FbxObject? material)
        {
            if (material == null)
            {
                if (defaultMaterial < 0)
                {
                    defaultMaterial = data.Materials.Count;
                    data.Materials.Add(new FbxMaterialData { Name = "(default)" });
                    perMaterial.Add(new List<uint>());
                }
                return defaultMaterial;
            }
            if (materialIndex.TryGetValue(material.Id, out var index)) return index;
            index = data.Materials.Count;
            data.Materials.Add(readMaterial(scene, material, fbxFile, findTexture, embeddedFolder, data.Warnings));
            perMaterial.Add(new List<uint>());
            materialIndex[material.Id] = index;
            return index;
        }

        var vertexOf = new Dictionary<VertexKey, uint>();
        foreach (var geometry in scene.OfKind("Geometry", "Mesh"))
        {
            var node = geometry.Node;
            var positions = node.ChildArray<double>("Vertices");
            var polygonIndex = node.ChildArray<int>("PolygonVertexIndex");
            if (positions == null || polygonIndex == null || positions.Length < 9) continue;
            data.MeshCount++;
            var model = geometry.Parents.Select(p => p.Parent).FirstOrDefault(p => p.Kind == "Model");
            if (onlyNode != null && model?.Name != onlyNode) { data.MeshCount--; continue; }   // 部品 : そのノードの物だけ
            if (model == null) data.Warnings.Add($"mesh '{geometry.Name}' is not attached to a node : placed at the origin");
            var matrix = model == null ? Matrix4x4.Identity :
                         onlyNode != null ? FbxTransforms.Geometric(model) :   // 部品はノードの空間 (ノードの位置はエンティティの Transform が持つ)
                         FbxTransforms.Geometric(model) * FbxTransforms.Global(model, globals);
            var toEngine = matrix * axes * Matrix4x4.CreateScale(unit);
            Matrix4x4.Invert(matrix * axes, out var inverse);
            var normalMatrix = Matrix4x4.Transpose(inverse);
            var flip = FbxTransforms.Determinant3(matrix) * axisDeterminant < 0;   // 鏡 (行列式が負) なら入れ替える

            var count = positions.Length / 3;
            var local = new Vector3[count];
            for (int i = 0; i < count; i++) local[i] = new Vector3((float)positions[i * 3], (float)positions[i * 3 + 1], (float)positions[i * 3 + 2]);
            var world = local.Select(p => Vector3.Transform(p, toEngine)).ToArray();

            var normals = LayerReader.From(node, "LayerElementNormal", "Normals", "NormalsIndex", 3);
            var uvs = LayerReader.From(node, "LayerElementUV", "UV", "UVIndex", 2);
            var colors = LayerReader.From(node, "LayerElementColor", "Colors", "ColorIndex", 4);
            var materials = MaterialLayer.From(node);
            var modelMaterials = model?.Children.Where(c => c.Child.Kind == "Material" && c.Property == null).Select(c => c.Child).ToList() ?? new List<FbxObject>();

            // 法線が無い時 : 点ごとに面の法線 (Newell) を足す (FBX の空間で)
            Vector3[]? smooth = null;
            if (normals == null)
            {
                smooth = new Vector3[count];
                var corners = new List<int>();
                foreach (var raw in polygonIndex)
                {
                    corners.Add(raw < 0 ? ~raw : raw);
                    if (raw >= 0) continue;
                    var n = newell(corners.Select(c => local[Math.Min(c, count - 1)]).ToArray());
                    foreach (var c in corners) if (c < count) smooth[c] += n;
                    corners.Clear();
                }
            }

            var polygonCorners = new List<(int ControlPoint, int PolygonVertex)>();
            var polygon = 0;
            var triangles = new List<int>();
            for (int pv = 0; pv < polygonIndex.Length; pv++)
            {
                var raw = polygonIndex[pv];
                polygonCorners.Add((raw < 0 ? ~raw : raw, pv));
                if (raw >= 0) continue;
                // 角が全部範囲の中か (FbxAsset が確かめているが、ここでも落ちないように)
                if (polygonCorners.Count >= 3 && polygonCorners.All(c => c.ControlPoint < count))
                {
                    var materialSlot = materials?.Get(polygon) ?? 0;
                    var material = materialFor(materialSlot >= 0 && materialSlot < modelMaterials.Count ? modelMaterials[materialSlot] : null);
                    var target = perMaterial[material];
                    var cornerIndex = new uint[polygonCorners.Count];
                    for (int c = 0; c < polygonCorners.Count; c++)
                    {
                        var (cp, polygonVertex) = polygonCorners[c];
                        var n = normals != null ? normals.Get3(polygonVertex, cp, polygon) : smooth![cp];
                        n = Vector3.TransformNormal(n, normalMatrix);
                        n = n.LengthSquared() > 1e-20f ? Vector3.Normalize(n) : Vector3.UnitY;
                        var uv = uvs != null ? uvs.Get2(polygonVertex, cp, polygon) : Vector2.Zero;
                        uv.Y = 1.0f - uv.Y;   // FBX の V は上向き、DirectX は下向き
                        var color = colors != null ? colors.Get4(polygonVertex, cp, polygon) : Vector4.One;
                        var key = new VertexKey(geometry.Id, cp, n, uv, color);
                        if (!vertexOf.TryGetValue(key, out var index))
                        {
                            index = (uint)data.Positions.Count;
                            vertexOf[key] = index;
                            var p = world[cp];
                            data.Positions.Add(p);
                            data.Normals.Add(n);
                            data.UVs.Add(uv);
                            data.Colors.Add(color);
                            data.Sources.Add((geometry.Id, cp));
                            data.BoundsMin = Vector3.Min(data.BoundsMin, p);
                            data.BoundsMax = Vector3.Max(data.BoundsMax, p);
                        }
                        cornerIndex[c] = index;
                    }
                    triangles.Clear();
                    Triangulate(polygonCorners.Select(c => local[c.ControlPoint]).ToArray(), triangles);
                    for (int t = 0; t < triangles.Count; t += 3)
                    {
                        target.Add(cornerIndex[triangles[t]]);
                        if (flip) { target.Add(cornerIndex[triangles[t + 2]]); target.Add(cornerIndex[triangles[t + 1]]); }
                        else { target.Add(cornerIndex[triangles[t + 1]]); target.Add(cornerIndex[triangles[t + 2]]); }
                    }
                }
                polygonCorners.Clear();
                polygon++;
            }
        }
        for (int m = 0; m < perMaterial.Count; m++)
        {
            if (perMaterial[m].Count == 0) continue;
            data.SubMeshes.Add(new FbxSubMeshData { Material = m, FirstIndex = data.Indices.Count, IndexCount = perMaterial[m].Count });
            data.Indices.AddRange(perMaterial[m]);
        }
        data.ImportMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return data;
    }

    // 頂点をまとめる鍵 (同じメッシュの同じ点で、法線 / UV / 色が同じ角は 1 つ)
    private readonly record struct VertexKey(long Mesh, int ControlPoint, Vector3 Normal, Vector2 UV, Vector4 Color);

    private static Vector3 newell(Vector3[] p)
    {
        var n = Vector3.Zero;
        for (int i = 0; i < p.Length; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Length];
            n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        return n;
    }

    // ---------------------------------------------------------------------
    // 多角形 → 三角形 (耳を切る方法)。戻り値は角の番号 (元の向きのまま)
    // ---------------------------------------------------------------------
    public static void Triangulate(Vector3[] points, List<int> triangles)
    {
        var n = points.Length;
        if (n < 3) return;
        if (n == 3) { triangles.AddRange(new[] { 0, 1, 2 }); return; }
        var normal = newell(points);
        if (normal.LengthSquared() < 1e-24f) { fan(n, triangles); return; }
        normal = Vector3.Normalize(normal);
        // 平面に写す (u, v は normal に垂直。u × v = normal なので、多角形は 2D で反時計回り)
        var u = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY, normal));
        var v = Vector3.Cross(normal, u);
        var p2 = points.Select(p => new Vector2(Vector3.Dot(p, u), Vector3.Dot(p, v))).ToArray();
        var remaining = Enumerable.Range(0, n).ToList();
        static float cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        var guard = 0;
        while (remaining.Count > 3 && guard++ < n * n)
        {
            var clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int ia = remaining[(i + remaining.Count - 1) % remaining.Count], ib = remaining[i], ic = remaining[(i + 1) % remaining.Count];
                Vector2 a = p2[ia], b = p2[ib], c = p2[ic];
                if (cross(a, b, c) <= 1e-12f) continue;   // 凹んだ角 (耳ではない)
                var inside = false;
                foreach (var k in remaining)
                {
                    if (k == ia || k == ib || k == ic) continue;
                    var q = p2[k];
                    if (cross(a, b, q) >= 0 && cross(b, c, q) >= 0 && cross(c, a, q) >= 0) { inside = true; break; }
                }
                if (inside) continue;
                triangles.Add(ia); triangles.Add(ib); triangles.Add(ic);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped) break;   // 壊れた多角形 (自分と交差など) : 残りは扇で
        }
        if (remaining.Count == 3) { triangles.Add(remaining[0]); triangles.Add(remaining[1]); triangles.Add(remaining[2]); }
        else if (remaining.Count > 3) for (int i = 1; i + 1 < remaining.Count; i++) { triangles.Add(remaining[0]); triangles.Add(remaining[i]); triangles.Add(remaining[i + 1]); }
    }

    private static void fan(int n, List<int> triangles)
    {
        for (int i = 1; i + 1 < n; i++) { triangles.Add(0); triangles.Add(i); triangles.Add(i + 1); }
    }

    // ---------------------------------------------------------------------
    // 層を読む (LayerElementNormal / UV / Color)
    // ---------------------------------------------------------------------
    private sealed class LayerReader
    {
        private double[] _values = Array.Empty<double>();
        private int[]? _index;
        private int _components;
        private string _mapping = "";

        public static LayerReader? From(FbxNode geometry, string layerName, string valuesName, string indexName, int components)
        {
            // 層が複数ある時は最初の物 (番号 0) を使う
            var layer = geometry.ChildrenNamed(layerName).OrderBy(l => l[0] is int i ? i : 0).FirstOrDefault();
            if (layer == null) return null;
            var values = layer.Child(valuesName)?[0] switch
            {
                double[] d => d,
                float[] f => f.Select(x => (double)x).ToArray(),
                _ => null,
            };
            if (values == null || values.Length < components) return null;
            var reference = layer.Child("ReferenceInformationType")?[0] as string ?? "Direct";
            return new LayerReader
            {
                _values = values,
                _components = components,
                _mapping = layer.Child("MappingInformationType")?[0] as string ?? "ByPolygonVertex",
                _index = reference is "IndexToDirect" or "Index" ? layer.ChildArray<int>(indexName) : null,
            };
        }

        private int slot(int polygonVertex, int controlPoint, int polygon)
        {
            var s = _mapping switch
            {
                "ByControlPoint" or "ByVertice" or "ByVertex" => controlPoint,
                "ByPolygon" => polygon,
                "AllSame" => 0,
                _ => polygonVertex,
            };
            if (_index != null) s = s < _index.Length ? _index[s] : -1;
            return s >= 0 && (s + 1) * _components <= _values.Length ? s : -1;
        }

        public Vector3 Get3(int pv, int cp, int polygon)
        {
            var s = slot(pv, cp, polygon);
            return s < 0 ? Vector3.Zero : new Vector3((float)_values[s * 3], (float)_values[s * 3 + 1], (float)_values[s * 3 + 2]);
        }

        public Vector2 Get2(int pv, int cp, int polygon)
        {
            var s = slot(pv, cp, polygon);
            return s < 0 ? Vector2.Zero : new Vector2((float)_values[s * 2], (float)_values[s * 2 + 1]);
        }

        public Vector4 Get4(int pv, int cp, int polygon)
        {
            var s = slot(pv, cp, polygon);
            return s < 0 ? Vector4.One : new Vector4((float)_values[s * 4], (float)_values[s * 4 + 1], (float)_values[s * 4 + 2], (float)_values[s * 4 + 3]);
        }
    }

    private sealed class MaterialLayer
    {
        private int[] _materials = Array.Empty<int>();
        private bool _allSame;

        public static MaterialLayer? From(FbxNode geometry)
        {
            var layer = geometry.ChildrenNamed("LayerElementMaterial").OrderBy(l => l[0] is int i ? i : 0).FirstOrDefault();
            var materials = layer?.ChildArray<int>("Materials");
            if (layer == null || materials == null || materials.Length == 0) return null;
            return new MaterialLayer { _materials = materials, _allSame = (layer.Child("MappingInformationType")?[0] as string) == "AllSame" || materials.Length == 1 };
        }

        public int Get(int polygon) => _allSame ? _materials[0] : polygon < _materials.Length ? _materials[polygon] : 0;
    }

    // ---------------------------------------------------------------------
    // マテリアル
    // ---------------------------------------------------------------------
    private static FbxMaterialData readMaterial(FbxScene scene, FbxObject material, string fbxFile, Func<string, string?>? findTexture, string? embeddedFolder, List<string> warnings)
    {
        var props = FbxScene.Properties70(material.Node);
        Vector3 vector(string name, Vector3 fallback) => props.TryGetValue(name, out var p) && p.Values.Length >= 3 ? p.Vector : fallback;
        float number(string name, float fallback) => props.TryGetValue(name, out var p) && p.Values.Length >= 1 ? (float)p.Number() : fallback;
        var result = new FbxMaterialData { Name = material.Name };
        var diffuse = vector("DiffuseColor", vector("Diffuse", new Vector3(0.8f))) * number("DiffuseFactor", 1.0f);
        // 不透明度 : Opacity があればそれ。無ければ 1 − TransparencyFactor × TransparentColor (Maya / Blender の書き方)
        var alpha = props.ContainsKey("Opacity") ? number("Opacity", 1) :
                    1.0f - number("TransparencyFactor", 0) * (props.ContainsKey("TransparentColor") ? (vector("TransparentColor", Vector3.One) is var t ? (t.X + t.Y + t.Z) / 3 : 1) : 1);
        alpha = Math.Clamp(alpha, 0, 1);
        result.Transparent = alpha < 0.999f;
        // 粗さ : 鋭さの指数 n → sqrt(2 / (n + 2)) (OBJ / PMX の読み込みと同じ。0.3 〜 1)
        var shininess = number("ShininessExponent", number("Shininess", 0));
        result.Roughness = shininess > 0 ? Math.Clamp(MathF.Sqrt(2.0f / (shininess + 2.0f)), 0.3f, 1.0f) : 0.6f;
        // 色のテクスチャ : DiffuseColor に繋がっている Texture
        var texture = material.Children.Where(c => c.Child.Kind == "Texture")
            .OrderBy(c => c.Property == "DiffuseColor" ? 0 : c.Property == "Diffuse" ? 1 : c.Property != null && c.Property.Contains("color", StringComparison.OrdinalIgnoreCase) ? 2 : 9)
            .FirstOrDefault(c => c.Property is "DiffuseColor" or "Diffuse" || (c.Property ?? "").Contains("color", StringComparison.OrdinalIgnoreCase)).Child;
        if (texture != null)
        {
            var (path, name, embedded) = findTextureFile(texture, fbxFile, findTexture, embeddedFolder);
            result.TextureName = name;
            result.TexturePath = path;
            result.TextureEmbedded = embedded;
            if (path == null) warnings.Add($"material '{material.Name}' : texture '{name}' was not found (drawn with the color only)");
        }
        // テクスチャがあれば色は白 (テクスチャそのまま。OBJ / PMX の読み込みと同じ)。不透明度は残す
        result.Color = result.TexturePath != null ? new Vector4(1, 1, 1, alpha) : new Vector4(diffuse, alpha);
        return result;
    }

    private static (string? Path, string Name, bool Embedded) findTextureFile(FbxObject texture, string fbxFile, Func<string, string?>? findTexture, string? embeddedFolder)
    {
        var relative = texture.Node.Child("RelativeFilename")?[0] as string ?? "";
        var absolute = texture.Node.Child("FileName")?[0] as string ?? "";
        var name = Path.GetFileName((relative.Length > 0 ? relative : absolute).Replace('\\', '/'));
        if (name.Length == 0) name = texture.Name;
        var folder = Path.GetDirectoryName(Path.GetFullPath(fbxFile)) ?? "";
        // 1. FBX に入っている (Video の Content)
        var video = texture.Children.Select(c => c.Child).FirstOrDefault(c => c.Kind == "Video");
        if (video?.Node.Child("Content")?[0] is byte[] { Length: > 0 } content)
        {
            var target = embeddedFolder ?? Path.Combine(Path.GetTempPath(), "GameEngineFbxTextures");
            try
            {
                Directory.CreateDirectory(target);
                var file = Path.Combine(target, $"{Path.GetFileNameWithoutExtension(fbxFile)}_{name}");
                if (!File.Exists(file) || new FileInfo(file).Length != content.Length) File.WriteAllBytes(file, content);
                return (file, name, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return (resolveExternal(relative, absolute, name, folder, findTexture), name, false);
    }

    // FBX からの相対 / 書いてある絶対のパス / FBX の隣 / 名前で探す
    private static string? resolveExternal(string relative, string absolute, string name, string folder, Func<string, string?>? findTexture)
    {
        foreach (var candidate in new[] { relative.Length > 0 ? Path.Combine(folder, relative.Replace('\\', '/')) : "", absolute, Path.Combine(folder, name) })
            if (candidate.Length > 0 && File.Exists(candidate)) return Path.GetFullPath(candidate);
        return findTexture?.Invoke(name);
    }

    /// <summary>
    /// FBX が使うテクスチャ (書き出しの時に一緒に入れる物を調べる。AN2)。File = 見つかったファイル (FBX に入っている物 / 見つからない物は null)
    /// </summary>
    public static List<(string Name, string? File, bool Embedded)> TextureReferences(FbxScene scene, string fbxFile, Func<string, string?>? findTexture = null)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(fbxFile)) ?? "";
        var result = new List<(string, string?, bool)>();
        foreach (var texture in scene.OfKind("Texture"))
        {
            var relative = texture.Node.Child("RelativeFilename")?[0] as string ?? "";
            var absolute = texture.Node.Child("FileName")?[0] as string ?? "";
            var name = Path.GetFileName((relative.Length > 0 ? relative : absolute).Replace('\\', '/'));
            var embedded = texture.Children.Any(c => c.Child.Kind == "Video" && c.Child.Node.Child("Content")?[0] is byte[] { Length: > 0 });
            result.Add((name, embedded ? null : resolveExternal(relative, absolute, name, folder, findTexture), embedded));
        }
        return result;
    }
}

// ==========================================================================
// FBX のノードの行列 (行ベクトル。v' = v × M)
// ==========================================================================
public static class FbxTransforms
{
    private static Matrix4x4 euler(Vector3 degrees, int order)
    {
        var x = Matrix4x4.CreateRotationX(degrees.X * MathF.PI / 180);
        var y = Matrix4x4.CreateRotationY(degrees.Y * MathF.PI / 180);
        var z = Matrix4x4.CreateRotationZ(degrees.Z * MathF.PI / 180);
        // order : 0 XYZ / 1 XZY / 2 YZX / 3 YXZ / 4 ZXY / 5 ZYX (最初の軸から回す。行ベクトルなので左から掛ける)
        return order switch
        {
            1 => x * z * y,
            2 => y * z * x,
            3 => y * x * z,
            4 => z * x * y,
            5 => z * y * x,
            _ => x * y * z,
        };
    }

    /// <summary>ノード自身の行列 (親を含まない)。translation / rotation / scaling を渡すと Lcl の値の代わりに使う (アニメーション。AN3)</summary>
    public static Matrix4x4 Local(FbxObject model, Vector3? translation = null, Vector3? rotation = null, Vector3? scaling = null)
    {
        var props = FbxScene.Properties70(model.Node);
        Vector3 v(string name, Vector3 fallback) => props.TryGetValue(name, out var p) && p.Values.Length >= 3 ? p.Vector : fallback;
        var order = props.TryGetValue("RotationOrder", out var o) ? (int)o.Number() : 0;
        var t = Matrix4x4.CreateTranslation(translation ?? v("Lcl Translation", Vector3.Zero));
        var r = euler(rotation ?? v("Lcl Rotation", Vector3.Zero), order);
        var s = Matrix4x4.CreateScale(scaling ?? v("Lcl Scaling", Vector3.One));
        var rotationOffset = Matrix4x4.CreateTranslation(v("RotationOffset", Vector3.Zero));
        var rotationPivot = Matrix4x4.CreateTranslation(v("RotationPivot", Vector3.Zero));
        var scalingOffset = Matrix4x4.CreateTranslation(v("ScalingOffset", Vector3.Zero));
        var scalingPivot = Matrix4x4.CreateTranslation(v("ScalingPivot", Vector3.Zero));
        var pre = euler(v("PreRotation", Vector3.Zero), 0);
        var post = euler(v("PostRotation", Vector3.Zero), 0);
        Matrix4x4.Invert(rotationPivot, out var rotationPivotInverse);
        Matrix4x4.Invert(scalingPivot, out var scalingPivotInverse);
        Matrix4x4.Invert(post, out var postInverse);
        return scalingPivotInverse * s * scalingPivot * scalingOffset * rotationPivotInverse * postInverse * r * pre * rotationPivot * rotationOffset * t;
    }

    /// <summary>世界の行列 (親の Model をたどる。cache に覚える)</summary>
    public static Matrix4x4 Global(FbxObject model, Dictionary<long, Matrix4x4> cache, int depth = 0)
    {
        if (cache.TryGetValue(model.Id, out var known)) return known;
        var parent = model.Parents.Select(p => p.Parent).FirstOrDefault(p => p.Kind == "Model" && p != model);
        var result = Local(model) * (parent != null && depth < 256 ? Global(parent, cache, depth + 1) : Matrix4x4.Identity);
        cache[model.Id] = result;
        return result;
    }

    /// <summary>メッシュだけに掛かる行列 (GeometricTranslation / Rotation / Scaling。子には伝わらない)</summary>
    public static Matrix4x4 Geometric(FbxObject model)
    {
        var props = FbxScene.Properties70(model.Node);
        Vector3 v(string name, Vector3 fallback) => props.TryGetValue(name, out var p) && p.Values.Length >= 3 ? p.Vector : fallback;
        return Matrix4x4.CreateScale(v("GeometricScaling", Vector3.One)) * euler(v("GeometricRotation", Vector3.Zero), 0) * Matrix4x4.CreateTranslation(v("GeometricTranslation", Vector3.Zero));
    }

    /// <summary>
    /// FBX の軸 → エンジン (左手系 / +X 右 / +Y 上 / +Z 奥)。FBX の (横, 上, 前) を右手系の (X, Y, Z) と見て、Z を反転して左手系にする
    /// determinant : 負なら鏡 (右手系 → 左手系)
    /// </summary>
    public static Matrix4x4 AxisMatrix(FbxScene scene, out float determinant)
    {
        var (up, upSign, front, frontSign, coord, coordSign) = scene.Axes;
        up = Math.Clamp(up, 0, 2); front = Math.Clamp(front, 0, 2); coord = Math.Clamp(coord, 0, 2);
        if (up == front || up == coord || front == coord) { up = 1; front = 2; coord = 0; upSign = 1; frontSign = 1; coordSign = 1; }
        var m = new float[3, 3];
        m[coord, 0] = coordSign < 0 ? -1 : 1;
        m[up, 1] = upSign < 0 ? -1 : 1;
        m[front, 2] = frontSign < 0 ? 1 : -1;   // 右手系の前 (手前) → 左手系の奥は反対
        var matrix = new Matrix4x4(m[0, 0], m[0, 1], m[0, 2], 0, m[1, 0], m[1, 1], m[1, 2], 0, m[2, 0], m[2, 1], m[2, 2], 0, 0, 0, 0, 1);
        determinant = Determinant3(matrix);
        return matrix;
    }

    public static float Determinant3(Matrix4x4 m) =>
        m.M11 * (m.M22 * m.M33 - m.M23 * m.M32) - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31) + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
}
