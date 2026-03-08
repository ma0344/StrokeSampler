# Pencil Parity Playbook（InkCanvas Pencil 風合い再現：検証手順書）

## 目的
- UWP の `InkCanvas`（Pencil）の見た目を基準（ゴール）として、Skia（または他の描画器）で近い風合いを再現する。
- 「生成 → 比較 → 合否判断」を再現可能な形で固定し、S/圧力/更新点間隔/更新点数/不透明度の探索を回せるようにする。

## 用語
- **オリジナル線**: UWP `InkCanvas` の Pencil（`InkDrawingAttributes.CreateForPencil()`）で生成した線（2点線など）。
- **単点Dot**: 1点（または実装上の2点）で生成するDot（`BuildSDotStroke`）。
- **疑似線**: Dotを複数並べて線に近づけたもの（dot2 / dotN / dotstepline）。
- **ROI**: 画像比較に使う切り出し領域。

## 前提（重要）
- 比較は **透過PNG（transparent）** を使う。
  - 不透明背景だと alpha が全画素255に張り付き、AlphaDiff が無意味になる。
- 差分は `|A1-A2|`（alphaの絶対差）を基本指標とする。
- 8bit量子化（BGRA8）の影響は前提として受け入れる。

### Verified: No（重ね回数）の累積は BGRA8 source-over（逐次8bit丸め）
alignedN系列（N1/N2/N4/N8 など）の「No累積」の挙動は、画素ごとに `srcA = alpha(N=1)` を用いて、

`dstA_{t+1} = dstA_t + (srcA * (255 - dstA_t) + 127) / 255`

を `t=0..N-1` で逐次適用（各ステップで8bit整数演算）するモデル（DotLab検算: `model=so255, quantize=round`）で **差分0の完全一致**が得られた。

含意:
- 「飽和capが255に届かない」現象は、PNG保存ではなく **逐次の8bit丸めで増分が0になって止まる**ことで説明できる。
- `cap(x,y)` を独立パラメータとして持たず、`srcA` から決まる固定点として扱える可能性が高い。

## 合格条件（判定ルール）
- 最終目的が「風合い再現」であるため、**最終判定は目視**を優先する。
- AlphaDiff（CSV/vis16/vis32）は、主に以下の目的で使う。
  - 差分の発生箇所が「輪郭（位置差）」か「内部（濃度/ノイズ/属性差）」かを切り分ける
  - スイープ（dotStep/Op/N等）で、比較対象の当たりを付ける
- 高精度に固定したい局所ルールについては、開始点ROI(N1)のような限定条件において `roi_diff_sum01=0`（完全一致）を合格条件として採用してよい。

---

## 手順の全体像
1. InkDrawGenで「オリジナル線」と「疑似線（dotN等）」のPNGを生成する（透明）。
2. DotLabで `Export Alpha Diff (PNG vs PNG)` を使って、差分の統計CSVと差分PNG（通常/vis16/vis32）を出す。
3. DotLabのバッチ比較（必要なら）で、スイープ結果をCSVサマリに落として最良候補を選ぶ。
4. vis16/vis32 を見て「差がどこに出ているか（輪郭/内部/局所）」を判断する。

---

## InkDrawGen: 生成（オリジナル線）
### 1) 線(2点)の単体生成
- ボタン: `線(2点)生成`
- 主な設定:
  - `JobType = Line`
  - `StartX/StartY`, `EndX/EndY`
  - `S`（例: 200）
  - `P`（例: 1）
  - `Op`（オリジナル線は通常 `Op=1`）
  - `transparent = True`
  - `dpi`, `scale`
  - `ROI` と `出力サイズ(px)` は線が入る範囲にする

### 2) 线の長さスイープ（EndX / StartX）
- `線(2点) EndXスイープ`
  - `endX sweep start/end/step` を使って `EndX` をスイープする
- `線(2点) StartXスイープ`
  - `endX sweep start/end/step` を流用して `StartX` をスイープする

注意:
- ROIが `x=0,y=0,w=18,h=202` のように原点付近のままだと、負座標の線はROI外になり空画像になる。

