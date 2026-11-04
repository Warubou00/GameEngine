using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// Project Settings の窓 (ED6。Edit > Project Settings)
//   ProjectSettings (時間 / 物理 / レイヤーの名前) をインスペクターと同じ部品で出し、レイヤーの衝突の表を三角のチェックの並びで出す
//   値を変えるとすぐ engine へ書き (Apply)、触り終わったら プロジェクトのフォルダの ProjectSettings.json に書く
//   ※プロジェクトの設定の変更は元に戻せない (シーンの変更ではないので)
// ==========================================================================
internal sealed class ProjectSettingsWindow
{
    private readonly Engine _engine;
    private readonly Inspector _inspector;
    private readonly string _file;
    private bool _dirty;

    public ProjectSettings Settings { get; private set; }

    public ProjectSettingsWindow(Engine engine, Inspector inspector, string file)
    {
        _engine = engine;
        _inspector = inspector;
        _file = file;
        try
        {
            Settings = ProjectSettings.Load(file);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ProjectSettings] Failed to read {file} : {ex.Message} (using defaults)");
            Settings = new ProjectSettings();
        }
        Settings.Apply(engine);
        Console.WriteLine($"[ProjectSettings] {(File.Exists(file) ? "Loaded" : "Defaults (no file yet)")} : {file}");
    }

    public void Draw(ref bool open)
    {
        if (!open) { saveIfDirty(); return; }
        ImGui.SetNextWindowSize(new Vector2(560, 640), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("Project Settings", ref open))
        {
            ImGui.TextDisabled(_file);
            var changed = _inspector.DrawObject(Settings);
            ImGui.SeparatorText("Layer Collision");
            changed |= drawLayerMatrix();
            if (changed)
            {
                Settings.Apply(_engine);
                _dirty = true;
            }
        }
        ImGui.End();
        if (_dirty && !ImGui.IsAnyItemActive()) saveIfDirty();
    }

    private void saveIfDirty()
    {
        if (!_dirty) return;
        Settings.Save(_file);
        _dirty = false;
        Console.WriteLine($"[ProjectSettings] Saved {_file}");
    }

    // 名前の付いたレイヤーどうしの表 (Unity と同じ三角)。チェック = 当たる
    private bool drawLayerMatrix()
    {
        var layers = Enumerable.Range(0, ProjectSettings.LayerCount).Where(i => !string.IsNullOrEmpty(Settings.LayerNames[i])).ToList();
        if (layers.Count == 0) { ImGui.TextDisabled("Name some layers above to edit the table."); return false; }
        var changed = false;
        if (ImGui.BeginTable("layers", layers.Count + 1, ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("");
            foreach (var layer in layers) ImGui.TableSetupColumn(layer.ToString());
            ImGui.TableHeadersRow();
            foreach (var a in layers)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text($"{a} : {Settings.LayerName(a)}");
                foreach (var b in layers)
                {
                    ImGui.TableNextColumn();
                    if (b < a) continue;   // 対称なので半分だけ
                    var collide = Settings.GetLayerCollision(a, b);
                    if (ImGui.Checkbox($"##{a}_{b}", ref collide))
                    {
                        Settings.SetLayerCollision(a, b, collide);
                        changed = true;
                    }
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{Settings.LayerName(a)} x {Settings.LayerName(b)}");
                }
            }
            ImGui.EndTable();
        }
        return changed;
    }
}
