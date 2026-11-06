using System.Diagnostics;
using System.Numerics;

namespace GameEngine.Fbx;

// ==========================================================================
// FBX のスケルトン (.skl.fbx) とアニメーション (.anm.fbx) → エンジンの形 (AN3)
//   空間 : FBX の行列 L (行ベクトル) を、エンジンの空間の行列 C⁻¹ × L × C にする (C = 軸の変換 × 単位 (cm → m))。
//          メッシュの点の変換 (p × C) と合わせると、骨の子の物の位置が FBX と同じになる (鏡の変換も C⁻¹ と C で消える)
//   スケルトン : 骨 (LimbNode / Root) と、骨の上にある Null (根) を親から順に。休みの姿勢 = Lcl の値
//   アニメーション : 最初の AnimationStack の最初の AnimationLayer。曲線 (AnimationCurve) を決まった刻み (FBX の fps) で計算して、
//          骨ごとに 位置 / 回転 (四元数) / 大きさ の列にする (毎フレームは刻みの間を直線 / 球面線形で補うだけ)
//          曲線の補間 : 一定 / 直線 / 3 次 (傾きは自分で決めた物 (KeyAttrDataFloat) か、自動 = 前後のキーから)
//          回転は FBX と同じく X / Y / Z の角度を別々に補間して、ノードの式 (回す順番 / PreRotation / 中心) で行列にする
// ==========================================================================
public sealed class FbxSkeletonBone
{
    public string Name = "";
    public int Parent = -1;
    /// <summary>LimbNode / Root (骨) か、骨の上の Null (根) か</summary>
    public bool IsBone;
    public Vector3 Position;
    public Quaternion Rotation = Quaternion.Identity;
    public Vector3 Scale = Vector3.One;
    /// <summary>休みの姿勢の世界の行列 (エンジンの空間。スケルトンの根から)</summary>
    public Matrix4x4 BindGlobal = Matrix4x4.Identity;
}

public sealed class FbxAnimationTrack
{
    public string Name = "";
    public bool HasTranslation, HasRotation, HasScale;
    public Vector3[] Positions = Array.Empty<Vector3>();
    public Quaternion[] Rotations = Array.Empty<Quaternion>();
    public Vector3[] Scales = Array.Empty<Vector3>();
}

public sealed class FbxAnimationData
{
    public string Name = "";
    public float FrameRate = 30;
    public float Duration;
    public int FrameCount;
    public List<FbxAnimationTrack> Tracks = new();
    public List<string> Warnings = new();
    public double ImportMilliseconds;
}

public static class FbxAnimationImporter
{
    /// <summary>FBX の空間 → エンジンの空間 (p_engine = p_fbx × C)</summary>
    public static Matrix4x4 SpaceMatrix(FbxScene scene) =>
        FbxTransforms.AxisMatrix(scene, out _) * Matrix4x4.CreateScale((float)(scene.UnitScaleFactor * 0.01));

    /// <summary>FBX の行列 → エンジンの空間の行列 (C⁻¹ × L × C)</summary>
    public static Matrix4x4 ToEngine(Matrix4x4 fbx, Matrix4x4 space, Matrix4x4 spaceInverse) => spaceInverse * fbx * space;

    public static void Decompose(Matrix4x4 m, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        if (!Matrix4x4.Decompose(m, out scale, out rotation, out position))
        {
            // 大きさが 0 などで分けられない : 位置だけ
            position = m.Translation;
            rotation = Quaternion.Identity;
            scale = Vector3.One;
        }
        rotation = Quaternion.Normalize(rotation);
    }

    private static bool isBone(FbxObject o) => o.Kind == "Model" && (o.SubKind == "LimbNode" || o.SubKind == "Root");
    private static FbxObject? parentModel(FbxObject o) => o.Parents.Select(p => p.Parent).FirstOrDefault(p => p.Kind == "Model" && p != o);

