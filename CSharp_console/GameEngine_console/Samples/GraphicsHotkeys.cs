using GameEngine;
using GameEngine.Rendering;

namespace GameEngine_console;

// ==========================================================================
// 描画の切り替え (キー)
//   F2 : 描画パイプライン Forward ⇔ Deferred
//   F3 : ライティング Unlit → Lambert → PBR
//   F4 : SSAO の ON / OFF
//   F5 : 遮蔽だけを白黒で表示 (SSAO の確認用)
//   F6 : アンチエイリアス なし → FXAA → MSAA 2x → 4x → 8x (Deferred の MSAA は FXAA になる)
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
        if (Input.GetKeyDown(Keys.F4)) _graphics.Ssao.Enabled  = !_graphics.Ssao.Enabled;
        if (Input.GetKeyDown(Keys.F5)) _graphics.Ssao.ShowOnly = !_graphics.Ssao.ShowOnly;
        if (Input.GetKeyDown(Keys.F6))
        {
            _graphics.AntiAliasing = _graphics.AntiAliasing switch
            {
                AntiAliasing.None   => AntiAliasing.Fxaa,
                AntiAliasing.Fxaa   => AntiAliasing.Msaa2x,
                AntiAliasing.Msaa2x => AntiAliasing.Msaa4x,
                AntiAliasing.Msaa4x => AntiAliasing.Msaa8x,
                _                   => AntiAliasing.None,
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

    public static AntiAliasing? ParseAntiAliasing(string name) => name switch
    {
        "none"  => AntiAliasing.None,
        "fxaa"  => AntiAliasing.Fxaa,
        "msaa2" => AntiAliasing.Msaa2x,
        "msaa4" => AntiAliasing.Msaa4x,
        "msaa8" => AntiAliasing.Msaa8x,
        _       => null,
    };

    public static LightingModel? ParseLighting(string name) => name switch
    {
        "unlit"   => LightingModel.Unlit,
        "lambert" => LightingModel.Lambert,
        "pbr"     => LightingModel.PBR,
        _         => null,
    };
}
