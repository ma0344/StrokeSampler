# Copilot 作業サマリ（スレッド共有用）

## 追加: 検証手順書の集約（2026-02）
- 手順書: `docs/pencil-parity-playbook.md`
- 以後の検証手順（InkDrawGenでの生成→DotLabでの差分/集計→目視確認）は上記へ集約する。

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

## 追加実装: DotLab αトーンカーブ（GIMP .crv）LUTの可視化出力（2026-02）
- 目的: GIMPのトーンカーブをαチャンネルに適用した変換を、Dot側に同じ変換として適用し、線側αとの比較を行う。
- 方針: 3D LUT（`.cube`）は使わず、GIMP `.crv` の `(channel alpha)` にある `samples 256` を 1D LUT（0..1正規化の出力テーブル）として扱う。
- 実装: `DotLab/Analysis/LineN1VsDotN1Matcher.cs` で `DotLab/LUT/Dot P1 LUT.crv` を読み、`Match line N1 vs dot N1` 実行時に `*-lut-*` の可視化PNGを追加出力。
  - 出力例: `lineN1-vs-dotN1-heatmap-lut-th1(-fullw)-P{p}.png`, `lineN1-vs-dotN1-diffmag-lut-th1(-fullw)-P{p}.png`
- 備考: ビルド時に `DotLab.exe` がロックされる場合があるため、実行中のDotLabを終了してからビルドする。

## 追加実装: LUT未検出/読込失敗時の警告ダイアログ（2026-02）
- 目的: 起動方法（作業ディレクトリ）差などで `.crv` が見つからずLUT無効になる場合に、原因調査ができるようにする。
- 実装: `Match line N1 vs dot N1` 実行時に、LUTがロードできない場合は `requested/resolved/error` を含む警告ダイアログを1回表示。
- 確認: LUTファイル名変更で「見つからない」警告が表示されることを確認。

## 観測: LUTは高圧側で改善するが低圧側で悪化し得る（2026-02）
- `th=1` のheatmap/CSVでは、P=0.9〜1.0で over(青)が大きく減る一方、低圧側では under(緑)が増えるケースがある。
- 次アクション: LUT適用の閾値（th）または適用条件（圧力帯域/α帯域）の調査が必要。

## 実施済み（主な委譲/移植）

## 追加実装: DotLab LineN1 vs DotN1 (Opacity sweep) バッチとサマリCSV（2026-02）
- 目的: LineN1フォルダとDot(Opacity sweep)フォルダを別指定し、P一致の組み合わせでAlphaDiff統計を総当たり出力する。
- UI: Analysisに `Line folder` / `Dot (Opacity sweep) folder` / `CSV output folder` と `Run batch and export CSV` を追加。
- 出力: `lineN1-vs-dotN1-opacitysweep-match-YYYYMMDD-HHmmss.csv`（総当たり）と、同一出力フォルダへ
  `lineN1-vs-dotn1-opacitysweep-summary-YYYYMMDD-HHmmss.csv`（line_fileごとに roi_diff_sum01 最小行を抽出）を追加出力。
- CSV列: dot側の `dot_file` から `-Op(value)` をパースして `dot_opacity` 列を追加。

## 修正: InkDrawGen Opのファイル名端数（2026-02）
- 原因: Opを描画用にfloat化した値をそのままファイル名へ出していたため、`0.15f -> 0.150000005...` の表記が出る。
- 対応: 描画用(float)とファイル名用(double, 丸め済み)を分離し、ファイル名には丸め済みのOpを出すように修正。

## 追加実装: StrokeSampler 疑似線(Dot連続)の更新点(DotStep)スイープPNG出力（2026-02）
- 目的: 更新点（点間隔）をレンジ指定でスイープし、他の値を固定したまま疑似線（Dotを並べたスタンプ列）を生成してHiRes PNGを一括出力する。
- UI: `DotStep(px) start/end/step` と `Export PseudoLine (DotStep Sweep)` ボタンを追加。
- 実装: `Helpers/TestMethods.ExportPseudoLineDotStepSweepAsync` を追加し、`MainPage.xaml.cs` から1行委譲。
- 備考: 出力は指定のStart/End（X方向長）に対して `count=floor(len/step)+1` のdot数を自動算出して並べる。ファイル名に `dotstep{step}` を含める。

## 追加実装: InkDrawGen 疑似線(Dot連続)のDotStep(少数)入力と出力（2026-02）
- UI: `InkDrawGen/MainPage.xaml` に `dotStep start/end/step` を追加。
- 状態: `InkDrawGenUiState` に `DotStepX` を追加し、`InkDrawGenUiReader` で読み取る。
- 生成: `RunInkDrawJobsService` の `JobType=Line` で `dotStep>0` の場合、Lineストロークの代わりに Start→End のX方向に Dot を `dotStep` 刻みで並べて疑似線としてレンダ（少数指定可）。
- 命名: `extraSuffix` に `dotstepline-step{dotStep}` を付与してファイル名に残す。
- 追記: DotStepはレンジ指定でスイープし、dotStepごとに疑似線PNGを個別出力する（FirstOrDefaultで固定しない）。

