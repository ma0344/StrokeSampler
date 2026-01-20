# Skia移植向け：観測データ（代表サブセット）

## 目的
Skia側で以下を行うための、最小限の観測データセットを明示する。
- paper-noiseの正規/反転を自動判定する（円内MAE/RMSEなど）
- LUT差し替え（`F(r_norm)`）後の描画結果を観測PNGと比較する

## 前提
- 画像は `512×512` だが、評価は **直径Sの中心円（radius=S/2）** を主に対象とし、円外は比較から除外する。
- S/P/N はファイル名から取得できる。

## セット定義
- S（直径）: `10, 12, 100, 200`
- P: `0.05, 0.5, 1.0`
- N: `1, 10, 100`

合計: `4 × 3 × 3 = 36` ケース

## ファイルの場所
- ベースディレクトリ: `Sample/Compair/CSV/N`

各ケースに以下が存在する。
- 観測PNG: `dot512-material-S{S}-P{P}-N{N}.png`
- 観測CSV: `radial-falloff-S{S}-P{P}-N{N}.csv`

## 全36ケース（存在確認済み）

| S | P | N | 観測PNG | 観測CSV |
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

## 付属（生成/検証用）
- 一覧生成スクリプト: `tools/list-representative-sample.ps1`
