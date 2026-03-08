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

## 追加実装: DotLab alignedN系列のモデル検算（2026-02）
- 目的: P0.5で観測された「No依存の増え方（指数型）+ 画素ごとのcap」モデルを、alignedN系列（N1/飽和N/N2/4/8）で機械的に検算できるようにする（PowerQuery不要）。
- UI: `Verify alignedN series model (N1/cap/N2/4/8)`
- 入力: `alignedN{n}` を含むPNGをまとめて選択（最低 `alignedN1` と `alignedN1024` 等の飽和Nが必要）。
- 出力:
  - `alignedN-model-verify-*-summary-*.csv`（Nごとに `floor/round` の誤差統計を出力）
  - best量子化の `pred` / `diffabs` PNG（および `diffabs` の `vis16`）
  - 参照用に `a(N1)` / `cap(Nsat)` のPNGも出力
  - 追記: 8bit量子化がレンダリング時点で入る前提に合わせ、closed-form（最後に量子化）に加えて stepwise（各ステップで量子化）モデルも同一CSVに出力するようにした。
  - 追記: 差分が小さい（max_abs_diffが1〜3など）ケースでも分布を見分けやすいよう、`diffmask`（diff>0の二値）と `vis64` / `visAuto` を追加出力する。
  - 追記: BGRA8のsource-over（255分母）を各ステップで整数演算するモデル（`so255`）と、飽和capでクランプする派生（`so255cap`）も同一CSVに併記し、どのモデルがN2/4/8に最も一致するかを比較できるようにした。
  - 検証メモ: `so255` の `round` が N2/N4/N8 で `rmse=0, max_abs_diff=0, mismatch_px=0` になれば、No累積は「N1αをsrcAとしてBGRA8 source-overを逐次適用（+127丸め）」と見なしてよい。

## 追加実装: 手順書更新と補助ツール（2026-02）
- `docs/pencil-parity-playbook.md` に、No累積の確定モデル（BGRA8 source-over, 255分母, +127丸めの逐次適用）を追記。
- `docs/pencil-parity-playbook.md` に、小さい差分（max_abs_diffが1〜3程度）で `visAuto` が `diffmask` と同じに見える条件と読み方を追記。
- DotLabに `ImageAlphaRadialKernelNoiseExporter` を追加。
  - 入力PNGのαから、半径方向平均（radial mean）kernel画像と、`alpha/kernel` 比の可視化（ratio=1→128, 0..2→0..255）をPNG/CSVで出力。

## 追加実装: DotLab 紙目テクスチャ周期探索（shift self-match）（2026-02）
- 目的: 紙目PNGがタイルとして繰り返されて見える場合に、平行移動の自己一致から周期（タイル幅/高さ）候補を推定する。
- UI: `Period search (shift self-match)` / `Export Alpha Shift Period CSV (PNG)`
- 出力: `shift-period-<input>.csv`（X/Y両方向、MAE/RMSE、valid_rate）
- SkiaTesterに、alignedN1 PNGから `so255/round` で alignedN2/4/8 相当を生成してPNG出力するボタンを追加。

## 修正: NoiseRatioの比較がAlphaDiffで常にdiff=0になる問題（2026-02）
- `noiseRatio-vis2x.png` は可視化用にGray8(不透明)で出力していたため、DotLabの `Export Alpha Diff`（Alpha比較）では常にdiff=0になってしまう。
- 値をAlphaに格納した `*-alpha.png`（`noiseRatio-vis2x-alpha.png` / `kernel-alpha.png`）も追加出力し、AlphaDiffで紙目推定の差分比較ができるようにした。

## 追記: paper noise（noiseRatio推定）のP依存は実用上ほぼ不変（2026-02）
- `P0.25` と `P0.5` の `*-noiseRatio-vis2x-alpha.png` を半径プロファイル化して比較。
- 外周の不安定さを避けるため、`kernel-profile` の `mean_alpha_byte > 32` を満たす範囲（例: `r_bin < 502`）だけを採用して差分統計を算出。
- 結果例（`S200, scale10, r_bin < 502`）:
  - `diff_abs` 平均 `≈ 0.0107`（≈ `2.73/255`）
  - `diff_abs` p95 `≈ 0.0211`（≈ `5.39/255`）
  - `diff_abs` 最大 `≈ 0.0294`（≈ `7.49/255`）
