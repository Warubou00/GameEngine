namespace GameEngine;

// ==========================================================================
// サブシーン (v1.14。CAPCOM の RE Engine の「シーンの中のシーン」に当たる)
//   このエンティティが、別のシーンのファイル (.scene.json) を子として持つ。中の物は SceneManager が読み込む / 外す
//   状態 : 未ロード → (Load) → 待機 (読んであるが無効) ⇔ 有効 → (Unload) → 未ロード
//     有効 / 無効の切り替えは印を変えるだけ (物とアセットは残るので一瞬で戻る)。Unload は中の物を消す
//   中の一番上の物 (親の無い Transform) は、このエンティティの子になる (このエンティティを動かすと、中の物も全部動く)
//   このエンティティを無効にする / 消すと、中の物も無効になる / 外れる
//
//   スクリプトから : Scenes.LoadSubScene(entity) / UnloadSubScene(entity) / SetSubSceneActive(entity, true)
//   中にさらに SubScene があっても良い (同じファイルを中で読む輪は読まない)
// ==========================================================================
[ComponentInfo(DisplayName = "Sub Scene", Category = "Scene")]
public struct SubScene : IComponent
{
    [AssetPath(AssetKind.Scene)]
    [Tooltip("The scene file (.scene.json) this entity holds")]
    public string Scene = "";

    [Tooltip("Load it together with the scene that contains this entity")]
    public bool LoadOnStart = true;

    [Tooltip("Activate it when loaded (off = standby until SetSubSceneActive)")]
    public bool ActiveOnLoad = true;

    public SubScene()
    {
    }
}
