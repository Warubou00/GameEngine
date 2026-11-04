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

    public EntityInfo(string name)
    {
        Id     = Guid.NewGuid();
        Name   = name;
        Active = true;
    }
}
