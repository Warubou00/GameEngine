using System.Numerics;
using System.Text.Json;

namespace GameEngine;

// ==========================================================================
// スプライトのアニメーション (v2.4。.spriteanim.json)
//   画像を順に切り替えて見せる。GameObject (SpriteRenderer + SpriteAnimator) と Widget (USpriteImage) の両方で使う
//   コマ = 画像のパス + 切り出す四角 (ピクセル。無ければ画像全体) + 時間 (0 = 1 / FrameRate 秒) + イベントの名前
//   シート (1 枚に並べた画像) の格子はエディターがコマに展開して書く (アセットにはコマだけを持つ)
//   GPU のテクスチャはコマごとに 1 枚 (読み込んだ時に切り出す。隣のコマがにじまない)。初めて使う時に AssetManager が作る
//   同じパスは同じインスタンス (読み直すと中身を差し替えて Version が上がる。使っている物は次のフレームから新しいコマ)
//
//   {
//     "format": "GameEngineSpriteAnimation", "version": 1,
//     "frameRate": 12, "loop": "Loop", "pixelsPerUnit": 100, "pivot": [0.5, 0.5],
//     "frames": [ { "image": "Assets/Sprites/run.png", "rect": [0, 0, 64, 64], "duration": 0, "event": "step" } ]
//   }
// ==========================================================================

/// <summary>終わりまで行った時の動き</summary>
public enum SpriteLoopMode
{
    /// <summary>最初に戻って繰り返す</summary>
    Loop,
    /// <summary>最後のコマで止まる (OnSpriteFinished が呼ばれる)</summary>
    Once,
    /// <summary>最後まで行ったら逆に戻り、また進む (繰り返す)</summary>
    PingPong,
}

/// <summary>再生する物 (SpriteAnimator / USpriteImage) でのループの指定</summary>
public enum SpriteLoopOverride
{
    /// <summary>アセットの Loop のまま</summary>
    UseAsset,
    Loop,
    Once,
    PingPong,
}

/// <summary>1 コマ</summary>
public sealed class SpriteFrame
{
    /// <summary>画像のアセットのパス</summary>
    public string Image = "";

    /// <summary>切り出す四角 (ピクセル。X, Y = 左上。幅か高さが 0 なら画像全体)</summary>
    public int X, Y, Width, Height;

    /// <summary>このコマを出す時間 (秒。0 = 1 / FrameRate)</summary>
    public float Duration;

    /// <summary>このコマに入った時に送るイベントの名前 (空 = 送らない)</summary>
    public string Event = "";

    public bool HasRect => Width > 0 && Height > 0;

    public SpriteFrame Clone() => (SpriteFrame)MemberwiseClone();
}

/// <summary>GPU のコマのテクスチャ (Texture = UI / マテリアルのテクスチャの番号。無ければ 0xFFFFFFFF)</summary>
public readonly record struct SpriteFrameTexture(uint Texture, int Width, int Height)
{
    public const uint None = 0xFFFFFFFFu;
    public static SpriteFrameTexture Empty => new(None, 0, 0);
    public bool IsValid => Texture != None;
    public Vector2 Size => new(Width, Height);
}

/// <summary>時間 → コマ の結果</summary>
public readonly record struct SpriteSample(int Frame, long Step, bool Finished);

public sealed class SpriteAnimation
{
    public const string Format = "GameEngineSpriteAnimation";
    public const int FormatVersion = 1;

    /// <summary>アセットのパス ("Assets/...")。コードで作った物は ""</summary>
    public string Path { get; internal set; }

    public float FrameRate = 12f;
    public SpriteLoopMode Loop = SpriteLoopMode.Loop;
    /// <summary>GameObject での大きさ (このピクセル数 = 1 m)</summary>
    public float PixelsPerUnit = 100f;
    /// <summary>中心 (0,0 = 左下 / 1,1 = 右上)</summary>
    public Vector2 Pivot = new(0.5f, 0.5f);
    public List<SpriteFrame> Frames = new();

    /// <summary>中身が変わった回数 (読み直し / SetJson)。使う物はこれでテクスチャを取り直す</summary>
    public int Version { get; private set; }

    internal AssetManager? Owner;
    internal SpriteFrameTexture[]? Textures;   // AssetManager が作る (Frames と同じ並び)
    internal int TexturesVersion = -1;
    internal string? TexturesKey;   // 作った時のコマの画像と四角 (時間 / イベントだけが変わった時は作り直さない)

