# Tsumugi Engine v2.4.0.0 リリースノート

- 前の版 : v2.3.0.0（develop `b118253`）
- この版 : v2.4.0.0（ブランチ `feature/v24-sprite-animation`。develop へのマージ前）
- 日付 : 2026-11-28

---

## 1. アップグレードできるか（結論）

**v2.3.0.0 / v2.2.0.0 / v2.1.0.0 で作ったプロジェクトは、そのまま v2.4.0.0 で開けます。**
シーン・プレハブ・マテリアル・Widget・ProjectSettings.json・シェーダーの形式は変わっていません（`hlslTemplate.shader` / C++ も変えていません）。
削除・名前変更したファイル / 公開 API はありません（足しただけです）。

| 確認する物 | 当てはまる時 | 影響 |
|---|---|---|
| ゲームのコード（`Scripts/**/*.cs`） | 下の **2-1** の型名を自分で定義していて、`using GameEngine;` している | 名前があいまい（ビルドエラー） |
| `Behaviour<T>` を継ぐクラス | `OnSpriteEvent` / `OnSpriteFinished` という名前のメソッドを自分で書いている | 警告（隠す）/ 同じ引数なら override 扱いになる |
| UI の画像（UImage / ボタンのブラシなど） | — | 読み込みが System.Drawing から自前の読み込みに変わった（**2-2**） |
| UI の Tick を上書きした自作の Widget | 入力を受けない時は Tick されないことに頼っている | 再生中はいつも Tick される（**2-2**） |

### 既存のプロジェクトの確認結果（2026-11-28）

| プロジェクト | EditorVersion | 2-1（型名） | OnSprite* | TGA | 判定 |
|---|---|---|---|---|---|
| `D:\TumugiEngineProject\PazzleGame\PazzleGame` | 2.2.0.0 | 該当なし | 該当なし | 0 | **そのまま上げてよい** |
| `D:\TumugiEngineProject\AssetCheck\AssetCheckProject` | 2.1.0.0 | 該当なし | 該当なし | 0 | **そのまま上げてよい** |
| `CSharp_console\Project`（リポジトリの見本） | 2.2.0.0 | 該当なし | `SpriteDemo.cs`（v2.4 の見本そのもの） | 0 | 上げてよい |

---

## 2. 確認が必要な所

### 2-1. 足した公開の型 / メンバー（名前の重複の確認用）

- `GameEngine` : `SpriteAnimation`、`SpriteFrame`、`SpriteFrameTexture`、`SpriteSample`、`SpriteLoopMode`、`SpriteLoopOverride`、`SpriteBillboard`、
  `SpriteRenderer`（コンポーネント）、`SpriteAnimator`（コンポーネント）、`USpriteImage`（Widget）、`UIPainter.Texture`、
  `AssetManager.LoadSpriteAnimation / ReloadSpriteAnimation / FindLoadedSpriteAnimation / LoadedSpriteAnimations / SaveSpriteAnimation / LoadSpriteTexture / Adopt / ReleaseSpriteTextures / SpriteTextureCount`、
  `Engine.SpriteStats`、`Behaviour<T>.OnSpriteEvent / OnSpriteFinished`、`AssetKind.SpriteAnimation`（最後に足した。.meta は名前で書くので既存の値は変わらない）
- `GameEngine.Editor` : `SpriteAnimationEditor`、`EditorApp.OpenSpriteAnimation / SpriteEditor`
- `GameEngine.Runtime.dll` が `GameEngine.Imaging.dll` を参照するようになった（書き出したゲーム / Player に 1 つ増える。Package と Build Game は自動で入る）

### 2-2. 動きが変わった所

| 変わった所 | 以前 | 今 | 戻し方 |
|---|---|---|---|
| UI の画像の読み込み（`UITextures.Get`） | System.Drawing（TGA は出ない） | 自前の読み込み（GameEngine.Imaging。PNG / JPEG / BMP / TGA）。色は同じ（v2.3 で System.Drawing と一致を確認済み） | — |
| UI の `Tick`（`UWidget.Tick`） | 再生中かつ入力を受ける時だけ | 再生中はいつも（エディターのカメラで見ている時も） | — |
| Project のテクスチャの右クリック | — | Create Sprite Animation が増えた | — |
| `.spriteanim.json` | 種類 Text | 種類 SpriteAnimation | — |

