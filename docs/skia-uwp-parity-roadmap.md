# Skia ? UWP 鉛筆パリティ Roadmap（Dot → Stroke → ISF）

## 目的
- UWP（Win2D / InkCanvas）の観測値（特に **pre-save alpha summary**）を根拠として、Skia側の鉛筆描画を段階的に一致させる。
- 実装の順序は **Dot → Stroke → ISF再生** とし、各段階で回帰チェック可能な検証ループを作る。

## 前提 / 方針
- パリティ優先（UWP観測値の再現が第一）。
- PNG保存後ではなく、描画直後の **pre-save alpha summary** で比較する。
- 変更は最小スコープで導入し、変更ごとに回帰チェックを行う。

参照:
- `docs/skia-uwp-parity-policy-message.md`
- `docs/skia-validation-decision-order.md`
- `docs/uwp-skia-pre-save-alpha-findings.md`

---

## フェーズ 0: 検証環境（SkiaTester）を整える
### ゴール
- WPF（`SkiaTester/`）で、鉛筆Dotを `S`（直径px）/`P`（pressure）などの入力で描画できる。
- 描画直後の `max_alpha` 等を算出し、UWP側CSVと突合できる。

### 成果物（例）
- 入力UI（S/Pの指定、表示補助（α無視表示、チェッカー背景））
- pre-save alpha summary（少なくとも `max_alpha` / `nonzero_count`）

---

## フェーズ 1: Dot（単発）で P床（無描画しきい値）を一致させる（最優先）
### ゴール
- UWPで観測された **S依存のP床** をSkia側でも再現し、境界前後で
  - `P <= p_floor(S)` → `max_alpha = 0`
  - `P >  p_floor(S)` → `max_alpha = 1/255`
 となる。

### 仕様（確定事項）
- P床はS（直径px）依存。
- `S=1..34` はテーブル参照。
- `S>=35` は定数 `0.0105`。
- 判定は **`if (pressure <= p_floor(S)) return;`（完全に描画しない）**。

### 検証
- 代表Sで境界前後を手動/自動で確認（例: `S=24` の `0.0102`、`S=100` の `0.0104/0.0105` など）。

---

## フェーズ 2: Dot（単発）で形（分布）と濃度の一致を進める
### ゴール
- P床の一致を前提に、Dotの形状（半径方向減衰）と濃度（alphaのスケール/量子化）をUWP観測に近づける。

### 検証観点（例）
- LUT `F(r_norm)` による半径方向減衰の一致
- 紙目ノイズ `N(x,y)` の掛け方（正/反転、タイル規約）
- 8bit量子化境界（`1/255` 刻みの立ち上がり）

参照:
- `docs/skia-migration-uwp-pencil-model.md`
- `docs/representative-sample-subset.md`

---

## フェーズ 3: N（同一点重ね）を一致させる
### ゴール
- 同一点にN回スタンプしたときの `max_alpha` 増え方がUWP観測と整合する。

### 参考
- `SrcOver` の理想形: `A_N = 1 - (1 - A_1)^N`（中心近傍の傾向確認に使う）

参照:
- `docs/skia-handover-reply-src-over-and-stacking.md`

---

## フェーズ 4: Stroke（点列）で線を引けるようにする
### ゴール
- Dot素材が揃った前提で、点列（スタンプ列）から線を生成し、UWPの線に近づける。

### 進め方（段階）
- Step A: 点列の間引き（過密スタンプ対策）
- Step B: coverage/remain による差分塗り寄せ
- Step C: gain/decay などで鉛筆らしさを残しつつ抑制

参照:
- `docs/skia-overstamp-mitigation-algorithm.md`

---

## フェーズ 5: ISFを読み込み、再生できるようにする
### ゴール
- ISF（InkStrokeContainer相当）を読み込み、Skia側で同等の描画結果を再現できる。

### 注意点
- ここに入る前に、DotとStrokeのパリティがある程度取れていること（入力由来の不一致を減らす）。

---

## 回帰チェック（必須）
- P床境界付近（例: `P=0.0104/0.0105` 周辺）で段差が過剰になっていないか。
- `N` を増やしたときに単調性が破れていないか。
- 代表サブセットで極端な濃さ/薄さが再発していないか。