- 解釈: `noiseRatio-vis2x` はクランプ＋8bit丸め済みの可視化量のため差がゼロにはならないが、パターン（山谷の位置）は概ね一致し、風合い再現の観点では「Pに対してほぼ不変」として扱ってよい。

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

## 追加実装: DotTester（N=1ドットの目視比較用WPF）とテスト環境整備（2026-02）
- 新規: `DotTester` プロジェクト（WPF/.NET 8）。観測PNG（expected）と再現（rendered）を左右に並べて表示し、`Render (N=1)` 押下で再現側のみ更新する。
- テスト環境: `scale`（InkDrawGenのscale相当）を追加し、`diameterPx = S * scale`、`canvas = diameterPx + pad`（Auto時）で `S200/scale10` → `canvas=2020` を再現側でも作れるようにした。
- 紙目: タイルPNG（alpha）を入力し、`noiseScale = scale / tileScale`（Auto時）で、タイルがどのscale基準で切り出されたか（例: tileScale=10）に追従できるようにした。

## 高速化: PaperNoiseサンプリングとPencilDotRendererのピクセル書き込み（2026-02）
- `PaperNoise.Sample01Bilinear` 等で `SKBitmap.GetPixel` を多用していたため、内部に `_pixels` をキャッシュして配列参照で読むように変更。
- `PencilDotRenderer.Render` の出力を `SetPixel` 多発から `bitmap.Pixels` への配列書き込み＋最後に一括反映へ変更。
- `DotTester` のステータスに `time=...ms` を表示し、2020固定での待ち時間・改善効果を確認しやすくした。

## 追加実装: DotTesterでkernel-profile.csvを減衰LUTとして利用（2026-02）
- `DotTester` の `Falloff CSV` で `normalized-falloff` に加え、`ImageAlphaRadialKernelNoiseExporter` が出力する `*-kernel-profile.csv` を指定できるようにした。
- `kernel-profile` は `mean_alpha(px)` を有効半径で正規化し、`r_norm=0..100` のLUTへ線形補間して `PencilDotRenderer` の `falloffLut` に渡す。
- 形状一致を優先して `PaperNoise` の既定をOFFにした。

## 追加実装: 紙目(B=紙目をαへ乗算)の検証パラメータ（2026-02）
- `PencilDotRenderer.Render` に `paperNoiseKClampMin/Max` を追加し、紙目係数kのクランプ範囲（既定0.5..1.5）を調整可能にした。
- `DotTester` に `Gain` / `kClamp(min..max)` / `Disable kMean norm` を追加し、外周の「まばら化」を紙目パラメータで詰められるようにした。

## 追加実装: 外周floor上昇（cutoff/紙目マスク）仮説の検証UI（2026-02）
- `DotTester` に `Cutoff alpha(byte)` と `noise-dependent cutoff (×k)` を追加し、微小α帯域の0落ちを制御できるようにした。
- `DotTester` に `PaperMask`（Multiply/Soft/Threshold）と `edge falloff`（StrongerAtEdge/ThresholdAtEdge）を追加し、外周ほど厳しく落とす挙動を検証できるようにした。

## 追加実装: カーネル（falloff）全体の明るさ/濃さ調整（2026-02）
- `PencilDotRenderer.Render` に `falloffScale` を追加し、半径減衰 `f` に乗算してクランプすることでカーネル全体を明るく/暗く調整できるようにした。
- `DotTester` に `FalloffScale` を追加し、UIから `falloffScale` を指定してレンダ結果を比較できるようにした。

## 追加実装: InkDrawGenでカーネル断面（中心Xスイープ）CSV出力（2026-03）
- `InkDrawGen` に観測点(px)固定で中心Xを `dx_px` だけ移動させ、同一画素のαを取得するCSV出力（`KernelSweepExportService`）を追加した。
- 2000×2000のPNGを大量生成せず、9×9等の小さなオフスクリーンに描画して中心1pxのαをサンプリングするため、出力は軽量に実行できる。

## 追加実装: InkDrawGenでkernel-sweep CSVを使ったfalloff相殺（紙目ベース単点PNG）出力（2026-03）
- `InkDrawGen` に `KernelCanceledDotExportService` を追加し、kernel-sweep CSVから `f(rNorm)` を構築して単点レンダのαを `alpha/f` で相殺したPNGを出力できるようにした。
- `InkDrawGen/MainPage.xaml` に `紙目ベース単点PNG` ボタンを追加し、CSV選択→PNG出力をUIから実行できるようにした。