    // ---------------------------------------------------------------------
    // スケルトン
    // ---------------------------------------------------------------------
    public static List<FbxSkeletonBone> ImportSkeleton(FbxScene scene)
    {
        var bones = scene.Objects.Values.Where(isBone).ToList();
        // 骨と、その上にある Model (根の Null など)
        var nodes = new HashSet<long>();
        foreach (var bone in bones)
            for (var cur = bone; cur != null && nodes.Add(cur.Id); cur = parentModel(cur)) { }
        // 親から順 (つながりの順を保つ)
        var ordered = new List<FbxObject>();
        void visit(FbxObject node, int depth)
        {
            if (depth > 256) return;
            ordered.Add(node);
            foreach (var child in node.Children.Select(c => c.Child).Where(c => c.Kind == "Model" && nodes.Contains(c.Id) && parentModel(c) == node)) visit(child, depth + 1);
        }
        foreach (var root in scene.Objects.Values.Where(o => nodes.Contains(o.Id) && (parentModel(o) is not { } p || !nodes.Contains(p.Id)))) visit(root, 0);
        var space = SpaceMatrix(scene);
        Matrix4x4.Invert(space, out var spaceInverse);
        var index = new Dictionary<long, int>();
        var result = new List<FbxSkeletonBone>();
        foreach (var node in ordered)
        {
            var local = ToEngine(FbxTransforms.Local(node), space, spaceInverse);
            Decompose(local, out var position, out var rotation, out var scale);
            var parent = parentModel(node) is { } p && index.TryGetValue(p.Id, out var pi) ? pi : -1;
            var bone = new FbxSkeletonBone
            {
                Name = node.Name, Parent = parent, IsBone = isBone(node), Position = position, Rotation = rotation, Scale = scale,
                BindGlobal = parent >= 0 ? local * result[parent].BindGlobal : local,
            };
            index[node.Id] = result.Count;
            result.Add(bone);
        }
        return result;
    }

    // ---------------------------------------------------------------------
    // アニメーション
    // ---------------------------------------------------------------------
    public static FbxAnimationData ImportAnimation(FbxScene scene, float? frameRate = null)
    {
        var start = Stopwatch.GetTimestamp();
        var data = new FbxAnimationData();
        var stacks = scene.OfKind("AnimationStack").ToList();
        if (stacks.Count == 0) return data;
        var stack = stacks[0];
        data.Name = stack.Name;
        var layers = stack.Children.Select(c => c.Child).Where(c => c.Kind == "AnimationLayer").ToList();
        if (layers.Count > 1) data.Warnings.Add($"{layers.Count} animation layers : only the first one ('{layers[0].Name}') is used (layers are not blended)");
        var layer = layers.FirstOrDefault();
        // 動く値 : CurveNode (T / R / S) → Model の Lcl Translation / Rotation / Scaling
        var curveNodes = layer != null
            ? layer.Children.Select(c => c.Child).Where(c => c.Kind == "AnimationCurveNode")
            : scene.OfKind("AnimationCurveNode");
        var channels = new Dictionary<long, (FbxObject Model, FbxChannel? T, FbxChannel? R, FbxChannel? S)>();
        long first = long.MaxValue, last = long.MinValue;
        foreach (var curveNode in curveNodes)
        {
            var target = curveNode.Parents.FirstOrDefault(p => p.Parent.Kind == "Model" && p.Property is "Lcl Translation" or "Lcl Rotation" or "Lcl Scaling");
            if (target.Parent == null) continue;
            var channel = FbxChannel.From(curveNode);
            if (channel == null) continue;
            first = Math.Min(first, channel.FirstTime);
            last = Math.Max(last, channel.LastTime);
            channels.TryGetValue(target.Parent.Id, out var entry);
            entry.Model = target.Parent;
            if (target.Property == "Lcl Translation") entry.T = channel;
            else if (target.Property == "Lcl Rotation") entry.R = channel;
            else entry.S = channel;
            channels[target.Parent.Id] = entry;
        }
        if (channels.Count == 0) return data;
        // 長さ : スタックの LocalStart / LocalStop (無ければキーの範囲)
        var props = FbxScene.Properties70(stack.Node);
        if (props.TryGetValue("LocalStop", out var stop) && stop.Values.Length > 0 && stop.Values[0] is long stopTime && stopTime > 0)
        {
            first = props.TryGetValue("LocalStart", out var s) && s.Values.Length > 0 && s.Values[0] is long st ? st : 0;
            last = stopTime;
        }
        data.FrameRate = frameRate ?? (float)scene.FrameRate;
        var startSeconds = FbxScene.Seconds(first);
        data.Duration = (float)Math.Max(0, FbxScene.Seconds(last) - startSeconds);
        data.FrameCount = Math.Max(1, (int)Math.Round(data.Duration * data.FrameRate)) + 1;
        var space = SpaceMatrix(scene);
        Matrix4x4.Invert(space, out var spaceInverse);
        foreach (var (_, (model, t, r, s)) in channels.OrderBy(c => c.Value.Model.Name, StringComparer.Ordinal))
        {
            var lclProps = FbxScene.Properties70(model.Node);
            Vector3 lcl(string name, Vector3 fallback) => lclProps.TryGetValue(name, out var p) && p.Values.Length >= 3 ? p.Vector : fallback;
            var track = new FbxAnimationTrack
            {
                Name = model.Name, HasTranslation = t != null, HasRotation = r != null, HasScale = s != null,
                Positions = new Vector3[data.FrameCount], Rotations = new Quaternion[data.FrameCount], Scales = new Vector3[data.FrameCount],
            };
            var t0 = lcl("Lcl Translation", Vector3.Zero);
            var r0 = lcl("Lcl Rotation", Vector3.Zero);
            var s0 = lcl("Lcl Scaling", Vector3.One);
            for (int f = 0; f < data.FrameCount; f++)
            {
                var seconds = startSeconds + Math.Min(data.Duration, f / data.FrameRate);
                var local = FbxTransforms.Local(model, t?.Evaluate(seconds, t0) ?? t0, r?.Evaluate(seconds, r0) ?? r0, s?.Evaluate(seconds, s0) ?? s0);
                Decompose(ToEngine(local, space, spaceInverse), out track.Positions[f], out track.Rotations[f], out track.Scales[f]);
                // 四元数は前の刻みと同じ側に (q と -q は同じ回転。補う時に遠回りしないように)
                if (f > 0 && Quaternion.Dot(track.Rotations[f], track.Rotations[f - 1]) < 0) track.Rotations[f] = -track.Rotations[f];
            }
            data.Tracks.Add(track);
        }
        data.ImportMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return data;
    }
}

