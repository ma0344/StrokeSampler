# Copilot 作業サマリ（スレッド共有用）

## 目的
このスレッドで実施した変更内容・現状を、別スレッド（別担当/別Copilot）に引き継ぐためのメモです。

## 進め方（方針）
## InkCanvas重ね塗り（Dot/S200）: 確定事項の整理（2026-02）
- 目的: 低圧/高圧での見た目差（等高線化、飽和帯、谷が残る等）が「スタンプ生成」か「InkCanvas累積合成」かを切り分け。
- 手順: `DrawDotButton` を同一座標にN回押してInkCanvasに累積 → `ExportHighResPngCroppedTransparentButton` で8bit alpha PNG出力 → ImageMagickでα差分統計。
- 実装: 切り分けのため「最後に追加したStrokeのみ」をHiResで出力するボタンを追加（出力名に `-laststroke` を付与）。
- 確定1: laststroke（1回分）の差分は P=0.1/P=1 ともに N1-N2, N2-N3 が完全一致（mean=0, stddev=0）。
  - 結論: 同一座標・同一筆圧・紙目ワールド固定なら、スタンプ生成は決定的で毎回同一。
- 確定2: canvas（累積）差分は P で挙動が異なる。
  - P=0.1: Δ12=Δ23、ΔΔ=0（増分が完全に一定）。
  - P=1: Δ23 < Δ12、ΔΔ≠0（飽和に近づくほど増分が減る挙動と整合）。
  - 結論: P依存の見た目差は主に InkCanvas 側の累積（合成・飽和・8bit量子化）で生じる。

詳細: `docs/inkcanvas-stack-analysis.md` に確定事項を整理。

## 追加実装: HiResエクスポートの保存前α統計CSV（2026-02）
- 目的: 8bit量子化が「PNG保存時のみ」か「描画ターゲット時点（CanvasRenderTarget BGRA8）」で既に起きているかを観測で切り分ける。
- UI: `Export HiRes PreSave α Stats (Canvas)` / `Export HiRes PreSave α Stats (LastStroke)`
- 出力: `pencil-highres-pre-save-alpha-...-pre-save-alpha-canvas...csv` / `...-pre-save-alpha-laststroke...csv`
- 集計項目: `alpha_min`, `alpha_max`, `alpha_mean(0..1)`, `alpha_stddev(0..1)`, `alpha_unique(0..256)`

## 方針決定: 合成式の推定（2026-02）
- 目的: InkCanvas累積の見た目をなるべく再現する。
- 方針: 合成候補を `source-over` / `add` / `max` の3つで実装し、HiResエクスポート（Win2Dレンダ）の `canvas` 出力と一致度（統計/差分）比較で推定する。

## 追加実装: HiRes Simulated Composite（2026-02）
- UI: `Export HiRes Simulated Composite (SO/Add/Max)`
- 入力: 現在の最後のStroke（`laststroke`）をHiResレンダしてα(BGRA8)を取得
- 出力: 合成式 `source-over` / `add` / `max` を N 回（`Dot512Overwrite`）適用したPNGと、pre-save α統計CSV

## 確定: HiResレンダ経路の累積はBGRA8上のsource-over（2026-02）
- P=1, N=3 で simulated source-over と実測canvas（pre-save）が統計完全一致。
- P=0.1 は N=3 では add と source-over が一致して見えるが、N=50 で add と source-over が分離。
- P=0.1, N=50 および P=1, N=50 で simulated source-over と実測canvas（pre-save）が統計完全一致。
- 結論: HiResエクスポート（Win2D `CanvasRenderTarget` + `DrawInk`）の累積合成は **BGRA8（8bit）** の上で **source-over** と見なしてよい。

## 追加実装: DotLab Analysis（2026-02）
- InkPointsDump解析: `DotLab/Sample/InkPointsDump/stroke_*_points.json` を読み、dd/dt（点間距離/時間差）統計をCSV出力するボタンを追加。
  - UI: `Export InkPointsDump Stats (dd/dt CSV)`
  - 出力: 選択フォルダに `inkpointsdump-dd-dt-stats-YYYYMMDD-HHmmss.csv`
  - 備考: dumpの `timestanp` typo と `timestamp` の両方に対応。
