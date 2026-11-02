using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// 負荷テスト用のシーン (以前は C++ の SceneRenderer::SpawnTestGrid / SetTestInsideView / SetTestGroundView)
// ==========================================================================
internal static class StressTest
{
    // モデルを count 個、正方形に近い格子 (side × side) に並べる。原点が中心。戻り値 = 格子の一辺の長さ
    // animate : 全部を回し続ける (毎フレーム全部の行列が変わる = C# → C++ へまとめて送る量が最大の場合を測る)
    public static float SpawnGrid(World world, Model model, int count, float spacing, bool animate = false)
    {
        var side = 1;
        while (side * side < count) side++;
        var half = (side - 1) * spacing * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var x = (i % side) * spacing - half;
            var z = (i / side) * spacing - half;

            var entity = world.CreateEntity();
            world.Add(entity, new Transform(new Vector3(x, 0, z)));
            world.Add(entity, new MeshRenderer(model));
            if (animate) world.Add(entity, new Spin { DegreesPerSecond = 45.0f + (i % 7) * 15.0f });
            else         world.Add(entity, new Static());   // 動かないので、行列は最初の 1 回だけ計算して送る
        }
        return side * spacing;
    }

    // カメラの置き方 (以前の C++ のテスト用カメラと同じ位置)
    public static Transform CameraFor(string view, float extent)
    {
        switch (view)
        {
            case "inside":
                // 格子の中心の少し上から、+Z 方向 (格子の奥) を見る。後ろ半分と左右の外側は映らない
                return Transform.LookingAt(new Vector3(0, 4, 0), new Vector3(0, 0, 40));
            case "ground":
                // 格子の手前の端の外、箱 (高さ 2) の半分の高さから奥を斜めに見る。手前の箱で奥がほとんど隠れる
                return Transform.LookingAt(new Vector3(0, 1, -152), new Vector3(25, 1, 0));
            default:
                // 全体が見えるように引いて、斜め上から原点を見る
                var distance = MathF.Max(10.0f, extent * 0.9f);
                return Transform.LookingAt(new Vector3(0, distance * 0.8f, -distance), Vector3.Zero);
        }
    }
}