/// <summary>CurveNode 1 つ (X / Y / Z の 3 本の曲線。曲線の無い軸はノードの値のまま)</summary>
public sealed class FbxChannel
{
    private readonly FbxCurve?[] _curves = new FbxCurve?[3];
    private readonly float?[] _defaults = new float?[3];
    public long FirstTime { get; private set; } = long.MaxValue;
    public long LastTime { get; private set; } = long.MinValue;

    public static FbxChannel? From(FbxObject curveNode)
    {
        var channel = new FbxChannel();
        var props = FbxScene.Properties70(curveNode.Node);
        var any = false;
        for (int axis = 0; axis < 3; axis++)
        {
            var name = "d|" + "XYZ"[axis];
            if (props.TryGetValue(name, out var p) && p.Values.Length > 0) channel._defaults[axis] = (float)p.Number();
            var curveObject = curveNode.Children.FirstOrDefault(c => c.Child.Kind == "AnimationCurve" && c.Property == name).Child;
            if (curveObject == null || FbxCurve.From(curveObject) is not { } curve) continue;
            channel._curves[axis] = curve;
            channel.FirstTime = Math.Min(channel.FirstTime, curve.FirstTime);
            channel.LastTime = Math.Max(channel.LastTime, curve.LastTime);
            any = true;
        }
        return any ? channel : null;
    }

    /// <summary>その時間の値。曲線の無い軸は CurveNode の値 (無ければ fallback = ノードの Lcl の値)</summary>
    public Vector3 Evaluate(double seconds, Vector3 fallback) => new(
        _curves[0]?.Evaluate(seconds) ?? _defaults[0] ?? fallback.X,
        _curves[1]?.Evaluate(seconds) ?? _defaults[1] ?? fallback.Y,
        _curves[2]?.Evaluate(seconds) ?? _defaults[2] ?? fallback.Z);
}

/// <summary>曲線 1 本 (キーの時間と値、キーごとの補間)</summary>
public sealed class FbxCurve
{
    public const int Constant = 0x02, Linear = 0x04, Cubic = 0x08, TangentUser = 0x0400, TangentBreak = 0x0800, TangentClamp = 0x1000;