- α差分出力: 実測canvas PNG と sim PNG を選択し、αの絶対差画像（PNG）と統計CSVを出力するボタンを追加。
  - UI: `Export Alpha Diff (Canvas vs Sim)`

## 追加実装: StrokeSampler 直線ストローク（指定条件）描画（2026-02）
- UI: `Draw Line (Fixed)` を追加（点数/点間隔を指定して直線ストロークを生成）
- 入力: Start/End座標（既存TextBox）、LinePts（点数）、LineStep(px)（点間隔）、P（Dot512 Pressure）、S（Dot512 Size）
- 生成: `InkStrokeBuilder.CreateStrokeFromInkPoints` により、指定点列でPencilStrokeを作成し `StrokeContainer.AddStroke` で描画

## 追加実装: StrokeSampler InkPointsDump自動保存（2026-02）
- `Draw Line (Fixed)` 実行時に、生成したInkPoint列を `ApplicationData.Current.LocalFolder/InkPointsDump` 配下へJSON自動保存する。
- 形式は `DotLab/Sample/InkPointsDump` と互換（キーは `timestanp` のtypoも踏襲）。
- `MainPage.xaml.cs` の各イベントハンドラは、原則として **処理本体をヘルパー/サービスへ移し、UI側は1行委譲**にする。
- 目的は「移動（責務分離）」で、挙動変更や最適化は基本的に行わない。
- ビルドが通ることを都度確認。

## 実施済み（主な委譲/移植）

### 1) Radial 系
- `ExportRadialAlphaCsvButton_Click()`
  - `RadialFalloffExportService.ExportRadialAlphaCsvAsync(MainPage)` を追加。
  - `MainPage.xaml.cs` 側は `await RadialFalloffExportService.ExportRadialAlphaCsvAsync(this);` に委譲。

- `ExportRadialFalloffBatchSizesNsButton_Click()`
  - `RadialFalloffExportService.ExportRadialFalloffBatchSizesNsAsync(MainPage)` を追加。
  - `MainPage.xaml.cs` 側は1行委譲。

- `ExportRadialFalloffBatchPsSizesNsButton_Click()`
  - `RadialFalloffExportService.ExportRadialFalloffBatchPsSizesNsAsync(MainPage)` を追加。
  - `MainPage.xaml.cs` 側は1行委譲。

### 2) Center alpha summary
- `Helpers/ExportCenterAlphaSummary.cs`
  - `ExportCenterAlphaSummary.ExportAsync(MainPage)` を実装（ボタン処理本体を移植）。
- `ExportCenterAlphaSummaryButton_Click()`
  - `await ExportCenterAlphaSummary.ExportAsync(this);` に1行委譲。

### 3) Radial samples summary
- `Helpers/ExportRadialSamplesSummaryButton.cs`
  - 当初 `ExportRadialSamplesSummaryButton` という型名が XAML の `Button` と衝突（CS1061）し得たため、
    **型名を `ExportRadialSamplesSummary` に変更**して衝突回避。
  - `ExportRadialSamplesSummary.ExportAsync(MainPage)` を実装（処理本体を移植）。
- `ExportRadialSamplesSummaryButton_Click()`
  - `await ExportRadialSamplesSummary.ExportAsync(this);` に1行委譲。

### 4) Estimated paper noise
- `Helpers/ExportEstimatedPaperNoise.cs`
  - `ExportEstimatedPaperNoise.ExportAsync(MainPage)` を実装。
  - .NET 5 互換のため `double.IsFinite` は使用せず、`double.IsNaN/IsInfinity` を使用。
- `ExportEstimatedPaperNoiseButton_Click()`
  - `await ExportEstimatedPaperNoise.ExportAsync(this);` に1行委譲。

> 注意: 元コードには `noise` 算出がコメントアウトされた痕跡がありました。現在の実装は「動く形」で `noise` を算出しています。
> 厳密に「コメントアウトを外しただけの挙動」を求める場合は、目的のアルゴリズム（`bin` の扱い等）を再定義する必要があります。

### 5) PaperNoise crop 24
- `Helpers/ExportPaperNoiseCrop24.cs`
  - `ExportPaperNoiseCrop24.ExportAsync(MainPage)` を実装。
  - `IAsyncOperation<T>` を `await` するために `using System;` を追加（CS4036 回避）。
