using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameEngine;

// ==========================================================================
// プロジェクトの設定 (ED6)。プロジェクトのフォルダの ProjectSettings.json (Assets の外。全部のシーンで同じ)
//   - 時間 : 固定の時間刻み / 1 フレームで進める刻みの上限
//   - 物理 : 重力 / 解き方 / 眠り / 並列のスレッド / レイヤーの名前と衝突の表
//   以前はコード (engine.Physics.Solver / Time.FixedDeltaTime / SetLayerCollision) で設定するだけだった
//   既定の値は今までのエンジンの既定と同じ (ファイルが無くても今まで通り)
//   Apply(engine) で engine へ書く。エディターは起動時と値を変えた時に呼ぶ
// ==========================================================================
public sealed class ProjectSettings
{
    public const int LayerCount = 32;
    public const string FileName = "ProjectSettings.json";

    // 今の設定 (インスペクターがレイヤーの名前を出すのに使う。Load / Apply で変わる)
    public static ProjectSettings Current { get; set; } = new();

    [Header("Time")]
    // 書き出した exe のアイコン (v1.3。Unity の Player Settings > Icon)。アセットのパス (png / jpg / bmp / ico)。null = エンジンのアイコン
    //   Project Settings の窓の Player に出す (インスペクターの部品ではなく、画像を落とす欄)
    [HideInInspector] public string? Icon;

    [Range(0.001f, 0.1f)] public float FixedDeltaTime = 1.0f / 60.0f;
    [Range(1, 20)]        public int   MaxFixedSteps  = 5;

    public PhysicsProjectValues Physics = new();

    [Header("Layers")]
    [Tooltip("Collider.Layer の名前 (空の所は Layer N と出る)")]
    public string[] LayerNames = defaultLayerNames();
    // レイヤー a の物がどのレイヤーと当たるか (ビット)。対称に保つ (SetLayerCollision で両方を変える)
    [HideInInspector] public uint[] LayerCollision = Enumerable.Repeat(0xFFFFFFFFu, LayerCount).ToArray();

    [Header("Input")]
    [Tooltip("InputActions.Get / GetDown / GetUp(name) : どれかのキー / ボタン")]
    public InputActionBinding[] InputActions = defaultActions();
    [Tooltip("InputActions.GetAxis(name) : -1〜1")]
    public InputAxisBinding[] InputAxes = defaultAxes();

    private static InputActionBinding[] defaultActions() => new[]
    {
        new InputActionBinding { Name = "Jump",   Keys = new[] { Keys.Space },  Buttons = new[] { GamepadButton.A } },
        new InputActionBinding { Name = "Fire",   Keys = new[] { Keys.Enter },  Buttons = new[] { GamepadButton.RightTrigger } },
        new InputActionBinding { Name = "Cancel", Keys = new[] { Keys.Escape }, Buttons = new[] { GamepadButton.B } },
    };

    private static InputAxisBinding[] defaultAxes() => new[]
    {
        new InputAxisBinding { Name = "Horizontal", Positive = Keys.D, Negative = Keys.A, AltPositive = Keys.Right, AltNegative = Keys.Left, UseGamepadAxis = true, GamepadAxis = GamepadAxis.LeftX },
        new InputAxisBinding { Name = "Vertical",   Positive = Keys.W, Negative = Keys.S, AltPositive = Keys.Up,    AltNegative = Keys.Down, UseGamepadAxis = true, GamepadAxis = GamepadAxis.LeftY },
    };

    private static string[] defaultLayerNames()
    {
        var names = new string[LayerCount];
        for (int i = 0; i < LayerCount; i++) names[i] = "";
        names[0] = "Default";
        return names;
    }

    public string LayerName(int layer) =>
        (uint)layer < LayerCount && !string.IsNullOrEmpty(LayerNames.ElementAtOrDefault(layer)) ? LayerNames[layer] : $"Layer {layer}";

    public bool GetLayerCollision(int a, int b) => (uint)a < LayerCount && (uint)b < LayerCount && (LayerCollision[a] & (1u << b)) != 0;

    public void SetLayerCollision(int a, int b, bool collide)
    {
        if ((uint)a >= LayerCount || (uint)b >= LayerCount) return;
        if (collide) { LayerCollision[a] |= 1u << b; LayerCollision[b] |= 1u << a; }
        else         { LayerCollision[a] &= ~(1u << b); LayerCollision[b] &= ~(1u << a); }
    }

