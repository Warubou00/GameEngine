# Tsumugi Engine v2.2.0.0 リリースノート

- 前の版 : v2.1.0.0（develop `c827425`）
- この版 : v2.2.0.0（develop `b9554e5`。ブランチ `feature/v22-local-lights-splash` / `feature/v22-scene-view-modes`）
- 日付 : 2026-11-22〜23

---

## 1. アップグレードできるか（結論）

**v2.1.0.0 で作ったプロジェクトは、そのまま v2.2.0.0 で開けます。**
シーン・プレハブ・マテリアル・ProjectSettings.json・ゲームのコードの形式は変わっていません。
削除・名前変更したファイル / 公開 API はありません（足しただけです）。

ただし、次の **「2. 確認が必要な所」** に当てはまるプロジェクトは、開く前に確認してください。

| 確認する物 | 当てはまる時 | 影響 |
|---|---|---|
| 自分のシェーダー（`Assets/**/*.hlsl`・`*.shadergraph.json`） | 下の **2-1** の名前を自分で定義している | コンパイルエラー（前の版のシェーダーのまま動く） |
| 自分のシェーダーで `g_Lights` を回している | `Type != 0` を「点光源」とみなしている | スポットライト（`Type == 2`）も点光源として計算される |
| ゲームのコード（`Scripts/**/*.cs`） | 下の **2-2** の型名を `GameEngine` 名前空間などで自分で定義している | 名前の重複（ビルドエラー） |
| 自分の RenderPipeline | `ShadowPass` を使わず影を自前で描いている | 点光源・スポットライトの影が出ない（光は出る） |
| エディターの拡張（`Scripts/Editor`） | なし（足しただけ） | なし |

### 既存のプロジェクトの確認結果（2026-11-23）

| プロジェクト | EditorVersion | 2-1（シェーダー） | 2-2（型名） | 点光源のあるシーン | 自分のシェーダー / パイプライン | 判定 |
|---|---|---|---|---|---|---|
| `D:\TumugiEngineProject\PazzleGame\PazzleGame` | 2.1.0.0 | 該当なし | 該当なし | 0 | 0 / 0 | **そのまま上げてよい** |
| `D:\TumugiEngineProject\AssetCheck\AssetCheckProject` | 2.1.0.0 | 該当なし | 該当なし | 0 | 0 / 0 | **そのまま上げてよい** |
| `CSharp_console\Project`（リポジトリの見本） | — | 該当なし | `SampleViewModes.cs`（v2.2 の見本そのもの） | 2（showcase） | 1 hlsl / 0 | 上げてよい |

---

## 2. 確認が必要な所

### 2-1. シーンのシェーダー（hlslTemplate.shader）に足した名前

自分のシェーダー（`.hlsl` アセット / シェーダーグラフ）は `hlslTemplate.shader` の後ろに付けてコンパイルされるため、次の名前を自分で定義していると重複します。

- 定数 : `LIGHT_DIRECTIONAL` `LIGHT_POINT` `LIGHT_SPOT` `CLUSTER_X` `CLUSTER_Y` `CLUSTER_Z` `CLUSTER_COUNT`
- リソース : `g_Clusters`（register `t6`）
- cbuffer FrameData の値 : `g_LocalShadowTexture` `g_LocalShadowTexelSize` `g_LocalShadowSoftness` `g_ClusterEnabled` `g_DirectionalCount` `g_ClusterZScale` `g_ClusterZBias`
  （`g_ShadowPadding` は無くなりました。並び / 大きさの前の部分は同じです）
- 関数 / 構造体 : `localAttenuation` `spotAttenuation` `localShadow` `ShadeParams` `shadeOneLight` `heatColor` `countLightsAt` `PSOverdraw` `PSDrawMap`
- `struct LightData` は 48 → 80 byte（後ろに `SpotScale` `SpotOffset` `SourceRadius` `FalloffExponent` `ShadowIndex` `ShadowNear` `ShadowBias` `ShadowTan` を追加）。前の 48 byte の並びは同じです
- `Lit()` / `shade()` / `shadeLights()` / `MainLight()` の使い方は変わりません（スポットライト・影・クラスターが自動で入ります）