- `ExportPaperNoiseCrop24Button_Click()`
  - `await ExportPaperNoiseCrop24.ExportAsync(this);` に1行委譲。

### 6) Generate 系
- `Helpers/GenerateHelper.cs`
  - `GenerateHelper.Generate(MainPage)`
  - `GenerateHelper.GenerateOverwriteSamples(MainPage)`
  - `GenerateHelper.GenerateDotGrid(MainPage)`
  を実装（それぞれ元のボタン処理本体を移植）。

- `MainPage.xaml.cs`
  - `GenerateButton_Click()` → `GenerateHelper.Generate(this);`
  - `GenerateOverwriteSamplesButton_Click()` → `GenerateHelper.GenerateOverwriteSamples(this);`
  - `GenerateDotGridButton_Click()` → `GenerateHelper.GenerateDotGrid(this);`

### 7) Dot512 export 一式
- `Helpers/ExportDot512.cs`
  - `namespace StrokeSampler.Helpers` の `static class ExportDot512` として、下記4メソッドを**実装移植**:
    - `ExportDot512Async(...)`
    - `ExportDot512BatchAsync(...)`
    - `ExportDot512BatchSizesAsync(...)`
    - `ExportDot512SlideAsync(...)`

- `Helpers/ExportHelpers.cs`
  - 上記4メソッドを `ExportDot512.*` への **1行委譲**に置換（呼び出し互換維持）。

### 8) Normalized falloff

## （追記）SkiaTester / PencilDotRenderer（紙目・マスク・プレビュー強化）

### 目的
- UWPの鉛筆ドットに近い雰囲気（紙目で「乗りやすさ/乗りにくさ」が出る）をSkia側で検証できるようにする。

### 追加した主な機能（SkiaTester側）
- `Preview` の表示モードを増設
  - `8bit` / `float` / `paperMask` / `falloffWeight` / `maskUsed`
  - `invert mask view`（mask系プレビューの反転表示）
- `PaperMask` のマスクモードに `soft(outA)` を追加（しきい値2値ではなく、床付きの連続マスク）
- `MaskFalloff` の方式選択を追加
  - `none` / `gain@edge` / `th@edge`

### 追加した主な機能（PencilDotRenderer側）
- `PaperCapMode.CapOutAlpha`（紙目で outA の上限を作る）
- `BaseShapeMode`（ベース形状の切替）
  - `IdealCircle` / `PaperOnly`（UI上は `paper+falloff`）
- `PaperMaskMode` 拡張
  - `SoftOutAlpha`（連続マスク）
- `PaperMaskFalloffMode` 拡張
  - `StrongerAtEdge`（外縁ほど gain を強める）
  - `ThresholdAtEdge`（外縁ほど threshold を上げる）

### 現状の論点（未解決）
- `paperMask` / `maskUsed` プレビューでは外縁の変化（falloff連動）が確認できるが、`float`（最終 outA）表示では中心/外縁で同タイミングに見えるケースが残っている。
- 次の切り分け候補: `outA` の中間値（mask適用前/後）の可視化を追加して、どの段で差が消えているかを特定する。

### （追記）切り分け強化
- `PencilDotRenderer.RenderOutAlpha01Parts(...)` を追加し、`RenderOutAlpha01` と同一の計算経路で `outA_base`（mask前）/`outA_masked`（mask後）を取得できるようにした。
- `SkiaTester` の `Preview: outA_base/outA_masked` はUI側で再実装せず、本APIの結果を表示するように置換した（UI側再実装のズレを排除）。
- `SkiaTester` の `Preview: paperMask` は `_paperNoise` キャッシュではなく `TryLoadPaperNoiseFromUi()` の結果を使うように変更し、`maskUsed/outA_masked` と同じノイズ取得経路に統一した。

### （追記）paper-only の falloff を閾値化
- `PencilDotRenderer.PaperOnlyFalloffMode` を追加（`None` / `RadiusThreshold`）。
- `BaseShapeMode.PaperOnly` のとき、半径閾値 `PaperOnlyTh`（0..1）で `f(r)` を2値化できるようにした。
- `SkiaTester` UIに `PaperOnlyFalloff` と `PaperOnlyTh` を追加し、`Render/RenderOutAlpha01/RenderOutAlpha01Parts` に渡すようにした。

