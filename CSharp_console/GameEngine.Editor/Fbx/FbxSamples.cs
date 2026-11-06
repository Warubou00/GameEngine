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

    /// <summary>メッシュ (Geometry)。法線 = 面ごと (ByPolygonVertex / Direct)、UV = 面の向きで投影 (ByPolygonVertex / IndexToDirect)</summary>
    private static FbxNode GeometryNode(long id, string name, MeshData mesh, int[]? polygonMaterials = null)
    {
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
                var p = mesh.Positions[polygon[i]] * 0.01f;
                var uv = axis == 0 ? new Vector2(p.Z, p.Y) : axis == 1 ? new Vector2(p.X, p.Z) : new Vector2(p.X, p.Y);
                uvs.AddRange(new double[] { uv.X, uv.Y });
            }
        }
        var corners = indices.Count;
        return Object("Geometry", id, name, "Mesh",
            Node("Properties70"),
            Node("GeometryVersion", 124),
            Node("Vertices", vertices),
            Node("PolygonVertexIndex", indices.ToArray()),
            Node("LayerElementNormal", 0).With(
                Node("Version", 102), Node("Name", ""), Node("MappingInformationType", "ByPolygonVertex"), Node("ReferenceInformationType", "Direct"),
                Node("Normals", normals.ToArray())),
            Node("LayerElementUV", 0).With(
                Node("Version", 101), Node("Name", "UVMap"), Node("MappingInformationType", "ByPolygonVertex"), Node("ReferenceInformationType", "IndexToDirect"),
                Node("UV", uvs.ToArray()), Node("UVIndex", Enumerable.Range(0, corners).ToArray())),
            Node("LayerElementMaterial", 0).With(
                Node("Version", 101), Node("Name", ""),
                Node("MappingInformationType", polygonMaterials == null ? "AllSame" : "ByPolygon"), Node("ReferenceInformationType", "IndexToDirect"),
                Node("Materials", polygonMaterials ?? new[] { 0 })),
            Node("Layer", 0).With(
                Node("Version", 100),
                Node("LayerElement").With(Node("Type", "LayerElementNormal"), Node("TypedIndex", 0)),
                Node("LayerElement").With(Node("Type", "LayerElementUV"), Node("TypedIndex", 0)),
                Node("LayerElement").With(Node("Type", "LayerElementMaterial"), Node("TypedIndex", 0))));
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

    private static FbxNode Document(int version, string creator, IEnumerable<(string Type, int Count)> definitions, List<FbxNode> objects, List<FbxNode> connections, string? take, double takeSeconds)
    {
        var top = Node("root").With(
            Node("FBXHeaderExtension").With(
                Node("FBXHeaderVersion", 1003),
                Node("FBXVersion", version),
                Node("Creator", creator)),
            Node("GlobalSettings").With(
                Node("Version", 1000),
                Properties70(
                    P("UpAxis", "int", "Integer", "", 1), P("UpAxisSign", "int", "Integer", "", 1),
                    P("FrontAxis", "int", "Integer", "", 2), P("FrontAxisSign", "int", "Integer", "", 1),
                    P("CoordAxis", "int", "Integer", "", 0), P("CoordAxisSign", "int", "Integer", "", 1),
                    P("OriginalUpAxis", "int", "Integer", "", 1), P("OriginalUpAxisSign", "int", "Integer", "", 1),
                    P("UnitScaleFactor", "double", "Number", "", 1.0), P("OriginalUnitScaleFactor", "double", "Number", "", 1.0),
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
    // Arm (スキン)
    // ---------------------------------------------------------------------
    public static readonly string[] ArmBones = { "Arm_Root", "Arm_Mid", "Arm_Tip", "Arm_End" };

    public static byte[] Arm(ArmPart part, bool compress = true, int version = 7400)
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
            var mesh = Cylinder(20, 300, 12, 6);
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
                    Node("Transform", Translation(new Vector3(0, -100 * b, 0))),     // メッシュ → 骨 (バインドの時)
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
