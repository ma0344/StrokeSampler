# 引継ぎ（現作業を新セッションで即継続するためのメモ）

このドキュメントは「いま何をしていて、次に何をするか」を最短で再開できるようにまとめたものです。

## 目的（現フェーズ）
線描画PNG（先頭N1領域）を、単点PNG（`aligned-dot-index`）の P と α補正で近似できるかを調べ、
高圧帯域（P=0.9〜1.0）の `IoU` 低下要因を特定する。

- 形の近さ: 2値化マスク（`alpha >= th`）の `IoU` / `mismatch`
- 濃さの近さ: `alpha_k`（線≒k×点）と `alpha_l1_scaled`
- 可視化: ヒートマップ（形）と差分強度（|α差|）

## 関係ファイル
- 実装: `DotLab/Analysis/LineN1VsDotN1Matcher.cs`
- 引継ぎ（詳細）: `docs/copilot-session-summary.md` の "Aligned line N1 vs aligned-dot-index N1" 節
- DotLab全体: `docs/dotlab-handover.md`

## 入力データ（フォルダ構成）
DotLabのマッチングは「単一フォルダ内のPNG」を対象にする。

同じフォルダに以下を混在させる:
- 線候補: `*-alignedN1-...N1N2-...png`
- 単点候補: `*-alignedN1-...aligned-dot-index...png`

（ファイル名から `-P{value}-` を正規表現で抽出して pressure を得る）

## 実行手順（DotLab）
1. DotLabを起動
2. 機能: `Match line N1 vs dot N1 (ROI 18px, folder)` を実行
3. 出力を確認
   - CSV: 列数が多いので表計算/フィルタ推奨
   - PNG:
     - 形状ヒートマップ（th別、ROI版＋全幅版）
       - `lineN1-vs-dotN1-heatmap-th{th}-P{lineP}.png`
       - `lineN1-vs-dotN1-heatmap-th{th}-fullw-P{lineP}.png`
     - α差分強度（th=1、ROI版＋全幅版）
       - `lineN1-vs-dotN1-diffmag-th1-P{lineP}.png`
       - `lineN1-vs-dotN1-diffmag-th1-fullw-P{lineP}.png`

## いま分かっていること（重要・観測）
- `th` を上げると `IoU=1` が出るケースがあるが、これは `union=0` の「空っぽ一致」。
  - そのため `union/coverage` のゲートを導入して自動除外している。
- ヒートマップ観測:
  - P<=0.8 は輪郭で青/緑が混在
  - P=0.9/1.0 は黒（一致）以外が青のみ（点側が外に出る傾向）

## 現在の実装上の重要パラメータ（LineN1VsDotN1Matcher）
- ROI:
  - `RoiWidthPx = 18`
  - `RoiY0 = 435`
  - `RoiY1Exclusive = 1592`
- 2値化閾値: `th = 1,2,3,4`
- 自動除外（疎すぎる一致の排除）:
  - `union >= 200`
  - `cov_line >= 200/ROIpx` かつ `cov_dot >= 200/ROIpx`

## 次にやること（ToDo）
### A. `th=2/3` のヒートマップで「縁取り由来」かを確定
- P=0.9/1.0 で、thを上げると青（点のみON）が消えるか確認。

### B. αスケール後の可視化（未実装）
- `alpha_k` を点側へ適用した後に
  - 2値化ヒートマップ（th=1）
  - diffmag（|α_line - clip(k*α_dot)|）
 も出して、補正が効くかを目視で確認したい。

### C. 理由の推定（仮説）
- 点が外に出る: 単点生成と線先頭（端点/キャップ/補間）が異なる、または飽和/量子化で外周に残差が出る可能性。

## 注意（ビルド時）
- DotLabを起動したままビルドすると `DotLab.exe` がロックされて `MSB3021/MSB3027` が出る。
  - 対策: DotLabを終了してからビルド。

## 追加観測（InkDrawGen vs StrokeSampler）
- StrokeSamplerの手動疑似線（`Draw Hold(Fixed)` を座標を変えて複数回押す）は、各回が**別ストローク**になる（同一ストローク内の点列ではない）。
- `S200/P1` の「更新点2（Start + 1更新点）」相当では、単点Dotの `Op` を大きくする必要がある（例: `Op≈0.5436`）ことを確認。
- InkDrawGenで `Op=1` + `2点疑似線`（`dot2`）にすると、StrokeSamplerのオリジナル線に近い濃度になるケースがある（要因切り分け中）。

### Dotストローク生成経路の互換化（コード）
- StrokeSampler側のDot生成（例: `StrokeHelpers.CreatePencilDot`）は `InkStrokeBuilder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity, null, null)` を使用。
- InkDrawGen側も同じシグネチャでストローク生成するように変更した（`InkDrawGen/Helpers/InkStrokeBuildService.cs`）。

### 手動検証手順（Dot Op=0.5436, 2点疑似線）
1. StrokeSamplerで `Draw Hold(Fixed)` を使用し、(100,101) に `S200 P1 Op0.5436` のDotを打つ。
2. 座標を (118,101) に変えて同条件でDotを打つ（別ストロークの2本）。
3. InkDrawGenで `JobType=Line`、`2点疑似線` をON、`dotStep=18`、`S200 P1 Op0.5436` でPNG出力する。
4. DotLabで同一ROIを比較し、差分が縮小/一致するか確認する。

## InkDrawGen: dot2疑似線のdotStepスイープ検証メモ
- dot2疑似線（`2点疑似線` ON）は、内部的に2本のDotストローク（別ストローク）を `startX` と `startX+dotStep` に生成する。
- `dotStep` が出力画像に反映されているかは、InkDrawGenのログに出る `dot2 dbg:` を見る。
  - 例: `dot2 dbg: dotStep=17.1 p1=(117.1,101) br1=(17.1,...)`
- 出力PNG同士が完全一致になる場合は、生成ではなくレンダ/変換（Scale/Translation/クロップ）の問題を疑う。
  - DotLabでの確認は `Export Alpha Diff (PNG vs PNG)` で2枚を選び、`diff_*` がゼロかを見る。
