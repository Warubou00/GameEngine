using GameEngine;
using GameEngine.Rendering;

namespace GameEngine_console;

// ==========================================================================
// 描画の切り替え (キー)
//   F2 : 描画パイプライン Forward ⇔ Deferred
//   F3 : ライティング Unlit → Lambert → PBR
//   今の設定はプロファイラー (F1) の "Render pipeline" に出る
// ==========================================================================
public sealed class GraphicsHotkeySystem : ISystem
{
    private readonly GraphicsSettings _graphics;

    public GraphicsHotkeySystem(GraphicsSettings graphics)
    {
        _graphics = graphics;
    }

    public void Update(World world)
    {
        if (Input.GetKeyDown(Keys.F2))
        {
            _graphics.Pipeline = _graphics.Pipeline is DeferredRenderPipeline
                ? new ForwardRenderPipeline()
                : new DeferredRenderPipeline();
        }
        if (Input.GetKeyDown(Keys.F3))
        {
            _graphics.LightingModel = _graphics.LightingModel switch
            {
                LightingModel.Unlit   => LightingModel.Lambert,
                LightingModel.Lambert => LightingModel.PBR,
                _                     => LightingModel.Unlit,
            };
        }
    }

    // コマンドライン引数の名前 → 設定
    public static RenderPipeline? ParsePipeline(string name) => name switch
    {
        "forward"  => new ForwardRenderPipeline(),
        "deferred" => new DeferredRenderPipeline(),
        _          => null,
    };

    public static LightingModel? ParseLighting(string name) => name switch
    {
        "unlit"   => LightingModel.Unlit,
        "lambert" => LightingModel.Lambert,
        "pbr"     => LightingModel.PBR,
        _         => null,
    };
}