## 修正: InkDrawGen Opスイープ(0.001刻み)とファイル名Op表記の安定化（2026-02）
- `OpacityRangeSpec` の正規化を0.001刻みに統一し、デバッグ残骸を削除。
- `FileNameBuilder` の `-Op` 表記を `0.###` に変更し、過剰桁や揺れを抑止。
- `RunInkDrawJobsService` の `opacityTag` 生成（小数第3位丸め）と意図コメントを整合。

## 修正: InkDrawGen Opスイープ(0.0001刻み)対応（2026-02）
- `OpacityRangeSpec.Normalize` の丸めを小数第4位に変更（0.0001刻み）。
- `FileNameBuilder` の `-Op` 表記を `0.####` に変更。
- `RunInkDrawJobsService` の `opacityTag` 生成（小数第4位丸め）へ変更し整合。

## 追加実装: InkDrawGen 2点疑似線(dot2)モード（2026-02）
- UI: `InkDrawGen/MainPage.xaml` に `2点疑似線（始点+更新点1つ）` チェックを追加。
- 状態: `InkDrawGenUiState` に `DotStepTwoPoints` を追加し、`InkDrawGenUiReader` で読み取る。
- 生成: `JobType=Line` かつ `dotStep>0` のとき、チェックONならDotを常に2点（`(x0,y0)` と `(x0+dotStep,y0)`）だけ描画してPNG出力。
- 命名: `extraSuffix` を `dot2-step{dotStep}` とし、通常のDot連続疑似線(`dotstepline-step`)と区別できるようにした。

## 追加実装: InkDrawGen 線(2点)生成ボタン（2026-02）
- UI: `InkDrawGen/MainPage.xaml` に `線(2点)生成` ボタンを追加。
- ハンドラ: `InkDrawGen/MainPage.xaml.cs` から `RunInkDrawJobsService.RunSingleLine2PointsAsync` に委譲。
- 動作: UIの `startX/Y` を始点、`endX/Y` を終点として2点のLine InkStrokeを生成する（dotStep疑似線モードを無効化して通常Line描画を明示）。

## 変更: InkDrawGenのファイル名へStartX/EndXを付与（2026-02）
- `RunInkDrawJobsService` の `extraSuffix` に `StartX{...}-EndX{...}` を追加し、出力PNGの区別を容易にした。

## 追加実装: InkDrawGen 線(2点) EndXスイープ出力（2026-02）
- UI: `endX sweep start/end/step` と `線(2点) EndXスイープ` ボタンを追加。
- 動作: `JobType=Line` の2点線を強制し、`endX` をレンジで差し替えて複数PNGを出力（dotStep疑似線は無効化）。

## 確定: N1始点ROIはDot Op=0.1795で完全一致（2026-02）
- DotLabのAlphaDiff（同一ROI切り出し）比較で、`S200 P1` の線(alignedN1)始点ROIに対して、単点Dotの `Op=0.1795`（同率で `Op=0.1796`）が `roi_diff_sum01=0` となり完全一致。
- 以降の検証は濃度Opを `0.1795` に固定し、更新点（点列/間隔）由来の差分に集中できる。

## 追加観測: 2点LineのEndXスイープでもN1はDot Opで完全一致できる（2026-02）
- `S200/DPI96/P1` の2点Lineを `Op=1` 固定で描画し、`endX=118..280 step18` をスイープしたところ、各endXごとに単点Dotの `Op` を調整することで `roi_diff_sum01=0`（完全一致）を達成。
- したがって、更新点数/線長に応じてN1の実効濃度スケール（単点Dotに対する必要Op）が変化する（2..12程度で顕著）。

## 追記: 更新点13点目以降でN1の最適Opが0.1795へ定常化（2026-02）
- EndXスイープを `EndX334` まで伸ばすと、`EndX316` / `EndX334` で `best_dot_opacity=0.17950`（完全一致）となり、更新点13点目以降で定常化することを確認。
- `EndX298`（更新点12）では `best_dot_opacity=0.17860`（完全一致）で、ここが定常化直前の遷移域。

## 追加実装: DotLabバッチ比較を全画像(w×h)のAlphaDiffに対応（2026-02）
- `Run batch and export CSV`（`RunLineN1VsDotOpacityBatchButton`）に `Use full image (w×h) AlphaDiff` オプションを追加。
  - ON: 画像全体のAlphaDiff統計で比較し、サマリbest選択は `diff_sum01` / `diff_nonzero_px`。
  - OFF: 従来通り左帯ROI(18px)の比較（N1用途）。

