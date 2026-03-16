# Kernel Prediction Validation 02

## 目的

`Kernel_Prediction_Validation.md`（セッション 1〜23）で積み上げた分析知見を踏まえ、
**再現モデル用の特徴量を既存 CSV から抽出して新しい CSV として出力する**方針を整理する。

---

## 方針

### 対象特徴量（`P` との相関が強い主要指標群）

| 出力列名 | 導出元 | 算出式・備考 |
|---|---|---|
| `joint_count` | `stair-summary` の `plateau_count` | plateau の総数。`P` と強く相関 |
| `joint_step` | `stair-summary` の `mean_riser01` | `mean_riser01 / P`。1 関節あたりの可動量 |
| `joint_span` | `stair-summary` の `median_tread_px` | 関節間の代表長（中央値が頑健） |
| `local_slope_u250` | compare の `obs_local_slope_u250` | 正規化座標 `u=0.25` における局所勾配 |
| `local_slope_u400` | compare の `obs_local_slope_u400` | 正規化座標 `u=0.40` における局所勾配 |
| `local_slope_u550` | compare の `obs_local_slope_u550` | 正規化座標 `u=0.55` における局所勾配 |
| `local_slope_u700` | compare の `obs_local_slope_u700` | 正規化座標 `u=0.70` における局所勾配 |
| `local_slope_u850` | compare の `obs_local_slope_u850` | 正規化座標 `u=0.85` における局所勾配 |
| `curvature_budget` | compare の `obs_curvature_budget` | 曲率的な余裕量 |
| `terminal_headroom` | compare の `obs_terminal_headroom` または `100 - last_nonzero_r_norm` | 終端の余裕量 |

> **注意:** `local_slope_u100` は頂上近傍の量子化・サンプル不足の影響を受けやすいため、
> 再現モデルの主指標からは除外する（診断列として compare CSV に残す）。

---

## 入力ファイル（C# 実装）

| 優先度 | ファイル | 用途 |
|---|---|---|
| 1 | `*-stair-detail.csv` | plateau 情報を復元し、sibling の `*-stair-summary.csv` と組み合わせて観測指標を算出 |
| 2 | wide CSV（`kernel-sweep-*.csv`）| `WideKernelAnalysisSource` として直接読み込んで指標を算出 |

> sibling の summary CSV は `*-stair-detail.csv` と同じディレクトリに存在する必要があります。

---

## 出力ファイル

入力の compare CSV と同じディレクトリに  
`*-reproduction-features.csv` として出力する。

出力列順:
```
p_header, p_value,
joint_count, joint_step, joint_span,
local_slope_u250, local_slope_u400, local_slope_u550,
local_slope_u700, local_slope_u850,
curvature_budget, terminal_headroom
```

---

## 実装

### C# 実装（主）

`InkDrawGen/Helpers/KernelSweepExportService.cs`

- **`ExportKernelReproductionFeaturesCsvAsync(MainPage page)`** — UI から呼び出されるエントリポイント
  - ユーザーが `*-stair-detail.csv`（または wide CSV）を選択する
  - stair-detail の場合は sibling の `*-stair-summary.csv` を自動解決
  - `BuildKernelObservedMetricsFromStairRows` で観測指標を構築
  - `BuildKernelReproductionFeaturesCsv` で CSV テキストを生成
  - 出力ファイル名: `*-reproduction-features.csv`

`InkDrawGen/MainPage.xaml`

- `カーネル再現特徴CSV` ボタンを「カーネル断面CSV(予測比較)」ボタンの直後に追加

`InkDrawGen/MainPage.xaml.cs`

- `ExportKernelReproductionFeaturesCsvButton_Click` ハンドラ（1行委譲）

### Python ユーティリティ（補助）

`DotLab/Analysis/extract_reproduction_features.py`

アプリを起動せずにコマンドラインから既存 CSV を直接処理できるユーティリティ。
入力は `*-stair-prediction-compare.csv`（主）または `*-stair-summary.csv`（フォールバック）。

```bash
# 引数なし — DotLab/Kernel 以下の全 compare CSV を自動処理
python extract_reproduction_features.py

# compare CSV を直接指定（summary は自動解決）
python extract_reproduction_features.py path/to/kernel-sweep-...-stair-prediction-compare.csv

# compare + summary を明示指定
python extract_reproduction_features.py <compare_csv> <summary_csv>

# 出力先まで指定
python extract_reproduction_features.py <compare_csv> <summary_csv> <output_csv>

# summary のみ（compare CSV が存在しない場合）
python extract_reproduction_features.py --summary <summary_csv>
python extract_reproduction_features.py --summary <summary_csv> <output_csv>
```

---

## セッション記録

### 1. 特徴量抽出スクリプトの実装依頼

**User**
> ユーザーは、`Kernel_prediction_validation_02.md` で整理している現在の方針に沿って、
> 既存の `stair-detail.csv` / `stair-summary.csv` / compare CSV などから、
> 再現モデル用の必要特徴を抽出して新しい CSV として出力する機能の追加を求めている。
> 対象は主に `P` に相関する強い指標群（例: `joint_count`、`joint_step`、`joint_span`、
> `local_slope_u250〜u850`、`curvature_budget`、`terminal_headroom`）の抽出で、
> 実装後はその変更を Git に反映して push したい。

**Copilot 要約（第1回: Python スクリプト）**
- `DotLab/Analysis/extract_reproduction_features.py` を新規作成した。
- `DotLab/Kernel` 配下の全 compare CSV を自動処理し、各 CSV と同ディレクトリに `*-reproduction-features.csv` を出力する。

### 2. 対象CSVの明確化

**User**
> 現在対象にするべきCSVはDotLab/Kernel 配下にあります。

**Copilot 要約**
- Python スクリプトの自動検出を `DotLab/Kernel` 配下全域に対応した。
- compare CSV がない summary CSV（例: `DotLab/Kernel/` 直下の count10）も自動検出して処理するよう対応した。
- `--summary` フラグで summary-only モードにも対応した。

### 3. InkDrawGen への機能追加依頼

**User**
> 機能はInkDrawGenに追加してください。

**Copilot 要約**
- `InkDrawGen/Helpers/KernelSweepExportService.cs` に以下を追加した。
  - `ExportKernelReproductionFeaturesCsvAsync(MainPage page)` — 入力は `*-stair-detail.csv`（または wide CSV）、出力は `*-reproduction-features.csv`
  - `BuildKernelReproductionFeaturesCsv(inputFileName, rows)` — 再現特徴 CSV を組み立てるプライベートメソッド
- `InkDrawGen/MainPage.xaml` に「カーネル再現特徴CSV」ボタンを追加した。
- `InkDrawGen/MainPage.xaml.cs` に1行委譲のクリックハンドラを追加した。
