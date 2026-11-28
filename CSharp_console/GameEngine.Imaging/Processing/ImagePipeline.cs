using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameEngine.Imaging.Processing;

// ==========================================================================
// 処理の並び (v2.3。非破壊 : 元の画像は変えず、上から順に掛けた結果を作る)
//   Run は段ごとの結果を PipelineCache に取っておき、変わった処理から後ろだけ計算し直す
//   (最後の処理のつまみを動かしている間は、その 1 つだけを計算する)
//   JSON : 処理の種類は "type"。Undo / Redo はこの JSON を積む
//   返す画像はキャッシュの中の物 (書き換えない。書き換えるなら Clone)
// ==========================================================================
public sealed class ImagePipeline
{
    public List<ImageOperation> Operations { get; set; } = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public string ToJson() => JsonSerializer.Serialize(Operations, JsonOptions);

    public static ImagePipeline FromJson(string json) => new() { Operations = JsonSerializer.Deserialize<List<ImageOperation>>(json, JsonOptions) ?? new() };

    internal static ImageOperation CloneOperation(ImageOperation operation) =>
        JsonSerializer.Deserialize<ImageOperation>(JsonSerializer.Serialize(operation, JsonOptions), JsonOptions)!;

    /// <summary>source に全部の処理を掛ける。cache を渡すと前の結果を使い回す</summary>
    public ImageBuffer Run(ImageBuffer source, ProcessContext? ctx = null, PipelineCache? cache = null)
    {
        ctx ??= ProcessContext.Default;
        var keys = new string[Operations.Count];
        var key = $"{source.Width}x{source.Height}@{ctx.Scale}";
        for (int i = 0; i < Operations.Count; i++) keys[i] = key += "|" + JsonSerializer.Serialize(Operations[i], JsonOptions);

        var current = source;
        int start = 0;
        if (cache != null)
        {
            if (!ReferenceEquals(cache.Source, source)) cache.Reset(source);
            var (stage, image) = cache.Find(keys);
            if (image != null) { current = image; start = stage + 1; }
            cache.Truncate(start);
        }

        for (int i = start; i < Operations.Count; i++)
        {
            ctx.ThrowIfCanceled();
            var op = Operations[i];
            if (op.Enabled && op.Amount > 0f)
            {
                var output = new ImageBuffer(current.Width, current.Height);
                op.Apply(current, output, ctx);
                if (op.Amount < 1f) blend(current, output, op.Amount);
                current = output;
            }
            cache?.Add(keys[i], current);
        }
        return current;
    }

    // dst = lerp(src, dst, amount)
    private static void blend(ImageBuffer src, ImageBuffer dst, float amount)
    {
        var s = src.Pixels;
        var d = dst.Pixels;
        Parallel.For(0, src.Height, y =>
        {
            for (int i = y * src.Width * 4, end = i + src.Width * 4; i < end; i++) d[i] = s[i] + (d[i] - s[i]) * amount;
        });
    }
}

/// <summary>段ごとの結果 (メモリが多すぎたら前の方の段から捨てる)</summary>
public sealed class PipelineCache
{
    private readonly List<(string Key, ImageBuffer? Image)> _stages = new();

    /// <summary>取っておく量の上限 (byte)</summary>
    public long Budget { get; set; } = 512L << 20;

    public ImageBuffer? Source { get; private set; }

    public int Count => _stages.Count;

    /// <summary>index 番目の処理の結果 (取っていない / 捨てた時は null)。計算と同じスレッドで使う</summary>
    public ImageBuffer? StageOutput(int index) => index >= 0 && index < _stages.Count ? _stages[index].Image : null;

    public void Reset(ImageBuffer? source)
    {
        Source = source;
        _stages.Clear();
    }

    // 前から鍵が同じ所までのうち、画像が残っている一番後ろの段
    internal (int Stage, ImageBuffer? Image) Find(string[] keys)
    {
        int match = 0;
        while (match < keys.Length && match < _stages.Count && _stages[match].Key == keys[match]) match++;
        for (int i = match - 1; i >= 0; i--)
            if (_stages[i].Image != null) return (i, _stages[i].Image);
        return (-1, null);
    }

    internal void Truncate(int count)
    {
        if (_stages.Count > count) _stages.RemoveRange(count, _stages.Count - count);
    }

    internal void Add(string key, ImageBuffer image)
    {
        _stages.Add((key, image));
        // 同じ画像 (飛ばした段) は 1 つと数える。最後の段は捨てない
        long total = 0;
        var seen = new HashSet<ImageBuffer>(ReferenceEqualityComparer.Instance);
        for (int i = _stages.Count - 1; i >= 0; i--)
        {
            var img = _stages[i].Image;
            if (img == null || ReferenceEquals(img, Source) || !seen.Add(img)) continue;
            total += (long)img.Pixels.Length * sizeof(float);
            if (total > Budget && i < _stages.Count - 1) _stages[i] = (_stages[i].Key, null);
        }
    }
}