### S200 テンプレ（UI値例）
以下は「まず同じデータを再生成する」ための例。出力に写る範囲に合わせてROIは適宜調整する。

共通（S200/P1想定）:
- `S start/end/step`: `200 / 200 / 0`
- `P start/end/step`: `1 / 1 / 0`
- `Op start/end/step`: `1 / 1 / 0`（オリジナル線）
- `N start/end/step`: `1 / 1 / 0`
- `scale`: `10`
- `dpi`: `96`
- `transparent`: `true`

線(2点)の例:
- `JobType`: `Line`
- `StartX/StartY`: `100 / 101`
- `EndX/EndY`: `118 / 101`（18px刻みの例）

dotN疑似線の例（同じ幅に収める場合）:
- `JobType`: `Line`
- `dotStep start/end/step`: `18 / 18 / 0`
- `N個疑似線（StartX基準でN個固定）`: `true`
- `dot count`: `2`（`EndX=118` 相当の最小例）
- `Op start/end/step`: `0.54360 / 0.54360 / 0`（開始点ROI(N1)の一致用の例。表を参照）

### S200 テンプレ（CSV例）
`CSVからバッチ`で回す場合の最小例（dotNを1枚出す）。

```
jobType,s_start,s_end,s_step,pressure_start,pressure_end,pressure_step,n_start,n_end,n_step,scale,dpi,transparent,start_x,start_y,end_x,end_y,step_x,step_y,repeat_count,roi_x,roi_y,roi_w,roi_h,run_tag,dot_step_fixed_count,dot_step_count
line,200,200,0,1,1,0,1,1,0,10,96,true,100,101,118,101,0,0,0,0,0,180,2020,S200-dotN,true,2
```

---

## InkDrawGen: 生成（疑似線 dotN）

### dotN（StartX基準でN個固定）
目的: `StartX + dotStep*i`（`i=0..N-1`）でDotを並べ、同じ長さのオリジナル線と見比べる。

- 設定:
  - `JobType = Line`
  - `dotStep start/end/step` で `dotStep` を指定（スイープ可）
  - `N個疑似線（StartX基準でN個固定）` をON
  - `dot count` または `dot count start/end/step` で N を指定（スイープ可）
  - `Op` は固定なら `OpStart=OpEnd` にする

- 出力ファイル名:
  - サフィックスに `dotN{N}-step{dotStep}` が付く

---

## InkDrawGen: CSVバッチ

### dotNをCSVから指定する列
- `dot_step_fixed_count`: true/false
- `dot_step_count`: N（単一指定）
- `dot_step_count_start`, `dot_step_count_end`, `dot_step_count_step`: Nスイープ

疑似線の更新点間隔（dotStep）をCSVから指定する列:
- `dot_step_start`, `dot_step_end`, `dot_step_step`: dotStep（スイープ可）
- `dot_step_two_points`: true/false（ONの場合は2点疑似線 `dot2` を出力）

注意:
- `jobType=line` で `dot_step_*` を指定すると、通常Lineではなく疑似線（Dotを並べた線）になる。
- 逆に、CSVバッチで必ず通常の「線(2点)」を生成したい場合は、`dot_step_start=0, dot_step_end=0, dot_step_step=0` を明示しておくと安全。

### 線長を I と Np で指定する列（線(2点)生成向け）
- `interval`（別名: `i`）: 更新点間隔 I（DIP）
- `npoints`（別名: `np`, `n_points`）: 始点からの更新点数 Np

挙動:
- `npoints>0` が指定されている場合、`end_x/end_y` は無視され、水平線として以下で上書きされる。
  - `EndX = StartX + interval*(npoints-1)`
  - `EndY = StartY`
- `interval` を省略した場合は `interval = 0.09 * S`（既知の関係）を使用する。

例（S100, I=9, Npを行ごとに変更）:
- `S100` では `interval=9`（=0.09*100）
- `npoints=2,3,4,5,6,8,10,12,14` を行ごとに指定して出力する

