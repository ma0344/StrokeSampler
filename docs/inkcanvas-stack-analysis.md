# InkCanvas 重ね塗り挙動の切り分けメモ（確定事項）

## 目的
`StrokeSampler` の `DrawDotButton` による同一座標への重ね塗り（InkCanvas累積）で、低圧/高圧で挙動が変わる原因を切り分けた結果の「確定事項」を記録する。

## 前提（測定手順）
- `Dot512PressureNumberBox` で筆圧を設定
- `Dot512SizeTextBox` でサイズ（例: S=200）を設定
- `DrawDotButton` を重ね塗り回数分押す（同一 InkCanvas に累積）
- `ExportHighResPngCroppedTransparentButton` で cropped + transparent PNG を出力
- 出力PNG（8bit alpha）を Gimp / ImageMagick でスポイト、ヒストグラム、差分等で評価

### 重要: 差分の意味
ImageMagick の `-compose difference` は引き算ではなく **絶対差** `|A-B|`。

## 確定事項

### 1) 1回分のストローク（スタンプ）は同一になる
同一座標・同一筆圧・紙目ワールド固定の条件では、各回の「1ストローク分（laststroke）」のPNGは **完全一致**した。

根拠（ImageMagickでのα差分統計）:
- `alpha_diff_laststroke_P0.1_N1_N2.png  mean=0  stddev=0`
- `alpha_diff_laststroke_P0.1_N2_N3.png  mean=0  stddev=0`
- `alpha_diff_laststroke_P1_N1_N2.png    mean=0  stddev=0`
- `alpha_diff_laststroke_P1_N2_N3.png    mean=0  stddev=0`

結論:
- 「P依存で勾配率が変わるように見える」等の現象は、**スタンプ生成（`CreatePencilDot`）の中身が回数に応じて変化している**ことが原因ではない。

### 2) P依存の挙動差は、InkCanvas側の累積で生じる
累積結果（canvas）の差分統計は、P=0.1 と P=1 で明確に異なる。

#### P=0.1
- `alpha_diff_canvas_P0.1_N1_N2.png  mean=0.00922285  stddev=0.00560776`
- `alpha_diff_canvas_P0.1_N2_N3.png  mean=0.00922285  stddev=0.00560776`
- `alpha_diff_canvas_P0.1_delta_delta.png  mean=0  stddev=0`

解釈:
- 増分が **完全に一定**（Δ12=Δ23、かつ ΔΔ=0）。

#### P=1
- `alpha_diff_canvas_P1_N1_N2.png  mean=0.143656  stddev=0.10309`
- `alpha_diff_canvas_P1_N2_N3.png  mean=0.0875596  stddev=0.0583849`
- `alpha_diff_canvas_P1_delta_delta.png  mean=0.0560963  stddev=0.0530849`

解釈:
- 2回目の増分が1回目より小さい（Δ23 < Δ12）。
- これは「飽和へ近づくほど増分が減る」挙動と整合し、**累積合成 + 8bit量子化**側の影響が支配的である。

結論:
- 低圧/高圧での見た目（等高線化、255/254/…帯、谷が残る等）の差は、主に **InkCanvasの累積（合成・飽和・量子化）**で生じる。

## 実験用の補助出力（実装）
切り分けのため、最後に追加されたStrokeのみをHiResで出力するボタンを追加した。

- UI: `Export HiRes LastStroke (Cropped+Transparent)`
- 出力ファイル名: `...-laststroke-...png`

## 次の調査候補（未確定）
- InkCanvas（あるいは描画経路）の合成式が source-over 相当であるか、または別の合成式であるか
- 8bit量子化が「最後だけ」か「各回（途中バッファ）」でも発生しているか

## 次の方針（決定ログ）
- 目的が「InkCanvas累積の見た目をなるべく再現」であるため、合成式候補を `source-over` / `add` / `max` の3つで実装し、HiResエクスポート（Win2Dレンダ）の `canvas` 出力と一致度比較で合成式を推定する。

## 合成式推定の実験手順（実装済み）
### 目的
`laststroke`（1回分のスタンプ）を入力として、合成式を `source-over` / `add` / `max` でN回適用した結果を生成し、HiResエクスポートの `canvas` と比較する。

