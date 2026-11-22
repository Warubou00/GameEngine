using System.Numerics;
using System.Reflection;
using GameEngine.Rendering;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// v2.2 : シーンビューの表示モード (シーンビューのツールバーの「View」のプルダウン。RE Engine の表示の切り替えと同じ考え方)
//   Default      : 普段の描画
//   DrawMap      : どこに描画の負荷が掛かっているかを 青 → 赤 で出す (重なり / ライトの数 / 合わせた推定)
//   CollisionMap : コリジョンの形だけを塗りつぶしで出す (状態 / レイヤーで色分け)
//   プロジェクトごとに足せる : Project/Scripts/Editor (GameEditor.csproj) に
//     [SceneViewMode("名前")] public sealed class MyMode : SceneViewMode { ... } を書くと、プルダウンに出る (保存すると読み直す)
//   表示のモードはシーンビュー (メインのカメラ) の描画だけを変える。ゲームのカメラの設定 / 書き出したゲームには関係しない
// ==========================================================================

/// <summary>この SceneViewMode をシーンビューの View のプルダウンに出す (order の小さい順。組み込みは 0〜20)</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SceneViewModeAttribute : Attribute
{
    public string Name  { get; }
    public int    Order { get; }
    public SceneViewModeAttribute(string name, int order = 100) { Name = name; Order = order; }
}

/// <summary>
/// シーンビューの表示のモード。選ばれている間だけ動く (1 つの型に 1 つ作られる)
///   CreatePipeline : メインのカメラをこのパイプラインで描く (null なら普段の描き方のまま)。選んだ時に 1 回作り、外す時に Dispose する
///   OnEnable / OnDisable : 選んだ / 外した時 (描画の設定を一時的に変えるなら OnDisable で戻す)
///   OnUpdate : 毎フレーム (DebugDraw の線 / 三角形を足す など)
///   OnOptionsGUI : プルダウンの中の設定 (ImGui) / OnOverlayGUI : シーンビューの絵の上 (凡例など)
/// </summary>
public abstract class SceneViewMode
{
    public string Name { get; internal set; } = "";
    public World  World  => EditorApi.World;
    public Engine Engine => EditorApi.Engine;

    public virtual RenderPipeline? CreatePipeline() => null;
    public virtual void OnEnable() { }
    public virtual void OnDisable() { }
    public virtual void OnUpdate() { }
    public virtual void OnOptionsGUI() { }
    public virtual void OnOverlayGUI(ImDrawListPtr draw, Vector2 min, Vector2 size) { }

    /// <summary>凡例の色の帯 (左 → 右に colors を並べる)。OnOverlayGUI から使う</summary>
    protected static void DrawLegend(ImDrawListPtr draw, Vector2 min, Vector2 size, string title, ReadOnlySpan<(uint Color, string Label)> items)
    {
        const float row = 18.0f;
        var box = new Vector2(Math.Min(260.0f, size.X - 16.0f), 26.0f + row * items.Length);
        var origin = min + new Vector2(size.X - box.X - 8.0f, size.Y - box.Y - 8.0f);
        draw.AddRectFilled(origin, origin + box, 0xC0101012u, 4.0f);
        draw.AddText(origin + new Vector2(8, 5), 0xFFFFFFFFu, title);
        for (int i = 0; i < items.Length; i++)
        {
            var p = origin + new Vector2(8, 24 + i * row);
            draw.AddRectFilled(p, p + new Vector2(14, 12), items[i].Color);
            draw.AddText(p + new Vector2(20, -2), 0xFFE0E0E0u, items[i].Label);
        }
    }
}

// ==========================================================================
// モードの一覧と今のモード (EditorApp が持つ)
// ==========================================================================
internal sealed class SceneViewModes
{
    private readonly Engine _engine;
    private readonly List<SceneViewMode> _builtIn;
    private SceneViewMode? _current;         // null = Default
    private RenderPipeline? _pipeline;
    private string? _restoreName;           // ゲームのコードを読み直した後に戻すモードの名前