## 追加実装: DotTesterで再現（Rendered）PNG出力（2026-03）
- `DotTester` に `Save Rendered PNG...` ボタンを追加し、レンダ結果（再現画像）をPNGで保存できるようにした。

## 修正: Alphaチャンネル紙目タイルの統計に背景(A==0)が混ざって谷が浅くなる問題の対策（2026-03）
- `SkiaTester/Helpers/PaperNoise.cs` の無効背景判定をサンプルチャンネル依存にし、Alpha利用時は `A==0` を無効扱いにすることで、背景が統計に混ざってz正規化が歪むのを防いだ。
- `DotTester` のPaperNoise読み込みを `InvalidPixelMode.Legacy` + `SampleChannel.Alpha` に変更し、dot由来タイル（背景が透明）でも紙目の谷が浅く潰れにくいようにした。

## 修正: PaperNoiseの双線形サンプルが0.5pxずれて極値が平均化される問題の対策（2026-03）
- `SkiaTester/Helpers/PaperNoise.cs` の `Sample01Bilinear` を「ピクセル中心座標(0.5,1.5,...)」基準で扱うようにし、呼び出し側の `(x+0.5)` とtexel中心が一致するよう `-0.5` シフトを入れた。
- これにより、PaperNoiseタイルの「最深画素」が常に近傍平均との差で潰れて見える（谷が浅い）症状を軽減する。

## 追加実装: DotTesterで再現手法をSkiaTesterから切り離して再構築（2026-03）
- SkiaTester式（z正規化）を踏襲する不安があるため、DotTester側に再現の最小要素を1から実装した。
- `normalized-falloff` は `DotTester/Helpers/NormalizedFalloffLut.cs` で読み込み、`kernel-sweep→normalized-falloff` のLUTをそのまま `f(rNorm)` として使用する。
- PaperNoiseは `DotTester/Helpers/PaperNoiseTile.cs` でPNG(α)を読み込み、タイルサンプリングをUIで `Nearest/Bilinear` 切替できるようにした。
- k定義は `DotTester/Helpers/DotReproRenderer.cs` で実装し、UIから A/B/C（`k=n01/mean`, `blend`, `k=n01`）を選択できるようにした。
- 量子化は最後に1回だけ行う（内部はdoubleで `outA` を保持してから8bit化）。
- 旧UIのPaperMaskは互換のため残しつつ、再構築レンダラでは現時点で未適用（コア再現の検証を優先）。

## 修正: DotTesterのk定義(A/B)が同一になっていた問題とEdgePad未適用（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs` で A/B/C のk定義を仕様どおりに分離し、AとBが同じ見た目になる不具合を修正した。
- `DotTester` の `EdgePad(px)` を再構築レンダラへ渡し、外縁の落ち方をUIから調整できるようにした。

## 修正: NoiseDependentCutoffを谷で厳しくする（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs` の `NoiseDependentCutoff` を `cutoff *= k` から `cutoff /= k` に変更し、谷(k<1)で抜けが出やすい挙動にした。

## 追加実装: 外縁で紙目が消える問題の切り分け用に加算モデルとfalloffガンマを追加（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs` に紙目の適用モードを追加（`MultiplyAlpha`/`AddAlpha`）。
  - `AddAlpha` は中心のベースαを基準(aRef)にして `a01 += aRef*(k-1)` を適用し、外縁でベースαが小さくても紙目の差分が量子化で消えにくいようにする。
- `DotTester` のUIに `Apply(Multiply/Add)` と `Falloff Gamma` を追加し、検証時に切り替え可能にした。

## 追加実装: falloffの落ち始め位置を調整するRNormScaleを追加（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs` に `FalloffRNormScale` を追加し、`rNorm` をスケールしてLUT参照位置を調整できるようにした。
  - `RNormScale>1` で同じ距離でも外側の`rNorm`を参照するため、落ち始めが内側へ寄る（より早く落ちる）。
- `DotTester` のUIに `RNormScale` を追加し、見た目合わせで微調整できるようにした。

