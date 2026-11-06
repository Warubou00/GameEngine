using System.Diagnostics;

namespace GameEngine.Fbx;

// ==========================================================================
// FBX のアセット (AN1)。ファイルの名前で種類が決まり、中身がその種類として足りているかを確かめる
//   名前.mod.fbx : モデル (メッシュとマテリアル)。メッシュ (Geometry) が 1 つも無い / 多角形が壊れている → エラー
//   名前.skl.fbx : スケルトン。骨 (Model の LimbNode / Root) が 1 本も無い → エラー
//   名前.anm.fbx : アニメーション。AnimationStack が無い / キーのある曲線が骨 (Model) の位置・回転・大きさに繋がっていない → エラー
//   ただの .fbx は種類が分からないのでアセットにしない (名前を変えてもらう)
//   読めた物は Summary (中身の数) を持つ。中を使うのは AN2 (モデル) / AN3 (骨とアニメーション)
// ==========================================================================
public enum FbxAssetType { Model, Skeleton, Animation }

public sealed class FbxAsset
{
    public const string ModelExtension = ".mod.fbx";
    public const string SkeletonExtension = ".skl.fbx";
    public const string AnimationExtension = ".anm.fbx";

    public string Path { get; internal set; }
    public FbxAssetType Type { get; }
    /// <summary>読めなかった / 中身が足りない理由 (読めたら null)</summary>
    public string? Error { get; private set; }
    public bool IsValid => Error == null;
    /// <summary>気を付ける事 (読めるが、使う時に困るかもしれない物)</summary>
    public List<string> Warnings { get; } = new();
    /// <summary>中身の数 (表示用。順番もこのまま出す)</summary>
    public List<(string Label, string Value)> Summary { get; } = new();
    public FbxDocument? Document { get; private set; }
    public FbxScene? Scene { get; private set; }
    public long FileBytes { get; private set; }
    public double LoadMilliseconds { get; private set; }
    /// <summary>読み直した回数 (エディターが表示を作り直す印)</summary>
    public int Version { get; private set; }

    internal FbxAsset(string path, FbxAssetType type)
    {
        Path = path;
        Type = type;
    }

    /// <summary>名前から種類 (.mod.fbx / .skl.fbx / .anm.fbx 以外は null)</summary>
    public static FbxAssetType? TypeOf(string path)
    {
        var name = path.ToLowerInvariant();
        if (name.EndsWith(ModelExtension)) return FbxAssetType.Model;
        if (name.EndsWith(SkeletonExtension)) return FbxAssetType.Skeleton;
        if (name.EndsWith(AnimationExtension)) return FbxAssetType.Animation;
        return null;
    }

    public static string ExtensionOf(FbxAssetType type) => type switch
    {
        FbxAssetType.Model => ModelExtension,
        FbxAssetType.Skeleton => SkeletonExtension,
        _ => AnimationExtension,
    };

    /// <summary>ファイルを読んで確かめる (例外は出さず Error に入れる)</summary>
    public static FbxAsset Load(string path, string file)
    {
        var type = TypeOf(path) ?? throw new ArgumentException($"{path} : name it xxx.mod.fbx / xxx.skl.fbx / xxx.anm.fbx");
        var asset = new FbxAsset(path, type);
        asset.Reload(file);
        return asset;
    }

