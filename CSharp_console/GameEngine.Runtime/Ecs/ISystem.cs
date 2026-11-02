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