    public SceneViewModes(Engine engine)
    {
        _engine = engine;
        _builtIn = [new DrawMapViewMode { Name = "DrawMap" }, new CollisionMapViewMode { Name = "CollisionMap" }];
        EditorExtensions.BeforeClear += beforeExtensionsClear;
    }

    public const string DefaultName = "Default";
    public string CurrentName => _current?.Name ?? DefaultName;
    public SceneViewMode? Current => _current;

    /// <summary>選べるモード (Default を除く。組み込み → プロジェクトの物。order の順)</summary>
    public IEnumerable<SceneViewMode> Modes =>
        _builtIn.Select(m => (Mode: m, Order: m is DrawMapViewMode ? 0 : 10))
                .Concat(EditorExtensions.ViewModes.Select(v => (Mode: v.Mode, Order: v.Order)))
                .OrderBy(x => x.Order).Select(x => x.Mode);

    public IEnumerable<string> Names => new[] { DefaultName }.Concat(Modes.Select(m => m.Name));

    /// <summary>名前で選ぶ (無い名前なら false。Default で普段の描画)</summary>
    public bool Select(string name)
    {
        if (name == DefaultName) { set(null); return true; }
        var mode = Modes.FirstOrDefault(m => m.Name == name);
        if (mode == null) return false;
        set(mode);
        return true;
    }

    private void set(SceneViewMode? mode)
    {
        if (ReferenceEquals(mode, _current)) return;
        if (_current != null)
        {
            try { _current.OnDisable(); } catch (Exception ex) { log(_current, "OnDisable", ex); }
        }
        _engine.Graphics.ViewPipelineOverride = null;
        _pipeline?.Dispose();
        _pipeline = null;
        _current = mode;
        if (mode == null) return;
        try
        {
            mode.OnEnable();
            _pipeline = mode.CreatePipeline();
            _engine.Graphics.ViewPipelineOverride = _pipeline;
        }
        catch (Exception ex)
        {
            log(mode, "OnEnable", ex);
            _pipeline?.Dispose();
            _pipeline = null;
            _engine.Graphics.ViewPipelineOverride = null;
            _current = null;   // 使えないモードは外す
        }
    }

    // 毎フレーム (エディターの GUI の始め)
    public void Update()
    {
        if (_restoreName != null && Modes.Any(m => m.Name == _restoreName)) { var name = _restoreName; _restoreName = null; Select(name); }
        if (_current == null) return;
        try { _current.OnUpdate(); }
        catch (Exception ex) { log(_current, "OnUpdate", ex); set(null); }
    }

    // シーンビューのツールバーのプルダウン
    public void DrawCombo()
    {
        ImGui.SetNextItemWidth(150.0f);
        if (!ImGui.BeginCombo("##ViewMode", $"View : {CurrentName}")) return;
        foreach (var name in Names.ToList())
        {
            if (ImGui.Selectable(name, name == CurrentName)) Select(name);
        }
        if (_current != null)
        {
            ImGui.Separator();
            ImGui.TextDisabled($"{_current.Name} options");
            try { _current.OnOptionsGUI(); }
            catch (Exception ex) { log(_current, "OnOptionsGUI", ex); }
        }
        ImGui.EndCombo();
    }

    // シーンビューの絵の上
    public void DrawOverlay(ImDrawListPtr draw, Vector2 min, Vector2 size)
    {
        if (_current == null) return;
        try { _current.OnOverlayGUI(draw, min, size); }
        catch (Exception ex) { log(_current, "OnOverlayGUI", ex); set(null); }
    }

    // ゲームのコードを読み直す前 : プロジェクトのモードを外す (古い DLL の型を持たないように)。読み直した後に名前で戻す
    private void beforeExtensionsClear()
    {
        if (_current == null || _builtIn.Contains(_current)) return;
        _restoreName = _current.Name;
        set(null);
    }

    private static void log(SceneViewMode mode, string where, Exception ex) =>
        Console.WriteLine($"[SceneView] {mode.Name}.{where} : {(ex is TargetInvocationException t ? t.InnerException ?? ex : ex).Message}");
}

