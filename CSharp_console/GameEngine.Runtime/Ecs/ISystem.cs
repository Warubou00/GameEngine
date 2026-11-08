namespace GameEngine;

// ==========================================================================
// System (ECS の「処理」。Unity の MonoBehaviour.Update に当たるものはここに書く)
//   engine.AddSystem(new SpinSystem()) で登録すると、毎フレーム登録した順に呼ばれる。
//   エンジン組み込みの System (Transform の行列計算・カメラ・描画への反映) は、登録した System の後に動く
// ==========================================================================
public interface ISystem
{
    void Update(World world);
}

// ==========================================================================
// 固定の時間刻みで呼ばれる System (物理の前。Unity の FixedUpdate)
//   engine.AddSystem で ISystem と一緒に実装すれば両方 / engine.AddFixedSystem なら FixedUpdate だけ
//   1 フレームで 0 回〜Time.MaxFixedSteps 回呼ばれる。時間は Time.FixedDeltaTime を使う
//   剛体に力をかける / 速度を変えるのはここ (かけた力は、すぐ後の 1 刻みにかかる)
// ==========================================================================
public interface IFixedSystem
{
    void FixedUpdate(World world);
}

// ==========================================================================
// 全部の System の Update と物理の後、カメラと描画の前に呼ばれる System (v1.4。Unity の LateUpdate)
//   engine.AddSystem で ISystem と一緒に実装すれば両方。カメラを物に追いかけさせる時などに使う
//   ここで変えた Transform は、そのフレームの描画に間に合う (エンジンが行列を計算し直す)
// ==========================================================================
public interface ILateSystem
{
    void LateUpdate(World world);
}

// ==========================================================================
// ゲームの System の印 (ED7)。付けると、エディター (と Engine.AddGameSystems) が自動で作って登録する
//   作り方 : public な (Engine engine) か () のコンストラクター
//   Order の小さい順に登録する (同じなら型の名前の順)。ゲームの DLL を読み直すと、作り直される
//   ※どのシーンでも動くので、自分の物が無いシーンでは何もしないように書く (例 : 自分のコンポーネントの数が 0 なら戻る)
// ==========================================================================
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GameSystemAttribute : Attribute
{
    public int Order { get; init; }
}
