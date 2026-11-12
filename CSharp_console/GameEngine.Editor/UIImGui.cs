using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// ゲームの UI (UIDrawList。v1.7) を ImGui の描画のリストへ足す
//   UI のテクスチャの番号は ImGui の TextureId と同じ物 (どちらも Graphics.CreateUITexture) なので、そのまま使える
//   切り取りは ImGui の今の切り取り (窓の中) と重ねる
// ==========================================================================
internal static class UIImGui
{
    public static void Draw(ImDrawListPtr draw, UIDrawList list)
    {
        var quads = list.Quads;
        var i = 0;
        while (i < quads.Length)
        {
            // 同じテクスチャ / 切り取りの続く間をまとめる
            var texture = quads[i].Texture;
            var clip = quads[i].Clip;
            var end = i + 1;
            while (end < quads.Length && quads[end].Texture == texture && quads[end].Clip == clip && end - i < 8192) end++;
            draw.PushClipRect(new Vector2(clip.X, clip.Y), new Vector2(clip.Z, clip.W), true);
            draw.PushTextureID((IntPtr)texture);
            draw.PrimReserve((end - i) * 6, (end - i) * 4);
            for (int q = i; q < end; q++)
            {
                ref readonly var quad = ref quads[q];
                draw.PrimQuadUV(quad.P0, quad.P1, quad.P2, quad.P3,
                                quad.Uv0, new Vector2(quad.Uv1.X, quad.Uv0.Y), quad.Uv1, new Vector2(quad.Uv0.X, quad.Uv1.Y), quad.Color);
            }
            draw.PopTextureID();
            draw.PopClipRect();
            i = end;
        }
    }
}