## 修正: InkDrawGen疑似線(dotstep)のスイープで小数stepが同名上書きになる問題（2026-02）
- `dotStep` のファイル名表記が `0.###` 丸めだったため、`step=0.1` などのスイープでサフィックスが同一になり、出力が上書きされて「同じ画像しか出ない」ように見えるケースがあった。
- `InkDrawGen/Helpers/RunInkDrawJobsService.cs` の `dot2-step{...}` / `dotstepline-step{...}` 表記を `0.#####` に拡張して区別できるようにした。

## 追加調査/修正: dot2疑似線でdotStepを変えても出力PNGが同一に見える件（2026-02）
- `dot2 dbg` ログにより、生成段階では `p1=(startX+dotStep, startY)` と `BoundingRect.X` が `17, 17.1, 17.2...` のように変化することを確認。
- レンダ時の座標変換順の不整合の可能性を減らすため、`InkOffscreenRenderService` のROI平行移動を `Scale * Translation` の順に統一した。

## 修正: DotLab ExportAlphaDiff が常にdiff=0になるケース（2026-02）
- `Export Alpha Diff (PNG vs PNG)` のCSVに入力2ファイルの `path/size/SHA256` を出力するようにして、入力が本当に別物か確認できるようにした。
- 入力PNGのSHA256が異なるのに `diff_max=0` となる場合があり、原因は `ImageAlphaDiff` が `SKBitmap.GetPixel().Alpha` に依存していたこと。
  - デコード経路によりAlphaが常に255のように見えるケースがあり、差分が常に0になっていた。
  - `SKBitmap.Pixels` 配列から `Alpha` を参照する方式へ変更して解消した。

## 修正: DotLab バッチ比較(LineN1VsDotN1BatchMatcher)でも同様にdiff=0になるケース（2026-02）
- `LineN1VsDotN1BatchMatcher` の `ExtractFullAlpha` / `ExtractLeftRoiAlpha` も `SKBitmap.GetPixel().Alpha` を使っていたため、同様に `SKBitmap.Pixels` 参照へ切り替えた。

## Verified: S200 dot2疑似線のdotStepは18.00が最適（2026-02）
- 条件: `2180x2020` / `dpi96` / `S200` / `P1` / `N1` / `scale10` / 透過PNG
- DotLabのalpha差分（全画素 `|A1-A2|`）で `diff_sum01` 最小の `dot2-step` を採用。
- スイープ結果より `dot2-step=18.00` が最適（`17.9`〜`18.9` 含む）。
- `dot2-step17.99` vs `dot2-step18.01` の差分可視化では、2つ目ドットの輪郭のみ差が出て円内部のもじゃもじゃは出ないため、残差は主に微小な位置差（平行移動）と解釈できる。

## 追加: InkDrawGenの線(2点) StartXスイープ（2026-02）
- 目的: `EndX` を固定したまま `StartX` を範囲でスイープして複数長さのオリジナル線を生成する。
- UI: `線(2点) StartXスイープ` ボタンを追加（入力欄は `endX sweep start/end/step` を流用）。
- 注意: ROIが `x=0,y=0,w=18,h=202` のように原点周辺のままだと、`StartX` が負の線はROI外になり空画像になる。スイープする線分がROIに入るよう `RoiX/RoiW` を調整する。

## 追加: InkDrawGenのdotN疑似線（StartX基準でN個固定）（2026-02）
- 目的: 指定した `dotStep`（更新値）と `N` 個数でDotを並べた疑似線を生成し、同じ長さのオリジナル線と目視比較する。
- 設定:
  - `JobType = Line`
  - `dotStep start/end/step` に `dotStep` を設定（スイープしたい場合は範囲指定も可）
  - `N個疑似線（StartX基準でN個固定）` をON
  - `dot count` に N を入力
  - `Op` は固定値でよければ `OpStart=OpEnd` にする
- 出力ファイル名: サフィックスに `dotN{N}-step{dotStep}` が付与される

### CSVバッチでの指定
- 列: `dot_step_fixed_count`（true/false）
- 列: `dot_step_count`（N。1以上）
- 別名: `dotStepFixedCount` / `dotStepCount` も使用可

### 個数Nのスイープ
- UI: `dot count start/end/step` を設定すると、Nを範囲でスイープして出力する（`N個疑似線` がONであること）。
- CSV: `dot_step_count_start` / `dot_step_count_end` / `dot_step_count_step` を指定すると、Nを範囲でスイープして出力する。

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

---

## Aligned line N1 vs aligned-dot-index N1: 点→線先頭の近似（2026-02）