---

## 3. アップグレードの手順

1. （念のため）プロジェクトのフォルダをコピーしておく / git にコミットしておく
2. 下の **確認スクリプト** をプロジェクトのフォルダで実行し、何も出ないことを確かめる
3. ランチャーでプロジェクトを選び、`Package (TsumugiEngine_v2.4.0.0)` で開く（v2.3.0.0 以前も残っているので、版の選択で戻せます）
4. 開いたら `ProjectVersion.txt` が `EditorVersion: 2.4.0.0` になる
5. Console にスクリプトのエラーが無いこと、UI の画像の見た目が変わっていないことを確認する
6. 書き出したゲームは File > Build Game で書き出し直す（Player に GameEngine.Imaging.dll が入る）

### 確認スクリプト（PowerShell。プロジェクトのフォルダで実行）

```powershell
$names = 'SpriteAnimation|SpriteFrame|SpriteFrameTexture|SpriteSample|SpriteLoopMode|SpriteLoopOverride|SpriteBillboard|SpriteRenderer|SpriteAnimator|USpriteImage|SpriteAnimationEditor'
Get-ChildItem Scripts, Assets -Recurse -Include *.cs -ErrorAction SilentlyContinue | Select-String -Pattern "\b(class|struct|record|enum|interface)\s+($names)\b" | ForEach-Object { "[code : name clash] $($_.Path):$($_.LineNumber)" }
Get-ChildItem Scripts, Assets -Recurse -Include *.cs -ErrorAction SilentlyContinue | Select-String -Pattern '\b(OnSpriteEvent|OnSpriteFinished)\b' | ForEach-Object { "[code : same name as the new Behaviour methods] $($_.Path):$($_.LineNumber)" }
Get-ChildItem Scripts, Assets -Recurse -Include *.cs -ErrorAction SilentlyContinue | Select-String -Pattern 'override\s+void\s+Tick\s*\(' | ForEach-Object { "[widget : Tick is now called while not receiving input] $($_.Path):$($_.LineNumber)" }
```

何も出なければ、そのまま上げて問題ありません。

---

## 4. 新しい機能（概要）

- **スプライトのアニメーション**（`.spriteanim.json`）: コマ（画像 / 切り出す四角 / 時間 / イベント）、Loop / Once / PingPong、PixelsPerUnit / Pivot。シートはコマごとのテクスチャに切り出す（にじまない）
- **GameObject** : `SpriteRenderer`（板。ビルボード / 反転 / 大きさ）+ `SpriteAnimator`（Play / Stop / Pause / Resume / SetFrame、HideWhenStopped）。`OnSpriteEvent` / `OnSpriteFinished`。GameObject > 2D > Sprite / Animated Sprite。シーンビューでクリックして選べる
- **Widget** : `USpriteImage`（Widget Designer の Palette > Common。デザイナーの中でも動く）、`OnFrameEvent` / `OnFinished`
- **Sprite Animation Editor** : コマの一覧 / プレビュー / シートを切る / 自動保存 / Undo / Animate in scene
- **見本** : Assets > Create Sprite Samples（`Assets/Samples/Sprites`、`Assets/Scenes/sprites.scene.json`、`Scripts/SpriteDemo.cs`）
- 速さ（Release、RTX 2070 の PC）: 動くスプライト 1000 個 0.42 ms / 5000 個 1.78 ms（アニメーション + 板の同期、中央値）

詳しくは `Docs/Reference/index.html`（「スプライトのアニメーション (v2.4)」）と `.claud/log/v2.4.0.0_sprite_animation_2026-11-28.txt`。

---

## 5. 変更したファイル（v2.3.0.0 → v2.4.0.0）

**削除したファイル : なし / 名前を変えたファイル : なし**

### 5-1. プロジェクト側で確認するファイル

| 状態 | ファイル | 内容 |
|---|---|---|
| 追加 | `CSharp_console/Project/Scripts/SpriteDemo.cs` | 見本のスクリプト（自分のプロジェクトには入らない。使うなら写す） |
| 追加 | `CSharp_console/Project/Assets/Samples/Sprites/*`（png 13 / spriteanim.json 3 / widget.json 1 と .meta） | 見本（Assets > Create Sprite Samples で作り直せる） |
| 追加 | `CSharp_console/Project/Assets/Scenes/sprites.scene.json` | 見本のシーン |