// ==========================================================================
// DrawMap : 描画の負荷の推定を 青 → 赤 で出す (hlslTemplate.shader の PSDrawMap)
//   1. 重なり : ScenePass.Overdraw で、深度を見ずに 1 を加算する (R16F。隠れた面 / 半透明も数える)
//   2. 深度   : 一番手前の面の位置 (ScenePass.Shadow を視点 0 で。深度だけ)
//   3. 全画面 : 重なり / その位置に届くライトの数 (クラスター) から色を作る
//   GPU の実測ではない (D3D12 では物ごとの GPU 時間は取れない)。自分のシェーダーの物 / パーティクルは数えない
// ==========================================================================
internal sealed class DrawMapViewMode : SceneViewMode
{
    public enum Metric { Combined = 0, Overdraw = 1, Lights = 2 }
    public Metric Kind = Metric.Combined;
    public float MaxValue = 8.0f;      // 赤になる値
    public float LightWeight = 0.5f;   // Combined : ライト 1 つの重み
    public float Shape = 0.6f;         // 形の陰の強さ

    public override RenderPipeline? CreatePipeline() => new DrawMapPipeline(this);

    public override void OnOptionsGUI()
    {
        var kind = (int)Kind;
        if (ImGui.Combo("Metric", ref kind, "Combined\0Overdraw\0Lights\0")) Kind = (Metric)kind;
        ImGui.SliderFloat("Red at", ref MaxValue, 1.0f, 32.0f, "%.0f");
        if (Kind == Metric.Combined) ImGui.SliderFloat("Light weight", ref LightWeight, 0.0f, 2.0f, "%.2f");
        ImGui.SliderFloat("Shape", ref Shape, 0.0f, 1.0f, "%.2f");
        ImGui.TextDisabled("Estimate (not measured GPU time)");
    }

    public override void OnOverlayGUI(ImDrawListPtr draw, Vector2 min, Vector2 size)
    {
        // 色の帯 (0 → 赤になる値) と目盛り
        var width = Math.Min(300.0f, size.X - 32.0f);
        var origin = min + new Vector2(size.X - width - 16.0f, size.Y - 58.0f);
        draw.AddRectFilled(origin - new Vector2(8, 22), origin + new Vector2(width + 8, 36), 0xC0101012u, 4.0f);
        var unit = Kind switch { Metric.Overdraw => "layers", Metric.Lights => "lights", _ => "layers x (1 + lights x w)" };
        draw.AddText(origin - new Vector2(0, 19), 0xFFFFFFFFu, $"DrawMap : {Kind} ({unit})");
        const int steps = 32;
        for (int i = 0; i < steps; i++)
        {
            var a = Heat(i / (float)steps);
            var b = Heat((i + 1) / (float)steps);
            var x0 = origin.X + width * i / steps;
            var x1 = origin.X + width * (i + 1) / steps;
            draw.AddRectFilledMultiColor(new Vector2(x0, origin.Y), new Vector2(x1, origin.Y + 12), a, b, b, a);
        }
        for (int i = 0; i <= 4; i++)
        {
            var x = origin.X + width * i / 4;
            var label = (MaxValue * i / 4).ToString(MaxValue * i / 4 % 1 == 0 ? "0" : "0.0");
            draw.AddText(new Vector2(x - 4, origin.Y + 14), 0xFFD0D0D0u, i == 4 ? label + "+" : label);
        }
    }

    // シェーダーの heatColor と同じ色 (凡例)
    internal static uint Heat(float t)
    {
        ReadOnlySpan<Vector3> stops = [new(0.05f, 0.10f, 0.60f), new(0.0f, 0.70f, 0.95f), new(0.10f, 0.85f, 0.25f), new(1.0f, 0.90f, 0.10f), new(1.0f, 0.12f, 0.05f)];
        var x = Math.Clamp(t, 0.0f, 1.0f) * 4.0f;
        var i = Math.Min((int)x, 3);
        var c = Vector3.Lerp(stops[i], stops[i + 1], x - i);
        // 画面は sRGB : シェーダーの値 (線形のままトーンマップせずに出す) と同じに見えるよう、そのまま使う
        return ImGui.GetColorU32(new Vector4(c, 1.0f));
    }