### 2-2. 足した公開の型 / メンバー（名前の重複の確認用）

- `GameEngine` : `SpotLight`（コンポーネント）、`LocalLightValues`、`PointLight` のフィールド（`SourceRadius` `UseInverseSquaredFalloff` `FalloffExponent` `CastShadows` `ShadowBias`）、`DebugDraw.Triangle / Quad / SolidBox / SolidSphere / SolidCapsule / TriangleCount / LastTriangleCount / MaxTriangles`、`CollisionDraw.SolidPart / LayerHue`、`PhysicsSettings.ShowCollidersSolid / ColliderColorsByLayer`、`EngineSettings.ShowWindow`、`Engine.WindowShown`、`GraphicsSettings.LocalLights / LocalShadowSlices / ViewPipelineOverride`、`RenderingProjectValues.LocalLights`
- `GameEngine.Rendering` : `LocalLightSettings`、`ScenePass.Overdraw`、`RenderCommandBuffer.DrawDebugTriangles(wireframe, brightness)`、`ShadowPass.LocalShadowMap`
- `GameEngine.Editor` : `SceneViewMode`、`SceneViewModeAttribute`

### 2-3. 動きが変わった所（見た目 / 設定）

| 変わった所 | 以前 | 今 | 戻し方 |
|---|---|---|---|
| ライトの計算 | 全部のピクセルで全部のライトを回す | 画面の区画（クラスター）ごとに届くライトだけ | Project Settings > Rendering > Local Lights > Clustered を切る |
| 既存の `PointLight` | 影なし | **影なしのまま**（`CastShadows` の既定は false） | — |
| 新しい `SpotLight` | — | 既定で影あり（Max Shadow Slices の枚数まで） | `CastShadows` を切る |
| エディターの起動 | すぐ白い窓が出る | スプラッシュ画面 → 最初のフレームを描いてから窓を出す | `--no-splash` |
| Package の Editor フォルダ | 新しい版だけ（前の版は消える） | 前の版も残る | — |

点光源の距離の減衰（Inverse Squared Falloff）は以前と同じ式なので、既存のシーンの見た目は変わりません。

### 2-4. ProjectSettings.json

`Rendering.LocalLights`（Clustered / Shadows / ShadowResolution / MaxShadowSlices / ShadowSoftness）が増えました。
ファイルに無い時は既定値（Clustered on / Shadows on / 1024 / 12 / 1.0）を使うので、書き換えは要りません。

---

## 3. アップグレードの手順

1. （念のため）プロジェクトのフォルダをコピーしておく / git にコミットしておく
2. 下の **確認スクリプト** をプロジェクトのフォルダで実行し、何も出ないことを確かめる
3. ランチャーでプロジェクトを選び、`Package (TsumugiEngine_v2.2.0.0)` で開く
   （Package のランチャーは一番新しい版を選びます。v2.1.0.0 も残っているので、版の選択で戻せます）
4. 開いたら `ProjectVersion.txt` が `EditorVersion: 2.2.0.0` になる。この後 v2.1.0.0 で開くと、ランチャーが確認を出します
5. Console にシェーダー / スクリプトのエラーが無いこと、シーンの見た目が変わっていないことを確認する
6. 書き出したゲームを使っている場合は、File > Build Game で書き出し直す（Player とシェーダーが新しくなる）

### 確認スクリプト（PowerShell。プロジェクトのフォルダで実行）