    // コマの画像と四角を並べた鍵
    internal string FramesKey() => string.Join("|", Frames.Select(f => $"{f.Image}:{f.X},{f.Y},{f.Width},{f.Height}"));

    /// <summary>画像のファイルが変わった : 次に使う時にコマを作り直す</summary>
    internal void InvalidateTextures() { TexturesKey = null; Version++; }

    public SpriteAnimation() : this("") { }

    public SpriteAnimation(string path) => Path = path;

    public SpriteAnimation(string path, string json) : this(path) => SetJson(json);

    public int FrameCount => Frames.Count;

    /// <summary>コマ i を出す時間 (秒)</summary>
    public float FrameDuration(int i)
    {
        if (i < 0 || i >= Frames.Count) return 0f;
        var d = Frames[i].Duration;
        return d > 0f ? d : 1f / Math.Max(FrameRate, 0.001f);
    }

    /// <summary>全部のコマを 1 回出す時間 (秒)</summary>
    public float Duration
    {
        get
        {
            float total = 0;
            for (int i = 0; i < Frames.Count; i++) total += FrameDuration(i);
            return total;
        }
    }

    public SpriteLoopMode LoopFor(SpriteLoopOverride loop) => loop switch
    {
        SpriteLoopOverride.Loop => SpriteLoopMode.Loop,
        SpriteLoopOverride.Once => SpriteLoopMode.Once,
        SpriteLoopOverride.PingPong => SpriteLoopMode.PingPong,
        _ => Loop,
    };

    /// <summary>
    /// 再生を始めてからの時間 → コマ。Step = 始めてから何コマ目か (0 から。繰り返しても増え続ける。イベントを送るのに使う)。
    /// Finished = Once で最後のコマの時間を過ぎた
    /// </summary>
    public SpriteSample Sample(float time, SpriteLoopMode loop)
    {
        int n = Frames.Count;
        if (n == 0) return new SpriteSample(-1, 0, true);
        if (time < 0) time = 0;
        // PingPong の 1 往復の並び : 0 1 2 … n-1 n-2 … 1 (コマが 1 つ / 2 つなら 0 1 0 1 …)
        int cycle = loop == SpriteLoopMode.PingPong ? Math.Max(1, 2 * n - 2) : n;
        float cycleTime = 0;
        for (int s = 0; s < cycle; s++) cycleTime += FrameDuration(frameOfStep(s, n, loop));
        if (cycleTime <= 0) return new SpriteSample(0, 0, false);

        if (loop == SpriteLoopMode.Once && time >= cycleTime) return new SpriteSample(n - 1, n - 1, true);
        long cycles = (long)MathF.Floor(time / cycleTime);
        float t = time - cycles * cycleTime;
        int step = 0;
        for (; step < cycle - 1; step++)
        {
            float d = FrameDuration(frameOfStep(step, n, loop));
            if (t < d) break;
            t -= d;
        }
        return new SpriteSample(frameOfStep(step, n, loop), cycles * cycle + step, false);
    }

    /// <summary>何コマ目か (Step) → コマの番号</summary>
    public int FrameOfStep(long step, SpriteLoopMode loop)
    {
        int n = Frames.Count;
        if (n == 0) return -1;
        int cycle = loop == SpriteLoopMode.PingPong ? Math.Max(1, 2 * n - 2) : n;
        if (loop == SpriteLoopMode.Once) return (int)Math.Min(step, n - 1);
        return frameOfStep((int)(step % cycle), n, loop);
    }

    private static int frameOfStep(int step, int n, SpriteLoopMode loop)
    {
        if (loop != SpriteLoopMode.PingPong || n <= 1) return step % Math.Max(n, 1);
        return step < n ? step : 2 * n - 2 - step;
    }

    /// <summary>コマ i の GPU のテクスチャ (初めて聞かれた時に AssetManager が作る。作れなければ Empty)</summary>
    public SpriteFrameTexture TextureOf(int i)
    {
        if (i < 0 || i >= Frames.Count) return SpriteFrameTexture.Empty;
        if (Textures == null || TexturesVersion != Version)
        {
            if (Textures != null && TexturesKey != null && TexturesKey == FramesKey() && Textures.Length == Frames.Count) TexturesVersion = Version;
            else Owner?.CreateSpriteTextures(this);
        }
        return Textures != null && i < Textures.Length ? Textures[i] : SpriteFrameTexture.Empty;
    }

