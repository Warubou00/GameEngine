using System.Diagnostics;
using System.Numerics;

namespace GameEngine.Editor;

// ==========================================================================
// v2.4 の確認 (--editor-test-section sprites) : スプライトのアニメーション
//   1. アセット : JSON の往復 / シートの格子 / 時間 → コマ (Loop / Once / PingPong / コマごとの時間) / イベントの並び
//   2. 見本 (Assets > Create Sprite Samples) : ファイル / 種類 / コマのテクスチャ (同じ四角は共有) / UI の画像に TGA
//   3. GameObject : SpriteRenderer (画像だけ) / SpriteAnimator (編集中は最初のコマ、再生でコマが進む) / 大きさ = px / PPU /
//      マテリアルの共有 / 板をクリックで選べる / 外すと描画オブジェクトが消える
//   4. 見本のシーンを再生 : "step" イベント (OnSpriteEvent) / Space で爆発 → "boom" と OnSpriteFinished / HideWhenStopped / UI の USpriteImage
//   5. 窓 : 開く / コマを足す・消す・並べ替え / シートを切る / Undo / 自動で保存 → 読み直し / Widget Designer のパレット
//   sprites-bench : 動くスプライト 1000 / 5000 個の CPU 時間。sprites-view : 撮影用 (見本のシーンを再生したまま)
// ==========================================================================
internal sealed partial class EditorTest
{
    private IEnumerable<int> spriteAnimations()
    {
        var database = _app.Database;
        var assets = _app.Engine.Assets;

        // ---------- 1. アセット ----------
        var a = new SpriteAnimation("Assets/x.spriteanim.json") { FrameRate = 10, Loop = SpriteLoopMode.Loop, PixelsPerUnit = 50, Pivot = new Vector2(0.5f, 0) };
        a.Frames.AddRange(SpriteAnimation.SliceGrid("Assets/s.png", 100, 50, 4, 2, 6, 2, 1, 2, 3));
        a.Frames[2].Duration = 0.3f;
        a.Frames[3].Event = "hit";
        check(a.FrameCount == 6 && a.Frames[0].X == 2 && a.Frames[0].Y == 1 && a.Frames[1].X == 2 + 22 + 2 && a.Frames[4].Y == 1 + 22 + 3 && a.Frames[0].Width == 22 && a.Frames[0].Height == 22,
              "[sprites] SliceGrid (columns / rows / count / margin / spacing)");
        var b = new SpriteAnimation("Assets/x.spriteanim.json", a.ToJson());
        check(b.ToJson() == a.ToJson() && b.Frames[3].Event == "hit" && Math.Abs(b.Frames[2].Duration - 0.3f) < 1e-6f && b.Pivot == a.Pivot, "[sprites] JSON round trip");
        check(Math.Abs(a.Duration - (0.1f * 5 + 0.3f)) < 1e-5f, $"[sprites] duration with a per-frame time ({a.Duration:F2} s)");
        check(a.Sample(0.05f, SpriteLoopMode.Loop).Frame == 0 && a.Sample(0.15f, SpriteLoopMode.Loop).Frame == 1 && a.Sample(0.45f, SpriteLoopMode.Loop).Frame == 2 &&
              a.Sample(0.55f, SpriteLoopMode.Loop).Frame == 3 && a.Sample(0.85f, SpriteLoopMode.Loop) is { Frame: 0, Step: 6 }, "[sprites] Loop : time -> frame (frame 3 is 0.3 s)");
        check(a.Sample(10f, SpriteLoopMode.Once) is { Frame: 5, Finished: true } && !a.Sample(0.7f, SpriteLoopMode.Once).Finished, "[sprites] Once stops on the last frame");
        var ping = new SpriteAnimation { FrameRate = 1 };
        for (int i = 0; i < 4; i++) ping.Frames.Add(new SpriteFrame { Image = "a.png" });
        var seq = Enumerable.Range(0, 8).Select(i => ping.Sample(i + 0.5f, SpriteLoopMode.PingPong).Frame).ToArray();
        check(seq.SequenceEqual(new[] { 0, 1, 2, 3, 2, 1, 0, 1 }) && ping.FrameOfStep(4, SpriteLoopMode.PingPong) == 2, $"[sprites] PingPong ({string.Join(" ", seq)})");
        bool threw = false;
        try { _ = new SpriteAnimation("x", "{\"format\":\"Other\"}"); } catch (InvalidDataException) { threw = true; }
        check(threw, "[sprites] a different format is rejected");

        // ---------- 2. 見本 ----------
        var written = SpriteSamples.Write(database);
        yield return 0;
        check(written.Count >= 12 && database.Find($"{SpriteSamples.Folder}/Runner.spriteanim.json")?.Kind == AssetKind.SpriteAnimation &&
              database.Find(SpriteSamples.ScenePath)?.Kind == AssetKind.Scene, $"[sprites] samples written ({written.Count} files)");
        var run = assets.LoadSpriteAnimation($"{SpriteSamples.Folder}/Runner.spriteanim.json");
        var coin = assets.LoadSpriteAnimation($"{SpriteSamples.Folder}/Coin.spriteanim.json");
        check(run == assets.LoadSpriteAnimation($"{SpriteSamples.Folder}/Runner.spriteanim.json") && run.FrameCount == 8 && coin.FrameCount == 6 && coin.Loop == SpriteLoopMode.PingPong,
              "[sprites] the same path returns the same instance");
        var before = assets.SpriteTextureCount;
        var textures = Enumerable.Range(0, run.FrameCount).Select(run.TextureOf).ToList();
        check(textures.All(t => t.IsValid && t.Width == 64 && t.Height == 64) && textures.Select(t => t.Texture).Distinct().Count() == 8 && assets.SpriteTextureCount == before + 8,
              "[sprites] one 64x64 GPU texture per frame of the sheet");
        var dup = assets.Adopt(new SpriteAnimation());
        dup.Frames.Add(run.Frames[0].Clone()); dup.Frames.Add(run.Frames[0].Clone()); dup.Frames.Add(run.Frames[1].Clone());
        check(dup.TextureOf(0).Texture == dup.TextureOf(1).Texture && dup.TextureOf(0).Texture != dup.TextureOf(2).Texture, "[sprites] the same rect of the same image shares one texture");
        var tgaPath = $"{SpriteSamples.Folder}/Tga.tga";
        GameEngine.Imaging.Codecs.ImageCodec.Save(database.FullPath(tgaPath), GameEngine.Imaging.Codecs.ImageCodec.Load(database.FullPath($"{SpriteSamples.Folder}/Coin_0.png")).Image);
        database.Refresh();
        var tgaUi = UI.Manager!.Resources.Textures.Get(tgaPath);
        check(tgaUi.Size == new Vector2(48, 48), "[sprites] UI images can be TGA now (own decoder)");
        // 時間 / イベントだけを変えた時はテクスチャを作り直さない
        var first = run.TextureOf(0).Texture;
        run.Frames[0].Duration = 0.2f; run.MarkChanged();
        check(run.TextureOf(0).Texture == first, "[sprites] changing durations keeps the textures");
        run.Frames[0].Duration = 0; run.MarkChanged();

        // ---------- 3. GameObject ----------
        var still = World.CreateEntity("_SpriteStill");
        World.Add(still, new Transform(new Vector3(0, 1, 3)));
        World.Add(still, new SpriteRenderer($"{SpriteSamples.Folder}/Coin_0.png") { PixelsPerUnit = 48 });
        var animated = World.CreateEntity("_SpriteAnimated");
        World.Add(animated, new Transform(new Vector3(1, 1, 3)));
        World.Add(animated, new SpriteRenderer());
        World.Add(animated, new SpriteAnimator(run));
        for (int i = 0; i < 3; i++) yield return 0;
        var sr = World.Get<SpriteRenderer>(still);
        check(sr.IsCreated && sr.CurrentSize == new Vector2(1, 1), $"[sprites] SpriteRenderer with an image (size {sr.CurrentSize})");
        var ar = World.Get<SpriteRenderer>(animated);
        check(ar.IsCreated && ar.CurrentTexture == run.TextureOf(0).Texture && ar.CurrentSize == new Vector2(1, 1), "[sprites] while editing the animator shows the first frame");
        check(_app.Engine.SpriteStats.Materials >= 2, $"[sprites] shared materials ({_app.Engine.SpriteStats.Materials})");
        // たくさん置いても同じコマならマテリアルは増えない
        var many = new List<Entity>();
        for (int i = 0; i < 50; i++)
        {
            var e = World.CreateEntity("_SpriteMany");
            World.Add(e, new Transform(new Vector3(i * 0.1f, 3, 5)));
            World.Add(e, new SpriteRenderer($"{SpriteSamples.Folder}/Coin_0.png"));
            many.Add(e);
        }
        var materialsBefore = _app.Engine.SpriteStats.Materials;
        yield return 0;
        check(_app.Engine.SpriteStats.Materials == materialsBefore, "[sprites] 50 sprites with the same image share one material");
        // クリックで選べる (板に向けた光線)
        var hit = _app.PickForTest(new Vector3(0, 1, 0), Vector3.UnitZ);
        check(hit == still, $"[sprites] a ray hits the sprite quad ({hit})");
        // 編集中も動かす
        SpriteAnimator.PreviewInEditor = true;
        Time.CaptureDeltaTime = 0.25f;
        for (int i = 0; i < 2; i++) yield return 0;
        check(World.Get<SpriteAnimator>(animated).CurrentFrame > 0, $"[sprites] Animate in scene moves the animation while editing (frame {World.Get<SpriteAnimator>(animated).CurrentFrame})");
        SpriteAnimator.PreviewInEditor = false;
        Time.CaptureDeltaTime = 0;
        yield return 0;
        check(World.Get<SpriteAnimator>(animated).CurrentFrame == 0, "[sprites] turning it off goes back to the first frame");
        foreach (var e in many) World.DestroyEntity(e);
        World.DestroyEntity(still);
        World.DestroyEntity(animated);
        yield return 0;
        check(_app.Engine.SpriteStats.Sprites == 0 && World.GetPool<SpriteRenderer>().Count == 0, "[sprites] destroyed sprites remove their render objects");

        // ---------- 4. 見本のシーンを再生 ----------
        _app.OpenScene(database.FullPath(SpriteSamples.ScenePath));
        yield return 0;
        var runner = findByName("Runner");
        var explosion = findByName("Explosion");
        check(World.IsAlive(runner) && World.Has<SpriteAnimator>(runner) && World.Get<SpriteAnimator>(runner).Animation == run && World.GetPool(gameType("SpriteDemo")).Has(runner.Id),
              "[sprites-demo] the sample scene loads (SpriteAnimator / SpriteDemo)");
        Time.CaptureDeltaTime = 1f / 60f;
        _app.TogglePlay();
        for (int i = 0; i < 90; i++) yield return 0;   // 1.5 秒 : 15 コマ = "step" 3〜4 回
        var steps = (int)World.GetPool(gameType("SpriteDemo")).GetBoxed(findByName("Runner").Id).GetType().GetField("Steps")!.GetValue(World.GetPool(gameType("SpriteDemo")).GetBoxed(findByName("Runner").Id))!;
        runner = findByName("Runner");
        explosion = findByName("Explosion");
        check(steps >= 3 && World.Get<SpriteAnimator>(runner).IsPlaying, $"[sprites-demo] OnSpriteEvent \"step\" while running ({steps} steps)");
        check(!World.Get<SpriteRenderer>(explosion).IsCreated, "[sprites-demo] HideWhenStopped hides the explosion before it plays");
        var hud = World.Entities.Where(e => World.Has<UIWidget>(e)).Select(e => UI.GetWidget(e)).FirstOrDefault(w => w != null);
        var coinUi = hud?.GetWidget<USpriteImage>("Coin");
        check(coinUi != null && coinUi.IsPlaying && coinUi.CurrentAnimation == coin, $"[sprites-demo] USpriteImage plays in the HUD (frame {coinUi?.CurrentFrame})");
        Input.SimulateKeyPress(Keys.Space);
        for (int i = 0; i < 4; i++) yield return 0;
        check(World.Get<SpriteAnimator>(explosion).IsPlaying && World.Get<SpriteRenderer>(explosion).IsCreated, "[sprites-demo] Space plays the explosion");
        for (int i = 0; i < 50; i++) yield return 0;   // 8 コマ / 16 fps = 0.5 秒
        check(!World.Get<SpriteAnimator>(explosion).IsPlaying && !World.Get<SpriteRenderer>(explosion).IsCreated, "[sprites-demo] Once : stops and hides after the last frame");
        Input.SimulateKeyPress(Keys.C);
        yield return 0; yield return 0;
        check(coinUi != null && !coinUi.IsPlaying, "[sprites-demo] C stops the HUD coin (USpriteImage.Stop)");
        _app.TogglePlay();
        Time.CaptureDeltaTime = 0;
        yield return 0;
        check(World.Get<SpriteAnimator>(findByName("Runner")).CurrentFrame == 0, "[sprites-demo] stopping play goes back to the first frame");

        // ---------- 5. 窓 ----------
        var testPath = $"{SpriteSamples.Folder}/_EditorTest.spriteanim.json";
        _app.Engine.Assets.NoteOwnWrite(database.FullPath(testPath));
        File.WriteAllText(database.FullPath(testPath), new SpriteAnimation(testPath).ToJson());
        database.Refresh();
        check(_app.OpenSpriteAnimation(testPath) && _app.SpriteEditor.Asset!.FrameCount == 0, "[sprites-editor] open an empty animation");
        var editor = _app.SpriteEditor;
        editor.AddFrame($"{SpriteSamples.Folder}/Coin_0.png");
        editor.AddFrame($"{SpriteSamples.Folder}/Coin_1.png");
        editor.AddFrame($"{SpriteSamples.Folder}/Coin_2.png");
        editor.MoveFrame(2, -1);
        editor.RemoveFrame(0);
        check(editor.Asset!.FrameCount == 2 && editor.Asset.Frames[0].Image.EndsWith("Coin_2.png") && editor.UndoCount == 5, "[sprites-editor] add / move / remove frames");
        editor.Undo();
        check(editor.Asset.FrameCount == 3, "[sprites-editor] undo");
        editor.SheetImage = $"{SpriteSamples.Folder}/Explosion.png";
        editor.SheetColumns = 8; editor.SheetRows = 1; editor.SheetCount = 0;
        check(editor.SliceSheet(append: false) == 8 && editor.Asset.FrameCount == 8 && editor.Asset.Frames[7].X == 448, "[sprites-editor] slice a sheet (replace)");
        var saveWait = DateTime.Now;
        while (editor.Dirty && (DateTime.Now - saveWait).TotalSeconds < 3) yield return 0;   // 自動で保存 (0.5 秒後)
        var saved = new SpriteAnimation(testPath, File.ReadAllText(database.FullPath(testPath)));
        check(editor.SaveCount > 0 && saved.FrameCount == 8 && !editor.Dirty, $"[sprites-editor] autosave writes the file (saves {editor.SaveCount}, file {saved.FrameCount} frames, dirty {editor.Dirty})");
        _app.SpriteEditor.Close();
        File.Delete(database.FullPath(testPath));
        File.Delete(database.FullPath(testPath) + ".meta");
        File.Delete(database.FullPath(tgaPath));
        if (File.Exists(database.FullPath(tgaPath) + ".meta")) File.Delete(database.FullPath(tgaPath) + ".meta");
        database.Refresh();
        check(WidgetTypes.Find("SpriteImage") == typeof(USpriteImage), "[sprites-editor] SpriteImage is a widget type (Widget Designer palette)");
        _app.OpenScene(database.FullPath("Assets/Scenes/showcase.scene.json"));
        yield return 0;
    }

