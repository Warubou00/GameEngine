namespace GameEngine;

// ==========================================================================
// モデルを描く (Transform と一緒に付ける)
//   付けると、RenderSyncSystem が C++ 側に描画オブジェクトを作り (同じフレームの分はまとめて)、
//   Transform の行列が変わった時だけ C++ へ送る。外す / エンティティを削除すると C++ 側も削除される
// ==========================================================================
public struct MeshRenderer : IComponent
{
    public Model Model;

    // マテリアルのアセット (ED5)。null ならモデルに付いているマテリアルで描く。全部のサブメッシュに使う
    public Material? Material;

    internal uint Handle;    // C++ の描画オブジェクト (Created の時だけ有効)
    internal bool Created;
    internal uint AppliedMaterialPlusOne;   // C++ に設定したマテリアルの番号 + 1 (0 = モデルのマテリアル。RenderSyncSystem が見る)

    // C++ に描画オブジェクトがあるか (無効なエンティティ / 作る前は false)
    public readonly bool IsCreated => Created;

    // Material を C++ の描画オブジェクトに付け終わったか (ED5。付けるのは次の RenderSyncSystem)
    public readonly bool IsMaterialApplied => Material is { IsValid: true } m ? AppliedMaterialPlusOne == m.Handle + 1 : AppliedMaterialPlusOne == 0;

    public MeshRenderer(Model model)
    {
        Model = model;
    }
}
