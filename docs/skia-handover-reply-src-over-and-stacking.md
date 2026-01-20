# Skia側Copilot回答への返信（UWP Pencil 移植）

## 目的
`migration/skia-migration-uwp-pencil-model.md` を読んだ上でのご確認（「通常合成 `SrcOver` 前提でよいか」「重なりの合成則が支配的か」）に対し、こちらで取得した観測データを添えて回答します。

---

## 結論（先に）
- **現段階では `SrcOver` 前提のままで進めてください。**
- ただし「重ね（スタンプを同一点に繰り返す）による濃くなり方」が `SrcOver` の理想式で説明できるかは重要なので、こちらで **P×S×Nのデータセットを追加取得**しました。
- その結果、少なくとも **中心近傍（r=0 bin）**の増え方は、`SrcOver` の「指数飽和」モデルと整合する傾向が強いです。

---

## 質問1への回答：LUTは単発 `F(r)` だけか？重ねの濃くなり方も観測したか？
- 以前渡した `normalized-falloff-...csv` は **単発（N=1）**の距離減衰 `F(r)` です。
- 今回、新たに **重ね回数 N を振った** `radial-falloff-S{S}-P{P}-N{N}.csv` を作成し、
  さらにそれらから **中心α（r=0）**と複数半径の値を抽出したサマリCSVも作成しました。

---

## 質問2への回答：支配的なのは a) か b) か？
- **まずは a)（`baseAlpha` / `F(r)` / `noise` の再現）を優先**して差分実装するのが最小変更です。
- ただし b)（重なりの積み方）が `SrcOver` から大きく外れていないかを確認するため、以下のサマリを提示します。

### 観測サマリ（中心α：r=0）
- `center_alpha(S,P,N)` は、各 `(S,P)` ごとに
  - `N` に対して単調増加
  - 上限 `?1` に飽和
  という傾向を示します。

- これは `SrcOver` の同一点重ねで得られる理想式
  - `A_N = 1 - (1 - A_1)^N`
  と形状が一致しやすい（指数飽和）ため、
  **少なくとも中心近傍の累積は `SrcOver` モデルで十分説明できる可能性が高い**です。

> 注意: ここでの `A_N` は「中心1ピクセル」ではなく `radial-falloff` の `r=0 bin` の平均値です（完全一致は要求せず、傾向の一致を見ています）。

---

## Skia側の推奨実装方針（最小差分）
1. 合成は当面 `SKBlendMode.SrcOver` のまま
2. `StampSoftCircle` の "mask" 生成を、`F(r_norm)`（距離減衰LUT）参照に置換
3. 紙目は従来どおり `DstIn` 相当（アルファマスク）で適用
4. もし「重なりが合わない」と感じた場合のみ、半径別サマリを見て合成則の追加検討に進む

---

## ? 今回新たに渡すファイル（Skia側へコピー）

### 1) 中心αのサマリ（P×S×N）
- `Sample/Compair/CSV/N/center-alpha-vs-N-vs-P.csv`
  - 列: `S,P,N,center_alpha`
  - 用途: 重ね回数による飽和の一次判定（`SrcOver`式と比較しやすい）

### 2) 複数半径サンプルのサマリ（P×S×N）
- `Sample/Compair/CSV/N/alpha-samples-vs-N-vs-P.csv`
  - 列: `S,P,N,a_r0,a_r1,a_r2,a_r5,a_r10,a_r20,a_r50,a_r100`（※列は半径一覧設定に依存）
  - 用途: 中心だけでなく外縁まで含めた分布形状の変化を確認

### 3) 観測画像/観測CSVの代表サブセット（自動判定・目視確認用）
以下は、Skia側で「paper-noise反転の自動判定」や「円内MAE/RMSE比較」を回すための最小サブセットです。

- S（直径）: `10, 12, 100, 200`
- P: `0.05, 0.5, 1.0`
- N: `1, 10, 100`

各組み合わせについて、同じフォルダに次の2ファイルがあります：
- 観測PNG: `Sample/Compair/CSV/N/dot512-material-S{S}-P{P}-N{N}.png`
- 観測CSV: `Sample/Compair/CSV/N/radial-falloff-S{S}-P{P}-N{N}.csv`

存在確認済み（全36ケース）

