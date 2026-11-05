using System.Numerics;
using GameEngine;
using GameEngine.Editor;
using ImGuiNET;

namespace GameEngine_console.Editor;

// ==========================================================================
// 独自のインスペクターとギズモの例 (ED7 後半)
//   Spin : 速さをスライダーで、逆回し / 止めるボタン。選んだ物には回る向きの輪を描く
//   OrbitMover : 全部の物に、回る道 (輪) を描く (選んでいなくても)
// ==========================================================================
[CustomEditor(typeof(Spin))]
public sealed class SpinEditor : ComponentEditor<Spin>
{
    public override bool OnInspectorGUI(ref Spin spin)
    {
        var changed = ImGui.SliderFloat("Degrees / s", ref spin.DegreesPerSecond, -720.0f, 720.0f);
        if (ImGui.Button("Reverse")) { spin.DegreesPerSecond = -spin.DegreesPerSecond; changed = true; }
        ImGui.SameLine();
        if (ImGui.Button("Stop")) { spin.DegreesPerSecond = 0.0f; changed = true; }
        ImGui.TextDisabled(spin.DegreesPerSecond == 0.0f ? "not spinning" : $"one turn in {360.0f / MathF.Abs(spin.DegreesPerSecond):F1} s");
        return changed;
    }

    public override void OnDrawGizmosSelected(Gizmos gizmos, Spin spin)
    {
        if (!World.Has<Transform>(Entity)) return;
        var center = World.Get<Transform>(Entity).WorldPosition + new Vector3(0, 0.05f, 0);
        var color = Gizmos.Rgba(1.0f, 0.8f, 0.2f);
        gizmos.Circle(center, Vector3.UnitY, 1.2f, color);
        // 回る向きの矢印 (輪の手前)
        var sign = MathF.Sign(spin.DegreesPerSecond);
        var tip = center + new Vector3(0, 0, -1.2f);
        gizmos.Line(tip, tip + new Vector3(-0.3f * sign, 0, 0.15f), color);
        gizmos.Line(tip, tip + new Vector3(-0.3f * sign, 0, -0.15f), color);
        gizmos.Label(center + new Vector3(0, 0.3f, 0), $"{spin.DegreesPerSecond:F0} deg/s", color);
    }
}

[CustomEditor(typeof(OrbitMover))]
public sealed class OrbitMoverEditor : ComponentEditor<OrbitMover>
{
    public override void OnDrawGizmos(Gizmos gizmos, OrbitMover mover) =>
        gizmos.Circle(new Vector3(0, mover.Height, 0), Vector3.UnitY, mover.Radius, Gizmos.Rgba(0.3f, 0.9f, 1.0f, 0.6f));
}
