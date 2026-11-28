using System.Numerics;
using System.Text;
using System.Text.Json;
using GameEngine.Imaging;
using GameEngine.Imaging.Codecs;

namespace GameEngine.Editor;

// ==========================================================================
// スプライトの見本 (v2.4。Assets > Create Sprite Samples)。画像はコードで描く (外部の素材を使わない)
//   Assets/Samples/Sprites :
//     Runner.png (64x64 x 8 コマのシート 4x2) → Runner.spriteanim.json (10 fps / Loop / コマ 2 と 6 に "step")
//     Coin_0〜5.png (連番の画像)               → Coin.spriteanim.json (12 fps / PingPong)
//     Explosion.png (64x64 x 8 コマ 8x1)        → Explosion.spriteanim.json (16 fps / Once / コマ 1 に "boom")
//     SpriteHUD.widget.json (USpriteImage の Coin / Runner と文字)
//   Assets/Scenes/sprites.scene.json : 走る人 (SpriteDemo) / 縦に回る硬貨 5 つ / 爆発 (Space) / HUD
// ==========================================================================
internal static class SpriteSamples
{
    public const string Folder = "Assets/Samples/Sprites";
    public const string ScenePath = "Assets/Scenes/sprites.scene.json";

    /// <summary>見本を書く (ある物も書き直す)。戻り値 = 書いたファイルのアセットのパス</summary>
    public static List<string> Write(AssetDatabase database)
    {
        var written = new List<string>();
        var folder = database.FullPath(Folder);
        Directory.CreateDirectory(folder);
        void save(string name, ImageBuffer image)
        {
            ImageCodec.Save(Path.Combine(folder, name), image);
            written.Add($"{Folder}/{name}");
        }
        void text(string assetPath, string content)
        {
            File.WriteAllText(database.FullPath(assetPath), content, new UTF8Encoding(false));
            written.Add(assetPath);
        }

        // 走る人 (シート 4x2)
        var runner = new ImageBuffer(256, 128);
        for (int i = 0; i < 8; i++) drawRunner(runner, (i % 4) * 64, (i / 4) * 64, i);
        save("Runner.png", runner);
        var run = new SpriteAnimation($"{Folder}/Runner.spriteanim.json") { FrameRate = 10, Loop = SpriteLoopMode.Loop, PixelsPerUnit = 64, Pivot = new Vector2(0.5f, 0f) };
        run.Frames.AddRange(SpriteAnimation.SliceGrid($"{Folder}/Runner.png", 256, 128, 4, 2));
        run.Frames[1].Event = "step";
        run.Frames[5].Event = "step";
        text(run.Path, run.ToJson());

        // 硬貨 (連番)
        var coin = new SpriteAnimation($"{Folder}/Coin.spriteanim.json") { FrameRate = 12, Loop = SpriteLoopMode.PingPong, PixelsPerUnit = 96 };
        for (int i = 0; i < 6; i++)
        {
            var image = new ImageBuffer(48, 48);
            drawCoin(image, 1f - i / 5f * 0.9f);
            save($"Coin_{i}.png", image);
            coin.Frames.Add(new SpriteFrame { Image = $"{Folder}/Coin_{i}.png" });
        }
        text(coin.Path, coin.ToJson());

        // 爆発 (シート 8x1)
        var explosion = new ImageBuffer(512, 64);
        for (int i = 0; i < 8; i++) drawExplosion(explosion, i * 64, i / 7f);
        save("Explosion.png", explosion);
        var boom = new SpriteAnimation($"{Folder}/Explosion.spriteanim.json") { FrameRate = 16, Loop = SpriteLoopMode.Once, PixelsPerUnit = 32 };
        boom.Frames.AddRange(SpriteAnimation.SliceGrid($"{Folder}/Explosion.png", 512, 64, 8, 1));
        boom.Frames[0].Event = "boom";
        text(boom.Path, boom.ToJson());

        // HUD
        var canvas = new UCanvasPanel { Name = "Root" };
        var coinImage = new USpriteImage { Name = "Coin", Animation = coin.Path };
        canvas.AddChildToCanvas(coinImage, new Vector2(16, 16), new Vector2(48, 48));
        var info = new UTextBlock { Name = "Info", Text = "Space : explosion / P : pause runner / C : stop coin / 1 2 : speed" };
        canvas.AddChildToCanvas(info, new Vector2(76, 28), new Vector2(700, 30));
        var runnerImage = new USpriteImage { Name = "Runner", Animation = run.Path, SizeToFrame = false, ImageSize = new Vector2(96, 96) };
        canvas.AddChildToCanvas(runnerImage, new Vector2(16, 72), new Vector2(96, 96));
        text($"{Folder}/SpriteHUD.widget.json", WidgetSerializer.Save(canvas, new Vector2(1280, 720)));

        text(ScenePath, sceneJson(run.Path, coin.Path, boom.Path, $"{Folder}/SpriteHUD.widget.json"));
        database.Refresh();
        return written;
    }

