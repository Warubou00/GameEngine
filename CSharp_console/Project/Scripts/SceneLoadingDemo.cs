using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// シーンの管理 (v1.14) の見本 : ロード画面つきの切り替え / サブシーン (シーン : Assets/Scenes/scenes_demo.scene.json)
//   N : 次のシーンへ非同期で切り替える (ロード画面 : 進み具合のバー。読み終わっても MinLoadingSeconds は出す = AllowActivation)
//   B : 次のシーンへ同期で切り替える (比べる用。読む間は画面が止まる)
//   1 : サブシーン (Room) の有効 / 待機     2 : サブシーン (Room) を外す / 非同期で読む
//   この物 (Scene Switcher) は DontDestroyOnLoad で残るので、切り替えた先のシーンでも N / B が効く
// ==========================================================================
public struct SceneSwitcher : IComponent
{
    [Tooltip("Scenes to cycle through (separated by ;)")]
    public string Scenes = "Assets/Scenes/scenes_demo.scene.json;Assets/Scenes/showcase.scene.json;Assets/Scenes/physics.scene.json";
    [Tooltip("Keep the loading screen at least this long (seconds)")]
    public float MinLoadingSeconds = 0.5f;
    [Tooltip("Name of the entity that holds the sub scene (keys 1 / 2)")]
    public string RoomName = "Room";

    public SceneSwitcher()
    {
    }
}

[GameSystem]
public sealed class SceneSwitcherSystem : Behaviour<SceneSwitcher>
{
    private Entity _owner;               // 残っている (DontDestroyOnLoad の) 切り替え役
    private int _index;
    private SceneOperation? _loading;
    private float _loadingTime;
    private UUserWidget? _screen;
    private UProgressBar? _bar;
    private UTextBlock? _text;
    private float _fadeOut;              // 切り替えた後、ロード画面を少しだけ残す (秒)

    protected override void Start(ref SceneSwitcher switcher)
    {
        // 同じシーンをもう一度読むと、また切り替え役ができる : 残っている方だけを使う
        if (World.IsAlive(_owner) && _owner != Entity)
        {
            Destroy(Entity);
            return;
        }
        _owner = Entity;
        Scenes.DontDestroyOnLoad(Entity);
        Console.WriteLine("[SceneDemo] N : next scene (async + loading screen) / B : next scene (sync) / 1 : room on / off / 2 : room unload / load");
    }

    protected override void Update(ref SceneSwitcher switcher)
    {
        if (Entity != _owner) return;
        if (_screen != null && _loading == null)
        {
            // 切り替えた後 : 少し残してから外す (新しいシーンの最初のフレームを隠す)
            _fadeOut -= Time.DeltaTime;
            if (_fadeOut <= 0) { _screen.RemoveFromParent(); _screen = null; }
        }
        var list = switcher.Scenes.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (_loading != null)
        {
            updateLoading(switcher);
            return;
        }
        if (list.Length > 0 && Input.GetKeyDown(Keys.N)) startAsync(list[nextIndex(list)]);
        if (list.Length > 0 && Input.GetKeyDown(Keys.B))
        {
            var path = list[nextIndex(list)];
            Scenes.LoadScene(path);   // ゲームのコードの中なので、次のフレームの頭で読む (そのフレームは読み終わるまで止まる)
            Console.WriteLine($"[SceneDemo] Sync load requested : {path}");
        }

        // サブシーン
        var room = Scenes.FindSubSceneOwner(switcher.RoomName);
        if (room.IsNull) return;
        if (Input.GetKeyDown(Keys.D1) && Scenes.GetSubScene(room) is { State: SceneStatus.Loaded } scene)
        {
            Scenes.SetActive(scene, !scene.IsActiveSelf);
            Console.WriteLine($"[SceneDemo] Room {(scene.IsActiveSelf ? "active" : "standby")}");
        }
        if (Input.GetKeyDown(Keys.D2))
        {
            if (Scenes.GetSubScene(room) is { State: SceneStatus.Loaded })
            {
                Scenes.UnloadSubScene(room);
                Console.WriteLine("[SceneDemo] Room unloaded");
            }
            else if (Scenes.GetSubScene(room) == null)
            {
                var op = Scenes.LoadSubSceneAsync(room, active: true);
                if (op != null) op.Completed += o => Console.WriteLine($"[SceneDemo] Room loaded ({o.Frames} frames)");
            }
        }
    }

    private int nextIndex(string[] list)
    {
        _index = (_index + 1) % list.Length;
        return _index;
    }

    // ロード画面を出して、非同期で読み始める
    private void startAsync(string path)
    {
        _loading = Scenes.LoadSceneAsync(path);
        _loading.AllowActivation = false;   // 読み終わっても、ロード画面を少しは見せてから切り替える
        _loadingTime = 0;
        showScreen(path);
        Console.WriteLine($"[SceneDemo] Async load started : {path}");
    }

    private void updateLoading(in SceneSwitcher switcher)
    {
        var op = _loading!;
        _loadingTime += Time.DeltaTime;
        // 見た目の進み : 読む分 (0 〜 0.9) と、待つ時間を合わせる
        var shown = MathF.Min(op.Progress / 0.9f, _loadingTime / MathF.Max(0.01f, switcher.MinLoadingSeconds));
        _bar!.SetPercent(op.IsDone ? 1 : shown);
        _text!.Text = $"Loading {op.Scene.Name} ... {(int)(shown * 100)} %";
        if (op.IsReadyToActivate && _loadingTime >= switcher.MinLoadingSeconds) op.AllowActivation = true;   // 次のフレームの頭で切り替わる
        if (!op.IsDone) return;
        Console.WriteLine($"[SceneDemo] {(op.IsCancelled ? "Cancelled" : "Switched")} : {op.Scene.Name} ({op.Frames} frames, longest step {op.MaxStepMilliseconds:F0} ms" +
                          $"{(op.SlowestEntityMilliseconds > 16 ? $", slowest '{op.SlowestEntity}'" : "")})");
        _fadeOut = 0.25f;
        _loading = null;
    }

    // ロード画面 (コードで組む Widget。KeepOnSceneChange で、切り替えの瞬間にも消えない)
    private void showScreen(string path)
    {
        var root = new UCanvasPanel { Name = "LoadingRoot" };
        var back = new UBorder { Name = "LoadingBack", Background = UIBrush.Rounded(new Vector4(0.05f, 0.06f, 0.08f, 0.92f), 0) };
        var backSlot = root.AddChildToCanvas(back, Vector2.Zero, Vector2.Zero)!;
        backSlot.Anchors = new UIAnchors(0, 0, 1, 1);
        var box = new UVerticalBox { Name = "LoadingBox" };
        var boxSlot = root.AddChildToCanvas(box, Vector2.Zero, new Vector2(520, 90))!;
        boxSlot.Anchors = new UIAnchors(0.5f, 0.5f);
        boxSlot.Alignment = new Vector2(0.5f, 0.5f);
        _text = new UTextBlock { Name = "LoadingText", Text = "Loading ...", Font = new UIFont(26, bold: true) };
        box.AddChild(_text);
        _bar = new UProgressBar { Name = "LoadingBar", Percent = 0, DesiredBarSize = new Vector2(520, 22) };
        box.AddChild(_bar)!.Padding = new UIMargin(0, 14, 0, 0);
        _screen = UI.CreateWidget(root);
        _screen.KeepOnSceneChange = true;
        _screen.AddToViewport(100);
    }
}