## 修正: falloff距離計算をピクセル中心基準に変更（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs` の距離計算を `(x+0.5, y+0.5)` のピクセル中心基準へ変更し、falloff CSVの観測系列（dx_pxが整数で増える）と整合しやすくした。

## 追加実装: DotTesterでkernel-sweep CSVをFalloffとして直接読み込み（2026-03）
- `DotTester/MainWindow.xaml.cs` のFalloff CSVローダを拡張し、`kernel-sweep`（ヘッダが`dx_px,...`）を指定した場合でも読み込めるようにした。
  - `dxTarget = r_norm * Scale`（ScaleはDotTesterの`RenderScale`）で `r_norm=0..100` の `mean_alpha` を抽出し、内部の`NormalizedFalloffLut`として使用する。
  - kernel-sweep生成時のscaleとDotTesterのScaleが同一であることが前提。
  - FalloffキャッシュはパスだけでなくScaleも一致した場合のみ再利用する。

## 追加実装: DotTesterの紙目(k)生成をUWP/Skia寄せ（z正規化 + kMean正規化）（2026-03）
- `DotTester/Helpers/PaperNoiseTile.cs`
  - `Stddev01` を追加し、紙目タイルの標準偏差を取得できるようにした（z正規化のため）。
  - サンプル座標を「ピクセル中心基準（x=0.5がtexel0中心）」として扱い、nearest/bilinearとも `-0.5` シフトでSkia側と整合させた。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `KDefinition.ZNormalized` を追加（`z=(n01-mean)/std` を ±3 でクランプし、`k=1+(strength*gain)*z`）。
  - `DisableKMeanNormalization` を追加し、半径内平均で `kMean` を計測して `k/=kMean` する補正をON/OFFできるようにした。
  - `NoiseDependentCutoff` はSkiaTester互換の `cutoff *= k` に揃えた（谷の0落ちを起こしにくくする意図）。
- `DotTester/MainWindow.xaml`
  - `k mode` に `U: ZNorm (k=1+(s*g)*z)` を追加。
  - `noise-dependent cutoff` の表記を `(×k)` に更新（実装に合わせる）。

## 追加実装: DotTesterで紙目タイル統計(mean/stddev/min/max)をステータス表示（2026-03）
- `DotTester/MainWindow.xaml.cs` のステータス文字列に `noise(mean/std/min/max)` を追記し、`Stddev01` の確認がUI上でできるようにした。

## 追加実装: DotTesterのZNormでzクランプ幅を調整可能にする（2026-03）
- 背景: ZNormの`z`を固定で±3にクランプすると、タイルの実分布（例: minが-9σ相当）で谷側が張り付き、谷の階調が消えるケースがあった。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `DotReproRenderer.Options` に `PaperNoiseZClampAbs`（既定3.0）を追加。
  - `KMode=ZNormalized` のときの `z` クランプを `±PaperNoiseZClampAbs` で行い、`kMean` の事前計測側も同じクランプ幅で揃えた。
- `DotTester/MainWindow.xaml` / `DotTester/MainWindow.xaml.cs`
  - `zClamp` 入力UIを追加し、値を `PaperNoiseZClampAbs` に配線した。

## 更新: DotTesterのZNorm zClampを上下別 + ON/OFF（既定: -7/+7）（2026-03）
- 背景: タイルの分布で負側（谷）が深く、対称クランプだと谷側の階調が張り付きやすかったため、`zClamp-` を大きくして階調を残しつつ、`zClamp+` は別に抑えられる必要があった。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `DotReproRenderer.Options` に `EnablePaperNoiseZClamp` と `PaperNoiseZClampNegAbs` / `PaperNoiseZClampPosAbs` を追加。
  - `EnablePaperNoiseZClamp=false` の場合は `z` クランプを行わず、`kClamp` のみで抑制する。
  - 互換のため `PaperNoiseZClampNegAbs/PosAbs` が未指定(<=0)なら `PaperNoiseZClampAbs` を採用。
  - 既定値は `PaperNoiseZClampAbs=7`。
- `DotTester/MainWindow.xaml` / `DotTester/MainWindow.xaml.cs`
  - `zClamp` のON/OFFと `zClamp-` / `zClamp+` を追加（既定 7/7）。OFF時は数値入力を無効化。