    private sealed class DrawMapPipeline : RenderPipeline
    {
        private readonly DrawMapViewMode _mode;
        private RenderTexture? _overdraw;
        private FullscreenShader? _shader;
        public DrawMapPipeline(DrawMapViewMode mode) { _mode = mode; }
        public override string Name => "DrawMap";

        protected override void Initialize(GraphicsSettings settings) { }

        protected override void Render(RenderCommandBuffer cmd, GraphicsSettings settings)
        {
            _overdraw ??= RenderTexture.Create(RenderTextureFormat.R16F, name: "DrawMap Overdraw");
            _shader   ??= FullscreenShader.Load("../Shader/hlslTemplate.shader", "PSDrawMap");
            using (cmd.BeginPass("DrawMap Overdraw"))
            {
                cmd.SetRenderTarget(_overdraw);
                cmd.ClearColor(Vector4.Zero);
                cmd.DrawScene(ScenePass.Overdraw, RenderQueue.Opaque);
                cmd.DrawScene(ScenePass.Overdraw, RenderQueue.Transparent);
            }
            using (cmd.BeginPass("DrawMap Depth"))
            {
                cmd.SetRenderTargets(ReadOnlySpan<RenderTexture>.Empty, RenderTexture.SceneDepth);
                cmd.ClearDepth();
                cmd.DrawScene(ScenePass.Shadow, RenderQueue.Opaque);   // 視点 0 = カメラ。深度だけ
            }
            using (cmd.BeginPass("DrawMap"))
            {
                cmd.SetRenderTarget(settings.CameraTarget);
                cmd.DrawFullscreen(_shader, [_overdraw, RenderTexture.SceneDepth], [(float)_mode.Kind, _mode.MaxValue, _mode.LightWeight, _mode.Shape]);
            }
        }

        public override void Dispose()
        {
            _overdraw?.Dispose();
            _overdraw = null;
            base.Dispose();
        }
    }
}

// ==========================================================================
// CollisionMap : コリジョンの形だけを塗りつぶしで出す
//   普段の物は描かず、暗い背景に Physics.ShowCollidersSolid の三角形 (CollisionSystem が DebugDraw に足す) を深度つきで描く
//   色 : 状態 (動かない物 / 剛体 / 眠っている / 重なっている / トリガー / アセットの部品) かレイヤー
// ==========================================================================
internal sealed class CollisionMapViewMode : SceneViewMode
{
    // v2.2 : 描き方 (Solid : 塗りつぶし / Wireframe : 辺だけ。裏の辺も透けて見える / SolidWireframe : 塗りつぶし + 暗くした辺 (隠れた辺は出ない))
    public enum DrawStyle { Solid = 0, Wireframe = 1, SolidWireframe = 2 }
    public DrawStyle Style = DrawStyle.Solid;
    public bool ByLayer;
    private bool _savedLines, _savedSolid, _savedByLayer;

    public override RenderPipeline? CreatePipeline() => new CollisionMapPipeline(this);

    public override void OnEnable()
    {
        var physics = Engine.Physics;
        _savedLines = physics.ShowColliders; _savedSolid = physics.ShowCollidersSolid; _savedByLayer = physics.ColliderColorsByLayer;
        physics.ShowCollidersSolid = true;
    }

    public override void OnDisable()
    {
        var physics = Engine.Physics;
        physics.ShowColliders = _savedLines; physics.ShowCollidersSolid = _savedSolid; physics.ColliderColorsByLayer = _savedByLayer;
    }

    public override void OnUpdate()
    {
        Engine.Physics.ShowCollidersSolid    = true;
        Engine.Physics.ColliderColorsByLayer = ByLayer;
    }