    /// <summary>中身を変えた (エディター / コード)。テクスチャは次に使う時に作り直す</summary>
    public void MarkChanged() => Version++;

    // ---------------------------------------------------------------------
    // JSON
    // ---------------------------------------------------------------------

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("format", Format);
            w.WriteNumber("version", FormatVersion);
            w.WriteNumber("frameRate", FrameRate);
            w.WriteString("loop", Loop.ToString());
            w.WriteNumber("pixelsPerUnit", PixelsPerUnit);
            w.WriteStartArray("pivot");
            w.WriteNumberValue(Pivot.X);
            w.WriteNumberValue(Pivot.Y);
            w.WriteEndArray();
            w.WriteStartArray("frames");
            foreach (var f in Frames)
            {
                w.WriteStartObject();
                w.WriteString("image", f.Image);
                if (f.HasRect)
                {
                    w.WriteStartArray("rect");
                    w.WriteNumberValue(f.X); w.WriteNumberValue(f.Y); w.WriteNumberValue(f.Width); w.WriteNumberValue(f.Height);
                    w.WriteEndArray();
                }
                if (f.Duration > 0) w.WriteNumber("duration", f.Duration);
                if (!string.IsNullOrEmpty(f.Event)) w.WriteString("event", f.Event);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>JSON から中身を入れ替える (同じインスタンス。Version が上がる)。形が違えば InvalidDataException</summary>
    public void SetJson(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var format) || format.GetString() != Format)
            throw new InvalidDataException($"Not a sprite animation : {Path}");
        FrameRate = root.TryGetProperty("frameRate", out var rate) && rate.TryGetSingle(out var r) ? r : 12f;
        Loop = root.TryGetProperty("loop", out var loop) && Enum.TryParse<SpriteLoopMode>(loop.GetString(), true, out var l) ? l : SpriteLoopMode.Loop;
        PixelsPerUnit = root.TryGetProperty("pixelsPerUnit", out var ppu) && ppu.TryGetSingle(out var p) && p > 0 ? p : 100f;
        Pivot = root.TryGetProperty("pivot", out var pivot) && pivot.ValueKind == JsonValueKind.Array && pivot.GetArrayLength() >= 2
            ? new Vector2(pivot[0].GetSingle(), pivot[1].GetSingle()) : new Vector2(0.5f, 0.5f);
        var frames = new List<SpriteFrame>();
        if (root.TryGetProperty("frames", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in list.EnumerateArray())
            {
                var frame = new SpriteFrame { Image = e.TryGetProperty("image", out var image) ? image.GetString() ?? "" : "" };
                if (e.TryGetProperty("rect", out var rect) && rect.ValueKind == JsonValueKind.Array && rect.GetArrayLength() >= 4)
                {
                    frame.X = rect[0].GetInt32(); frame.Y = rect[1].GetInt32(); frame.Width = rect[2].GetInt32(); frame.Height = rect[3].GetInt32();
                }
                if (e.TryGetProperty("duration", out var d) && d.TryGetSingle(out var duration)) frame.Duration = Math.Max(0, duration);
                if (e.TryGetProperty("event", out var ev)) frame.Event = ev.GetString() ?? "";
                frames.Add(frame);
            }
        }
        Frames = frames;
        Version++;
    }

    /// <summary>シートの格子からコマを作る (左上から右へ、上の行から下へ)。count = 0 は全部</summary>
    public static List<SpriteFrame> SliceGrid(string image, int imageWidth, int imageHeight, int columns, int rows, int count = 0,
                                              int marginX = 0, int marginY = 0, int spacingX = 0, int spacingY = 0)
    {
        columns = Math.Max(1, columns);
        rows = Math.Max(1, rows);
        int cellW = (imageWidth - marginX * 2 - spacingX * (columns - 1)) / columns;
        int cellH = (imageHeight - marginY * 2 - spacingY * (rows - 1)) / rows;
        var frames = new List<SpriteFrame>();
        if (cellW <= 0 || cellH <= 0) return frames;
        int total = count > 0 ? Math.Min(count, columns * rows) : columns * rows;
        for (int i = 0; i < total; i++)
        {
            int c = i % columns, r = i / columns;
            frames.Add(new SpriteFrame { Image = image, X = marginX + c * (cellW + spacingX), Y = marginY + r * (cellH + spacingY), Width = cellW, Height = cellH });
        }
        return frames;
    }

    public override string ToString() => $"SpriteAnimation({Path}, {Frames.Count} frames)";
}
