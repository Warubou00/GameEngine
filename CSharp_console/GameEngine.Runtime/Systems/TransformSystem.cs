using System.Numerics;

namespace GameEngine;

// ==========================================================================
// Transform の行列を作る (エンジン組み込み。登録した System の後に動く)
//   - 行列が前のフレームから変わった物だけ Changed を立てる (描画へ送るのはその物だけ)
//   - Static が付いた物は最初の 1 回だけ (MarkDirty で作り直し)
//   詰めた配列を直接回す (ForEach の delegate を通さない分速い。1 万個で 0.1 ms 程度)
// ==========================================================================
internal sealed class TransformSystem : ISystem
{
    // このフレームで行列が変わった数 (0 なら RenderSyncSystem は送る物を探さない)
    // 剛体の位置を書いた CollisionSystem も足す (以前は TransformSystem だけが数えていたので、物理だけが動かした物が描画へ送られなかった)
    public int ChangedCount { get; internal set; }

    public void Update(World world)
    {
        var transforms = world.GetPool<Transform>();
        var statics    = world.GetPool<Static>();
        var entities   = transforms.Entities;
        var components = transforms.Components;
        var changed    = 0;

        for (int i = 0; i < components.Length; i++)
        {
            ref var t = ref components[i];
            if (t.HasMatrix && statics.Has(entities[i].Id)) continue;   // 動かない物 (Changed は送った側が下ろす)

            t.Changed = false;
            var matrix = Matrix4x4.CreateScale(t.Scale) *
                         Matrix4x4.CreateFromQuaternion(t.Rotation) *
                         Matrix4x4.CreateTranslation(t.Position);
            if (!t.HasMatrix || matrix != t.LocalToWorld)
            {
                t.LocalToWorld = matrix;
                t.HasMatrix    = true;
                t.Changed      = true;
                changed++;
            }
        }
        ChangedCount = changed;
    }
}