    public override void OnOptionsGUI()
    {
        var style = (int)Style;
        if (ImGui.Combo("Style", ref style, "Solid\0Wireframe\0Solid + Wireframe\0")) Style = (DrawStyle)style;
        ImGui.Checkbox("Color by layer", ref ByLayer);
        ImGui.TextDisabled($"{DebugDraw.LastTriangleCount} triangles last frame");
    }

    public override void OnOverlayGUI(ImDrawListPtr draw, Vector2 min, Vector2 size)
    {
        if (ByLayer)
        {
            var names = new List<(uint, string)>();
            for (int i = 0; i < 6; i++) { var h = CollisionSystemColors.Layer(i); names.Add((h, $"Layer {i}")); }
            DrawLegend(draw, min, size, "CollisionMap : layers (trigger = translucent)", names.ToArray());
            return;
        }
        DrawLegend(draw, min, size, "CollisionMap",
        [
            (rgb(0.62f, 0.62f, 0.64f), "Static"),
            (rgb(0.35f, 0.9f, 0.45f),  "Moving / rigid body"),
            (rgb(0.35f, 0.55f, 1.0f),  "Sleeping rigid body"),
            (rgb(1.0f, 0.3f, 0.25f),   "Overlapping"),
            (rgb(1.0f, 0.85f, 0.2f),   "Trigger (translucent)"),
            (rgb(0.45f, 0.75f, 0.85f), "Collision asset part (static)"),
        ]);
    }

    private static uint rgb(float r, float g, float b) => ImGui.GetColorU32(new Vector4(r, g, b, 1.0f));

    // 自分の描画先 (背景の色を最適化クリア値にして作る) に描いてから、カメラの描画先へ写す
    //   (カメラの描画先を別の色で消すと、D3D12 のデバッグレイヤーが「クリア値が違う (遅い)」と警告するため)
    private sealed class CollisionMapPipeline : RenderPipeline
    {
        private static readonly Vector4 Background = new(0.09f, 0.09f, 0.11f, 1.0f);
        private readonly CollisionMapViewMode _mode;
        private RenderTexture? _color;
        private FullscreenShader? _copy;
        public CollisionMapPipeline(CollisionMapViewMode mode) { _mode = mode; }
        public override string Name => "CollisionMap";
        protected override void Initialize(GraphicsSettings settings) { }

        protected override void Render(RenderCommandBuffer cmd, GraphicsSettings settings)
        {
            _color ??= RenderTexture.Create(RenderTextureFormat.RGBA8, 1.0f, Background, name: "CollisionMap");
            _copy  ??= FullscreenShader.Load("../Shader/postProcess.shader", "PSComposite");
            using var pass = cmd.BeginPass("CollisionMap");
            cmd.SetRenderTarget(_color, RenderTexture.SceneDepth);
            cmd.ClearColor(Background);
            cmd.ClearDepth();
            switch (_mode.Style)
            {
            case DrawStyle.Wireframe:
                cmd.DrawDebugTriangles(wireframe: true);   // 深度は空のまま : 裏の辺も見える (X 線のように)
                break;
            case DrawStyle.SolidWireframe:
                cmd.DrawDebugTriangles();
                cmd.DrawDebugTriangles(wireframe: true, brightness: 0.45f);   // 塗りつぶしの深度で隠れた辺は出ない
                break;
            default:
                cmd.DrawDebugTriangles();
                break;
            }
            cmd.SetRenderTarget(settings.CameraTarget);
            cmd.DrawFullscreen(_copy, [_color], []);
        }

        public override void Dispose()
        {
            _color?.Dispose();
            _color = null;
            base.Dispose();
        }
    }
}

// 凡例のレイヤーの色 (CollisionDraw.LayerHue。CollisionMap の三角形と同じ色)
internal static class CollisionSystemColors
{
    public static uint Layer(int layer)
    {
        var c = CollisionDraw.LayerHue(layer);
        return ImGui.GetColorU32(new Vector4(c, 1.0f));
    }
}