別名も利用可:
- `dotStepFixedCount`, `dotStepCount`
- `dotStepCountStart`, `dotStepCountEnd`, `dotStepCountStep`
- `dotStepStart`, `dotStepEnd`, `dotStepStep`
- `dotStepTwoPoints`

### Op（opacity）をCSVから指定する列
- 連続レンジ指定:
  - `op_start`, `op_end`, `op_step`
  - 別名: `opacity_start`, `opacity_end`, `opacity_step`
- 非連続リスト指定:
  - `op_values`（例: `0.12;0.1795;0.5436`）
  - 区切りは `;` または `,`
  - 別名: `opacity_values`

優先順位:
1. `op_values`
2. `op_start/op_end/op_step`
3. UIの `Op start/end/step`

---

## DotLab: 単発比較（PNG vs PNG）

### Export Alpha Diff
- UI: `Export Alpha Diff (PNG vs PNG)`
- 出力:
  - 差分PNG: `alpha-diff-...png`
  - 可視化PNG: `alpha-diff-...-vis16.png`, `alpha-diff-...-vis32.png`
  - 統計CSV: `alpha-diff-...csv`

指標（重要）:
- `diff_max`: 最大のalpha差（0..255）
- `diff_nonzero_px`: 差分が出た画素数
- `diff_sum01`: 総差分（`diff_sum/255`）

見方（目安）:
- **輪郭だけ**が出る: 主に微小な位置差（平行移動）
- **内部がノイジー**に出る: 濃度分布/ノイズ位相/属性差の可能性

#### 小さい差分（max_abs_diff が 1〜3 程度）のときの注意
- `diffabs` は `|A1-A2|` を0..255で保存するため、`max_abs_diff=1` のときは **ほぼ真っ黒**に見えるのが正常。
- `visAuto` は `max_abs_diff` が255になるように自動スケールするため、`max_abs_diff=1` のときは **diff=1が全部255（白）**になり、結果として `diffmask`（diff>0の二値）とほぼ同じ見た目になる。
- 「差がどれくらい小さいか」はCSVの `max_abs_diff` を見て判断し、画像は「差が出ている場所の分布（輪郭/内部）」の判断に使う。

#### 注意: 紙目推定PNG（kernel/noiseRatio）の比較は `*-alpha.png` を使う
DotLabの `Export Alpha Diff (PNG vs PNG)` は **Alphaチャネルのみ**を比較する。
`Export Radial Kernel+Noise PNG/CSV (PNG)` が出力する `noiseRatio-vis2x.png` は可視化用のため、Alphaが常に255（不透明）であり、AlphaDiffでは差分0になってしまう。
紙目推定（kernel/noiseRatio）をAlphaDiffで比較する場合は、値がAlphaに格納されている `*-alpha.png` を選択する。

---

## DotLab: バッチ比較（スイープ結果の集計）
- `Run batch and export CSV` を使い、総当たりで比較してサマリCSVを作る。
- `Use full image (w×h) AlphaDiff (instead of ROI 18px)`
  - ON: 全画素の差分
  - OFF: 従来の左帯ROI(18px)中心（N1用途）

---

## 用語（線を描画する際の変化要素と規則性）

### 変化要素
- `S`: Size（サイズ）
- `P`: Pressure（筆圧）
- `Op`: Opacity（透明度）
- `No`: OverWrite（重ね回数）
- `L`: Length（DIP長）
- `I`: Interval（更新点間隔。線の座標として有効な間隔）
- `Np`: Npoints（始点からの更新点の数）

### 規則性（現在の確定/運用）
- `S`: ストロークの開始値が保持されるため、1ストローク内では不変（検証はS固定系列が前提）。
- `P`: 入力によりストロークポイント単位で変化し得る（検証ではP固定系列も多い）。
- `Op`: （線の開始区間ROIの一致という文脈では）`L` / `Np` によって変化し得るが、`Np` が一定以上になると以後は変化しない（定常化）。
- `No`: 1回分スタンプ自体は `No` を増やしても変化しない（同一条件なら同一）。見た目の変化は主に累積合成（BGRA8上のsource-over）で発生する。
- `I`: `I = 0.09 * S`（`S20..180 step20` の境界スイープで確認）。`S` が一定なら1ストローク内で不変。
- `L`: ストロークの長さ（水平線なら `L = EndX - StartX`）。
- `Np`: `Np = floor(L / I) + 1`

