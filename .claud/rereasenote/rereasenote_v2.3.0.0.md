# Tsumugi Engine v2.3.0.0 リリースノート

- 前の版 : v2.2.0.0（develop `dd83188`）
- この版 : v2.3.0.0（ブランチ `feature/v23-image-editor`。develop へのマージ前）
- 日付 : 2026-11-28

---

## 1. アップグレードできるか（結論）

**v2.2.0.0（と v2.1.0.0）で作ったプロジェクトは、そのまま v2.3.0.0 で開けます。**
シーン・プレハブ・マテリアル・ProjectSettings.json・ゲームのコード・シェーダーの形式は変わっていません。
削除・名前変更したファイル / 公開 API はありません（足しただけです）。`hlslTemplate.shader` も変えていません。

| 確認する物 | 当てはまる時 | 影響 |
|---|---|---|
| ゲームのコード / エディターの拡張（`Scripts/**/*.cs`） | 下の **2-1** の型名を `GameEngine.Editor` 名前空間で自分で定義している | 名前の重複（ビルドエラー） |
| 自分のシェーダー | なし | なし |
| Project の画像のダブルクリック | 自分の `AssetImporter` で .png / .jpg / .bmp / .tga の `OnOpen` を使っている | **2-2** を参照 |

### 既存のプロジェクトの確認結果（2026-11-28）

| プロジェクト | EditorVersion | 2-1（型名） | 画像の数 | 判定 |
|---|---|---|---|---|
| `D:\TumugiEngineProject\PazzleGame\PazzleGame` | 2.2.0.0 | 該当なし | 0 | **そのまま上げてよい** |
| `D:\TumugiEngineProject\AssetCheck\AssetCheckProject` | 2.1.0.0 | 該当なし | 0 | **そのまま上げてよい** |
| `CSharp_console\Project`（リポジトリの見本） | 2.2.0.0 | 該当なし | 29 | 上げてよい |

---

## 2. 確認が必要な所

### 2-1. 足した公開の型 / メンバー（名前の重複の確認用）

- `GameEngine` : `Graphics.ReleaseUITexture(uint)`
- `GameEngine.Editor` : `ImageEditorManager`、`ImageEditorWindow`、`EditorApp.OpenImage` / `EditorApp.ImageEditors`
- `GameEngine.Imaging`（新しいアセンブリ `GameEngine.Imaging.dll`。エディターだけが参照。ゲームの書き出しには入らない） :
  `ImageBuffer`、`Codecs.*`（`ImageCodec` `ImageFormat` `ImageInfo` `DecodedImage` `ImageFormatException` `ImageSaveOptions` `Zlib`）、
  `Processing.*`（`ImageOperation` と各処理、`ImagePipeline` `PipelineCache` `ProcessContext` `Fft2D` `ToneCurve` `CurvePoint` `NormalMap` `NormalMapSettings` `ImageMath` `Lut` `ImageStatistics` ほか）
  — 別の名前空間なので、`using GameEngine.Imaging` をしなければ重なりません
- C++ : `Renderer_ReleaseUITexture`（Renderer.dll）、`IRenderHardwareInterface::ReleaseUITexture`（rhi.h。仮想関数を足したので RHI.dll と Renderer.dll は同じ版をそろえて使う）

### 2-2. 動きが変わった所

| 変わった所 | 以前 | 今 | 戻し方 |
|---|---|---|---|
| Project で画像（.png / .jpg / .jpeg / .bmp / .tga）をダブルクリック | 何も起きない（自分の `AssetImporter` があれば `OnOpen`） | 画像エディターが開く（**組み込みの画像エディターが先**。.dds は今まで通り） | 自分の `OnOpen` を使いたい時は、Assets > Show in Explorer など別の道で開く |
| UI のテクスチャ（C++） | `vector` で持つ（返せない） | 番号で引く表（`ReleaseUITexture` で返せる） | 作る / 描く動きは同じ |
| `Inflate.cs` | — | `#if GAMEENGINE_IMAGING` を足しただけ（Runtime の中では今までと同じ public / `GameEngine.Fbx`） | — |

---

## 3. アップグレードの手順

1. （念のため）プロジェクトのフォルダをコピーしておく / git にコミットしておく
2. 下の **確認スクリプト** をプロジェクトのフォルダで実行し、何も出ないことを確かめる
3. ランチャーでプロジェクトを選び、`Package (TsumugiEngine_v2.3.0.0)` で開く（v2.2.0.0 / v2.1.0.0 も残っているので、版の選択で戻せます）
4. 開いたら `ProjectVersion.txt` が `EditorVersion: 2.3.0.0` になる
5. Console にスクリプトのエラーが無いことを確認する
6. 書き出したゲームは書き出し直さなくてよい（ランタイムで変わったのはエディター用の関数を足しただけ。書き出し直しても問題ありません）

### 確認スクリプト（PowerShell。プロジェクトのフォルダで実行）

```powershell
Get-ChildItem Scripts -Recurse -Include *.cs | Select-String -Pattern '\b(class|struct|record|enum|interface)\s+(ImageEditorManager|ImageEditorWindow)\b|namespace\s+GameEngine\.Imaging\b|\bReleaseUITexture\b' | ForEach-Object { "[code] $($_.Path):$($_.LineNumber)" }
Get-ChildItem Scripts -Recurse -Include *.cs | Select-String -Pattern 'Extensions\s*=>.*\.(png|jpe?g|bmp|tga)\b' | ForEach-Object { "[importer : double click now opens the image editor] $($_.Path):$($_.LineNumber)" }
```

何も出なければ、そのまま上げて問題ありません。

---

## 4. 新しい機能（概要）

