using System.Numerics;
using System.Runtime.InteropServices;

namespace GameEngine.Rendering;

// ==========================================================================
// UI の描画 (エディターの ImGui など。ED3)
//   頂点 / インデックス (16bit) / 描画の命令を毎フレーム渡すと、全部描いた後に画面の一番上へ描かれる
//   UIVertex は ImGui の ImDrawVert と同じ並び (そのまま写せる)
// ==========================================================================
[StructLayout(LayoutKind.Sequential)]
public struct UIVertex
{
    public Vector2 Position;   // 画面のピクセル
    public Vector2 UV;
    public uint    Color;      // R8G8B8A8 (下位バイトが R)
}

[StructLayout(LayoutKind.Sequential)]
public struct UIDrawCommand
{
    public uint    IndexCount;
    public uint    IndexOffset;
    public uint    VertexOffset;
    public uint    Texture;    // GraphicsSettings.CreateUITexture の番号 / GraphicsSettings.ViewTexture
    public Vector4 Clip;       // 左 / 上 / 右 / 下 (ピクセル)
}