```powershell
$names = 'LIGHT_DIRECTIONAL|LIGHT_POINT|LIGHT_SPOT|CLUSTER_[XYZ]|CLUSTER_COUNT|g_Clusters|localAttenuation|spotAttenuation|localShadow|ShadeParams|shadeOneLight|heatColor|countLightsAt|PSOverdraw|PSDrawMap|g_LocalShadow\w*|g_Cluster\w*|g_DirectionalCount|g_ShadowPadding'
Get-ChildItem Assets -Recurse -Include *.hlsl, *.shadergraph.json | Select-String -Pattern "\b($names)\b" | ForEach-Object { "[shader] $($_.Path):$($_.LineNumber)" }
Get-ChildItem Assets -Recurse -Include *.hlsl, *.shadergraph.json | Select-String -Pattern 'Type\s*!=\s*0' | ForEach-Object { "[shader : spot light check] $($_.Path):$($_.LineNumber)" }
Get-ChildItem Scripts -Recurse -Include *.cs | Select-String -Pattern '\b(class|struct|record|enum)\s+(SpotLight|LocalLightSettings|LocalLightValues|SceneViewMode|SceneViewModeAttribute)\b' | ForEach-Object { "[code] $($_.Path):$($_.LineNumber)" }
Get-ChildItem Scripts -Recurse -Include *.cs | Select-String -Pattern ':\s*RenderPipeline\b' | ForEach-Object { "[pipeline : check local shadows] $($_.Path):$($_.LineNumber)" }
```

何も出なければ、そのまま上げて問題ありません。

---

## 4. 新しい機能（概要）

- **点光源 / スポットライト（UE 相当）** : `SpotLight`（内側 / 外側の角度）、Source Radius、Inverse Squared Falloff / Falloff Exponent、点光源（6 面）/ スポット（1 面）の影、クラスター（256 個で 4.39 → 0.94 ms）。GameObject > Light、シーンビューのライトの印と範囲
- **スプラッシュ画面** : エディターの起動中に段階 / 経過時間 / ログを表示
- **シーンビューの表示モード** : View のプルダウン（Default / DrawMap / CollisionMap）。CollisionMap は Solid / Wireframe / Solid + Wireframe。`[SceneViewMode]` でプロジェクトからモードを追加できる（見本 `Scripts/Editor/SampleViewModes.cs`）
- **Package** : 前の版のエディターを残す。Luncher の `.lnk` を残す

詳しくは `Docs/Reference/index.html`（「点光源 / スポットライト / クラスター / スプラッシュ画面 / シーンビューの表示モード (v2.2)」）と `.claud/log/v2.2.0.0_local_lights_splash_2026-11-22.txt`。

---

## 5. 変更したファイル（v2.1.0.0 → v2.2.0.0）

**削除したファイル : なし / 名前を変えたファイル : なし**

### 5-1. プロジェクト側で確認するファイル

| 状態 | ファイル | 内容 |
|---|---|---|
| 追加 | `CSharp_console/Project/Scripts/Editor/SampleViewModes.cs` | 表示モードの見本（Unlit (Albedo) / Light Ranges）。自分のプロジェクトには入らない。使うなら写す |
| 変更 | `RHI/Source/Shader/hlslTemplate.shader` | 自分のシェーダーの前に付く。**2-1 を確認** |
| 変更 | `RHI/Source/Shader/debug.shader` | 確認用の線 / 三角形（自分のシェーダーには関係しない） |

Package では `Editor/TsumugiEngine_v2.2.0.0/` が増え（`Editor/TsumugiEngine_v2.1.0.0/` は残る）、`Project/SampleProject/Scripts/Editor/SampleViewModes.cs` が増えました。

### 5-2. エンジン（ランタイム。書き出したゲームにも入る）