### 目的
- 線描画（`N1N2` の先頭領域）と、単点（`aligned-dot-index`）が「同じ領域」を切り出せる状態を作り、
  - 形（2値マスク）
  - 濃さ（α値）
 について「最も近い組み合わせ（P対応）」を探索・可視化する。

### 追加実装（StrokeSampler 側）
- 単点（`aligned-dot-index`）を **単一Nのみ** 出力する経路を追加。
  - `Helpers/ExportS200Service.cs`: `ExportAlignedDotIndexSingleAsync(...)`
  - `Helpers/AlignedJobsCsv.cs`: CSV拡張（`aligned_mode`/`single_n`）
  - `MainPage.xaml.cs`: `aligned_mode=dot-index-single` のジョブを解釈して P sweep を回す。
- 運用: `runTag` に `aligned-dot-index` を含めると DotLab 側が単点候補として認識。

### 追加実装（DotLab 側）
- `DotLab/Analysis/LineN1VsDotN1Matcher.cs`: フォルダ内の
  - 線候補（`-alignedN1` かつ `N1N2` を含む）
  - 単点候補（`aligned-dot-index` / `aligned-dot-index-` / `aligned-dot-index-single` を含む）
 から、線1枚ごとに最も近い単点を探索して CSV 化。

#### 比較ROI（確定）
- X: 左端 `18px`（`RoiWidthPx=18`）
- Y: `435..1591`（`RoiY0=435`, `RoiY1Exclusive=1592`）
- αのみ使用（RGBは無視）。

#### 形状比較（2値化）
- 2値化閾値 `th = 1,2,3,4` を同時に算出。
- `IoU`/`mismatch`/`coverage`/`inter`/`union` を出力し、best/second を記録。
- 「空っぽ一致（union=0でIoU=1）」が best にならないよう自動排除を導入。
  - `MinUnionGate=200`
  - `minCov=200/ROI画素数` を line/dot 双方に適用（ON画素が200px未満の候補は除外）

#### 濃さ補正の推定
- 形が近い候補に対して、線≒k×点 となるスケール係数 `alpha_k`（最小二乗）を推定。
- `alpha_l1_scaled`（k適用後の平均|α差|）を出力。
  - 形が近い（IoUが高い）候補ほど、単純なαスケールで見た目が寄る可能性がある。

#### 可視化（ヒートマップ/差分強度）
- best組み合わせについて、ROI内の2値マスク差分を画像化（thごと）。
  - dotのみON: 青 / lineのみON: 緑 / 両方ON: 黒 / 両方OFF: 白
  - 出力: `lineN1-vs-dotN1-heatmap-th{th}-P{lineP}.png`
- さらに **全幅版（180px）**も追加出力（`-fullw-` 付き）。
  - 出力: `lineN1-vs-dotN1-heatmap-th{th}-fullw-P{lineP}.png`
- α差分の大きさ（|α_line-α_dot|）を赤強度で可視化（th=1, ROI版＋全幅版）。
  - 出力: `lineN1-vs-dotN1-diffmag-th1-P{lineP}.png`
  - 出力: `lineN1-vs-dotN1-diffmag-th1-fullw-P{lineP}.png`

### 今後ひっくり返りにくい事実（観測）
- ヒートマップにより、差異は主に輪郭（境界）に出る。
- P<=0.8 では黒以外に青/緑が両方出るが、P=0.9/1.0 では黒以外が青のみ（=点側のON領域が外側に出る傾向）。
  - 高圧帯域のIoU低下は「線が太い」より「点側が外に出る（薄縁/外周ONが残る）」寄りの可能性。

（追記・定量化）
- `th=1` の best について、差分領域を over/under として定量化した。
  - `over_area`: dotのみON（ヒートマップ青）
  - `under_area`: lineのみON（ヒートマップ緑）
  - `over_alpha_median` / `under_alpha_median`: それぞれの領域でのα差中央値
- 代表例: P=0.9/1.0 では `under_area=0` かつ `over_alpha_median=1..2` となり、IoU低下要因が「点側の極薄縁（α=1〜2）の過剰」で説明できる。

（追記・低圧対応）
- 低圧 `P=0.1` は、空っぽ/疎すぎる一致の除外ゲート（union/coverage）が厳しすぎて候補が全落ちしやすかった。
- 対応として、`th=1` だけゲートを緩め（union>=20、ON>=20px相当）、`th=2/3` は従来の厳しいゲート（union>=200、ON>=200px相当）を維持した。

### 次の作業候補
- `th=2/3` で union/coverage が十分な条件での best がどう変わるかを再評価。
- `alpha_k` を適用した後に 2値化/差分強度を再可視化（補正が効くかの確認）。