### DotLab（新WPFプロジェクト）を追加
- SkiaTester が検証UI/分岐で肥大化してきたため、Dot再現の最小実験環境として `DotLab` を新設した。
- SkiaSharp（`SkiaSharp.Views.WPF`）を継続採用し、紙目の高さはPNGのAlpha（0..1）を使用する。
- ノイズはタイルとして繰り返しサンプリングし、オフセット方向は既存検証の合意（X増加でノイズ右、Y増加でノイズ上）に合わせる。

### DotLabの新モデル（壁貫通モデル）
- GIMPの手動分解で得た仮説を実装優先の形に落とし込み、以下の式を `DotLab.Rendering.DotModel` として実装した。
  - `B = P * f(r)`
  - `H = T(x,y)`（紙目の高さ＝alpha）
  - `wall = 1 - H`
  - `V = clamp((B - wall) / k, 0..1)`
  - `outA = 1 - (1 - V)^N`
- まずは中間値 `V/B/H/wall/outA` のプレビュー表示を優先し、SkiaTester側で起きた「UI計算と本体計算のズレ」を避ける（同一ループで算出した配列を表示）。
- `Helpers/ExportNormalizedFalloffService.cs`
  - `ExportNormalizedFalloffService.ExportAsync(MainPage mp)` を実装（旧 `ExportHelpers.ExportNormalizedFalloffAsync` の処理本体を移植）。
  - 内部で使う `TryParseFalloffFilename/TryParseFalloffCsv/SampleLinear/BuildNormalizedFalloffCsv` は `StrokeHelpers` にあるため、`using static StrokeSampler.StrokeHelpers;` を使用。
- `Helpers/ExportHelpers.cs`
  - `ExportNormalizedFalloffAsync(MainPage mp)` を **1行委譲**に変更：`=> ExportNormalizedFalloffService.ExportAsync(mp);`

### 9) ExportPng
- `Helpers/ExportPngService.cs`
  - `ExportPngService.ExportAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)` を実装（旧 `ExportHelpers.ExportPngAsync` の処理本体を移植）。
  - `FileSavePicker`/`CanvasRenderTarget.SaveAsync` の `await` を成立させるため、`System.Runtime.InteropServices.WindowsRuntime` 等の using を追加。
- `Helpers/ExportHelpers.cs`
  - `ExportPngAsync(...)` を **1行委譲**に変更：`=> ExportPngService.ExportAsync(...)`

## ビルド・検証状況
- 変更の節目ごとにビルドを実行し、最終時点でビルド成功。

## よく出た注意点（再発防止）
- 型名が XAML 側の `Button` 名などと衝突すると、`CS1061` になり得る。
  - 例: `ExportRadialSamplesSummaryButton` → `ExportRadialSamplesSummary` にリネーム。
- UWP の `IAsyncOperation<T>` をヘルパー側で `await` する場合、環境により `CS4036` が出ることがある。
  - `using System;` や `System.Runtime.InteropServices.WindowsRuntime` の追加で解消したケースあり。
- `.NET 5` では `double.IsFinite` が使えないため、`IsNaN/IsInfinity` を使う。

## 主な変更ファイル一覧
- `MainPage.xaml.cs`（イベントハンドラの1行委譲化）
- `Helpers/RadialFalloffExportService.cs`（Radial系CSV/Batchの委譲先追加）
- `Helpers/ExportCenterAlphaSummary.cs`（新規/実装追加）
- `Helpers/ExportRadialSamplesSummaryButton.cs`（型名変更＋実装追加）
- `Helpers/ExportEstimatedPaperNoise.cs`（実装追加、.NET5互換修正）
- `Helpers/ExportPaperNoiseCrop24.cs`（実装追加、CS4036対策）
- `Helpers/GenerateHelper.cs`（実装追加）
- `Helpers/ExportDot512.cs`（実装追加）
- `Helpers/ExportHelpers.cs`（Dot512系を1行委譲化）
- `Helpers/ExportNormalizedFalloffService.cs`（実装追加）
- `Helpers/ExportPngService.cs`（実装追加）

## 次に起こり得る作業
- `ExportHelpers` に残る他の export（`ExportPngAsync` など）も同様に個別ファイル化するか検討。
- `ExportEstimatedPaperNoise` のアルゴリズム整合（「意図通りのF(r)・noise推定」）が必要なら仕様を詰めて調整。