    // ---------------------------------------------------------------------
    // 絵
    // ---------------------------------------------------------------------

    private static void blend(ImageBuffer image, int x, int y, Vector4 color)
    {
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height || color.W <= 0) return;
        var (r, g, b, a) = image.GetPixel(x, y);
        float outA = color.W + a * (1 - color.W);
        if (outA <= 0) return;
        image.SetPixel(x, y, (color.X * color.W + r * a * (1 - color.W)) / outA, (color.Y * color.W + g * a * (1 - color.W)) / outA,
                       (color.Z * color.W + b * a * (1 - color.W)) / outA, outA);
    }

    private static void disc(ImageBuffer image, float cx, float cy, float rx, float ry, Vector4 color)
    {
        for (int y = (int)(cy - ry - 1); y <= cy + ry + 1; y++)
            for (int x = (int)(cx - rx - 1); x <= cx + rx + 1; x++)
            {
                float dx = (x + 0.5f - cx) / Math.Max(rx, 0.01f), dy = (y + 0.5f - cy) / Math.Max(ry, 0.01f);
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float edge = Math.Clamp((1f - d) * Math.Min(rx, ry), 0f, 1f);   // 縁を 1 ピクセルぼかす
                blend(image, x, y, color with { W = color.W * edge });
            }
    }

    private static void line(ImageBuffer image, Vector2 a, Vector2 b, float width, Vector4 color)
    {
        int steps = (int)(Vector2.Distance(a, b) * 2) + 1;
        for (int i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(a, b, (float)i / steps);
            disc(image, p.X, p.Y, width * 0.5f, width * 0.5f, color);
        }
    }

    private static void drawRunner(ImageBuffer image, int ox, int oy, int frame)
    {
        float phase = frame / 8f * MathF.Tau;
        float bob = MathF.Abs(MathF.Sin(phase)) * 3;
        var skin = new Vector4(1f, 0.85f, 0.7f, 1);
        var shirt = new Vector4(0.25f, 0.55f, 0.95f, 1);
        var leg = new Vector4(0.2f, 0.2f, 0.3f, 1);
        var hip = new Vector2(ox + 32, oy + 40 - bob);
        for (int side = 0; side < 2; side++)
        {
            float swing = MathF.Sin(phase + side * MathF.PI) * 0.6f;
            var knee = hip + new Vector2(MathF.Sin(swing) * 10, MathF.Cos(swing) * 10);
            var foot = knee + new Vector2(MathF.Sin(swing - 0.4f) * 11, MathF.Cos(swing - 0.4f) * 11);
            line(image, hip, knee, 5, side == 0 ? leg : (leg * 0.7f) with { W = 1 });
            line(image, knee, Vector2.Min(foot, new Vector2(ox + 63, oy + 63)), 5, side == 0 ? leg : (leg * 0.7f) with { W = 1 });
            float arm = -swing;
            var shoulder = hip + new Vector2(0, -16);
            line(image, shoulder, shoulder + new Vector2(MathF.Sin(arm) * 12, MathF.Cos(arm) * 12), 4, skin);
        }
        disc(image, hip.X, hip.Y - 9, 8, 12, shirt);
        disc(image, hip.X + 1, hip.Y - 27, 8, 8, skin);
        disc(image, hip.X + 4, hip.Y - 28, 1.5f, 1.5f, new Vector4(0, 0, 0, 1));
    }

    private static void drawCoin(ImageBuffer image, float widthScale)
    {
        var gold = new Vector4(1f, 0.8f, 0.2f, 1);
        var dark = new Vector4(0.75f, 0.5f, 0.1f, 1);
        disc(image, 24, 24, 20 * widthScale + 1, 20, dark);
        disc(image, 24, 24, 16 * widthScale + 0.5f, 16, gold);
        if (widthScale > 0.4f) line(image, new Vector2(24, 15), new Vector2(24, 33), 4 * widthScale, dark);
    }

    private static void drawExplosion(ImageBuffer image, int ox, float t)
    {
        float radius = 6 + t * 24;
        float alpha = 1 - t * 0.85f;
        var outer = Vector4.Lerp(new Vector4(1f, 0.55f, 0.1f, 1), new Vector4(0.4f, 0.4f, 0.4f, 1), t) with { W = alpha };
        var inner = Vector4.Lerp(new Vector4(1f, 1f, 0.6f, 1), new Vector4(0.55f, 0.5f, 0.45f, 1), t) with { W = alpha };
        disc(image, ox + 32, 32, radius, radius, outer);
        disc(image, ox + 32, 32, radius * 0.6f, radius * 0.6f, inner);
        for (int i = 0; i < 6; i++)
        {
            float a = i / 6f * MathF.Tau + t;
            disc(image, ox + 32 + MathF.Cos(a) * radius * 0.9f, 32 + MathF.Sin(a) * radius * 0.9f, 4 + t * 4, 4 + t * 4, outer);
        }
    }

    // ---------------------------------------------------------------------
    // シーン
    // ---------------------------------------------------------------------

    private static string sceneJson(string run, string coin, string boom, string hud)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("format", "GameEngineScene");
            w.WriteNumber("version", 1);
            w.WriteStartArray("entities");
            void vec(string name, params float[] v) { w.WriteStartArray(name); foreach (var x in v) w.WriteNumberValue(x); w.WriteEndArray(); }
            void transform(Vector3 p, Quaternion r, Vector3 s)
            {
                w.WriteStartObject();
                w.WriteString("type", "GameEngine.Transform");
                vec("Position", p.X, p.Y, p.Z); vec("Rotation", r.X, r.Y, r.Z, r.W); vec("Scale", s.X, s.Y, s.Z);
                w.WriteNull("Parent");
                w.WriteEndObject();
            }
            void entity(string id, string name, Action components)
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteString("name", name);
                w.WriteBoolean("active", true);
                w.WriteStartArray("components");
                components();
                w.WriteEndArray();
                w.WriteEndObject();
            }
            void sprite(string? image, string? animation, SpriteBillboard billboard, bool flip = false, bool playOnStart = true, bool hideWhenStopped = false)
            {
                w.WriteStartObject();
                w.WriteString("type", "GameEngine.SpriteRenderer");
                w.WriteString("Sprite", image ?? "");
                vec("Color", 1, 1, 1, 1);
                w.WriteBoolean("FlipX", flip);
                w.WriteString("Billboard", billboard.ToString());
                w.WriteEndObject();
                if (animation == null) return;
                w.WriteStartObject();
                w.WriteString("type", "GameEngine.SpriteAnimator");
                w.WritePropertyName("Animation");
                w.WriteStartObject(); w.WriteString("path", animation); w.WriteEndObject();
                w.WriteNumber("Speed", 1);
                w.WriteBoolean("PlayOnStart", playOnStart);
                w.WriteBoolean("HideWhenStopped", hideWhenStopped);
                w.WriteEndObject();
            }
            entity("5a1c0001-0000-4000-8000-000000000001", "Sun", () =>
            {
                w.WriteStartObject(); w.WriteString("type", "GameEngine.DirectionalLight"); vec("Color", 1, 0.95f, 0.88f); w.WriteNumber("Intensity", 1.6f); w.WriteBoolean("CastShadows", true); w.WriteEndObject();
                transform(new Vector3(0, 30, 0), Quaternion.Normalize(new Quaternion(0.36f, -0.28f, 0.11f, 0.88f)), Vector3.One);
            });
            entity("5a1c0001-0000-4000-8000-000000000002", "Main Camera", () =>
            {
                w.WriteStartObject(); w.WriteString("type", "GameEngine.Camera"); w.WriteEndObject();
                transform(new Vector3(0, 1.6f, -6), Quaternion.CreateFromAxisAngle(Vector3.UnitX, 8 * MathF.PI / 180), Vector3.One);
            });
            entity("5a1c0001-0000-4000-8000-000000000003", "Ground", () =>
            {
                w.WriteStartObject();
                w.WriteString("type", "GameEngine.MeshRenderer");
                w.WritePropertyName("Model"); w.WriteStartObject(); w.WriteString("path", "builtin:Plane"); w.WriteEndObject();
                w.WriteEndObject();
                w.WriteStartObject(); w.WriteString("type", "GameEngine.Static"); w.WriteEndObject();
                transform(Vector3.Zero, Quaternion.Identity, new Vector3(4, 1, 2));
            });
            entity("5a1c0001-0000-4000-8000-000000000004", "Runner", () =>
            {
                sprite(null, run, SpriteBillboard.None);
                w.WriteStartObject(); w.WriteString("type", "GameEngine_console.SpriteDemo"); w.WriteString("Role", "Runner"); w.WriteNumber("Speed", 2); w.WriteEndObject();
                transform(new Vector3(0, 0, 0), Quaternion.Identity, Vector3.One);
            });
            for (int i = 0; i < 5; i++)
            {
                int n = i;
                entity($"5a1c0001-0000-4000-8000-00000000001{n}", $"Coin {n + 1}", () =>
                {
                    sprite(null, coin, SpriteBillboard.Vertical);
                    transform(new Vector3(-4 + n * 2, 1.2f + (n % 2) * 0.4f, 2), Quaternion.Identity, Vector3.One);
                });
            }
            entity("5a1c0001-0000-4000-8000-000000000005", "Explosion", () =>
            {
                sprite(null, boom, SpriteBillboard.Camera, playOnStart: false, hideWhenStopped: true);
                w.WriteStartObject(); w.WriteString("type", "GameEngine_console.SpriteDemo"); w.WriteString("Role", "Explosion"); w.WriteEndObject();
                transform(new Vector3(0, 1, -0.5f), Quaternion.Identity, Vector3.One);
            });
            entity("5a1c0001-0000-4000-8000-000000000006", "Sign", () =>
            {
                sprite($"{Folder}/Coin_0.png", null, SpriteBillboard.None);
                transform(new Vector3(5, 2.5f, 3), Quaternion.Identity, new Vector3(2, 2, 2));
            });
            entity("5a1c0001-0000-4000-8000-000000000007", "HUD", () =>
            {
                w.WriteStartObject();
                w.WriteString("type", "GameEngine.UIWidget");
                w.WritePropertyName("Widget"); w.WriteStartObject(); w.WriteString("path", hud); w.WriteEndObject();
                w.WriteNumber("ZOrder", 0);
                w.WriteBoolean("Visible", true);
                w.WriteEndObject();
            });
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