    internal void Reload(string file)
    {
        Version++;
        Error = null;
        Warnings.Clear();
        Summary.Clear();
        Document = null;
        Scene = null;
        var start = Stopwatch.GetTimestamp();
        try
        {
            var data = File.ReadAllBytes(file);
            FileBytes = data.Length;
            Document = FbxDocument.Read(data);
            Scene = new FbxScene(Document);
            Summary.Add(("FBX", $"{Document.Version / 1000.0:F1} ({(Document.Is64Bit ? "64" : "32")}-bit records)"));
            Error = Type switch
            {
                FbxAssetType.Model => checkModel(Scene),
                FbxAssetType.Skeleton => checkSkeleton(Scene),
                _ => checkAnimation(Scene),
            };
            var (up, upSign, front, frontSign, _, _) = Scene.Axes;
            Summary.Add(("Axes", $"up {(upSign < 0 ? "-" : "+")}{"XYZ"[Math.Clamp(up, 0, 2)]} / front {(frontSign < 0 ? "-" : "+")}{"XYZ"[Math.Clamp(front, 0, 2)]}"));
            Summary.Add(("Unit", $"{Scene.UnitScaleFactor:G4} cm"));
            Summary.Add(("Objects", $"{Scene.Objects.Count} ({Scene.ConnectionCount} connections)"));
            if (Document.CompressedArrays > 0) Summary.Add(("Compressed", $"{Document.CompressedArrays} arrays ({Document.InflatedBytes / 1024.0:F0} KB)"));
            if (Scene.BrokenConnections > 0) Warnings.Add($"{Scene.BrokenConnections} connections point to missing objects (ignored)");
        }
        catch (FbxFormatException ex) { Error = ex.Message; }
        catch (IOException ex) { Error = $"cannot read the file : {ex.Message}"; }
        catch (UnauthorizedAccessException ex) { Error = $"cannot read the file : {ex.Message}"; }
        LoadMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (Error != null) Console.WriteLine($"[FBX] {Path} : {Error}");
        foreach (var warning in Warnings) Console.WriteLine($"[FBX] {Path} : warning : {warning}");
    }

    // ---------------------------------------------------------------------
    // 中身を確かめる (足りなければエラーの文を返す)
    // ---------------------------------------------------------------------

    private string? checkModel(FbxScene scene)
    {
        var geometries = scene.OfKind("Geometry", "Mesh").ToList();
        if (geometries.Count == 0) return $"no mesh (Geometry) : a {ModelExtension} file needs at least one mesh";
        long vertices = 0, polygons = 0, triangles = 0;
        foreach (var g in geometries)
        {
            var positions = g.Node.ChildArray<double>("Vertices");
            var indices = g.Node.ChildArray<int>("PolygonVertexIndex");
            if (positions == null || positions.Length % 3 != 0) return $"mesh '{g.Name}' has no vertices (Vertices)";
            if (indices == null || indices.Length == 0) return $"mesh '{g.Name}' has no polygons (PolygonVertexIndex)";
            if (indices[^1] >= 0) return $"mesh '{g.Name}' : the last polygon is not closed (PolygonVertexIndex must end with a negative index)";
            var count = positions.Length / 3;
            var corners = 0;
            foreach (var raw in indices)
            {
                var index = raw < 0 ? ~raw : raw;
                if (index >= count) return $"mesh '{g.Name}' : vertex index {index} is out of range ({count} vertices)";
                corners++;
                if (raw < 0)
                {
                    if (corners < 3) return $"mesh '{g.Name}' : a polygon has {corners} corners";
                    polygons++;
                    triangles += corners - 2;
                    corners = 0;
                }
            }
            vertices += count;
            if (!g.Parents.Any(p => p.Parent.Kind == "Model")) Warnings.Add($"mesh '{g.Name}' is not attached to a node (Model)");
        }
        Summary.Add(("Meshes", geometries.Count.ToString()));
        Summary.Add(("Vertices", vertices.ToString("N0")));
        Summary.Add(("Polygons", $"{polygons:N0} ({triangles:N0} triangles)"));
        Summary.Add(("Materials", scene.OfKind("Material").Count().ToString()));
        var textures = scene.OfKind("Texture").ToList();
        if (textures.Count > 0) Summary.Add(("Textures", string.Join(", ", textures.Select(t => System.IO.Path.GetFileName(t.Node.Child("RelativeFilename")?[0] as string ?? t.Name)))));
        var skins = scene.OfKind("Deformer", "Skin").Count();
        if (skins > 0) Summary.Add(("Skins", $"{skins} ({scene.OfKind("Deformer", "Cluster").Count()} bones with weights)"));
        return null;
    }

    private static bool isBone(FbxObject o) => o.Kind == "Model" && (o.SubKind == "LimbNode" || o.SubKind == "Root");

