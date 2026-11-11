namespace GameEngine;

// ==========================================================================
// モデルを描く (Transform と一緒に付ける)
//   付けると、RenderSyncSystem が C++ 側に描画オブジェクトを作り (同じフレームの分はまとめて)、
//   Transform の行列が変わった時だけ C++ へ送る。外す / エンティティを削除すると C++ 側も削除される
// ==========================================================================
//   v1.5 : 1 Mesh / 1 Material の原則 (Unity の MeshRenderer.materials と同じ)
//     Materials[s] がサブメッシュ s のマテリアル (null / 足りない所はモデルに付いているマテリアル)
//     Obj / Pmx / Fbx を読み込むと、エディターがサブメッシュごとのマテリアルのアセットを <モデル名>_Materials に作り、置く時に入れる
//     以前の Material (全部のサブメッシュに 1 つ) は読み込んだ時に Materials へ移す (古いシーン / コードのため。インスペクターには出さない)
//   影 : CastShadows (影を落とす) / ReceiveShadows (影を受ける)。どちらも既定でオン
//   カメラの Culling Mask と比べるレイヤーは、エンティティのレイヤー (EntityInfo.Layer)
public struct MeshRenderer : IComponent
{
    public Model Model;

    // サブメッシュごとのマテリアルのアセット (v1.5)
    public Material?[] Materials = [];

    // 以前のマテリアル (ED5。全部のサブメッシュに使った)。描く時に Materials へ移す
    [HideInInspector] public Material? Material;

    public bool CastShadows    = true;   // v1.5 : 影を落とす (切ると影のシャドーマップに描かない)
    public bool ReceiveShadows = true;   // v1.5 : 影を受ける (切るとシャドーマップを読まない)

    internal uint Handle;    // C++ の描画オブジェクト (Created の時だけ有効)
    internal bool Created;
    internal uint AppliedMaterialPlusOne;   // C++ に設定したマテリアルの番号 + 1 (0 = モデルのマテリアル。RenderSyncSystem が見る)
    internal int  AppliedMaterialsHash;     // v1.5 : C++ に設定したサブメッシュごとのマテリアル (0 = 何も設定していない)
    internal uint AppliedFlagsPlusOne;      // v1.5 : C++ に設定した印とレイヤー + 1 (0 = 既定のまま)

    // C++ に描画オブジェクトがあるか (無効なエンティティ / 作る前は false)
    public readonly bool IsCreated => Created;

    // Material を C++ の描画オブジェクトに付け終わったか (ED5。付けるのは次の RenderSyncSystem)
    // v1.5 : C++ へ送った物の印とレイヤー (下 8 bit = 1 影を落とさない / 2 影を受けない、その上 = レイヤーの番号。確認用)
    public readonly uint AppliedObjectFlags => AppliedFlagsPlusOne == 0 ? 0u : AppliedFlagsPlusOne - 1;

    // v1.5 : サブメッシュごとのマテリアル (Materials) を C++ へ送ってあるか (以前は 1 つの Material を見た)
    public readonly bool IsMaterialApplied => Material == null && AppliedMaterialPlusOne == 0 && AppliedMaterialsHash == materialsHash(Materials);

    internal static int materialsHash(Material?[]? materials)
    {
        if (materials == null || materials.Length == 0) return 0;
        var hash = 17;
        foreach (var m in materials) hash = hash * 31 + (m is { IsValid: true } ? (int)m.Handle + 1 : 0);
        return hash;
    }

    public MeshRenderer()
    {
    }

    public MeshRenderer(Model model)
    {
        Model = model;
    }

    /// <summary>サブメッシュ s のマテリアル (無ければ null = モデルのマテリアル)</summary>
    public readonly Material? MaterialOf(int subMesh) =>
        Materials is { } list && subMesh < list.Length ? list[subMesh] : null;
}
