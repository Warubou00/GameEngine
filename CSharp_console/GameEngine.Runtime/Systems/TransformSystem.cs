using System.Numerics;

namespace GameEngine;

// ==========================================================================
// Transform の行列を作る (エンジン組み込み。登録した System の後に動く)
//   - 行列が前のフレームから変わった物だけ Changed を立てる (描画へ送るのはその物だけ)
//   - Static が付いた物は最初の 1 回だけ (MarkDirty で作り直し。親が動いた時も作り直す)
//   - 親子 (ED2) : 親を先に計算してから子を計算する (ワールドの行列 = ローカルの行列 × 親のワールドの行列)。
//     親の行列が変わったら、子のローカルの値が同じでも子は変わった扱い
//     親が消えた / Transform が無い子は、親が無い物として扱う。親を辿って 64 段を超えたら (輪になっている) そこで止める
//   - 有効・無効 (ED2) : 自分の EntityInfo.Active と親の有効を合わせて、World の「有効」(IsActive) を決める
//   詰めた配列を直接回す (ForEach の delegate を通さない分速い。1 万個で 0.1 ms 程度)
// ==========================================================================
internal sealed class TransformSystem : ISystem
{
    private const int MaxDepth = 64;

    // このフレームで行列が変わった数 (0 なら RenderSyncSystem は送る物を探さない)
    // 剛体の位置を書いた CollisionSystem も足す (以前は TransformSystem だけが数えていたので、物理だけが動かした物が描画へ送られなかった)
    public int ChangedCount { get; internal set; }

    // 親のある Transform があったか (無ければ剛体の後に子を計算し直す必要が無い)
    public bool HasHierarchy { get; private set; }

    private int _frame;
    private ComponentPool<Transform>? _transforms;
    private ComponentPool<Static>?    _statics;
    private ComponentPool<EntityInfo>? _infos;
    private World? _world;
    private bool _keepChanged;
    private int  _changed;

    public void Update(World world) => update(world, keepChanged: false);

    // 剛体が位置を書いた後に、子だけ計算し直す (Changed は下ろさない。剛体の行列を描画へ送る印を消さないため)
    internal void UpdateAgain(World world) => update(world, keepChanged: true);

    private void update(World world, bool keepChanged)
    {
        _world       = world;
        _transforms  = world.GetPool<Transform>();
        _statics     = world.GetPool<Static>();
        _infos       = world.GetPool<EntityInfo>();
        _keepChanged = keepChanged;
        _changed     = 0;
        _frame++;
        if (_frame == 0) _frame = 1;

        // 1. 親の無い物 (一番多い。以前と同じ速い道 : 有効 / 無効は EntityInfo を持つ物だけ見る)
        //    以前 (ED2 の最初) は全部の物に有効 / 無効の計算と印の書き込みをしていて、動かない 1 万個で 0.06 → 0.2 ms になった
        var entities   = _transforms.Entities;
        var components = _transforms.Components;
        var anyInfo    = _infos.Count > 0;
        var hierarchy  = false;
        for (int i = 0; i < components.Length; i++)
        {
            ref var t = ref components[i];
            if (!t.Parent.IsNull) { hierarchy = true; continue; }
            var id = entities[i].Id;
            if (anyInfo && _infos.Has(id)) world.SetActiveInHierarchy(id, _infos.Get(id).Active);
            if (t.HasMatrix && _statics.Has(id)) continue;   // 動かない物 (Changed は送った側が下ろす)

            if (!keepChanged) t.Changed = false;
            var matrix = t.LocalMatrix;
            if (!t.HasMatrix || matrix != t.LocalToWorld)
            {
                t.LocalToWorld = matrix;
                t.HasMatrix    = true;
                t.Changed      = true;
                _changed++;
            }
        }

        // 2. 親のある物 (いる時だけ) : 親を先に計算する (親が親の無い物なら 1 で済んでいる)
        HasHierarchy = hierarchy;
        if (hierarchy)
        {
            for (int i = 0; i < components.Length; i++)
            {
                ref var t = ref components[i];
                if (!t.Parent.IsNull) compute(entities[i].Id, ref t, 0);
            }
        }

        // 3. Transform の無いエンティティの有効 / 無効 (自分の Active だけ)
        if (anyInfo)
        {
            var infoEntities = _infos.Entities;
            var infos        = _infos.Components;
            for (int i = 0; i < infos.Length; i++)
            {
                var id = infoEntities[i].Id;
                if (!_transforms.Has(id)) world.SetActiveInHierarchy(id, infos[i].Active);
            }
        }

        ChangedCount = keepChanged ? ChangedCount + _changed : _changed;
    }

    // 親のある物 : 親を先に計算する。戻り値はこのフレームで行列が変わったか
    private bool compute(int id, ref Transform t, int depth)
    {
        if (t._stamp == _frame) return t.Changed;
        t._stamp = _frame;
        var selfActive = !_infos!.Has(id) || _infos.Get(id).Active;

        var parent = t.Parent;
        var hasParent = !parent.IsNull && depth < MaxDepth && _world!.IsAlive(parent) && _transforms!.Has(parent.Id);
        var parentChanged = false;
        if (hasParent)
        {
            ref var p = ref _transforms!.Get(parent.Id);
            if (p.Parent.IsNull) parentChanged = p.Changed;   // 親の無い親は 1 で計算済み
            else parentChanged = compute(parent.Id, ref p, depth + 1);
            _world!.SetActiveInHierarchy(id, selfActive && _world.IsActive(parent));
            if (t.HasMatrix && _statics!.Has(id) && !parentChanged) return false;

            if (!_keepChanged) t.Changed = false;
            var matrix = t.LocalMatrix * p.LocalToWorld;
            if (!t.HasMatrix || matrix != t.LocalToWorld)
            {
                t.LocalToWorld = matrix;
                t.HasMatrix    = true;
                t.Changed      = true;
                _changed++;
            }
            return t.Changed;
        }

        // 親が消えた / 親に Transform が無い : 親の無い物として扱う
        _world!.SetActiveInHierarchy(id, selfActive);
        if (t.HasMatrix && _statics!.Has(id)) return false;
        if (!_keepChanged) t.Changed = false;
        var local = t.LocalMatrix;
        if (!t.HasMatrix || local != t.LocalToWorld)
        {
            t.LocalToWorld = local;
            t.HasMatrix    = true;
            t.Changed      = true;
            _changed++;
        }
        return t.Changed;
    }
}