    private double[] _times = Array.Empty<double>();
    private float[] _values = Array.Empty<float>();
    private int[] _flags = Array.Empty<int>();
    private float[] _rightSlope = Array.Empty<float>();      // キー i の右の傾き (値 / 秒)
    private float[] _nextLeftSlope = Array.Empty<float>();   // キー i + 1 の左の傾き
    public long FirstTime { get; private set; }
    public long LastTime { get; private set; }
    public int KeyCount => _times.Length;

    public static FbxCurve? From(FbxObject curve)
    {
        var times = curve.Node.ChildArray<long>("KeyTime");
        var values = curve.Node.ChildArray<float>("KeyValueFloat");
        if (times == null || values == null || times.Length == 0 || values.Length != times.Length) return null;
        var result = new FbxCurve
        {
            _times = times.Select(FbxScene.Seconds).ToArray(), _values = values, FirstTime = times[0], LastTime = times[^1],
            _flags = new int[times.Length], _rightSlope = new float[times.Length], _nextLeftSlope = new float[times.Length],
        };
        // キーの属性 : flags[k] / data[k × 4 ..] が、refCount[k] 個のキーに続けて使われる
        var flags = curve.Node.ChildArray<int>("KeyAttrFlags") ?? new[] { Linear };
        var data = curve.Node.ChildArray<float>("KeyAttrDataFloat") ?? Array.Empty<float>();
        var refCount = curve.Node.ChildArray<int>("KeyAttrRefCount") ?? new[] { times.Length };
        var key = 0;
        for (int a = 0; a < flags.Length && key < times.Length; a++)
        {
            var count = a < refCount.Length ? refCount[a] : times.Length - key;
            for (int n = 0; n < count && key < times.Length; n++, key++)
            {
                result._flags[key] = flags[a];
                result._rightSlope[key] = data.Length >= a * 4 + 2 ? data[a * 4] : 0;
                result._nextLeftSlope[key] = data.Length >= a * 4 + 2 ? data[a * 4 + 1] : 0;
            }
        }
        for (; key < times.Length; key++) result._flags[key] = flags.Length > 0 ? flags[^1] : Linear;
        return result;
    }

    // 自動の傾き (前後のキーから。端は 0。極値で止める物 (Clamp) は 0)
    private float autoSlope(int i)
    {
        if (i <= 0 || i >= _times.Length - 1) return 0;
        var (v0, v1, v2) = (_values[i - 1], _values[i], _values[i + 1]);
        if ((_flags[i] & TangentClamp) != 0 && (v1 >= v0 && v1 >= v2 || v1 <= v0 && v1 <= v2)) return 0;
        return (float)((v2 - v0) / (_times[i + 1] - _times[i - 1]));
    }

    public float Evaluate(double seconds)
    {
        var n = _times.Length;
        if (n == 1 || seconds <= _times[0]) return _values[0];
        if (seconds >= _times[n - 1]) return _values[n - 1];
        // 二分探索 : _times[i] <= seconds < _times[i + 1]
        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            var mid = (lo + hi) / 2;
            if (_times[mid] <= seconds) lo = mid; else hi = mid;
        }
        var i = lo;
        double t0 = _times[i], t1 = _times[i + 1], span = t1 - t0;
        float v0 = _values[i], v1 = _values[i + 1];
        var interpolation = _flags[i] & 0x0E;
        if (interpolation == Constant || span <= 0) return v0;
        var s = (float)((seconds - t0) / span);
        if (interpolation != Cubic) return v0 + (v1 - v0) * s;
        // 3 次 (エルミート) : 傾きは自分で決めた物 (User / Break) か自動
        var user = (_flags[i] & (TangentUser | TangentBreak)) != 0;
        var m0 = user ? _rightSlope[i] : autoSlope(i);
        var m1 = user ? _nextLeftSlope[i] : autoSlope(i + 1);
        float s2 = s * s, s3 = s2 * s;
        return (2 * s3 - 3 * s2 + 1) * v0 + (s3 - 2 * s2 + s) * (float)span * m0 + (-2 * s3 + 3 * s2) * v1 + (s3 - s2) * (float)span * m1;
    }
}
