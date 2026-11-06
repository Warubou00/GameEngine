using System.Numerics;
using GameEngine.Fbx;
using static GameEngine.Editor.FbxWriter;

namespace GameEngine.Editor;

// ==========================================================================
// 自作の FBX のサンプル (AN1)。本物の DCC ツール (Maya / Blender) の書き出しと同じ作りにする :
//   FBXHeaderExtension / GlobalSettings (Y 上・右手系・cm・30 fps) / Definitions / Objects / Connections / Takes
//   単位は cm (FBX の既定)。多角形は右手系で外から見て反時計回り
//
//   Box   : 立方体 (四角形 6 枚、マテリアル 1 つ、法線 / UV)
//   Arm   : 曲がる円柱 (12 角形のふた + 四角形)、骨 4 本 (Root → Mid → Tip → End)、重み付きのスキン、バインドポーズ、
//           Wave = 中と先の骨が Z の周りに振れる 2 秒 (直線の補間)
//   Robot : 階層型 : 箱の部品が骨の子 (スキンなし)。骨 7 本 + 根の Null。
//           Walk = 足と腕が振れ、腰が上下する 1 秒 (3 次の補間)
//   Crate : 箱 (AN2)。上下の面はテクスチャ (FBX からの相対パス ../../Textures/Noise/fbm.png)、横は色。マテリアルは多角形ごと
//           (ByPolygon)、法線は IndexToDirect
//   Panel : 星の形の板 (AN2)。Z 上 / m の FBX (軸と単位の変換)、凹んだ 10 角形 (耳を切る方法)、頂点の色 (ByControlPoint)、法線なし
//           (点ごとに作る)、FBX に入っているテクスチャ (市松模様の BMP)、ノードの PreRotation / RotationPivot / Geometric
//   1 つの物から .mod / .skl / .anm を書き分ける (同じ骨の名前)
// ==========================================================================
public static class FbxSamples
{
    public const string Folder = "Assets/Samples/Fbx";