## Verified Findings（確定事項）

### S200 dot2疑似線のdotStep最適値
- 適用範囲: `S200` / `P1` / `dpi96` / `scale10` / `transparent` / （評価は全画素AlphaDiffを使用）
- 条件: `2180x2020` / `dpi96` / `S200` / `P1` / `N1` / `scale10` / `transparent`
- 結論: `dot2-step=18.00` が最適（`17.9`〜`18.9` を含むスイープでも `18.00` 近傍が底）

### dot2疑似線の残差の見え方
- 適用範囲: 上記 `S200 dot2` の比較（`dot2-step17.99` vs `dot2-step18.01`）
- `dot2-step17.99` vs `dot2-step18.01` の比較では、差分は2つ目ドットの輪郭成分のみ（円内部のもじゃもじゃ無し）。
- したがって、残差は主に「2つ目ドットの中心位置の微小な平行移動（サブピクセル級）」に起因する可能性が高い。

### 更新点間隔（dotStep）はSに比例する（dotStep = 0.09 * S）
- 適用範囲: `S20..180 step20` / `P=1` / 透過PNG / （最短線が「単点→線（点が増える）」に変化する境界を同定）
- 方法: 各Sで、線の長さ（`EndX-StartX`）を `0.00001` 単位でスイープし、「単点になる範囲」と「線（点が増える）になる範囲」の境界長 `L_threshold` を求めた。
- 結果: `L_threshold` は明確に `0.09 * S` に一致し、更新点間隔（dotStep相当）は `dotStep(S) = 0.09 * S` とみなせる。
  - 例: `S200 -> 18.0`（既知の `dot2-step=18.00` と整合）

| S | 境界長 L_threshold (=0.09*S) |
|---:|---:|
| 20 | 1.8 |
| 40 | 3.6 |
| 60 | 5.4 |
| 80 | 7.2 |
| 100 | 9.0 |
| 120 | 10.8 |
| 140 | 12.6 |
| 160 | 14.4 |
| 180 | 16.2 |

### S200 線描開始点ROI(N1)の最適Dot Op（長さ依存の法則として採用）
- 適用範囲: `S200` / `P1` / `dpi96` / `scale10` / `transparent` / 開始点側ROI(N1)
- 条件: `S200` / `P1` / `dpi96` / `scale10` / `transparent` / 線は `Op=1`
- 対象: 開始点側ROI（N1）。線全体や終端の一致を保証するものではない。
- 事実: 長さ（`EndX`）ごとに Dot の最適 `Op` をスイープで求めると、ROI内のAlphaDiffが `roi_diff_sum01=0`（完全一致）になる。
- 採用: このCSVを「S200の線描開始点ROI(N1)の局所挙動法則（長さ依存の必要Op）」として採用する。

- 元CSV: `DotLab/Analysis/Dots/lineN1-vs-dotN1-opacitysweep-summary.csv`

| line_file (EndX) | best_dot_opacity |
|---:|---:|
| 118 | 0.54360 |
| 136 | 0.39220 |
| 154 | 0.31660 |
| 172 | 0.27100 |
| 190 | 0.24100 |
| 208 | 0.21942 |
| 226 | 0.20310 |
| 244 | 0.19050 |
| 262 | 0.18050 |
| 280 | 0.17210 |
| 298 | 0.17860 |
| 316 | 0.17950 |
| 334 | 0.17950 |

### 検証: N1 ROIの `Op(Np)` テーブルはSでほぼ不変（S100..200）
- 目的: `S200` で得られた「開始点ROI(N1)の `Op(Np)` テーブル」が、他の `S` でも同一系列として扱えるかを確認する。
- 方法:
  - `I = 0.09 * S` を用い、`Np` を `2,4,5,6,8,10,12,14` に固定して line（`Op=1`）を生成。
  - 量子化影響を減らすため、ROI内のピクセル数が概ね揃うよう `scale` を調整（例: `S100:20`, `S140:15`, `S180:11`, `S200:10`）。
  - 各 `S` ごとに Dot 側を Op sweep して、DotLabのサマリCSV（主にROI差分）で best を採用。
