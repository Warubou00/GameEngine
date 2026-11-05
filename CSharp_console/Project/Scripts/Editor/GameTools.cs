using System.Globalization;
using System.Numerics;
using GameEngine;
using GameEngine.Editor;
using ImGuiNET;

namespace GameEngine_console.Editor;

// ==========================================================================
// 独自の窓 / メニュー / アセットの取り込みの例 (ED7 後半)
// ==========================================================================

// 窓 : ゲームのコンポーネントの数と、まとめて変えるボタン (Game > Game Stats)
[EditorWindow("Game/Game Stats")]
public sealed class GameStatsWindow : EditorWindow
{
    public override string Title => "Game Stats";

    public override void OnGUI()
    {
        ImGui.Text($"Spin        {World.GetPool<Spin>().Count}");
        ImGui.Text($"OrbitMover  {World.GetPool<OrbitMover>().Count}");
        ImGui.Text($"StackBlock  {World.GetPool<StackBlock>().Count}");
        ImGui.Text($"Firework    {World.GetPool<Firework>().Count}");
        ImGui.Separator();
        if (ImGui.Button("Stop all spins"))
        {
            foreach (var entity in World.Entities.Where(e => World.Has<Spin>(e)).ToList())
                EditorApi.Modify<Spin>(entity, s => s with { DegreesPerSecond = 0.0f });   // 元に戻せる
        }
    }
}

public static class GameMenu
{
    // メニュー : カメラの前に回る箱を置く (Game > Create Spinning Box)
    [MenuItem("Game/Create Spinning Box")]
    public static void CreateSpinningBox()
    {
        var box = EditorApi.Engine.Assets.LoadModel("Assets/Models/obj/box.obj");
        var position = EditorApi.CameraPosition + EditorApi.CameraForward * 6.0f;
        EditorApi.CreateEntity("Spinning Box", position, entity =>
        {
            EditorApi.World.Add(entity, new MeshRenderer(box));
            EditorApi.World.Add(entity, new Spin { DegreesPerSecond = 90.0f });
        });
    }
}

// 取り込み : 色の一覧 (.palette.txt。1 行に「名前 #RRGGBB」) から、同じフォルダの「名前.material.json」を作る / 直す
[AssetImporter(".palette.txt")]
public sealed class PaletteImporter : AssetImporter
{
    public override string Label => "Palette";
    public override uint   Color => 0xFF4080F0;

    public override void OnImport(string assetPath, string fullPath)
    {
        var folder = assetPath[..assetPath.LastIndexOf('/')];
        foreach (var line in File.ReadAllLines(fullPath))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[1].StartsWith('#') || parts[1].Length != 7) continue;
            var rgb = int.Parse(parts[1][1..], NumberStyles.HexNumber);
            var color = new Vector4(((rgb >> 16) & 255) / 255.0f, ((rgb >> 8) & 255) / 255.0f, (rgb & 255) / 255.0f, 1.0f);
            var path = $"{folder}/{parts[0]}.material.json";
            var material = File.Exists(EditorApi.Assets.FullPath(path)) ? EditorApi.Engine.Assets.LoadMaterial(path)
                                                                         : EditorApi.Engine.Assets.CreateMaterialAsset(folder, parts[0]);
            material.Color = color;
            material.Apply();
            material.Save();
        }
    }
}