- **画像エディター** : Project の画像をダブルクリックで開く（`--open-image <path>` でも）
  - 色 : グレースケール（R×0.30 + G×0.59 + B×0.11）/ RGB 各スケール / ポスタライズ / ソラリゼーション / ネガポジ反転 / セピア / ガンマ補正
  - 画像処理 : ガウシアンブラー / 鮮鋭化 / ソーベル / エンボス / モザイク / 色収差
  - 周波数 : FFT のスペクトルの中央に黒丸（大きさ・ぼかし・反転（ローパス））
  - トーンカーブ : グラフに点を置く（RGB / R / G / B）
  - 法線マップ : 輝度を高さとして生成（DirectX / OpenGL）。表示だけの Normal / Luminance（輝度）モード
  - 加工前と比べる（After / Before / 並べる / ワイプ）、保存（上書き / 別名、PNG / JPEG / BMP / TGA）、リセット、Undo / Redo
- **GameEngine.Imaging** : 画像の読み書き（PNG / JPEG / BMP / TGA。全部自前）と加工のライブラリ。エンジンに依存しないので、DLL かソースを他のプロジェクトへ持ち出せる

詳しくは `Docs/Reference/index.html`（「画像エディター / GameEngine.Imaging (v2.3)」）と `.claud/log/v2.3.0.0_image_editor_2026-11-28.txt`。

---

## 5. 変更したファイル（v2.2.0.0 → v2.3.0.0）

**削除したファイル : なし / 名前を変えたファイル : なし**

### 5-1. プロジェクト側で確認するファイル

なし（プロジェクトの中のファイル・見本・シェーダーは変えていません）。
Package では `Editor/TsumugiEngine_v2.3.0.0/` が増えました（v2.1.0.0 / v2.2.0.0 は残る）。

### 5-2. エンジン（ランタイム。書き出したゲームにも入る）

| 状態 | ファイル |
|---|---|
| 変更 | `CSharp_console/GameEngine.Runtime/Assets/Fbx/Inflate.cs`（`#if GAMEENGINE_IMAGING` だけ） |
| 変更 | `CSharp_console/GameEngine.Runtime/Engine.cs`（`Graphics.ReleaseUITexture`） |
| 変更 | `CSharp_console/GameEngine.Runtime/Native/NativeMethods.cs` |
| 変更 | `RHI/Source/Public/rhi.h` |
| 変更 | `RHI/Source/RenderHardwareInterface.cpp` / `.h` |
| 変更 | `RHI/Source/DirectX12/RHIObject/CommandContext.cpp` / `.h` |
| 変更 | `Renderer/Source/rendererDll.cpp`（`Renderer_ReleaseUITexture`） |
| 変更 | `Renderer/Source/sceneRenderer.cpp` / `.h` |

### 5-3. 画像のライブラリ（新規。エディターだけが使う）

| 状態 | ファイル |
|---|---|
| 追加 | `CSharp_console/GameEngine.Imaging/GameEngine.Imaging.csproj` |
| 追加 | `CSharp_console/GameEngine.Imaging/ImageBuffer.cs` |
| 追加 | `CSharp_console/GameEngine.Imaging/Codecs/ImageCodec.cs` / `Png.cs` / `Jpeg.cs` / `Bmp.cs` / `Tga.cs` / `Deflater.cs` |
| 追加 | `CSharp_console/GameEngine.Imaging/Processing/ImageOperation.cs` / `ColorFilters.cs` / `Filters.cs` / `Frequency.cs` / `ToneCurve.cs` / `NormalMap.cs` / `ImagePipeline.cs` |

### 5-4. エディター / ツール / ドキュメント

| 状態 | ファイル |
|---|---|
| 変更 | `CSharp_console/Engine.props`（EngineVersion 2.3.0.0） |
| 追加 | `CSharp_console/GameEngine.Editor/ImageEditor/ImageEditorManager.cs` / `ImageEditorWindow.cs` / `ImageEditorWindow.Panels.cs` / `ImageView.cs` |
| 追加 | `CSharp_console/GameEngine.Editor/EditorTest.Imaging.cs` |
| 変更 | `CSharp_console/GameEngine.Editor/EditorApp.cs` / `EditorApp.Edit.cs` |
| 変更 | `CSharp_console/GameEngine.Editor/EditorTest.cs`（section `imaging` / `imaging-view`） |
| 変更 | `CSharp_console/GameEngine.Editor/GameEngine.Editor.csproj`（Imaging の参照、リファレンスに Imaging のソース） |
| 変更 | `CSharp_console/GameEngine.Editor/Program.cs`（`--open-image`） |
| 変更 | `CSharp_console/GameEngine.Editor/ProjectPanel.cs`（画像のダブルクリック） |
| 変更 | `CSharp_console/GameEngine.Editor/ScriptReference.cs`（Imaging API） |
| 変更 | `Docs/Reference/index.html` / `template.html` / `make_reference.py` |
| 追加 | `.claud/log/v2.3.0.0_image_editor_design_2026-11-28.txt` / `.claud/log/v2.3.0.0_image_editor_2026-11-28.txt` |
| 追加 | `.claud/rereasenote/rereasenote_v2.3.0.0.md`（このファイル） |

---

## 6. 確認済みのこと

- ビルド : RHI / Renderer（Debug / Release）、Imaging / Runtime / Editor / Launcher / Player : 成功（以前からの警告だけ）
- コーデックと処理の単体の確認（System.Drawing と比べる）: 119 / 0
- エディターのテスト : 794 / 0（imaging 53 を含む）。Package の Release のエディターで imaging 53 / 0
- D3D12 デバッグレイヤー : エラー / 警告なし