### UI
- `Export HiRes Simulated Composite (SO/Add/Max)`
  - `UIHelpers.GetDot512Overwrite` を N として利用
  - 各合成モードごとに PNG と α統計CSV を出力する

### 出力
- PNG: `pencil-highres-sim-...-{tag}.png`
- CSV: `pencil-highres-sim-pre-save-alpha-...-{tag}.csv`

## 合成式推定の結果（確定）
### P=1, N=3
実測（HiRes pre-save canvas）と simulated の統計が **完全一致**した。

- 実測 canvas: `alpha_max=245, mean=0.47962554, stddev=0.35800732, unique=148`
- simulated source-over: `alpha_max=245, mean=0.47962554, stddev=0.35800732, unique=148`

一方で、他の候補は一致しない。

- simulated add: `alpha_max=255, mean=0.57888421, stddev=0.42994251, unique=86`
- simulated max: `alpha_max=167, mean=0.24841, stddev=0.20768991, unique=168`

結論:
- P=1 でのInkCanvas累積（HiResレンダ経路）は **source-over 相当**である。

### P=0.1, N=3
実測（HiRes pre-save canvas）と simulated の統計が一致した。

- 実測 canvas: `alpha_max=18, mean=0.02766854, stddev=0.01682329, unique=7`
- simulated source-over: `alpha_max=18, mean=0.02766854, stddev=0.01682329, unique=7`
- simulated add: `alpha_max=18, mean=0.02766854, stddev=0.01682329, unique=7`

結論:
- P=0.1 では `add` と `source-over` の差が統計上出ない範囲（飽和が十分小さい範囲）にある。
- 少なくとも `max` は不一致である（`alpha_max=6` のまま）。

### N=50での追認（確定）
P=0.1/P=1 ともに N=50 で実測（HiRes pre-save canvas）と simulated source-over が一致した。

- P=0.1, N=50
  - 実測 canvas: `alpha_max=176, mean=0.34423477, stddev=0.19734977, unique=7`
  - simulated source-over: `alpha_max=176, mean=0.34423477, stddev=0.19734977, unique=7`
  - simulated add: `alpha_max=255, mean=0.46114058, stddev=0.28038433, unique=7`（不一致）

- P=1, N=50
  - 実測 canvas: `alpha_max=255, mean=0.72779309, stddev=0.43154587, unique=25`
  - simulated source-over: `alpha_max=255, mean=0.72779309, stddev=0.43154587, unique=25`

結論:
- HiResレンダ経路（Win2D `CanvasRenderTarget` + `DrawInk`）の累積は **BGRA8（8bit）** の上で **source-over** で行われると見なしてよい。

## 調査の進め方（直線ストローク：点列→描画の変換を観測）
作業手順のループフローは `docs/sampling-loop-workflow.md` を参照。

### 目的
実デバイス由来の点列（InkPointsDump）と、制御した点列（直線固定）を比較し、「点間隔（dd）」「時間差（dt）」「点列の描画変換（補間/埋め）」の影響を分離して観測する。

### 手順（推奨）
1. StrokeSamplerでキャンバスをクリアする
2. `Dot512 Pressure` と `Dot512 Size` を設定する（例: P=0.1/1.0、S=200）
3. Start/Endを水平直線に設定する（例: Start=260,440 End=1260,440）
4. `LinePts` と `LineStep(px)` を変えて `Draw Line (Fixed)` を実行する
   - 実行すると、ストロークを描画しつつ `LocalFolder/InkPointsDump` に points JSON を自動保存する
5. 直後にHiRes出力を行う（`Export HiRes PNG (Cropped+Transparent)` など）
6. DotLabで以下を実行する
   - `Export InkPointsDump Stats (dd/dt CSV)` で、dumpフォルダからdd/dt統計CSVを生成する
   - `Export Alpha Diff (Canvas vs Sim)` で、実測canvas PNG と sim-sourceover PNG のα差分を生成する

### 観測の見方（目安）
- `LineStep(px)` を大きくしても線が埋まる → Inkの補間（点増し/連続形状化）の可能性
- `LineStep(px)` が大きいと途切れが出る → 点列の離散性（スタンプ間隔）に依存
- dd/dt統計で `dd=0` が多いのに濃淡が変わる → 同一点でpressure変化による累積の影響
