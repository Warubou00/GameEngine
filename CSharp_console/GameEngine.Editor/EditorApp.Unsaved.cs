using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// 保存していない変更を聞く (ED10)
//   シーンに保存していない変更がある時に、エディターを閉じる (窓の × / File > Exit / Alt+F4) / シーンを替える
//   (New Scene / Open Scene / Project のシーンをダブルクリック / シーンをシーンビューへドロップ) と、
//   「Save / Don't Save / Cancel」の窓を出す。Save で保存 (名前が無ければ名前を付けて保存) してから続ける
//   再生中なら、止めてから (再生の前の状態を) 保存する。再生中の変更は元々保存されない
//   ※コード / 確認から呼ぶ OpenScene / Engine.Quit は聞かない。UI は RequestOpenScene / RequestNewScene / RequestQuit を使う
// ==========================================================================
public enum UnsavedChoice { Save, DontSave, Cancel }

internal sealed partial class EditorApp
{
    private sealed record PendingAction(string What, Action Run);
    private PendingAction? _pending;
    private bool _openPrompt;
    private bool _allowClose;   // 聞いた後 / ForceQuit : 窓を閉じてよい

    /// <summary>聞いている事 (無ければ null。確認用)</summary>
    public string? PendingPrompt => _pending?.What;

    public void RequestOpenScene(string path) => confirm($"opening {Path.GetFileName(path)}", () => OpenScene(path));
    public void RequestNewScene() => confirm("creating a new scene", newScene);
    public void RequestQuit() => confirm("closing the editor", ForceQuit);

    /// <summary>聞かずに閉じる (確認 / --quit-after)</summary>
    public void ForceQuit()
    {
        _allowClose = true;
        _engine.Quit();
    }

    // 変更が無ければすぐ、あれば聞いてから action
    private void confirm(string what, Action action)
    {
        if (!IsDirty) { action(); return; }
        _pending = new PendingAction(what, action);
        _openPrompt = true;
    }

    // 窓の × / Alt+F4 : 変更があれば閉じるのをやめて聞く (Windows の終了の時は止めない)
    private void onWindowClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || !IsDirty || e.CloseReason is CloseReason.WindowsShutDown or CloseReason.TaskManagerClosing) return;
        e.Cancel = true;
        RequestQuit();
    }

    /// <summary>聞いた答え (窓のボタン / 確認)</summary>
    public void ResolvePrompt(UnsavedChoice choice)
    {
        var pending = _pending;
        _pending = null;
        if (pending == null || choice == UnsavedChoice.Cancel) return;
        if (choice == UnsavedChoice.Save && !saveBeforeLeaving()) return;   // 名前を付ける窓でやめた → 何もしない
        pending.Run();
    }

    // 保存する (再生中なら止めてから)。戻り値 : 保存したか
    private bool saveBeforeLeaving()
    {
        if (_playing) stop();
        saveScene(false);   // 名前が無ければ名前を付ける窓 (やめると保存されない)
        return !IsDirty;
    }

    private void drawUnsavedPrompt()
    {
        if (_openPrompt) { ImGui.OpenPopup("Unsaved Changes"); _openPrompt = false; }
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal("Unsaved Changes", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings)) return;
        if (_pending == null) { ImGui.CloseCurrentPopup(); ImGui.EndPopup(); return; }
        var name = _scenePath != null ? Path.GetFileName(_scenePath) : "The new scene";
        ImGui.Text($"{name} has unsaved changes.");
        ImGui.Text($"Save them before {_pending.What}?");
        if (_playing) ImGui.TextDisabled("(play mode stops; changes made while playing are not saved)");
        ImGui.Spacing();
        UnsavedChoice? choice = null;
        if (ImGui.Button("Save", new Vector2(110, 0)) || ImGui.IsKeyPressed(ImGuiKey.Enter)) choice = UnsavedChoice.Save;
        ImGui.SetItemDefaultFocus();
        ImGui.SameLine();
        if (ImGui.Button("Don't Save", new Vector2(110, 0))) choice = UnsavedChoice.DontSave;
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(110, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape)) choice = UnsavedChoice.Cancel;
        if (choice is { } c)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            ResolvePrompt(c);   // 閉じてから (シーンを開く / 閉じるのは窓の外で)
            return;
        }
        ImGui.EndPopup();
    }
}