    /// <summary>サンプルを全部 folder (フルパス) に書く。戻り値 = 書いたファイルの名前</summary>
    public static List<string> WriteAll(string folder)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        void write(string name, byte[] data) { File.WriteAllBytes(Path.Combine(folder, name), data); written.Add(name); }
        write("Box.mod.fbx", Box(compress: false));
        write("Arm.mod.fbx", Arm(ArmPart.Model));
        write("Arm.skl.fbx", Arm(ArmPart.Skeleton));
        write("Arm_Wave.anm.fbx", Arm(ArmPart.Animation));
        write("Robot.mod.fbx", Robot(ArmPart.Model));
        write("Robot.skl.fbx", Robot(ArmPart.Skeleton));
        write("Robot_Walk.anm.fbx", Robot(ArmPart.Animation));
        write("Crate.mod.fbx", Crate());
        write("Panel.mod.fbx", Panel());
        return written;
    }

    public enum ArmPart { Model, Skeleton, Animation }

    // ---------------------------------------------------------------------
    // 形
    // ---------------------------------------------------------------------
    public sealed class MeshData
    {
        public List<Vector3> Positions = new();
        public List<int[]> Polygons = new();

        /// <summary>多角形の向きを外向きにそろえる (面の法線が 中心 → 面の中心 と同じ向き)</summary>
        public void OrientOutward(Vector3 center)
        {
            for (int i = 0; i < Polygons.Count; i++)
            {
                var p = Polygons[i];
                var normal = Newell(p);
                var faceCenter = p.Aggregate(Vector3.Zero, (a, k) => a + Positions[k]) / p.Length;
                if (Vector3.Dot(normal, faceCenter - center) < 0) Polygons[i] = p.Reverse().ToArray();
            }
        }

        public Vector3 Newell(int[] polygon)
        {
            var n = Vector3.Zero;
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = Positions[polygon[i]];
                var b = Positions[polygon[(i + 1) % polygon.Length]];
                n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
            }
            return Vector3.Normalize(n);
        }
    }

    public static MeshData BoxMesh(Vector3 center, Vector3 size)
    {
        var m = new MeshData();
        var h = size * 0.5f;
        for (int i = 0; i < 8; i++)
            m.Positions.Add(center + new Vector3((i & 1) != 0 ? h.X : -h.X, (i & 2) != 0 ? h.Y : -h.Y, (i & 4) != 0 ? h.Z : -h.Z));
        m.Polygons.AddRange(new[] { new[] { 0, 1, 3, 2 }, new[] { 4, 6, 7, 5 }, new[] { 0, 4, 5, 1 }, new[] { 2, 3, 7, 6 }, new[] { 0, 2, 6, 4 }, new[] { 1, 5, 7, 3 } });
        m.OrientOutward(center);
        return m;
    }

    public static MeshData Cylinder(float radius, float height, int sides, int rings)
    {
        var m = new MeshData();
        for (int r = 0; r <= rings; r++)
            for (int s = 0; s < sides; s++)
            {
                var a = s * MathF.Tau / sides;
                m.Positions.Add(new Vector3(radius * MathF.Cos(a), height * r / rings, radius * MathF.Sin(a)));
            }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
                m.Polygons.Add(new[] { r * sides + s, r * sides + (s + 1) % sides, (r + 1) * sides + (s + 1) % sides, (r + 1) * sides + s });
        m.Polygons.Add(Enumerable.Range(0, sides).ToArray());                          // 下のふた (12 角形)
        m.Polygons.Add(Enumerable.Range(rings * sides, sides).Reverse().ToArray());    // 上のふた
        // 向き : 横は軸から外へ、ふたは上下へ
        for (int i = 0; i < m.Polygons.Count; i++)
        {
            var p = m.Polygons[i];
            var c = p.Aggregate(Vector3.Zero, (acc, k) => acc + m.Positions[k]) / p.Length;
            var outward = i < rings * sides ? new Vector3(c.X, 0, c.Z) : new Vector3(0, c.Y < height / 2 ? -1 : 1, 0);
            if (Vector3.Dot(m.Newell(p), outward) < 0) m.Polygons[i] = p.Reverse().ToArray();
        }
        return m;
    }

    // ---------------------------------------------------------------------
    // ノード
    // ---------------------------------------------------------------------
    private sealed class Ids
    {
        private long _next = 1_000_000_000;
        public long Next() => _next += 7;
    }

    private static double[] Translation(Vector3 t) => new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, t.X, t.Y, t.Z, 1 };

    private static FbxNode Properties70(params FbxNode[] p) => Node("Properties70").With(p);

    private static FbxNode ModelNode(long id, string name, string subKind, Vector3 translation, Vector3 rotation = default) =>
        Object("Model", id, name, subKind,
            Node("Version", 232),
            Properties70(
                P("RotationOrder", "enum", "", "", 0),
                P("InheritType", "enum", "", "", 1),
                P3("Lcl Translation", "Lcl Translation", translation),
                P3("Lcl Rotation", "Lcl Rotation", rotation),
                P3("Lcl Scaling", "Lcl Scaling", Vector3.One)),
            Node("Shading", true),
            Node("Culling", "CullingOff"));

    private static FbxNode BoneAttribute(long id, string name) =>
        Object("NodeAttribute", id, name, "LimbNode", Properties70(P("Size", "double", "Number", "", 33.0)), Node("TypeFlags", "Skeleton"));

    private static FbxNode MaterialNode(long id, string name, Vector3 diffuse) =>
        Object("Material", id, name, "",
            Node("Version", 102),
            Node("ShadingModel", "lambert"),
            Node("MultiLayer", 0),
            Properties70(
                P3("DiffuseColor", "Color", diffuse),
                P("DiffuseFactor", "Number", "", "A", 1.0),
                P3("AmbientColor", "Color", Vector3.Zero),
                P3("EmissiveColor", "Color", Vector3.Zero)));

    /// <summary>メッシュの書き方の選び方 (AN2 : 層の並び方 / 引き方を変えて読む側を確かめる)</summary>
    public sealed class GeometryOptions
    {
        public bool Normals = true;            // false : 法線を書かない (読む側が作る)
        public bool IndexedNormals;            // true : 法線を IndexToDirect (同じ法線は 1 つ)
        public Vector4[]? ControlPointColors;  // 頂点の色 (ByControlPoint / Direct)
        public float UvScale = 0.01f;          // UV = 位置 × この値 (cm の FBX は 0.01 = 1 m で 1 回。m の FBX は 1)
    }

    /// <summary>メッシュ (Geometry)。法線 = 面ごと (ByPolygonVertex / Direct)、UV = 面の向きで投影 (ByPolygonVertex / IndexToDirect)</summary>
    private static FbxNode GeometryNode(long id, string name, MeshData mesh, int[]? polygonMaterials = null, GeometryOptions? options = null)
    {
        options ??= new GeometryOptions();
        var vertices = mesh.Positions.SelectMany(p => new double[] { p.X, p.Y, p.Z }).ToArray();
        var indices = new List<int>();
        var normals = new List<double>();
        var uvs = new List<double>();
        foreach (var polygon in mesh.Polygons)
        {
            var n = mesh.Newell(polygon);
            var axis = MathF.Abs(n.X) > MathF.Abs(n.Y) && MathF.Abs(n.X) > MathF.Abs(n.Z) ? 0 : MathF.Abs(n.Y) > MathF.Abs(n.Z) ? 1 : 2;
            for (int i = 0; i < polygon.Length; i++)
            {
                indices.Add(i == polygon.Length - 1 ? ~polygon[i] : polygon[i]);
                normals.AddRange(new double[] { n.X, n.Y, n.Z });
                var p = mesh.Positions[polygon[i]] * options.UvScale;
                var uv = axis == 0 ? new Vector2(p.Z, p.Y) : axis == 1 ? new Vector2(p.X, p.Z) : new Vector2(p.X, p.Y);
                uvs.AddRange(new double[] { uv.X, uv.Y });
            }
        }
        var corners = indices.Count;
        FbxNode? normalLayer = null;
        if (options.Normals && !options.IndexedNormals)
            normalLayer = Node("LayerElementNormal", 0).With(
                Node("Version", 102), Node("Name", ""), Node("MappingInformationType", "ByPolygonVertex"), Node("ReferenceInformationType", "Direct"),
                Node("Normals", normals.ToArray()));
        else if (options.Normals)
        {
            // 同じ法線は 1 つにして番号で引く
            var unique = new List<Vector3>();
            var normalIndex = new List<int>();
            for (int i = 0; i < normals.Count; i += 3)
            {
                var n = new Vector3((float)normals[i], (float)normals[i + 1], (float)normals[i + 2]);
                var k = unique.FindIndex(u => Vector3.DistanceSquared(u, n) < 1e-10f);
                if (k < 0) { k = unique.Count; unique.Add(n); }
                normalIndex.Add(k);
            }
            normalLayer = Node("LayerElementNormal", 0).With(
                Node("Version", 102), Node("Name", ""), Node("MappingInformationType", "ByPolygonVertex"), Node("ReferenceInformationType", "IndexToDirect"),
                Node("Normals", unique.SelectMany(u => new double[] { u.X, u.Y, u.Z }).ToArray()), Node("NormalsIndex", normalIndex.ToArray()));
        }
        var colorLayer = options.ControlPointColors == null ? null : Node("LayerElementColor", 0).With(
            Node("Version", 101), Node("Name", "Col"), Node("MappingInformationType", "ByControlPoint"), Node("ReferenceInformationType", "Direct"),
            Node("Colors", options.ControlPointColors.SelectMany(c => new double[] { c.X, c.Y, c.Z, c.W }).ToArray()));
        var layer = Node("Layer", 0).With(Node("Version", 100));
        if (normalLayer != null) layer.With(Node("LayerElement").With(Node("Type", "LayerElementNormal"), Node("TypedIndex", 0)));
        if (colorLayer != null) layer.With(Node("LayerElement").With(Node("Type", "LayerElementColor"), Node("TypedIndex", 0)));
        layer.With(Node("LayerElement").With(Node("Type", "LayerElementUV"), Node("TypedIndex", 0)),
                   Node("LayerElement").With(Node("Type", "LayerElementMaterial"), Node("TypedIndex", 0)));
        return Object("Geometry", id, name, "Mesh",
            Node("Properties70"),
            Node("GeometryVersion", 124),
            Node("Vertices", vertices),
            Node("PolygonVertexIndex", indices.ToArray()),
            normalLayer,
            colorLayer,
            Node("LayerElementUV", 0).With(
                Node("Version", 101), Node("Name", "UVMap"), Node("MappingInformationType", "ByPolygonVertex"), Node("ReferenceInformationType", "IndexToDirect"),
                Node("UV", uvs.ToArray()), Node("UVIndex", Enumerable.Range(0, corners).ToArray())),
            Node("LayerElementMaterial", 0).With(
                Node("Version", 101), Node("Name", ""),
                Node("MappingInformationType", polygonMaterials == null ? "AllSame" : "ByPolygon"), Node("ReferenceInformationType", "IndexToDirect"),
                Node("Materials", polygonMaterials ?? new[] { 0 })),
            layer);
    }

    /// <summary>曲線 (AnimationCurve)。cubic = 3 次 (自動の傾き)、でなければ直線</summary>
    private static FbxNode CurveNode(long id, double[] seconds, float[] values, bool cubic)
    {
        // KeyAttrFlags : 補間 (0x02 一定 / 0x04 直線 / 0x08 3 次) | 傾き (0x0100 自動) | 0x2000 (時間によらない)。Blender と同じ書き方
        var flags = (cubic ? 0x08 : 0x04) | 0x0100 | 0x2000;
        return Object("AnimationCurve", id, "", "",
            Node("Default", 0.0),
            Node("KeyVer", 4009),
            Node("KeyTime", seconds.Select(FbxScene.Time).ToArray()),
            Node("KeyValueFloat", values),
            Node("KeyAttrFlags", new[] { flags }),
            Node("KeyAttrDataFloat", new[] { 0f, 0f, 9.419963e-30f, 0f }),
            Node("KeyAttrRefCount", new[] { seconds.Length }));
    }

    /// <summary>軸と単位 (既定 : Y 上 / Z 前 / X 横 / cm)。Z 上の FBX は (2, 1, 1, -1, 0, 1)</summary>
    public readonly record struct AxisSettings(int Up = 1, int UpSign = 1, int Front = 2, int FrontSign = 1, int Coord = 0, int CoordSign = 1, double Unit = 1.0);

    private static FbxNode Document(int version, string creator, IEnumerable<(string Type, int Count)> definitions, List<FbxNode> objects, List<FbxNode> connections, string? take, double takeSeconds, AxisSettings? axes = null)
    {
        // 既定は書いて渡す (record struct の new AxisSettings() は引数の既定を使わず全部 0 になる。以前はそれで UpAxis / 単位が 0 だった)
        var a = axes ?? new AxisSettings(1, 1, 2, 1, 0, 1, 1.0);
        var top = Node("root").With(
            Node("FBXHeaderExtension").With(
                Node("FBXHeaderVersion", 1003),
                Node("FBXVersion", version),
                Node("Creator", creator)),
            Node("GlobalSettings").With(
                Node("Version", 1000),
                Properties70(
                    P("UpAxis", "int", "Integer", "", a.Up), P("UpAxisSign", "int", "Integer", "", a.UpSign),
                    P("FrontAxis", "int", "Integer", "", a.Front), P("FrontAxisSign", "int", "Integer", "", a.FrontSign),
                    P("CoordAxis", "int", "Integer", "", a.Coord), P("CoordAxisSign", "int", "Integer", "", a.CoordSign),
                    P("OriginalUpAxis", "int", "Integer", "", a.Up), P("OriginalUpAxisSign", "int", "Integer", "", a.UpSign),
                    P("UnitScaleFactor", "double", "Number", "", a.Unit), P("OriginalUnitScaleFactor", "double", "Number", "", a.Unit),
                    P("TimeMode", "enum", "", "", 6), P("TimeSpanStart", "KTime", "Time", "", 0L),
                    P("TimeSpanStop", "KTime", "Time", "", FbxScene.Time(takeSeconds)), P("CustomFrameRate", "double", "Number", "", -1.0))),
            Node("Documents").With(Node("Count", 1), Node("Document", 1_000_000L, "Scene", "Scene").With(Node("RootNode", 0L))),
            Node("References"),
            Node("Definitions").With(new[] { Node("Version", 100), Node("Count", definitions.Sum(d => d.Count) + 1),
                Node("ObjectType", "GlobalSettings").With(Node("Count", 1)) }
                .Concat(definitions.Select(d => Node("ObjectType", d.Type).With(Node("Count", d.Count)))).ToArray()),
            Node("Objects").With(objects.ToArray()),
            Node("Connections").With(connections.ToArray()),
            Node("Takes").With(new[] { Node("Current", take ?? "") }.Concat(take == null ? Array.Empty<FbxNode>() : new[]
            {
                Node("Take", take).With(Node("FileName", take + ".tak"),
                    Node("LocalTime", 0L, FbxScene.Time(takeSeconds)), Node("ReferenceTime", 0L, FbxScene.Time(takeSeconds))),
            }).ToArray()));
        return top;
    }

    private static List<(string, int)> Count(List<FbxNode> objects) =>
        objects.GroupBy(o => o.Name).Select(g => (g.Key, g.Count())).ToList();

    private static byte[] Finish(FbxNode document, int version, bool compress) => Write(document.Children, version, compress);

    // ---------------------------------------------------------------------
    // Box
    // ---------------------------------------------------------------------
    public static byte[] Box(bool compress = true, int version = 7400) =>
        MeshModel("Box", BoxMesh(Vector3.Zero, new Vector3(100)), new Vector3(0, 50, 0), "Red", new Vector3(0.8f, 0.15f, 0.1f), compress, version);

    /// <summary>メッシュ 1 つとマテリアル 1 つのモデル (.mod.fbx の中身)</summary>
    public static byte[] MeshModel(string name, MeshData mesh, Vector3 position, string materialName, Vector3 color, bool compress = true, int version = 7400)
    {
        var ids = new Ids();
        long model = ids.Next(), geometry = ids.Next(), material = ids.Next();
        var objects = new List<FbxNode>
        {
            GeometryNode(geometry, name, mesh),
            ModelNode(model, name, "Mesh", position),
            MaterialNode(material, materialName, color),
        };
        var connections = new List<FbxNode> { ConnectOO(model, 0), ConnectOO(geometry, model), ConnectOO(material, model) };
        return Finish(Document(version, "GameEngine FbxSamples (AN1)", Count(objects), objects, connections, null, 0), version, compress);
    }

    /// <summary>平らな格子 (n × n の四角形。大きなメッシュの時間を測る用)。少し波打たせる</summary>
    public static MeshData Grid(int n, float size)
    {
        var m = new MeshData();
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
                m.Positions.Add(new Vector3((x / (float)n - 0.5f) * size, MathF.Sin(x * 0.2f) * MathF.Cos(z * 0.2f) * size * 0.02f, (z / (float)n - 0.5f) * size));
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                m.Polygons.Add(new[] { z * (n + 1) + x, (z + 1) * (n + 1) + x, (z + 1) * (n + 1) + x + 1, z * (n + 1) + x + 1 });
        return m;
    }

    // ---------------------------------------------------------------------
    // Crate (テクスチャ + 多角形ごとのマテリアル + IndexToDirect の法線)
    // ---------------------------------------------------------------------
    public const string CrateTexture = "../../Textures/Noise/fbm.png";   // Assets/Samples/Fbx から見た相対パス

    public static byte[] Crate(bool compress = true, int version = 7400, string texture = CrateTexture)
    {
        var ids = new Ids();
        long model = ids.Next(), geometry = ids.Next(), wood = ids.Next(), paint = ids.Next(), textureId = ids.Next();
        var mesh = BoxMesh(Vector3.Zero, new Vector3(100));
        // 上下 (法線が Y) の面はマテリアル 0 (テクスチャ)、横は 1 (色)
        var polygonMaterials = mesh.Polygons.Select(p => MathF.Abs(mesh.Newell(p).Y) > 0.5f ? 0 : 1).ToArray();
        var objects = new List<FbxNode>
        {
            GeometryNode(geometry, "Crate", mesh, polygonMaterials, new GeometryOptions { IndexedNormals = true }),
            ModelNode(model, "Crate", "Mesh", new Vector3(0, 50, 0)),
            MaterialNode(wood, "Top", new Vector3(0.6f, 0.6f, 0.6f)),
            MaterialNode(paint, "Side", new Vector3(0.15f, 0.35f, 0.8f)),
            Object("Texture", textureId, "TopTexture", "", Node("Type", "TextureVideoClip"), Node("Version", 202),
                Node("TextureName", "TopTexture\0\x01Texture"), Node("FileName", "C:/missing/" + Path.GetFileName(texture)), Node("RelativeFilename", texture)),
        };
        var connections = new List<FbxNode>
        {
            ConnectOO(model, 0), ConnectOO(geometry, model), ConnectOO(wood, model), ConnectOO(paint, model),
            ConnectOP(textureId, wood, "DiffuseColor"),
        };
        return Finish(Document(version, "GameEngine FbxSamples (AN2) : crate", Count(objects), objects, connections, null, 0), version, compress);
    }

    // ---------------------------------------------------------------------
    // Panel (Z 上 / m、凹んだ多角形、頂点の色、法線なし、入っているテクスチャ、ノードの細かい変換)
    // ---------------------------------------------------------------------
    public static MeshData Star(float outer, float inner, float thickness, int points = 5)
    {
        // Z 上の FBX なので、星は XY の面 (地面) に置き、厚みは Z
        var m = new MeshData();
        var n = points * 2;
        for (int layer = 0; layer < 2; layer++)
            for (int i = 0; i < n; i++)
            {
                var a = i * MathF.Tau / n + MathF.PI / 2;
                var r = i % 2 == 0 ? outer : inner;
                m.Positions.Add(new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), layer * thickness));
            }
        m.Polygons.Add(Enumerable.Range(0, n).ToArray());          // 下 (凹んだ 10 角形)
        m.Polygons.Add(Enumerable.Range(n, n).ToArray());          // 上
        for (int i = 0; i < n; i++) m.Polygons.Add(new[] { i, (i + 1) % n, n + (i + 1) % n, n + i });
        // 向き : ふたは厚みの向き、横は中心から外
        for (int i = 0; i < m.Polygons.Count; i++)
        {
            var p = m.Polygons[i];
            var c = p.Aggregate(Vector3.Zero, (acc, k) => acc + m.Positions[k]) / p.Length;
            var outward = i < 2 ? new Vector3(0, 0, i == 0 ? -1 : 1) : new Vector3(c.X, c.Y, 0);
            if (Vector3.Dot(m.Newell(p), outward) < 0) m.Polygons[i] = p.Reverse().ToArray();
        }
        return m;
    }

    /// <summary>市松模様の BMP (24bit、size × size)。FBX に入れるテクスチャ (外部の物を使わずに作る)</summary>
    public static byte[] CheckerBmp(int size = 64, int cell = 8)
    {
        var row = (size * 3 + 3) & ~3;
        var data = new byte[54 + row * size];
        void u32(int at, int v) => BitConverter.TryWriteBytes(data.AsSpan(at), v);
        data[0] = (byte)'B'; data[1] = (byte)'M';
        u32(2, data.Length); u32(10, 54); u32(14, 40); u32(18, size); u32(22, size);
        BitConverter.TryWriteBytes(data.AsSpan(26), (short)1); BitConverter.TryWriteBytes(data.AsSpan(28), (short)24);
        u32(34, row * size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var on = (x / cell + y / cell) % 2 == 0;
                var o = 54 + y * row + x * 3;
                data[o] = on ? (byte)40 : (byte)230; data[o + 1] = on ? (byte)200 : (byte)230; data[o + 2] = on ? (byte)250 : (byte)230;   // BGR : 黄と白
            }
        return data;
    }

    public static byte[] Panel(bool compress = true, int version = 7500)
    {
        var ids = new Ids();
        long model = ids.Next(), geometry = ids.Next(), material = ids.Next(), texture = ids.Next(), video = ids.Next();
        var mesh = Star(1.0f, 0.45f, 0.08f);   // m (UnitScaleFactor = 100)
        var colors = mesh.Positions.Select((p, i) => new Vector4(0.5f + 0.5f * MathF.Cos(i * 0.6f), 0.6f, 0.5f + 0.5f * MathF.Sin(i * 0.6f), 1)).ToArray();
        var node = ModelNode(model, "Panel", "Mesh", new Vector3(0, 0, 0.5f));
        // ノードの細かい変換 : 回す中心 (RotationPivot) と回す前の回転 (PreRotation) と、メッシュだけの位置 (Geometric)。
        // PreRotation は Z の周りに 90° (星の向きが変わるだけ)、Geometric は中心を Z へ少し上げる
        node.Child("Properties70")!.With(
            P3("RotationPivot", "Vector3D", new Vector3(0, 0, 0), ""),
            P3("PreRotation", "Vector3D", new Vector3(0, 0, 90), ""),
            P3("GeometricTranslation", "Vector3D", new Vector3(0, 0, 0.02f), ""));
        var objects = new List<FbxNode>
        {
            GeometryNode(geometry, "Panel", mesh, null, new GeometryOptions { Normals = false, ControlPointColors = colors, UvScale = 1.0f }),
            node,
            MaterialNode(material, "Checker", new Vector3(1, 1, 1)),
            Object("Video", video, "checker.bmp", "Clip", Node("Type", "Clip"), Node("Properties70").With(P("Path", "KString", "XRefUrl", "", "checker.bmp")),
                Node("UseMipMap", 0), Node("Filename", "checker.bmp"), Node("RelativeFilename", "checker.bmp"), Node("Content", CheckerBmp())),
            Object("Texture", texture, "Checker", "", Node("Type", "TextureVideoClip"), Node("Version", 202),
                Node("TextureName", "Checker\0\x01Texture"), Node("FileName", "checker.bmp"), Node("RelativeFilename", "checker.bmp")),
        };
        var connections = new List<FbxNode>
        {
            ConnectOO(model, 0), ConnectOO(geometry, model), ConnectOO(material, model), ConnectOO(video, texture), ConnectOP(texture, material, "DiffuseColor"),
        };
        var zUp = new AxisSettings(Up: 2, UpSign: 1, Front: 1, FrontSign: -1, Coord: 0, CoordSign: 1, Unit: 100);
        return Finish(Document(version, "GameEngine FbxSamples (AN2) : panel", Count(objects), objects, connections, null, 0, zUp), version, compress);
    }

    // ---------------------------------------------------------------------
    // Arm (スキン)
    // ---------------------------------------------------------------------
    public static readonly string[] ArmBones = { "Arm_Root", "Arm_Mid", "Arm_Tip", "Arm_End" };

    /// <param name="sides">円柱の周りの数 / rings : 縦の数 (AN4 : 時間を測る時は細かく)</param>
    public static byte[] Arm(ArmPart part, bool compress = true, int version = 7400, int sides = 12, int rings = 6)
    {
        var ids = new Ids();
        var objects = new List<FbxNode>();
        var connections = new List<FbxNode>();
        // 骨 : 100 cm ずつ上へ
        var bones = ArmBones.Select(_ => ids.Next()).ToArray();
        for (int i = 0; i < bones.Length; i++)
        {
            var attribute = ids.Next();
            objects.Add(ModelNode(bones[i], ArmBones[i], "LimbNode", i == 0 ? Vector3.Zero : new Vector3(0, 100, 0)));
            objects.Add(BoneAttribute(attribute, ArmBones[i]));
            connections.Add(ConnectOO(attribute, bones[i]));
            connections.Add(ConnectOO(bones[i], i == 0 ? 0 : bones[i - 1]));
        }
        if (part == ArmPart.Model)
        {
            long model = ids.Next(), geometry = ids.Next(), material = ids.Next(), skin = ids.Next();
            var mesh = Cylinder(20, 300, sides, rings);
            objects.Add(GeometryNode(geometry, "Arm", mesh));
            objects.Add(ModelNode(model, "Arm", "Mesh", Vector3.Zero));
            objects.Add(MaterialNode(material, "Skin", new Vector3(0.9f, 0.7f, 0.55f)));
            objects.Add(Object("Deformer", skin, "Skin", "Skin", Node("Version", 101), Node("Link_DeformAcuracy", 50.0)));
            connections.AddRange(new[] { ConnectOO(model, 0), ConnectOO(geometry, model), ConnectOO(material, model), ConnectOO(skin, geometry) });
            // 重み : 骨の真ん中 (50 / 150 / 250 cm) からの距離で山の形。足して 1
            for (int b = 0; b < 3; b++)
            {
                var indexes = new List<int>();
                var weights = new List<double>();
                for (int v = 0; v < mesh.Positions.Count; v++)
                {
                    var y = mesh.Positions[v].Y;
                    float tent(int k) => MathF.Max(0, 1 - MathF.Abs(y - (50 + 100 * k)) / 100);
                    var sum = tent(0) + tent(1) + tent(2);
                    var w = tent(b) / sum;
                    if (w > 0) { indexes.Add(v); weights.Add(w); }
                }
                var cluster = ids.Next();
                objects.Add(Object("Deformer", cluster, ArmBones[b], "Cluster",
                    Node("Version", 100), Node("UserData", "", ""),
                    Node("Indexes", indexes.ToArray()), Node("Weights", weights.ToArray()),
                    // Transform = 休みの姿勢のメッシュの世界の行列 (FBX の決まり。メッシュは原点なので単位行列)。
                    // 以前は骨の行列の逆を書いていて、読む側が正しく読むと中の骨の所が 2 m ずれた (AN4 のテストで分かった)
                    Node("Transform", Translation(Vector3.Zero)),
                    Node("TransformLink", Translation(new Vector3(0, 100 * b, 0)))));  // 骨の世界の行列 (バインドの時)
                connections.Add(ConnectOO(cluster, skin));
                connections.Add(ConnectOO(bones[b], cluster));
            }
        }
        if (part != ArmPart.Animation) objects.Add(BindPose(ids.Next(), bones.Select((b, i) => (b, new Vector3(0, 100 * i, 0))).ToList()));
        double take = 0;
        if (part == ArmPart.Animation)
        {
            take = 2.0;
            long stack = ids.Next(), layer = ids.Next();
            objects.Add(Stack(stack, "Wave", take));
            objects.Add(Object("AnimationLayer", layer, "BaseLayer", ""));
            connections.Add(ConnectOO(layer, stack));
            var seconds = new[] { 0.0, 0.5, 1.0, 1.5, 2.0 };
            foreach (var (bone, amount) in new[] { (1, 40f), (2, 60f) })
            {
                long curveNode = ids.Next(), curve = ids.Next();
                objects.Add(Object("AnimationCurveNode", curveNode, "R", "", Properties70(P("d|X", "Number", "", "A", 0.0), P("d|Y", "Number", "", "A", 0.0), P("d|Z", "Number", "", "A", 0.0))));
                objects.Add(CurveNode(curve, seconds, new[] { 0f, amount, 0f, -amount, 0f }, cubic: false));
                connections.Add(ConnectOO(curveNode, layer));
                connections.Add(ConnectOP(curveNode, bones[bone], "Lcl Rotation"));
                connections.Add(ConnectOP(curve, curveNode, "d|Z"));
            }
        }
        var name = part switch { ArmPart.Model => "Arm model", ArmPart.Skeleton => "Arm skeleton", _ => "Arm wave" };
        return Finish(Document(version, $"GameEngine FbxSamples (AN1) : {name}", Count(objects), objects, connections, take > 0 ? "Wave" : null, take), version, compress);
    }

    private static FbxNode BindPose(long id, List<(long Bone, Vector3 World)> bones) =>
        Object("Pose", id, "BindPose", "BindPose", new[] { Node("Type", "BindPose"), Node("Version", 100), Node("NbPoseNodes", bones.Count) }
            .Concat(bones.Select(b => Node("PoseNode").With(Node("Node", b.Bone), Node("Matrix", Translation(b.World))))).ToArray());

    private static FbxNode Stack(long id, string name, double seconds) =>
        Object("AnimationStack", id, name, "", Properties70(
            P("LocalStart", "KTime", "Time", "", 0L), P("LocalStop", "KTime", "Time", "", FbxScene.Time(seconds)),
            P("ReferenceStart", "KTime", "Time", "", 0L), P("ReferenceStop", "KTime", "Time", "", FbxScene.Time(seconds))));

    // ---------------------------------------------------------------------
    // Robot (階層型 : 部品が骨の子)
    // ---------------------------------------------------------------------
    public static readonly (string Name, int Parent, Vector3 Offset)[] RobotBones =
    {
        ("Hips", -1, new Vector3(0, 100, 0)),
        ("Spine", 0, new Vector3(0, 15, 0)),
        ("Head", 1, new Vector3(0, 65, 0)),
        ("ArmL", 1, new Vector3(36, 55, 0)),
        ("ArmR", 1, new Vector3(-36, 55, 0)),
        ("LegL", 0, new Vector3(14, -5, 0)),
        ("LegR", 0, new Vector3(-14, -5, 0)),
    };

    public static byte[] Robot(ArmPart part, bool compress = true, int version = 7500)
    {
        var ids = new Ids();
        var objects = new List<FbxNode>();
        var connections = new List<FbxNode>();
        var root = ids.Next();
        objects.Add(ModelNode(root, "Robot", "Null", Vector3.Zero));
        connections.Add(ConnectOO(root, 0));
        var bones = RobotBones.Select(_ => ids.Next()).ToArray();
        var world = new Vector3[RobotBones.Length];
        for (int i = 0; i < RobotBones.Length; i++)
        {
            var (name, parent, offset) = RobotBones[i];
            var attribute = ids.Next();
            world[i] = (parent < 0 ? Vector3.Zero : world[parent]) + offset;
            objects.Add(ModelNode(bones[i], name, "LimbNode", offset));
            objects.Add(BoneAttribute(attribute, name));
            connections.Add(ConnectOO(attribute, bones[i]));
            connections.Add(ConnectOO(bones[i], parent < 0 ? root : bones[parent]));
        }
        if (part == ArmPart.Model)
        {
            long metal = ids.Next(), accent = ids.Next();
            objects.Add(MaterialNode(metal, "Metal", new Vector3(0.55f, 0.58f, 0.62f)));
            objects.Add(MaterialNode(accent, "Accent", new Vector3(0.95f, 0.55f, 0.1f)));
            // 部品 : (骨, 名前, 骨からの位置, 大きさ, マテリアル)
            var parts = new (int Bone, string Name, Vector3 Offset, Vector3 Size, long Material)[]
            {
                (0, "Pelvis", new Vector3(0, 0, 0), new Vector3(40, 16, 24), metal),
                (1, "Torso", new Vector3(0, 30, 0), new Vector3(56, 60, 30), metal),
                (2, "HeadBox", new Vector3(0, 16, 0), new Vector3(30, 30, 30), accent),
                (3, "ArmLBox", new Vector3(0, -30, 0), new Vector3(12, 60, 12), metal),
                (4, "ArmRBox", new Vector3(0, -30, 0), new Vector3(12, 60, 12), metal),
                (5, "LegLBox", new Vector3(0, -45, 0), new Vector3(16, 90, 16), accent),
                (6, "LegRBox", new Vector3(0, -45, 0), new Vector3(16, 90, 16), accent),
            };
            foreach (var (bone, name, offset, size, material) in parts)
            {
                long model = ids.Next(), geometry = ids.Next();
                objects.Add(GeometryNode(geometry, name, BoxMesh(Vector3.Zero, size)));
                objects.Add(ModelNode(model, name, "Mesh", offset));
                connections.AddRange(new[] { ConnectOO(model, bones[bone]), ConnectOO(geometry, model), ConnectOO(material, model) });
            }
        }
        if (part != ArmPart.Animation) objects.Add(BindPose(ids.Next(), bones.Select((b, i) => (b, world[i])).ToList()));
        double take = 0;
        if (part == ArmPart.Animation)
        {
            take = 1.0;
            long stack = ids.Next(), layer = ids.Next();
            objects.Add(Stack(stack, "Walk", take));
            objects.Add(Object("AnimationLayer", layer, "BaseLayer", ""));
            connections.Add(ConnectOO(layer, stack));
            var seconds = new[] { 0.0, 0.25, 0.5, 0.75, 1.0 };
            void curve(int bone, string property, string channel, string nodeName, float[] values)
            {
                long curveNode = ids.Next(), c = ids.Next();
                objects.Add(Object("AnimationCurveNode", curveNode, nodeName, "", Properties70(P("d|X", "Number", "", "A", 0.0), P("d|Y", "Number", "", "A", 0.0), P("d|Z", "Number", "", "A", 0.0))));
                objects.Add(CurveNode(c, seconds, values, cubic: true));
                connections.Add(ConnectOO(curveNode, layer));
                connections.Add(ConnectOP(curveNode, bones[bone], property));
                connections.Add(ConnectOP(c, curveNode, channel));
            }
            curve(5, "Lcl Rotation", "d|X", "R", new[] { 25f, 0f, -25f, 0f, 25f });    // 左足
            curve(6, "Lcl Rotation", "d|X", "R", new[] { -25f, 0f, 25f, 0f, -25f });   // 右足
            curve(3, "Lcl Rotation", "d|X", "R", new[] { -20f, 0f, 20f, 0f, -20f });   // 左腕 (足と逆)
            curve(4, "Lcl Rotation", "d|X", "R", new[] { 20f, 0f, -20f, 0f, 20f });
            curve(0, "Lcl Translation", "d|Y", "T", new[] { 100f, 96f, 100f, 96f, 100f });   // 腰の上下
        }
        var label = part switch { ArmPart.Model => "Robot model", ArmPart.Skeleton => "Robot skeleton", _ => "Robot walk" };
        return Finish(Document(version, $"GameEngine FbxSamples (AN1) : {label}", Count(objects), objects, connections, take > 0 ? "Walk" : null, take), version, compress);
    }
}