## 追加実装: InkDrawGenでkernel-sweep CSVからDotTester用 normalized-falloff CSVを生成（2026-03）
- `InkDrawGen` に `KernelSweepToNormalizedFalloffExportService` を追加し、kernel-sweep CSVの `alpha01` を `r_norm` 整数（= `dx_px/scale`）へ落として `normalized-falloff` 形式CSVを出力できるようにした。
- `InkDrawGen/MainPage.xaml` に `normalized-falloff CSV` ボタンを追加し、DotTesterで読み込むLUTを手作業なしで生成できるようにした。

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

## 修正: InkDrawGen CSVバッチで小数座標が同名上書きになる問題（2026-02）
- CSVバッチは `start_x/end_x` を `double` として受け付けるが、ファイル名の `StartX/EndX` タグをint丸めしていたため、`EndX=108.0` と `108.5` のようなケースで出力が上書きされ「小数が効かない」ように見えることがあった。
- `InkDrawGen/Helpers/RunInkDrawJobsService.cs` の `StartX/EndX` タグを小数も出力する方式に変更し、ファイル名衝突を回避した。

## 確定: 更新点間隔（dotStep相当）は S の 0.09 倍（2026-02）
- 目的: `S200 -> 18px` を根拠に、各Sで更新点間隔が線形（比例）かを確認。
- 方法: `S=20..180 step20` で最短線の長さ（`EndX-StartX`）を `0.00001` 単位でスイープし、「単点→線（点が増える）」に変化する境界長 `L_threshold` を同定。
- 結果: `L_threshold = 0.09 * S` が明確に成立。
  - 例: `S100 -> 9.0`, `S180 -> 16.2`, `S200 -> 18.0`
  - よって更新点間隔（dotStep相当）は `dotStep(S) = 0.09 * S` とみなせる。

## 追記: 線描画の用語定義（S/P/Op/No/L/I/Np）（2026-02）
- 目的: L（DIP長）と更新点数（Np）が混線して混乱しやすいため、共通言語として定義を固定。
- 追記先: `docs/pencil-parity-playbook.md` と `docs/handover.md`
- 重要式: `I = 0.09 * S`, `Np = floor(L / I) + 1`

## 確定: N1 ROIの `Op(Np)` テーブルはSでほぼ不変（S100..200, 2026-02）
- 目的: `S200` で得た `Op(Np)` を、他の `S` にも共通テーブルとして適用できるか確認。
- 方法: `S=100/140/180/200` で `Np=2,4,5,6,8,10,12,14` の2点Line（`Op=1`）を生成し、Dot側をOp sweepで照合。量子化影響を減らすため `scale` を調整（例: `S100:20`, `S140:15`, `S180:11`, `S200:10`）。
- 結果: best `Op` はS間で同一系列に乗り、差が出ても概ね `1e-3` 程度で、最小となる帯域はほぼ一致。
- 結論: 少なくともこの条件範囲では、N1 ROIの `Op(Np)` は **Sに対してほぼ不変**として扱ってよい。

## 確定: dotN疑似線は2点Lineを線全体で目視再現できる（S10..200, 2026-02）
- `S=10/20/40/80/100/140/180/200` で、各Sの `I=0.09*S` と `Np` を揃えたペア（2点Line vs dotN）を生成して線全体を目視比較。
- 最終出力サイズを揃えるため `scale=2000/S`（例: `S200:10`, `S140:14`, `S100:20`, `S20:100`, `S10:200`）を採用。
- 結果: 上記範囲では、dotN疑似線と2点Lineは線全体としてほぼ同じ見た目となり、形状近似として採用できる。

## 次の課題: ストローク内P変動の再現（2026-02）
- これまでの検証はストローク内の `P` を一定に固定していた。
- 今後はストローク内で `P(t)` を変動させたときの挙動（更新点の寄与/濃度/欠け）を再現し、ISF由来の鉛筆ストロークをSkia等で再現できる状態に持っていく。

## 追加観測: 単点Dotにおける P と Op は同じ効き方ではない（2026-02）
- `Op` を下げると、点の縁が透明化していき見た目の直径が退縮する。
- `P` を下げた場合は透明化は起きるが直径は退縮せず、外周ほど「α>0の画素密度」が下がる（密度が0にはならない）。
- したがって `P -> Op(P)` の単純な置換モデルは成立しない可能性が高く、Skia等での再現では `P` はフォールオフ/密度側のパラメータとして扱う必要がある。

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

