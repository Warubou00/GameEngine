namespace GameEngine.Fbx;

// ==========================================================================
// FBX の中身を引ける形にする (AN1)
//   Objects : 物ごとのノード (Geometry / Model / Material / Texture / Deformer / Pose / AnimationStack / AnimationLayer /
//             AnimationCurveNode / AnimationCurve / NodeAttribute ...)。最初のプロパティが Id (int64)、
//             2 つ目が「名前 \0\x01 種類」、3 つ目が細かい種類 (Mesh / LimbNode / Null / Skin / Cluster ...)
//   Connections : C "OO" 子 親 (物 → 物) / C "OP" 子 親 "プロパティ" (物 → 物のプロパティ。例 : 曲線 → "d|X")
//   Properties70 : P "名前" "型" "型2" "印" 値... (位置 / 回転 / 色 などの値)
//   GlobalSettings : 軸 (UpAxis / FrontAxis / CoordAxis と符号) / 単位 (UnitScaleFactor、cm = 1) / 時間 (TimeMode / CustomFrameRate)
// ==========================================================================
public sealed class FbxObject
{
    public long Id { get; init; }
    /// <summary>ノードの名前 (Geometry / Model / Material ...)</summary>
    public string Kind { get; init; } = "";
    /// <summary>名前 (例 : "Arm")</summary>
    public string Name { get; init; } = "";
    /// <summary>細かい種類 (Mesh / LimbNode / Null / Skin / Cluster ...)。無ければ ""</summary>
    public string SubKind { get; init; } = "";
    public FbxNode Node { get; init; } = null!;
    /// <summary>この物に繋がっている物 (子 → この物)</summary>
    public List<(FbxObject Child, string? Property)> Children { get; } = new();
    /// <summary>この物が繋がっている先 (この物 → 親)</summary>
    public List<(FbxObject Parent, string? Property)> Parents { get; } = new();

    public override string ToString() => $"{Kind}::{Name} ({SubKind}, {Id})";
}

/// <summary>Properties70 の P (名前 → 型 と 値)</summary>
public sealed record FbxProperty(string Name, string Type, object[] Values)
{
    public double Number(int i = 0) => Values.Length > i ? Values[i] switch { int v => v, long v => v, float v => v, double v => v, short v => v, bool v => v ? 1 : 0, _ => 0 } : 0;
    public System.Numerics.Vector3 Vector => new((float)Number(0), (float)Number(1), (float)Number(2));
    public string Text => Values.Length > 0 ? Values[0] as string ?? "" : "";
}

public sealed class FbxScene
{
    /// <summary>FBX の時間の単位 (1 秒 = 46186158000)</summary>
    public const long TimeUnitsPerSecond = 46186158000L;

    public FbxDocument Document { get; }
    public Dictionary<long, FbxObject> Objects { get; } = new();
    public int ConnectionCount { get; private set; }
    public int BrokenConnections { get; private set; }   // 無い物を指す繋がり (飛ばした)
    public IReadOnlyDictionary<string, FbxProperty> GlobalSettings { get; }

    /// <summary>軸 (0 = X / 1 = Y / 2 = Z) と符号。既定は Y 上 / Z 前 (奥ではなく手前) / X 右 (Maya / FBX の既定)</summary>
    public (int Up, int UpSign, int Front, int FrontSign, int Coord, int CoordSign) Axes { get; }
    /// <summary>1 単位が何 cm か (既定 1 = cm。m で書き出すと 100)</summary>
    public double UnitScaleFactor { get; }
    /// <summary>ファイルの fps (TimeMode。分からなければ 30)</summary>
    public double FrameRate { get; }

    public FbxScene(FbxDocument document)
    {
        Document = document;
        var objects = document["Objects"];
        if (objects != null)
        {
            foreach (var node in objects.Children)
            {
                if (node.Properties.Count < 2 || node[0] is not long id) continue;
                var fullName = node[1] as string ?? "";
                var separator = fullName.IndexOf("\0\x01", StringComparison.Ordinal);
                var name = separator >= 0 ? fullName[..separator] : fullName;
                // 古い書き出しは "Model::Name" の形
                if (separator < 0 && name.Contains("::", StringComparison.Ordinal)) name = name[(name.IndexOf("::", StringComparison.Ordinal) + 2)..];
                Objects[id] = new FbxObject { Id = id, Kind = node.Name, Name = name, SubKind = node[2] as string ?? "", Node = node };
            }
        }
        foreach (var c in document["Connections"]?.ChildrenNamed("C") ?? Enumerable.Empty<FbxNode>())
        {
            if (c.Properties.Count < 3) continue;
            ConnectionCount++;
            var child = c[1] is long a ? a : 0;
            var parent = c[2] is long b ? b : 0;
            var property = c.Properties.Count > 3 ? c[3] as string : null;
            if (!Objects.TryGetValue(child, out var childObject)) { BrokenConnections++; continue; }
            if (!Objects.TryGetValue(parent, out var parentObject)) continue;   // 0 = シーンの根 (普通)
            childObject.Parents.Add((parentObject, property));
            parentObject.Children.Add((childObject, property));
        }
        GlobalSettings = Properties70(document["GlobalSettings"]);
        int axis(string name, int fallback) => GlobalSettings.TryGetValue(name, out var p) ? (int)p.Number() : fallback;
        Axes = (axis("UpAxis", 1), axis("UpAxisSign", 1), axis("FrontAxis", 2), axis("FrontAxisSign", 1), axis("CoordAxis", 0), axis("CoordAxisSign", 1));
        UnitScaleFactor = GlobalSettings.TryGetValue("UnitScaleFactor", out var unit) && unit.Number() > 0 ? unit.Number() : 1.0;
        FrameRate = frameRate(axis("TimeMode", 0), GlobalSettings.TryGetValue("CustomFrameRate", out var custom) ? custom.Number() : 0);
    }

    public IEnumerable<FbxObject> OfKind(string kind, string? subKind = null) =>
        Objects.Values.Where(o => o.Kind == kind && (subKind == null || o.SubKind == subKind));

    /// <summary>Properties70 (無ければ空)。node はその親 (Model など。中の Properties70 を見る) でも、Properties70 そのものでもよい</summary>
    public static Dictionary<string, FbxProperty> Properties70(FbxNode? node)
    {
        var result = new Dictionary<string, FbxProperty>(StringComparer.Ordinal);
        var list = node == null ? null : node.Name == "Properties70" ? node : node.Child("Properties70");
        if (list == null) return result;
        foreach (var p in list.ChildrenNamed("P"))
        {
            if (p.Properties.Count < 4 || p[0] is not string name) continue;
            result[name] = new FbxProperty(name, p[1] as string ?? "", p.Properties.Skip(4).ToArray());
        }
        return result;
    }

    public static double Seconds(long time) => time / (double)TimeUnitsPerSecond;
    public static long Time(double seconds) => (long)Math.Round(seconds * TimeUnitsPerSecond);

    // FBX の TimeMode → fps (0 = 既定 / 14 = 自分で決めた値)
    private static double frameRate(int mode, double custom) => mode switch
    {
        1 => 120, 2 => 100, 3 => 60, 4 => 50, 5 => 48, 6 => 30, 7 => 30, 8 => 29.97, 9 => 29.97, 10 => 25, 11 => 24, 12 => 1000, 13 => 23.976,
        14 => custom > 0 ? custom : 30, 15 => 96, 16 => 72, 17 => 59.94, 18 => 119.88,
        _ => 30,
    };
}
