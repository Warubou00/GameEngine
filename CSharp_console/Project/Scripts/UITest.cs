using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// UI (v1.7) の確かめ : Widget の重ね表示と on / off (シーン : Assets/Scenes/ui.scene.json)
//   シーンに置いた 3 つの UIWidget : HUD (z 0) / Menu (z 10) / Popup (z 20、最初は隠す)
//   1 : HUD    の on / off (シーンの UIWidget コンポーネントの Visible を書き換える)
//   2 : Menu   の on / off (UserWidget の IsVisible をコードで切り替える)
//   3 : Popup  の on / off (Menu の「Popup を開く」/ Popup の「閉じる」でも)
//   4 : コードで組んだトースト (Widget アセットを使わない UserWidget。z 30) を出す / 外す
//   Z : Menu の重なり順を 10 ↔ 30 (Popup の暗い背景の手前 / 奥。手前の時だけ Menu を押せる)
//   Menu : スコア +10 / タブ (WidgetSwitcher) / 箱を回す (CheckBox) / 速さ (Slider) / 名前 (EditableTextBox → HUD に出す)
// ==========================================================================
public struct UITest : IComponent
{
    public float SpinSpeed;   // 度 / 秒
    public bool  Spin;
    public int   Score;
}

[GameSystem]
public sealed class UITestSystem : Behaviour<UITest>
{
    private UUserWidget? _hud, _menu, _popup, _toast;
    private Entity _hudEntity, _cube;
    private float _time;

    protected override void Start(ref UITest test)
    {
        test.Spin = true;
        _hud = _menu = _popup = _toast = null;
        _hudEntity = _cube = Entity.Null;
        foreach (var entity in World.Entities)
        {
            if (!World.Has<EntityInfo>(entity)) continue;
            var name = World.Get<EntityInfo>(entity).Name;
            if (name == "HUD") _hudEntity = entity;
            if (name == "Cube") _cube = entity;
        }
    }