    // ---------------------------------------------------------------------
    // engine へ書く
    // ---------------------------------------------------------------------
    public void Apply(Engine engine)
    {
        Time.FixedDeltaTime = FixedDeltaTime;
        Time.MaxFixedSteps  = Math.Clamp(MaxFixedSteps, 1, 20);
        var solver = engine.Physics.Solver;
        var p = Physics;
        if (solver.Gravity != p.Gravity)                             solver.Gravity = p.Gravity;
        if (solver.SubSteps != p.SubSteps)                           solver.SubSteps = p.SubSteps;
        if (solver.Iterations != p.Iterations)                       solver.Iterations = p.Iterations;
        if (solver.RelaxIterations != p.RelaxIterations)             solver.RelaxIterations = p.RelaxIterations;
        if (solver.ContactHertz != p.ContactHertz)                   solver.ContactHertz = p.ContactHertz;
        if (solver.ContactDampingRatio != p.ContactDampingRatio)     solver.ContactDampingRatio = p.ContactDampingRatio;
        if (solver.ContactPushMaxVelocity != p.ContactPushMaxVelocity) solver.ContactPushMaxVelocity = p.ContactPushMaxVelocity;
        if (solver.RestitutionThreshold != p.RestitutionThreshold)   solver.RestitutionThreshold = p.RestitutionThreshold;
        if (solver.SpeculativeDistance != p.SpeculativeDistance)     solver.SpeculativeDistance = p.SpeculativeDistance;
        if (solver.JointHertz != p.JointHertz)                       solver.JointHertz = p.JointHertz;
        if (solver.JointDampingRatio != p.JointDampingRatio)         solver.JointDampingRatio = p.JointDampingRatio;
        if (solver.MaxLinearSpeed != p.MaxLinearSpeed)               solver.MaxLinearSpeed = p.MaxLinearSpeed;
        if (solver.EnableSleep != p.EnableSleep)                     solver.EnableSleep = p.EnableSleep;
        if (solver.TimeToSleep != p.TimeToSleep)                     solver.TimeToSleep = p.TimeToSleep;
        if (solver.SleepLinearVelocity != p.SleepLinearVelocity)     solver.SleepLinearVelocity = p.SleepLinearVelocity;
        if (solver.SleepAngularVelocity != p.SleepAngularVelocity)   solver.SleepAngularVelocity = p.SleepAngularVelocity;
        if (p.WorkerCount >= 0) engine.Physics.SetWorkerCount(p.WorkerCount);
        // レイヤーの表 : C++ と違う組だけ書く (書くと次の Step で全部の組を調べ直すので)
        for (int a = 0; a < LayerCount; a++)
        {
            for (int b = a; b < LayerCount; b++)
            {
                var collide = GetLayerCollision(a, b);
                if (engine.Physics.GetLayerCollision(a, b) != collide) engine.Physics.SetLayerCollision(a, b, collide);
            }
        }
        GameEngine.InputActions.Set(InputActions, InputAxes);
        Current = this;
    }

    // ---------------------------------------------------------------------
    // ファイル (JSON)
    // ---------------------------------------------------------------------
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        IncludeFields = true, WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        Converters = { new Vector3Converter(), new JsonStringEnumConverter() },   // 列挙は名前で書く (キーの名前が読める)
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    // 無い値は既定のまま (後から項目を足しても古いファイルを読める)
    public static ProjectSettings FromJson(string json)
    {
        var settings = JsonSerializer.Deserialize<ProjectSettings>(json, JsonOptions) ?? new ProjectSettings();
        // 配列の長さを 32 に揃える (手で書いたファイルなど)
        if (settings.LayerNames.Length != LayerCount) settings.LayerNames = settings.LayerNames.Concat(Enumerable.Repeat("", LayerCount)).Take(LayerCount).ToArray();
        if (settings.LayerCollision.Length != LayerCount) settings.LayerCollision = settings.LayerCollision.Concat(Enumerable.Repeat(0xFFFFFFFFu, LayerCount)).Take(LayerCount).ToArray();
        return settings;
    }

    public static ProjectSettings Load(string file) =>
        File.Exists(file) ? FromJson(File.ReadAllText(file, Encoding.UTF8)) : new ProjectSettings();

    public void Save(string file) => File.WriteAllText(file, ToJson(), new UTF8Encoding(false));

    private sealed class Vector3Converter : JsonConverter<Vector3>
    {
        public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var values = JsonSerializer.Deserialize<float[]>(ref reader) ?? Array.Empty<float>();
            return values.Length >= 3 ? new Vector3(values[0], values[1], values[2]) : Vector3.Zero;
        }

        public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X); writer.WriteNumberValue(value.Y); writer.WriteNumberValue(value.Z);
            writer.WriteEndArray();
        }
    }
}

// 物理の設定の値 (engine.Physics.Solver と同じ既定)
public struct PhysicsProjectValues
{
    public Vector3 Gravity = new(0.0f, -9.81f, 0.0f);
    [Range(1, 16)] public int SubSteps = 4;
    [Range(1, 8)]  public int Iterations = 1;
    [Range(0, 8)]  public int RelaxIterations = 1;
    public float ContactHertz = 30.0f;
    public float ContactDampingRatio = 10.0f;
    public float ContactPushMaxVelocity = 3.0f;
    public float RestitutionThreshold = 1.0f;
    public float SpeculativeDistance = 0.02f;
    public float JointHertz = 60.0f;
    public float JointDampingRatio = 2.0f;
    public float MaxLinearSpeed = 400.0f;
    [Header("Sleep")]
    public bool  EnableSleep = true;
    public float TimeToSleep = 0.5f;
    public float SleepLinearVelocity = 0.1f;
    public float SleepAngularVelocity = 2.0f;
    [Tooltip("Physics worker threads (-1 = auto : logical cores - 1, up to 3)")]
    [Range(-1, 16)] public int WorkerCount = -1;
    public PhysicsProjectValues() { }
}