| 状態 | ファイル |
|---|---|
| 変更 | `CSharp_console/GameEngine.Runtime/Assets/CollisionMesh.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Assets/Model.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Components/Light.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Components/RenderSettings.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/DebugDraw.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Engine.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Native/NativeMethods.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Physics.cs` |
| 追加 | `CSharp_console/GameEngine.Runtime/Rendering/LocalLightSettings.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Rendering/RenderCommandBuffer.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Rendering/ShadowPass.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Systems/CollisionSystem.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Systems/CollisionSystem.Compound.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Systems/LightSystem.cs` |
| 変更 | `CSharp_console/GameEngine.Runtime/Systems/RenderSettingsSystem.cs` |
| 変更 | `RHI/Source/Public/rhi.h` |
| 変更 | `RHI/Source/Public/rhiTypes.h`（LightData 80 / LightingSettings 32 / ShadowSettings 64 byte） |
| 変更 | `RHI/Source/RenderHardwareInterface.cpp` / `.h` |
| 変更 | `RHI/Source/DirectX12/RHIObject/Buffer.cpp` / `.h` |
| 変更 | `RHI/Source/DirectX12/RHIObject/CommandContext.cpp` / `.h` / `CommandContext_Passes.cpp` |
| 変更 | `RHI/Source/DirectX12/RHIObject/Pipeline.cpp` / `.h`（ルートシグネチャ [9] = t6 を追加） |
| 変更 | `Renderer/Source/rendererDll.cpp`（`Renderer_SetDebugTriangles`） |
| 変更 | `Renderer/Source/sceneRenderer.cpp` / `.h` |

C++ の構造体の大きさとルートシグネチャが変わったので、RHI.dll / Renderer.dll / シェーダーは必ず同じ版の物を一緒に使ってください（Package と書き出したゲームは自動でそろいます）。

### 5-3. エディター / 見本 / ツール

| 状態 | ファイル |
|---|---|
| 変更 | `CSharp_console/Engine.props`（EngineVersion 2.2.0.0） |
| 変更 | `CSharp_console/GameEngine.Editor/ConsoleLog.cs` |
| 追加 | `CSharp_console/GameEngine.Editor/EditorApp.Lights.cs` |
| 変更 | `CSharp_console/GameEngine.Editor/EditorApp.cs` |
| 変更 | `CSharp_console/GameEngine.Editor/EditorExtensions.cs` |
| 追加 | `CSharp_console/GameEngine.Editor/EditorTest.Lights.cs` |
| 追加 | `CSharp_console/GameEngine.Editor/EditorTest.ViewModes.cs` |
| 変更 | `CSharp_console/GameEngine.Editor/EditorTest.cs` |
| 変更 | `CSharp_console/GameEngine.Editor/Program.cs`（`--no-splash` / `--view-mode`） |
| 追加 | `CSharp_console/GameEngine.Editor/SceneViewModes.cs` |
| 追加 | `CSharp_console/GameEngine.Editor/SplashScreen.cs` |
| 変更 | `CSharp_console/GameEngine_console/Program.cs`（`--scene lights` / `--light-bench` / `--gpu-stats`） |
| 追加 | `CSharp_console/GameEngine_console/Samples/LightSamples.cs` |
| 変更 | `Tools/make_package.py` |
| 変更 | `Docs/Reference/index.html` / `template.html` |
| 変更 | `CLAUD.md`（5-11 前の版のエディターを残す / 9 リリースノート） |
| 追加 | `.claud/log/v2.2.0.0_local_lights_splash_2026-11-22.txt` と `.claud/log/images/v2.2.0.0_*.png`（9 枚） |
| 追加 | `.claud/rereasenote/rereasenote_v2.2.0.0.md`（このファイル） |

---

## 6. 確認済みのこと

- ビルド : RHI / Renderer（Debug / Release）、Runtime / Editor / Launcher / Player / 見本 : 成功（以前からの警告だけ）
- エディターのテスト : 742 / 0（lights 16 / viewmodes 15 を含む）
- D3D12 デバッグレイヤー : ライトの見本（Forward / Deferred / MSAA4）、DrawMap、CollisionMap（3 つの Style）でエラー / 警告なし
- Package : v2.2.0.0 のエディターで SampleProject を開き、スクリプトのビルドと表示モードの読み込みを確認
