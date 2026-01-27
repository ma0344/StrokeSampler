# Skia側共有: UWP再現（parity）に向けた方針（限定的特例の扱い）

本件は「見た目/モデルの連続性」を直接のゴールにするのではなく、まず **UWP（Win2D）の観測値を再現すること（数値parity）**を優先します。

ただし、UWP側の挙動には `P床` や `8bit量子化` など **不連続が含まれる**ため、「UWPに数値を合わせること」と「連続で滑らかなモデルにすること」は同値ではありません。
このため、Skia側では **UWP再現の根拠が取れる範囲で限定的な特例（クランプ、低圧域のみ係数変更等）を許容**する方針で進めます。

## 特例導入のルール（必ずセットで運用）
1. **根拠（Evidence）**
   - pre-save / diagnostics CSVで原因が特定できること（例: `alpha_mul_clamped` の固定、`remain` による2回目の減衰など）
   - 可能なら数式で説明できる形にする（例: `alpha_candidate_max = 255 * alpha_mul * aeff_max`）

2. **適用範囲（Scope）**
   - 条件を明示し、影響範囲を最小化する（例: `Clamp01(pressure) <= 0.02` のみ）

3. **受け入れ基準（Acceptance）**
   - UWP CSVで観測された立ち上がりと段階値に一致（または十分近似）
   - N回重ねは「同座標にN回スタンプ」等のdiagnosticsで原因が可視化できること

4. **副作用チェック（Regression）**
   - 近傍圧力（例: `P=0.0106+`）で同種の段差が過剰になっていないか
   - `N` を増やしたときに単調性が破れていないか（例: `N=2` が `N=1` より薄くならない）

## diagnostics を使った数式ベースの切り分け（線形にならない理由）
Skia側の diagnostics 列（`alpha_candidate_max`, `alpha_candidate_last_max`, `alpha_mul_clamped`, `remain_last_max`, `aEff_max`, `aEff_last_max` など）から、原因を次の順で切り分けられます。

前提（概略）:
- `alpha_candidate ≒ color_alpha * alpha_mul * aEff`
- `alpha_byte = Round(alpha_candidate)`（8bit量子化の段差）
- `aEff = aBase * remain`, `remain = 1 - cov`
- `cov <- cov + aEff * gain`（coverage更新）

1) **量子化境界（0.5/1.5）起因**
- `alpha_candidate_max` が `0.5` 近傍なら、わずかな差で `0→1` が発生
- `alpha_candidate_max` が `1.5` 近傍なら、わずかな差で `1→2` が発生

2) **coverage(remain)起因（N増加で2回目以降が効かない）**
- `alpha_candidate_last_max` が `alpha_candidate_max` から大きく落ちる場合は、2回目以降の `remain` 減衰が支配
- 目安: `aEff_last_max / aEff_max` の比が小さいほど、反復スタンプが効きにくい

3) **clamp張り付き起因**
- `alpha_mul_clamped` が下限で固定されていると、pressureを変えても `alpha_candidate_max` が動きにくい

UWP観測が「N=1/2/3で max_alpha が 1/2/3 /255 と線形増加」なら、Skiaも同条件で parity を満たす必要があります。

## 決定木（簡略版）: 観測→原因→最小アクション
※ parity（UWP観測の再現）を優先し、変更のスコープはできる限り狭める。

1) **P床の一致**
- UWPで `P<=0.0104` が完全0 なのに Skiaが非ゼロ
  - → **入口で `P<=0.0104` を無描画**（最優先）

2) **N=1 立ち上がり（0→1段）が遅い/出ない**
- diagnostics: `alpha_candidate_max < 0.5`
  - → **量子化境界未達**
  - → 低圧域のみ（例: `p<=0.02`）で係数調整
    1. `alpha_mul_clamped` 張り付きがあれば下限を緩める/撤廃
    2. `PressureToPencilDarknessScale`（lowScale）を調整

3) **Nを増やしても最終 `max_alpha` が増えない（UWPは線形）**
- diagnostics（同座標にN回スタンプ）: `alpha_candidate_last_max < 0.5`
  - かつ `aEff_last_max << aEff_max`（または `remain_last_max` が低下）
    - → **coverage(remain)が支配**
    - → 低圧域のみ coverage 更新を弱める
      - `gain` を下げる（例: p<=0.02 のみ）
      - 必要なら `CoverageDecay` も調整

4) **段（1→2段）が出ない**
- `alpha_candidate_max >= 0.5` だが `alpha_candidate_last_max < 1.5`
  - → **2段目境界(1.5)未達**
  - → `aEff_last_max/aEff_max` が小さければ coverage 側、そうでなければ上流ゲイン側を調整

5) **回帰チェック（必須）**
- 近傍圧力（`P=0.0106+`）で段差が過剰になっていないか
- `N` 増加で最終結果（pre-saveの `max_alpha` / `center4_mean`）が単調に悪化していないか

この方針で、根拠のある最小差分を積み重ねてUWPの数値再現を進めたいです。