- `ExportRadialAlphaBatchPsSizesNsButton_Click()`
  - `RadialFalloffExportService.ExportRadialAlphaBatchPsSizesNsAsync(MainPage)` を追加。
  - P×S×Nごとに `dot512-material-...png` と `radial-alpha-...csv`（p_ge_*付き）を一括出力する。
  - CSVファイル名に `-bin{binSize}` を付与して、bin設定の取り違えを避ける。
  - `MainPage.xaml.cs` 側は1行委譲。

- `ExportRadialAlphaKneeSummaryButton_Click()`
  - `RadialFalloffExportService.ExportRadialAlphaKneeSummaryAsync(MainPage)` を追加。
  - 指定フォルダ内の `radial-alpha-*.csv` を走査して、`p_ge_50` / `p_ge_100` が 0.99 / 0.95 / 0.90 を下回る最初の半径（線形補間）を自動抽出し、`radial-alpha-knee-summary.csv` を出力する。
  - 低圧で `p_ge_*` が中心でも 0.99 を満たさず `r=0.5` に張り付くケースがあるため、最大値正規化の交点半径も出力する。
    - `p50_max` / `p100_max`
    - `p50_rMax099` / `p50_rMax095` / `p50_rMax090`
    - `p100_rMax099` / `p100_rMax095` / `p100_rMax090`

- InkDrawGen
  - `RadialAlphaProfileExportService` を追加。
    - `半径αCSV(PNG→CSV)`：選択したPNG（複数可）を半径方向bin集計して `radial-alpha-{png名}-bin{bin}.csv` を出力する（`mean_alpha01` と `p_ge_*` を含む）。
    - `半径α kneeサマリCSV`：フォルダ内の `radial-alpha-*.csv` を走査して交点半径（絶対0.99/0.95/0.90 + 最大値正規化rMax*）を抽出し `radial-alpha-knee-summary.csv` を出力する。maxが絶対閾値に届かない場合は空欄にする。
    - 半径解析の中心は既定で画像中心（(w-1)/2,(h-1)/2）とし、必要な場合のみ `α重心を中心にする` をONにしてα重心へ切替できる。
    - 低圧でMaxαが50未満になるケース（例: P0.4でMaxα=43、P0.1でMaxα=5）に対応するため、閾値に`5`を追加して`p_ge_5`列を生成し、kneeサマリ側も`p_ge_5`/`p_ge_40`を抽出対象に追加した。
    - PNG→CSVには`max_alpha`列も追加し、`p_ge_*`がゼロになる理由（Maxαが閾値未満）をCSVから判別できるようにした。

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

## 追加実装: DotTesterの再現レンダラでPaperMaskを適用（2026-03）
- 背景: 外縁部の「起伏/落ち方」が再現側で平坦に見えるケースがあり、SkiaTester側にある `PaperMask`（outAマスク + 外縁フォールオフ）をDotTester側でも適用して調整できるようにする。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `PaperMaskMode` / `PaperMaskFalloffMode` を追加し、`DotReproRenderer.Options` に `PaperMaskMode/PaperMaskThreshold01/PaperMaskGain/PaperMaskFalloffMode` を追加。
  - `paperMask` はノイズ `z=(n01-mean)/std` を0..1へ写像して生成し、`outA` に乗算して適用する。
  - 適用順序は SkiaTester 互換で `outA → paperMask → cutoff`。
- `DotTester/MainWindow.xaml.cs`
  - `PaperMask` UI（mode/th/gain/edge）を読み取り、`DotReproRenderer.Options` へ配線。

## 追加実装: DotTesterで紙目強度を外縁ほど強める（edge boost）（2026-03）
- 背景: `PaperMask` は outA に直接マスクを掛けるため、微小な調整が難しく（小gainだと全域で減衰して中心まで抜けやすい）、外縁部だけを狙って調整できる方式が必要だった。
- 方針: 紙目の `Strength`（および ZNormの `strength*gain`）を、中心→外縁に向かって半径依存でスケールする。
  - 中心は1倍、外縁で `1 + boost` 倍。
  - `gamma` で立ち上がりの形を調整。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `DotReproRenderer.Options` に `EnablePaperNoiseEdgeBoost` / `PaperNoiseEdgeBoost` / `PaperNoiseEdgeBoostGamma` を追加。
  - kMean事前計測と描画の両方で同じスケールを適用し、再正規化の不整合を避けた。
