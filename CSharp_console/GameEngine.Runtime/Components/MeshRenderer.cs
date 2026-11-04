namespace GameEngine;

// ==========================================================================
// モデルを描く (Transform と一緒に付ける)
//   付けると、RenderSyncSystem が C++ 側に描画オブジェクトを作り (同じフレームの分はまとめて)、
//   Transform の行列が変わった時だけ C++ へ送る。外す / エンティティを削除すると C++ 側も削除される
// ==========================================================================
public struct MeshRenderer : IComponent
{
    public Model Model;

    internal uint Handle;    // C++ の描画オブジェクト (Created の時だけ有効)
    internal bool Created;

    // C++ に描画オブジェクトがあるか (無効なエンティティ / 作る前は false)
    public readonly bool IsCreated => Created;

    public MeshRenderer(Model model)
    {
        Model = model;
    }
}