    // 計測 (--editor-test-section sprites-bench) : 動くスプライトの CPU 時間 (アニメーション + 板の同期)
    private IEnumerable<int> spriteBench()
    {
        SpriteSamples.Write(_app.Database);
        var run = _app.Engine.Assets.LoadSpriteAnimation($"{SpriteSamples.Folder}/Runner.spriteanim.json");
        foreach (var count in new[] { 1000, 5000 })
        {
            var list = new List<Entity>();
            for (int i = 0; i < count; i++)
            {
                var e = World.CreateEntity("_Bench");
                World.Add(e, new Transform(new Vector3(i % 100 * 0.6f - 30, i / 100 * 0.6f, 20)));
                World.Add(e, new SpriteRenderer { Billboard = i % 2 == 0 ? SpriteBillboard.Camera : SpriteBillboard.None });
                World.Add(e, new SpriteAnimator(run) { Speed = 0.5f + i % 7 * 0.25f });
                list.Add(e);
            }
            SpriteAnimator.PreviewInEditor = true;
            for (int i = 0; i < 30; i++) yield return 0;
            var samples = new List<double>();
            for (int i = 0; i < 120; i++)
            {
                yield return 0;
                samples.Add(_app.Engine.SpriteStats.Milliseconds);
            }
            samples.Sort();
            Console.WriteLine($"[Test] sprites-bench {count} sprites : animation + sprite sync median {samples[samples.Count / 2]:F3} ms, max {samples[^1]:F3} ms, materials {_app.Engine.SpriteStats.Materials}, matrices sent {_app.Engine.SpriteStats.MatricesSent}");
            check(_app.Engine.SpriteStats.Sprites == count, $"[sprites-bench] {count} sprites");
            SpriteAnimator.PreviewInEditor = false;
            foreach (var e in list) World.DestroyEntity(e);
            for (int i = 0; i < 3; i++) yield return 0;
        }
    }

    // 撮影用 (--editor-test-section sprites-view) : 見本のシーンを再生したまま 30 秒
    private IEnumerable<int> spriteView()
    {
        SpriteSamples.Write(_app.Database);
        _app.OpenScene(_app.Database.FullPath(SpriteSamples.ScenePath));
        yield return 0;
        _app.TogglePlay();
        var start = DateTime.Now;
        var boomed = false;
        while ((DateTime.Now - start).TotalSeconds < 30)
        {
            if (!boomed && (DateTime.Now - start).TotalSeconds > 6) { Input.SimulateKeyPress(Keys.Space); boomed = true; }
            yield return 0;
        }
        _app.TogglePlay();
    }
}