- `DotTester/MainWindow.xaml` / `DotTester/MainWindow.xaml.cs`
  - edge boostのON/OFFと `boost/gamma` 入力を追加してOptionsへ配線。

## 変更: DotTesterのUI変更を即時レンダへ反映（2026-03）
- 背景: パラメータ調整で都度 `Render` ボタンを押すのが手間なため、UI変更時に自動で再レンダしたい。
- 対応: `DotTester/MainWindow.xaml.cs` で主要入力（`NumberBox/ComboBox/CheckBox/TextBox`）の変更イベントを購読し、デバウンス（150ms）で自動レンダをスケジュールする。
  - 自動レンダ時はエラーでダイアログ連発しないよう、Status表示のみ更新。
  - `Render (N=1)` ボタンは従来通り明示実行（エラーはダイアログ表示）。

## 追加実装: DotTesterで紙目タイル極値点の一致度スコアを算出（2026-03）
- 背景: タイル内の最明/最暗画素が周期的に現れる点（例: 明16点・暗18点）の値が一致すれば、紙目の位相/強度が視覚的に近いと判断しやすい。
- 対応: `DotTester` にタイル座標（Bright/Dark）とexpected解釈（Auto/alpha/白背景255-輝度）を指定して、該当点群の誤差（MAE/RMSE/MaxAbs、上位差分）を表示する評価機能を追加。
- `DotTester/Helpers/TileExtremaMatchEvaluator.cs`
  - タイル周期からキャンバス上の該当座標を列挙（texel中心を厳密にサンプルできる点のみ）し、expected/renderedの値を比較してスコア化。
- `DotTester/MainWindow.xaml` / `DotTester/MainWindow.xaml.cs`
  - 座標入力とEvaluateボタンを追加。

## 追加実装: DotTesterでタイル極値点スコアを目的関数にした総当たり探索（2026-03）
- 目的: 紙目の見た目合わせを「目視」から「スコア最小化」へ寄せ、offset/strength/gain等の探索を自動化する。
- `DotTester/MainWindow.xaml`
  - `Sweep` UIを追加（Offset/Strength/Gainの範囲・step、kMean stride、Search/Cancel、Apply）。
- `DotTester/MainWindow.xaml.cs`
  - 非同期で候補を総当たりし、タイル極値点のMAEが最小になる設定を探索。
  - `Apply`有効時は最良値をUIへ反映し、最後にフルレンダでRenderedを更新。

### 追記（2026-03）
- Sweepの進捗が分かるように、`SweepProgressBar` と `SweepProgressTextBlock` を追加。
- 探索対象に `FalloffScale` / `FalloffRNormScale` / `FalloffGamma` も追加し、必要に応じて総当たり可能にした。

### 追記（2026-03）
- Sweepを段階探索（Coarse→Refine）に対応。
  - 粗探索: `SweepCoarseOffsetStepNumberBox`（offsetの間引き）と `SweepCoarseStepMultiplierNumberBox`（各step倍率）で探索点を削減。
  - 局所探索: `SweepRefineTopKNumberBox`（上位K）をseedに、`SweepRefineOffsetRadiusNumberBox`（offset±）と `SweepRefineStepsRadiusNumberBox`（各step±N）で近傍のみを細かく再探索。
- Sweepを並列評価に対応。
  - `SweepParallelDegreeNumberBox` で最大同時実行数を指定（既定4）。
- 反復上限をUIで設定可能に変更。
  - `SweepMaxItersNumberBox`（既定200万）。段階探索では上限内に収まるseed数に自動調整。

### 追記（2026-03）
- Sweep結果の比較上位10件をCSV出力。
  - `sweep-top10-settings-*.csv`: 上位10件の設定値（offset/strength/gain/falloff等）とMAE。
  - `sweep-top10-score-*.csv`: 上位10件のスコア詳細（MAE/RMSE/MaxAbs/平均expected/rendered等）。
  - 出力先はExpected PNGと同じフォルダ配下の `SweepResult`。
  - `sweep-top10-points-*.csv`: 上位10件×34点の点詳細（expected/rendered/diff）を点ごとに1行で出力。

### 追記（2026-03）
- InkDrawGenに「紙目周期解析CSV(PNG)」を追加。
  - PNGのαから自己相関（X/Y方向の相関係数・MAE）を算出して、周期候補をCSV/ダイアログ/ログに出力。

