using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// Project Settings の窓 (ED6。Edit > Project Settings)
//   ProjectSettings (時間 / 物理 / レイヤーの名前) をインスペクターと同じ部品で出し、レイヤーの衝突の表を三角のチェックの並びで出す
//   値を変えるとすぐ engine へ書き (Apply)、触り終わったら プロジェクトのフォルダの ProjectSettings.json に書く
//   ※プロジェクトの設定の変更は元に戻せない (シーンの変更ではないので)
//   Player (v1.3) : 書き出した exe のアイコン。Project の画像 (png / jpg / bmp) か .ico を落とす (Unity の Default Icon)
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
            var changed = drawPlayer();
            changed |= _inspector.DrawObject(Settings);
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

    // ---------------------------------------------------------------------
    // Player : exe のアイコン (v1.3)
    // ---------------------------------------------------------------------
    private string? _previewPath;
    private uint _previewTexture;

    private bool drawPlayer()
    {
        ImGui.SeparatorText("Player");
        var changed = false;
        var icon = Settings.Icon;
        // 見本の絵 (変わった時だけ作り直す)
        if (icon != _previewPath)
        {
            _previewPath = icon;
            _previewTexture = icon != null ? loadPreview(_engine.Assets.Database?.FullPath(icon)) : 0;
        }
        var size = new Vector2(64, 64);
        if (_previewTexture != 0) ImGui.Image((IntPtr)_previewTexture, size);
        else ImGui.Button("Default##iconPreview", size);
        if (dropIcon() is { } dropped) { Settings.Icon = dropped; changed = true; }
        ImGui.SameLine();
        ImGui.BeginGroup();
        ImGui.Text("Icon");
        ImGui.TextDisabled(icon ?? "(the engine icon) drop an image or .ico from Project");
        if (dropIcon() is { } dropped2) { Settings.Icon = dropped2; changed = true; }
        if (icon != null && ImGui.SmallButton("Clear")) { Settings.Icon = null; changed = true; }
        ImGui.EndGroup();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("The icon of the built game's exe (File > Build Game). Images are made into 16-256 px icons.");
        return changed;
    }

    // Project から落とされた画像 / .ico (アセットのパス)
    private static string? dropIcon()
    {
        if (!ImGui.BeginDragDropTarget()) return null;
        string? path = null;
        if (ProjectPanel.Dragged is { } dragged && (dragged.Kind == AssetKind.Texture && !dragged.Path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ||
                                                    dragged.Path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)))
        {
            var accepted = ProjectPanel.AcceptDrop(dragged.Kind);
            if (accepted != null) path = accepted.Path;
        }
        ImGui.EndDragDropTarget();
        return path;
    }

    // 見本の絵 (64x64 の RGBA にして UI のテクスチャへ)
    private uint loadPreview(string? file)
    {
        if (file == null || !File.Exists(file)) return 0;
        try
        {
            using var source = file.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? new System.Drawing.Icon(file, 64, 64).ToBitmap() : new System.Drawing.Bitmap(file);
            using var bitmap = new System.Drawing.Bitmap(64, 64, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, 64, 64);
            }
            var pixels = new byte[64 * 64 * 4];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    var c = bitmap.GetPixel(x, y);
                    var i = (y * 64 + x) * 4;
                    (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]) = (c.R, c.G, c.B, c.A);
                }
            return _engine.Graphics.CreateUITexture(64, 64, pixels);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or System.Runtime.InteropServices.ExternalException)
        {
            Console.WriteLine($"[ProjectSettings] Cannot read the icon {file} : {ex.Message}");
            return 0;
        }
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
