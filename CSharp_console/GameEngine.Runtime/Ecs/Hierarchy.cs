using System.Numerics;

namespace GameEngine;

// ==========================================================================
// 親子の操作 (ED2)
//   SetParent      : 親を変える (keepWorld = true ならワールドの位置 / 回転 / 大きさを保つ。輪になる時は変えない)
//   GetChildren    : 子の一覧 (全部の Transform を見る。エディター / ツール用で、毎フレーム使う物ではない)
//   DestroyWithChildren : 子 (孫も) と一緒に消す (World.DestroyEntity だけだと、子は親の無い物になる)
//   GetWorldMatrix : 今のワールドの行列を親から計算する (TransformSystem を待たずに欲しい時)
// ==========================================================================
public static class Hierarchy
{
    private const int MaxDepth = 64;

    public static Matrix4x4 GetWorldMatrix(World world, Entity entity)
    {
        var transforms = world.GetPool<Transform>();
        var matrix = Matrix4x4.Identity;
        var current = entity;
        for (int depth = 0; depth < MaxDepth && world.IsAlive(current) && transforms.Has(current.Id); depth++)
        {
            ref var t = ref transforms.Get(current.Id);
            matrix *= t.LocalMatrix;
            current = t.Parent;
            if (current.IsNull) break;
        }
        return matrix;
    }

    /// <summary>ancestor が entity の親 / 祖先か (entity 自身も含む)</summary>
    public static bool IsAncestor(World world, Entity ancestor, Entity entity)
    {
        var transforms = world.GetPool<Transform>();
        var current = entity;
        for (int depth = 0; depth < MaxDepth && world.IsAlive(current); depth++)
        {
            if (current == ancestor) return true;
            if (!transforms.Has(current.Id)) return false;
            current = transforms.Get(current.Id).Parent;
            if (current.IsNull) return false;
        }
        return false;
    }

    /// <summary>親を変える (parent = Entity.Null で親を外す)。戻り値 : 変えたか (輪になる / Transform が無い時は false)</summary>
    public static bool SetParent(World world, Entity child, Entity parent, bool keepWorld = true)
    {
        var transforms = world.GetPool<Transform>();
        if (!world.IsAlive(child) || !transforms.Has(child.Id)) return false;
        if (!parent.IsNull && (!world.IsAlive(parent) || !transforms.Has(parent.Id) || IsAncestor(world, child, parent))) return false;

        ref var t = ref transforms.Get(child.Id);
        if (keepWorld)
        {
            var childWorld  = GetWorldMatrix(world, child);
            var parentWorld = parent.IsNull ? Matrix4x4.Identity : GetWorldMatrix(world, parent);
            Matrix4x4.Invert(parentWorld, out var inverse);
            if (Matrix4x4.Decompose(childWorld * inverse, out var scale, out var rotation, out var position))
            {
                t.Position = position;
                t.Rotation = Quaternion.Normalize(rotation);
                t.Scale    = scale;
            }
        }
        t.Parent = parent;
        t.HasMatrix = false;   // 動かない物 (Static) でも次のフレームで計算し直す
        return true;
    }

    public static List<Entity> GetChildren(World world, Entity parent)
    {
        var result = new List<Entity>();
        var transforms = world.GetPool<Transform>();
        var entities = transforms.Entities;
        var components = transforms.Components;
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i].Parent == parent) result.Add(entities[i]);
        }
        return result;
    }

    public static void DestroyWithChildren(World world, Entity entity)
    {
        if (!world.IsAlive(entity)) return;
        foreach (var child in GetChildren(world, entity)) DestroyWithChildren(world, child);
        world.DestroyEntity(entity);
    }
}