### 追記（2026-03）
- 紙目タイル周期の確定: **435pxではなく436px**。
  - 周期（タイルサイズ）と切り出し位相（開始位置）を正しく合わせると、DotTesterの基本設定（falloff系=1）でもDotLabの差分で規則的な格子が消えることを確認。
  - 切り出し位置は縦横で2〜3px程度の補正が効いた。

### 追記（2026-03）
- DotTesterに edge帯の「Expected α × Noise × Falloff」散布図CSV出力を追加。
  - 目的: 外縁部での「単純乗算モデル」 vs 「閾値/減算/マスク系モデル」や、`Nearest` vs `Bilinear` の影響を当てるための観測データを出す。
  - UI: `Edge scatter`（a8 min/max, stride）→ `Export CSV`
  - 出力: Expected PNGと同じフォルダ配下の `SweepResult/edge-scatter-*.csv`
  - CSV列: `expected_a8` / `rendered_a8` / `n01_nearest` / `n01_bilinear` / `f` / `base_a01`（ほか座標/距離）

### 追記（2026-03）
- DotTesterに `Bicubic` サンプリング（Catmull-Rom）と outA の実験モデル `Wall-through` を追加。
  - 目的: 4) サンプラー差（Nearest/Bilinear/Bicubic）と、1) 合成式差（掛け算系 vs 減算/閾値系）をUI切替で素早く試す。
  - UI:
    - `Sampling: Bicubic`
    - `Model: Wall-through` / `WallK`
  - モデル概要: `wall = 1 - n01`、`outA = clamp((baseA01 - wall) / WallK, 0..1)`（N=1）
  - `edge-scatter-*.csv` に `n01_bicubic` 列を追加し、同一点の `Nearest/Bilinear/Bicubic` を同時に比較できるようにした。
- `DotTester/Helpers/DotReproRenderer.cs`
  - フルレンダを回さず、指定座標のα(8bit)だけ評価できる`PointEvaluator`を追加。
  - ZNormalizedでkMean再正規化が有効な場合、探索用にkMeanをstride指定で粗く計算可能。
- `DotTester/Helpers/TileExtremaMatchEvaluator.cs`
  - 点列挙(`EnumeratePoints`)とexpectedのα換算(`GetExpectedAlphaByte`)を公開し、探索で再利用。

## 修正: kernel-sweep→normalized-falloff変換を線形補間に変更（2026-03）
- 背景: `kernel-sweep`（`dx_px`）から `normalized-falloff`（`r_norm`）へ落とす際に、scaleや欠損の影響で最近傍1点を拾うと落ち方が段付きになりやすい。
- 対応: 変換時に `dxTarget=r*scale` の前後2点（`dx0<dxTarget<dx1`）から線形補間して `alpha(r_norm)` を生成する。
- `InkDrawGen/Helpers/KernelSweepToNormalizedFalloffExportService.cs`
  - `ReadKernelSweepAlphaByRNormAsync` の近傍探索（最寄り点）を、前後2点の線形補間に置換（範囲外は端でクランプ）。

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

## 追加実装: DotTester SweepのWall-through拡張 + ROI評価 + CSV出力整理（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs`
  - `OutAlphaModel=WallThrough` でも `Strength`（+`EdgeBoost`）で紙目寄与を調整できるよう、紙目 `n01` を `mean` へブレンドしてから壁貫通計算へ入れるようにした（中心の紙目が立ちやすい問題の緩和）。
- `DotTester/MainWindow.xaml`
  - Sweep項目に `WallK` / `Wall base×` / `Wall bias` の探索（ON + min/max/step）を追加。
  - Sweepの評価点として ROI(xywh) を指定できるUIを追加（stride、詳細CSV最大行数も指定）。
- `DotTester/MainWindow.xaml.cs`
  - Sweep候補に `WallK/base/bias` を追加し、staged（coarse/refine）も含めて探索できるようにした。
  - Sweepの評価点を「タイル極値点（従来）」または「ROIサンプル（stride間引き）」へ切り替え可能にした。
  - Sweep結果CSVを Top10の `summary`（候補パラメータ+スコア詳細+ROI設定）と `detail`（点/ROIのサンプル差分）に整理した。ROI詳細は行数上限で抑制。