- 結果（結論）:
  - best `Op` は `S=100/140/180/200` で同一系列に乗り、差が出ても概ね `1e-3` 程度（8bit alpha量子化の影響が支配的）で、最小となる帯域はほぼ一致した。
  - よって少なくともこの条件範囲では、`Op(Np)` は **Sに対してほぼ不変な共通テーブル**として扱ってよい。

### 検証: dotN疑似線と2点Lineは線全体で目視一致（S10..200）
- 目的: `I=0.09*S` と `Np` を揃えたとき、dotN疑似線（DotをN個並べる）で2点Lineの見た目を再現できるか確認する。
- 方法:
  - `S=10/20/40/80/100/140/180/200` を対象に、各Sで `I=0.09*S` を採用。
  - `Np` を複数（例: `2..14` の代表値）に振り、同一 `S/I/Np` のペア（2点Line vs dotN）を生成して線全体を目視比較。
  - 最終出力サイズを揃えるため `scale=2000/S` を採用（例: `S200:10`, `S140:14`, `S100:20`, `S20:100`, `S10:200`）。
  - ROIは線全体が入るように十分広い範囲を使用。
- 結果（結論）:
  - 上記範囲では、dotN疑似線と2点Lineは線全体としてほぼ同じ見た目になり、形状近似として採用できる。

---

## 実装の手法候補（モデル化案）
このセクションは「確定事項」ではなく、実装時にパラメータを連続化/自動化するための候補をまとめたもの。

### N（更新点数）の定義（このCSVにおける換算）
`lineN1-vs-dotN1-opacitysweep-summary.csv` は `EndX` を持つため、`dotStep=18`（更新点18px刻み）と `StartX=100` を仮定すると、更新点数 `N` は次で換算できる。

- `N = (EndX - StartX) / dotStep + 1`

※ `EndX` が18刻みでない場合は、この換算は成立しない。

### `Op(N)` の候補
#### 候補A: 指数収束（説明向き）
- 形: `Op(N) = Op∞ + A * exp(-k*(N-2))`
- 解釈: 更新点数が増えるほど開始点ROIの局所見えが定常値 `Op∞` に収束する。

#### 候補B: 有理式（簡易）
- 形: `Op(N) = Op∞ + A / (N + B)`

#### 候補C: 区分（実装向き）
- 例: 小さいNはテーブル参照（または線形補間）、一定以上は定数
  - `N <= N0`: テーブル参照
  - `N >= N1`: `Op = Op∞` 固定

### 注意
- 上の候補は「開始点ROI(N1)」に対する局所モデル。線全体/終端の一致を保証しない。
- `N=11` 近傍のような軽い揺れがあるため、実際の運用では区分（候補C）やテーブル参照が安全。

---

## トラブルシュート

### 空の画像（透明で何も描かれていない）
- ROIが描画領域を含んでいるか確認する。
  - `StartX` や `EndX` が負の場合、`roi_x` も負へ調整する。

### 差分が常に0になる
- 入力PNGが透明ではなく、alphaが全画素255になっていないか確認する。
- `Export Alpha Diff` のCSVで `png*_alpha_sum` / `png*_alpha_nonzero_px` を確認する。

### DotLabの差分が0に見える（実装経路由来）
- 過去に `SKBitmap.GetPixel().Alpha` 参照の影響で、入力SHAが違うのに `diff_max=0` のように見えるケースがあった。
- 現在は `SKBitmap.Pixels` 参照へ修正済みだが、類似の症状（差分0が不自然）が出た場合はDotLab側のalpha抽出経路を疑う。

---

## 次のステップ（Skia等へ移植する際）
- まずは「単点Dot」「dotN疑似線」「線(2点)」の出力PNGを基準として固定し、同一条件でSkia側出力とのAlphaDiffを回す。
- 位置ズレと濃度差を混同しないため、差分可視化（vis16/vis32）で差分の形状を確認する。
