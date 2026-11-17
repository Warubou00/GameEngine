namespace GameEngine;

// ==========================================================================
// エンティティの名前 / 有効・無効 / 保存しても変わらない ID (ED1)
//   Entity (番号 + 世代) は実行ごとに変わるので、保存したシーンでは Id (GUID) で物を指す
//   (Joint.ConnectedBody などのエンティティへの参照は、保存すると相手の Id になる)
//   world.CreateEntity("名前") で作ると付く。付いていない物は、保存する時に付ける
//   Active : ED1 では保存するだけ (無効な物を System が飛ばすのは ED2 / ED3 で)
// ==========================================================================
[ComponentInfo(DisplayName = "Entity", Hidden = true)]
public struct EntityInfo : IComponent
{
    [HideInInspector] public Guid Id;
    public string Name;
    public bool   Active;
    [Layer] public int Layer;   // v1.5 : レイヤー (カメラの Culling Mask で写すかを決める。Unity の GameObject.layer。名前は Project Settings)
    // v1.18 : タグ (Unity の GameObject.tag。null / 空 = Untagged)。FindWithTag で速く探せる。候補の名前は Project Settings の TagNames
    //   書き換えは GameObject.Tag / World.SetTag で (ref で直接書き換えると、FindWithTag の索引が古いままになることがある)
    [Tag] public string? Tag;

    // v1.14 : どのシーン (SceneManager の Scene.Id) から来た物か。0 = どのシーンでもない (コードで作った物など。今のメインシーンの物として扱う)
    //   保存しない / インスペクターに出さない (public でないので ComponentRegistry のフィールドに入らない)
    //   以前はどのシーンの物かを覚えていなかったので、一部のシーン (サブシーン) だけを外せなかった
    internal int SceneId;

    public EntityInfo(string name)
    {
        Id     = Guid.NewGuid();
        Name   = name;
        Active = true;
    }
}