Package では `Editor/TsumugiEngine_v2.4.0.0/` が増え（前の版は残る）、`Project/SampleProject/Scripts/SpriteDemo.cs` を足しました。

### 5-2. エンジン（ランタイム。書き出したゲームにも入る）

| 状態 | ファイル |
|---|---|
| 追加 | `CSharp_console/GameEngine.Runtime/Assets/SpriteAnimation.cs` |
| 追加 | `CSharp_console/GameEngine.Runtime/Assets/AssetManager.Sprites.cs` |
| 追加 | `CSharp_console/GameEngine.Runtime/Components/Sprite.cs` |
| 追加 | `CSharp_console/GameEngine.Runtime/Systems/SpriteSystems.cs` |
| 追加 | `CSharp_console/GameEngine.Runtime/UI/SpriteImage.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/GameEngine.Runtime.csproj`（GameEngine.Imaging を参照） |
| 変更 | `CSharp_console/GameEngine.Runtime/Assets/AssetDatabase.cs`（AssetKind.SpriteAnimation） |
| 変更 | `CSharp_console/GameEngine.Runtime/Assets/Material.cs`（internal TextureHandleOverride） |
| 変更 | `CSharp_console/GameEngine.Runtime/Assets/Model.cs`（ReloadTexture でスプライトも） |
| 変更 | `CSharp_console/GameEngine.Runtime/Ecs/Behaviour.cs`（OnSpriteEvent / OnSpriteFinished） |
| 変更 | `CSharp_console/GameEngine.Runtime/Engine.cs`（スプライトの System / SpriteStats） |
| 変更 | `CSharp_console/GameEngine.Runtime/Serialization/SceneSerializer.cs`（SpriteAnimation のパス） |
| 変更 | `CSharp_console/GameEngine.Runtime/Systems/CameraSystems.cs`（Forward / Up） |
| 変更 | `CSharp_console/GameEngine.Runtime/UI/UIDrawList.cs`（UIPainter.Texture） |
| 変更 | `CSharp_console/GameEngine.Runtime/UI/UIManager.cs`（Tick） |
| 変更 | `CSharp_console/GameEngine.Runtime/UI/UIResources.cs`（画像の読み込み） |

C++ / シェーダーは変えていません。

### 5-3. エディター / ツール / ドキュメント

| 状態 | ファイル |
|---|---|
| 変更 | `CSharp_console/Engine.props`（EngineVersion 2.4.0.0） |
| 追加 | `CSharp_console/GameEngine.Editor/SpriteAnimationEditor.cs` / `SpriteSamples.cs` / `EditorApp.Sprites.cs` / `EditorTest.Sprites.cs` |
| 変更 | `CSharp_console/GameEngine.Editor/EditorApp.cs` / `EditorApp.Edit.cs` / `EditorTest.cs` / `Inspector.cs` / `Inspector.AssetField.cs` / `Program.cs`（`--open-sprite`） / `ProjectPanel.cs` / `SceneTools.cs`（板のクリック） / `WidgetDesigner.cs` |
| 変更 | `.gitignore`（*.spriteanim.json と見本の png） |
| 変更 | `Docs/Reference/index.html` / `template.html` / `make_reference.py` |
| 追加 | `.claud/log/v2.4.0.0_sprite_animation_design_2026-11-28.txt` / `.claud/log/v2.4.0.0_sprite_animation_2026-11-28.txt` / `.claud/log/images/v2.4.0.0_*.png` |
| 追加 | `.claud/rereasenote/rereasenote_v2.4.0.0.md`（このファイル） |

---

## 6. 確認済みのこと

- ビルド : Imaging / Runtime / Editor / Launcher / Player（Debug / Release）: 成功（以前からの警告だけ）
- エディターのテスト : 829 / 0（sprites 36 を含む）。Package の Release のエディターで sprites 36 / 0、sprites-bench
- D3D12 デバッグレイヤー : エラー / 警告なし
