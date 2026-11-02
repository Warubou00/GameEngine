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