    protected override void Update(ref UITest test)
    {
        // UIWidget の UserWidget は再生の最初のフレームの UI の更新で作られるので、できてから繋ぐ
        if (_menu == null && !bind(ref test)) return;
        _time += Time.DeltaTime;

        // キー (文字の欄を書いている間は使わない)
        if (UI.FocusedWidget == null)
        {
            if (Input.GetKeyDown(Keys.D1) && World.IsAlive(_hudEntity))
            {
                ref var widget = ref World.Get<UIWidget>(_hudEntity);
                widget.Visible = !widget.Visible;   // コンポーネントの値 → 次の UI の更新で反映
                Console.WriteLine($"[UITest] HUD {(widget.Visible ? "on" : "off")} (component)");
            }
            if (Input.GetKeyDown(Keys.D2)) { _menu!.IsVisible = !_menu.IsVisible; Console.WriteLine($"[UITest] Menu {(_menu.IsVisible ? "on" : "off")} (code)"); }
            if (Input.GetKeyDown(Keys.D3)) togglePopup();
            if (Input.GetKeyDown(Keys.D4)) toggleToast();
            if (Input.GetKeyDown(Keys.Z))
            {
                _menu!.SetZOrder(_menu.ZOrder == 10 ? 30 : 10);
                Console.WriteLine($"[UITest] Menu z {_menu.ZOrder} (Popup z {_popup?.ZOrder})");
            }
        }

        // HUD : HP の棒を動かす / スコア
        if (_hud != null)
        {
            var hp = 0.55f + 0.4f * MathF.Sin(_time * 1.3f);
            if (_hud.GetWidget<UProgressBar>("HpBar") is { } bar)
            {
                bar.SetPercent(hp);
                bar.SetFillColorAndOpacity(hp < 0.35f ? new Vector4(1, 0.35f, 0.3f, 1) : new Vector4(0.3f, 0.9f, 0.4f, 1));
            }
            _hud.GetWidget<UTextBlock>("Score")?.SetText($"Score : {test.Score}");
        }
        // 箱を回す
        if (test.Spin && World.IsAlive(_cube) && World.Has<Transform>(_cube))
        {
            ref var transform = ref World.Get<Transform>(_cube);
            transform.Rotation = Quaternion.Normalize(transform.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, test.SpinSpeed * MathEx.Deg2Rad * Time.DeltaTime));
        }
        // トーストは 3 秒で消える
        if (_toast != null && _toast.IsInViewport && _time > _toastUntil) _toast.RemoveFromParent();
    }

    // 部品のイベントを繋ぐ (一度だけ)
    private bool bind(ref UITest test)
    {
        foreach (var widget in UI.Widgets)
        {
            if (widget.Name == "HUD") _hud = widget;
            if (widget.Name == "Menu") _menu = widget;
        }
        // Popup は隠したまま作られる (Visible = false) ので、画面の一覧ではなくエンティティから探す
        foreach (var entity in World.Entities)
            if (World.Has<UIWidget>(entity) && UI.GetWidget(entity) is { Name: "Popup" } popup) _popup = popup;
        if (_menu == null) return false;

        var entity0 = Entity;   // イベントの中で自分のコンポーネントを書くため (ref は持てない)
        var world = World;
        _menu.GetWidget<UButton>("OpenPopup")!.OnClicked += togglePopup;
        _menu.GetWidget<UButton>("AddScore")!.OnClicked += () => { world.Get<UITest>(entity0).Score += 10; };
        var pages = _menu.GetWidget<UWidgetSwitcher>("Pages")!;
        _menu.GetWidget<UButton>("Tab1")!.OnClicked += () => pages.SetActiveWidgetIndex(0);
        _menu.GetWidget<UButton>("Tab2")!.OnClicked += () => pages.SetActiveWidgetIndex(1);
        var spin = _menu.GetWidget<UCheckBox>("SpinCheck")!;
        spin.IsChecked = test.Spin;
        spin.OnCheckStateChanged += on => world.Get<UITest>(entity0).Spin = on;
        var slider = _menu.GetWidget<USlider>("SpeedSlider")!;
        slider.SetValue(test.SpinSpeed);
        var speedLabel = _menu.GetWidget<UTextBlock>("SpeedLabel")!;
        speedLabel.SetText($"回る速さ : {test.SpinSpeed:F0}");
        slider.OnValueChanged += v => { world.Get<UITest>(entity0).SpinSpeed = v; speedLabel.SetText($"回る速さ : {v:F0}"); };
        _menu.GetWidget<UEditableTextBox>("NameBox")!.OnTextCommitted += (text, _) =>
            _hud?.GetWidget<UTextBlock>("Hello")?.SetText(string.IsNullOrWhiteSpace(text) ? "名前を入れて Enter" : $"こんにちは、{text} さん");
        // 色のボタン : HUD の題の色
        for (int i = 0; i < 6; i++)
        {
            var button = _menu.GetWidget<UButton>($"Color{i}");
            if (button == null) continue;
            var color = button.BackgroundColor;
            button.OnClicked += () => _hud?.GetWidget<UTextBlock>("Title")?.SetColorAndOpacity(color);
        }
        if (_popup != null)
        {
            _popup.GetWidget<UButton>("Close")!.OnClicked += togglePopup;
            _popup.GetWidget<UButton>("Ok")!.OnClicked += () => { world.Get<UITest>(entity0).Score += 100; togglePopup(); };
        }
        Console.WriteLine($"[UITest] bound : HUD {_hud != null}, Menu {_menu != null}, Popup {_popup != null}");
        return true;
    }

    private void togglePopup()
    {
        if (_popup == null) return;
        _popup.IsVisible = !_popup.IsVisible;
        Console.WriteLine($"[UITest] Popup {(_popup.IsVisible ? "on" : "off")}");
    }

    private float _toastUntil;

    // Widget のアセットを使わず、コードで組む (UE の C++ で Slate / UMG を組むのと同じ)
    private void toggleToast()
    {
        if (_toast is { IsInViewport: true }) { _toast.RemoveFromParent(); return; }
        var root = new UCanvasPanel { Name = "ToastRoot" };
        var border = new UBorder { Name = "Toast", Background = UIBrush.Rounded(new Vector4(0.2f, 0.45f, 0.9f, 0.95f), 10) };
        var slot = root.AddChildToCanvas(border, new Vector2(0, 120), Vector2.Zero)!;
        slot.Anchors = new UIAnchors(0.5f, 0);
        slot.Alignment = new Vector2(0.5f, 0);
        slot.AutoSize = true;
        border.AddChild(new UTextBlock { Name = "ToastText", Text = $"コードで作った Widget (z 30)  {DateTime.Now:HH:mm:ss}", Font = new UIFont(26, bold: true) })!.Padding = new UIMargin(28, 14);
        _toast = UI.CreateWidget(root);
        _toast.AddToViewport(30);
        _toastUntil = _time + 3.0f;
        Console.WriteLine("[UITest] Toast on (code)");
    }
}