    private string? checkSkeleton(FbxScene scene)
    {
        var bones = scene.Objects.Values.Where(isBone).ToList();
        if (bones.Count == 0) return $"no skeleton (no LimbNode bones) : a {SkeletonExtension} file needs a skeleton";
        var roots = bones.Where(b => !b.Parents.Any(p => isBone(p.Parent))).ToList();
        int depth(FbxObject b) { var d = 1; var cur = b; for (int guard = 0; guard < 256; guard++) { var parent = cur.Parents.FirstOrDefault(p => isBone(p.Parent)).Parent; if (parent == null) break; d++; cur = parent; } return d; }
        Summary.Add(("Bones", bones.Count.ToString()));
        Summary.Add(("Roots", string.Join(", ", roots.Take(4).Select(r => r.Name)) + (roots.Count > 4 ? " ..." : "")));
        Summary.Add(("Depth", bones.Max(depth).ToString()));
        var duplicates = bones.GroupBy(b => b.Name).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0) Warnings.Add($"bone names used twice : {string.Join(", ", duplicates.Take(5))} (bones are matched by name)");
        if (scene.OfKind("Pose", "BindPose").Any()) Summary.Add(("Bind pose", "yes"));
        return null;
    }

    private string? checkAnimation(FbxScene scene)
    {
        var stacks = scene.OfKind("AnimationStack").ToList();
        if (stacks.Count == 0) return $"no animation (AnimationStack) : a {AnimationExtension} file needs an animation";
        long first = long.MaxValue, last = long.MinValue;
        var curves = 0;
        var keys = 0L;
        var targets = new HashSet<long>();
        foreach (var curve in scene.OfKind("AnimationCurve"))
        {
            var times = curve.Node.ChildArray<long>("KeyTime");
            var values = curve.Node.ChildArray<float>("KeyValueFloat");
            if (times == null || times.Length == 0) continue;
            if (values == null || values.Length != times.Length) return $"curve '{curve.Name}' ({curve.Id}) : {times.Length} key times but {values?.Length ?? 0} values";
            for (int i = 1; i < times.Length; i++) if (times[i] < times[i - 1]) return $"curve {curve.Id} : key times go backwards";
            // 曲線 → CurveNode (T / R / S) → Model の Lcl Translation / Rotation / Scaling
            var node = curve.Parents.Select(p => p.Parent).FirstOrDefault(p => p.Kind == "AnimationCurveNode");
            var target = node?.Parents.FirstOrDefault(p => p.Parent.Kind == "Model" && p.Property != null && p.Property.StartsWith("Lcl ", StringComparison.Ordinal));
            if (target is not { Parent: { } model }) continue;
            curves++;
            keys += times.Length;
            targets.Add(model.Id);
            first = Math.Min(first, times[0]);
            last = Math.Max(last, times[^1]);
        }
        if (curves == 0) return "no animated values : no curve with keys is connected to a node's Lcl Translation / Rotation / Scaling";
        // 長さ : スタックの LocalStart / LocalStop があればそれ (無ければキーの範囲)
        var props = FbxScene.Properties70(stacks[0].Node);
        if (props.TryGetValue("LocalStop", out var stop) && stop.Values.Length > 0 && stop.Values[0] is long stopTime && stopTime > 0)
        {
            first = props.TryGetValue("LocalStart", out var startProp) && startProp.Values.Length > 0 && startProp.Values[0] is long s ? s : 0;
            last = stopTime;
        }
        var seconds = FbxScene.Seconds(last - first);
        Summary.Add(("Clips", string.Join(", ", stacks.Select(s => s.Name))));
        Summary.Add(("Length", $"{seconds:F3} s ({seconds * scene.FrameRate:F0} frames at {scene.FrameRate:G4} fps)"));
        Summary.Add(("Animated nodes", targets.Count.ToString()));
        Summary.Add(("Curves", $"{curves} ({keys:N0} keys)"));
        if (stacks.Count > 1) Warnings.Add($"{stacks.Count} animation stacks : the first one ('{stacks[0].Name}') is used");
        return null;
    }

    public override string ToString() => $"FbxAsset({Path}, {Type}{(Error != null ? ", error" : "")})";
}