| S | P | N | PNG | CSV |
|---:|---:|---:|---|---|
| 10 | 0.05 | 1 | `Sample/Compair/CSV/N/dot512-material-S10-P0.05-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S10-P0.05-N1.csv` |
| 10 | 0.05 | 10 | `Sample/Compair/CSV/N/dot512-material-S10-P0.05-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S10-P0.05-N10.csv` |
| 10 | 0.05 | 100 | `Sample/Compair/CSV/N/dot512-material-S10-P0.05-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S10-P0.05-N100.csv` |
| 10 | 0.5 | 1 | `Sample/Compair/CSV/N/dot512-material-S10-P0.5-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S10-P0.5-N1.csv` |
| 10 | 0.5 | 10 | `Sample/Compair/CSV/N/dot512-material-S10-P0.5-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S10-P0.5-N10.csv` |
| 10 | 0.5 | 100 | `Sample/Compair/CSV/N/dot512-material-S10-P0.5-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S10-P0.5-N100.csv` |
| 10 | 1 | 1 | `Sample/Compair/CSV/N/dot512-material-S10-P1-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S10-P1-N1.csv` |
| 10 | 1 | 10 | `Sample/Compair/CSV/N/dot512-material-S10-P1-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S10-P1-N10.csv` |
| 10 | 1 | 100 | `Sample/Compair/CSV/N/dot512-material-S10-P1-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S10-P1-N100.csv` |
| 12 | 0.05 | 1 | `Sample/Compair/CSV/N/dot512-material-S12-P0.05-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S12-P0.05-N1.csv` |
| 12 | 0.05 | 10 | `Sample/Compair/CSV/N/dot512-material-S12-P0.05-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S12-P0.05-N10.csv` |
| 12 | 0.05 | 100 | `Sample/Compair/CSV/N/dot512-material-S12-P0.05-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S12-P0.05-N100.csv` |
| 12 | 0.5 | 1 | `Sample/Compair/CSV/N/dot512-material-S12-P0.5-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S12-P0.5-N1.csv` |
| 12 | 0.5 | 10 | `Sample/Compair/CSV/N/dot512-material-S12-P0.5-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S12-P0.5-N10.csv` |
| 12 | 0.5 | 100 | `Sample/Compair/CSV/N/dot512-material-S12-P0.5-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S12-P0.5-N100.csv` |
| 12 | 1 | 1 | `Sample/Compair/CSV/N/dot512-material-S12-P1-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S12-P1-N1.csv` |
| 12 | 1 | 10 | `Sample/Compair/CSV/N/dot512-material-S12-P1-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S12-P1-N10.csv` |
| 12 | 1 | 100 | `Sample/Compair/CSV/N/dot512-material-S12-P1-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S12-P1-N100.csv` |
| 100 | 0.05 | 1 | `Sample/Compair/CSV/N/dot512-material-S100-P0.05-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S100-P0.05-N1.csv` |
| 100 | 0.05 | 10 | `Sample/Compair/CSV/N/dot512-material-S100-P0.05-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S100-P0.05-N10.csv` |
| 100 | 0.05 | 100 | `Sample/Compair/CSV/N/dot512-material-S100-P0.05-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S100-P0.05-N100.csv` |
| 100 | 0.5 | 1 | `Sample/Compair/CSV/N/dot512-material-S100-P0.5-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S100-P0.5-N1.csv` |
| 100 | 0.5 | 10 | `Sample/Compair/CSV/N/dot512-material-S100-P0.5-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S100-P0.5-N10.csv` |
| 100 | 0.5 | 100 | `Sample/Compair/CSV/N/dot512-material-S100-P0.5-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S100-P0.5-N100.csv` |
| 100 | 1 | 1 | `Sample/Compair/CSV/N/dot512-material-S100-P1-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S100-P1-N1.csv` |
| 100 | 1 | 10 | `Sample/Compair/CSV/N/dot512-material-S100-P1-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S100-P1-N10.csv` |
| 100 | 1 | 100 | `Sample/Compair/CSV/N/dot512-material-S100-P1-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S100-P1-N100.csv` |
| 200 | 0.05 | 1 | `Sample/Compair/CSV/N/dot512-material-S200-P0.05-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S200-P0.05-N1.csv` |
| 200 | 0.05 | 10 | `Sample/Compair/CSV/N/dot512-material-S200-P0.05-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S200-P0.05-N10.csv` |
| 200 | 0.05 | 100 | `Sample/Compair/CSV/N/dot512-material-S200-P0.05-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S200-P0.05-N100.csv` |
| 200 | 0.5 | 1 | `Sample/Compair/CSV/N/dot512-material-S200-P0.5-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S200-P0.5-N1.csv` |
| 200 | 0.5 | 10 | `Sample/Compair/CSV/N/dot512-material-S200-P0.5-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S200-P0.5-N10.csv` |
| 200 | 0.5 | 100 | `Sample/Compair/CSV/N/dot512-material-S200-P0.5-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S200-P0.5-N100.csv` |
| 200 | 1 | 1 | `Sample/Compair/CSV/N/dot512-material-S200-P1-N1.png` | `Sample/Compair/CSV/N/radial-falloff-S200-P1-N1.csv` |
| 200 | 1 | 10 | `Sample/Compair/CSV/N/dot512-material-S200-P1-N10.png` | `Sample/Compair/CSV/N/radial-falloff-S200-P1-N10.csv` |
| 200 | 1 | 100 | `Sample/Compair/CSV/N/dot512-material-S200-P1-N100.png` | `Sample/Compair/CSV/N/radial-falloff-S200-P1-N100.csv` |

### 3) 元の距離減衰CSV（必要に応じて）
- `Sample/Compair/CSV/N/radial-falloff-S{S}-P{P}-N{N}.csv`
  - 列: `r,mean_alpha`
  - 用途: サマリが不足した場合の詳細参照
***
