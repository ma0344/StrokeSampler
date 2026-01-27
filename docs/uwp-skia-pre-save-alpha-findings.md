# UWP vs Skia 保存前α統計（pre-save alpha summary）で判明した事実

## 目的
UWP（Win2D）とSkiaのdot描画について、PNG保存後ではなく「描画直後（保存前）のα統計」を比較し、差分の原因を切り分ける。

## 前提 / 対象データ
- 画像サイズ: 512x512
- 背景: 透過（clear）
- 集計対象: 描画後のピクセルのAチャンネル（0..255）
- 主なCSV
  - UWP: `Sample/Compair/uwp-pre-save-alpha-summary.csv`
  - Skia: `Sample/Compair/Skia/skia-pre-save-alpha-summary.csv`
  - Skia（低Pまで）: `Sample/Compair/Skia/skia-pre-save-alpha-summary-down-to-1e-6.csv`

## 集計指標（CSV列の意味）
- `center4_mean`: 中心4px（(255,255)(256,255)(255,256)(256,256)）のα平均（0..1）
- `max_alpha`: 全画像での最大α（0..1）
- `nonzero_count`: 全画像で alpha_byte != 0 のピクセル数
- `nonzero_ratio`: `nonzero_count / (512*512)`
- `*_circle`: 直径Sの中心円内だけで上記を集計したもの
  - 円判定中心: `cx=(W-1)/2`, `cy=(H-1)/2`（512なら 255.5）
  - 半径: `S/2`
  - 判定: `dx*dx + dy*dy <= r*r`

## 判明した事実

### 1) UWPには「P床（無描画しきい値）」が存在する
UWPでは `P<=0.0104` の範囲で、保存前から **完全に描画されない**（全ピクセルα=0）。

例（`S=100, N=1`、`Sample/Compair/uwp-pre-save-alpha-summary.csv`）:
- `P=0.0104`: `max_alpha=0`, `nonzero_count=0`
- `P=0.0105`: `max_alpha=0.003922 (=1/255)`, `nonzero_count=1`

→ これはPNG保存（エンコード）由来ではなく、描画結果そのものの段階で起きている。

### 2) UWP低P域は 1/255 刻みの段階値で立ち上がる
UWPの `max_alpha` は、立ち上がり直後に `1/255` になり、低P域では段階値として観測される。

### 3) UWPでは N=2 が N=1 の倍率として現れる（低P域）
同じ(S,P)で N=2 にすると、`max_alpha` が `1/255 -> 2/255` のように倍率で増える。

例（`S=100, P=0.0105`）:
- `N=1`: `max_alpha=0.003922`
- `N=2`: `max_alpha=0.007843`

また `nonzero_count` は Nで増えにくく、「非ゼロ領域の拡大」よりも「既存非ゼロの濃度増加」に寄っている。

### 4) SkiaにはUWPと同等の「P床（完全0になる閾値）」が見当たらない
Skiaは `P=0.01` の時点で既に非ゼロの描画結果になっており、UWPのような `P<=0.0104` の完全0挙動と一致しない。

例（`S=100, N=1`、`Sample/Compair/Skia/skia-pre-save-alpha-summary.csv`）:
- `P=0.01`: `center4_mean=0.00882353 (=2.25/255)`, `max_alpha=0.02745098 (=7/255)`, `nonzero_count=227`

### 5) Skiaは Pを 1e-6 まで下げても非ゼロが残り、低Pで張り付く（下限/クランプ疑い）
`Sample/Compair/Skia/skia-pre-save-alpha-summary-down-to-1e-6.csv` では、Skiaは `P=0.001` 以下で `center4_mean` / `max_alpha` / `nonzero_count` がほぼ一定になり、0へ収束しない。

例（`S=100, N=1`）:
- `P=0.001`: `center4_mean=0.00784314 (=2/255)`, `max_alpha=0.02352941 (=6/255)`, `nonzero_count=220`
- `P=0.000001`: 上記と同一

→ UWPの「完全0になる床」とは別種で、Skia側に最小値/クランプ/量子化の下限が入っている可能性が高い。

### 6) Skiaは「そもそも濃すぎる」（ゲイン過大 + 低Pで薄くならない）
ここでいう「濃すぎる」は、見た目の印象ではなく保存前統計（`center4_mean` / `max_alpha`）の差として観測できる。

主に次の2点が合わさって差分になっている可能性が高い。

1) ゲイン過大（同じPでもSkiaの出力αが強い）
- UWPは `P=0.0105` で初めて `max_alpha=1/255`（立ち上がり）
- Skiaは `P=0.01` の時点で `max_alpha=7/255`（例: `S=100,N=1`）

2) 低Pで薄くならない（0へ近づかず段階値に張り付く）
- Skiaは `P=0.001` から `P=1e-6` まで `max_alpha=6/255`（例: `S=100,N=1`）のように、Pを下げても薄くならない

このため、Skia側にP床を入れて「P<=0.0104を完全0」に揃えた後も、床の上（`P>=0.0105`）で「立ち上がり直後から濃い」差が残る可能性がある。

### 7) Skia側の `nonzero_ratio_circle` の仕様修正が入った
Skia側は当初 `nonzero_ratio_circle` の分母が全体ピクセル数になっていたが、円内ピクセル数で割るよう修正された。

- 修正前: `nonzero_count_circle / (512*512)`
- 修正後: `nonzero_count_circle / circlePixelCount`

これにより、`nonzero_ratio_circle` は全体比より大きい値になる（分母が小さいため）。

## ここから言えること（結論）
- UWPとSkiaの差の主要因の1つは、UWPの **P床（P<=0.0104で完全0）**。
- Skiaは低Pで0に落ちないため、UWPと同等にするなら **Skia側にP床（無描画しきい値）を導入するのが最短**。

## 方針（意思決定ルール）: UWP再現のための限定的な特例導入
本調査は「見た目/モデルの連続性」そのものよりも、まず **UWP（Win2D）の観測値を再現すること**を優先目標とする。

ただし、UWPは `P床` や `8bit量子化` など **不連続を含む挙動**を持つため、「UWPに数値を合わせること」と「連続で滑らかなモデルにすること」は同値ではない。
このため本件では、UWP再現の根拠が取れる範囲で **限定的な特例（クランプ、低圧域のみ係数変更等）を許容**する。

特例を追加する場合は、以下をセットで記録・確認する。

### 1) 根拠（Evidence）
- どのCSV（pre-save / diagnostics）で、どの列（例: `alpha_mul_clamped` 固定、`remain_last_max` 低下等）により原因を確定したか
- 可能であれば数式で説明できる形（例: `alpha_candidate_max = 255 * alpha_mul * aeff_max`）で残す

### 2) 適用範囲（Scope）
- 適用条件を明確化する（例: `Clamp01(pressure) <= 0.02` のみ）
- 影響範囲を最小化する（全域変更ではなく、原因が観測された領域に限定する）

### 3) 受け入れ基準（Acceptance）
- UWP側CSVで観測された「立ち上がり点」や段階値（例: `P=0.0105, N=1 -> 1/255`）に一致（または十分近似）すること
- N回重ねの挙動は diagnostics 側（同座標にN回スタンプ等）で原因が可視化できること

### 4) 副作用チェック（Regression）
- 近傍圧力（例: `P=0.0106+`）でも同種の問題が再発しないか（量子化境界を跨いだ段差が過剰になっていないか）
- `N` を変えたときに単調性が破れていないか（例: `N=2` が `N=1` より薄くならない）

## Skia diagnostics を使った「線形に見えない」原因の数式切り分け
ここでは Skia 側の diagnostics CSV（例: `alpha_candidate_max`, `remain_last_max` など）を用い、
「なぜ N 回スタンプしても線形に増えない/増え方が段になるのか」を原因別に切り分ける。

前提: `LegacyPngRenderer` 実装（概略）
- 1スタンプのピクセル寄与に相当する値（最大点側）は概ね次で表せる:
  - `alpha_candidate ≒ color_alpha * alpha_mul * aEff`
  - `alpha_byte = Round(alpha_candidate)`（※ここで8bitの段が発生）
- `aEff = aBase * remain`
  - `aBase = falloff * noise`
  - `remain = 1 - cov`
- coverage 更新（過剰累積の抑制）:
  - `cov <- cov + aEff * gain`（clip 0..1）

### 1) 「Nの増え方」が段になる主因: 8bit量子化
diagnostics の `alpha_candidate_max` が `0.5` や `1.5` の近傍にあると、
わずかな係数差（pressureやremainの変化）で `alpha_byte` が `0→1` / `1→2` のように飛ぶ。

実務上は次の境界だけを見ればよい:
- `Round` で 1 を得る境界: `alpha_candidate >= 0.5`
- `Round` で 2 を得る境界: `alpha_candidate >= 1.5`

### 2) 「Nを増やすと増えない/薄くなる」主因: coverage(remain) による2回目以降の減衰
同一点（またはほぼ同一点）への反復スタンプでは、2回目以降は `remain` が下がるため、
`alpha_candidate_last_max`（最後の1回の最大寄与）が小さくなる。

diagnostics で次を確認する:
- `alpha_candidate_last_max ≒ alpha_candidate_max * (aEff_last_max / aEff_max)`
- `aEff_last_max ≒ aBase_max * remain_last_at_max`

例: `alpha_candidate_max` が 0.50（=ギリギリ1段目）だと、
2回目以降で `remain` が少し下がるだけで `alpha_candidate_last_max < 0.5` となり、
「最後の回の寄与が0（Roundで0）」になり得る。
（※最終画像が薄くなるかどうかは別で、これは“最後の回が効かない”ことの診断）

### 3) 原因別の見分け方（チェックリスト）
1. **`aBase_max` がPで急変していないか**
   - `falloff_max` / `noise_max` / `abase_max` が一定なら、形状/紙目が原因ではない可能性が高い
2. **`alpha_mul_clamped` が固定されていないか**
   - 固定（下限張り付き）なら、pressureを変えても `alpha_candidate_max` が動きにくい
3. **`alpha_candidate_max` が 0.5/1.5 境界の近傍にいないか**
   - 近い場合、PやNで段差が出るのは自然（量子化起因）
4. **`remain_last_max` / `aEff_last_max` が N 増加で落ちていないか**
   - 落ちる場合、原因は coverage による減衰（2回目以降が効きにくい）
5. **UWPの観測と一致するか**
   - 例: UWPが `N=1/2/3` で `max_alpha=1/2/3 /255` と線形に増えるなら、
     Skia側も「同一点反復で2回目以降が0になる」挙動は parity 観点で要修正

## 決定木（観測 → 原因 → 推奨アクション）
ここでは diagnostics CSV と pre-save 統計CSVの観測値を入力として、
Parity（UWP観測値の再現）を優先しつつ「最小スコープの特例/調整」を選択するための決定木を示す。

### 入力（最低限）
- pre-save（UWP/Skia）: `S,P,N,max_alpha,nonzero_count,center4_mean`
- Skia diagnostics（必要に応じて N回同座標スタンプ版）:
  - 係数: `alpha_mul_raw`,`alpha_mul_clamped`,`darkness_scale`
  - 形状: `radius_px`,`step_px`
  - 基底: `falloff_max`,`noise_max`,`abase_max`
  - coverage: `remain_max`,`remain_last_max`,`aeff_max`,`aeff_last_max`
  - 量子化: `alpha_candidate_max`,`alpha_candidate_last_max`,`alpha_byte_*_pred`

### 0. 前提確認（Parityの定義）
1. UWP側で同条件（S,P,N）の観測が取れているか
2. Skia側が同条件（S,P,N,ノイズ有無等）で比較できているか

### 1. P床（無描画しきい値）の分岐
- 観測: UWPで `P<=0.0104` が完全0
  - Skiaで `P<=0.0104` が非ゼロ → **アクション**: Skia入口で `P<=0.0104` を無描画
  - Skiaで0一致 → 次へ

### 2. N=1 立ち上がり（0→1段）
目的: `P=0.0105,N=1` で `max_alpha=1/255` に到達すること

- 観測: Skia `P=0.0105,N=1` が0のまま
  - diagnostics: `alpha_candidate_max < 0.5` → **原因**: 量子化境界未達
    - **アクション（優先順）**
      1) 低圧域のみ `alpha_mul_clamped` 下限を緩める/撤廃（張り付きがある場合）
      2) 低圧域のみ `PressureToPencilDarknessScale`（lowScale）を調整
      3) 低圧域のみ `ComputePencilAlphaMultiplier` 側を調整（波及注意）

- 観測: Skia `P=0.0105,N=1` が過大（例: 7/255）
  - diagnostics: `alpha_mul_clamped` が下限固定、または `alpha_candidate_max >> 1.5` → **原因**: ゲイン過大/下限張り付き
    - **アクション**: clamp下限や低圧域ゲイン（darkness/alphaMul）を減らす（範囲限定）

### 3. N=2,3… が線形に増えない（同一点反復の問題）
目的: UWP観測が線形（例: `N=1/2/3 -> 1/2/3 /255`）なら、それに合わせる

- 観測: Skia最終 `max_alpha` が `N` に比例して増えない/頭打ち
  - diagnostics（N回同座標版）で `alpha_candidate_last_max` を確認
    - `alpha_candidate_last_max < 0.5` で last が0予測
      - `aeff_last_max << aeff_max` または `remain_last_max` が目立って低下 → **原因**: coverage(remain) 減衰が支配
        - **アクション候補（波及が小さい順）**
          1) 低圧域のみ coverage更新 `gain` を下げる（UWPが線形増加なら、同一点反復でremainが潰れないようにする）
          2) 低圧域のみ `CoverageDecay` を調整（効果は場面依存）
          3) 同一点反復（stationary）に限定して coverage更新を弱める/遅延（狙い撃ち、Parity根拠がある場合のみ）
      - `remain_last_max ≈ 1` なのに `alpha_candidate_last_max` が落ちる
        - **注意**: `remain_last_max` は「最後の回でremainが最大の画素」を拾うため、中心の潰れを見落とす。
          この場合は **center4等の局所指標**を diagnostics に追加して判断する。

    - `alpha_candidate_last_max >= 0.5` だが `>=1.5` に届かない
      - **原因**: 2段目（2/255）境界に未達（量子化境界）
      - **アクション**: 1回目の余裕（`alpha_candidate_max`）を上げるか、coverage減衰を弱める（どちらが原因かは `aeff_last_max/aeff_max` で判定）

### 4. 回帰チェック（必須）
特例/調整の後は必ず次を確認する:
- 近傍圧力（例: `P=0.0106+`）で段差が過剰になっていないか
- `N` を増やしても最終結果（pre-saveの `max_alpha` / `center4_mean`）が単調に悪化しないか
- UWP側の観測（線形/頭打ち/非線形）と合っているか

## Skia側で差分を埋めるために必要になり得る追加事項（TODO）

### A) P床（無描画しきい値）の導入（最優先）
- 目的: `P<=0.0104` をUWP同様に完全0（描画しない）にする
- 論点: `<=0.0104` とするか、`<0.0105` とするか（境界条件）
- 推奨: まずは実測どおり `P<=0.0104` を0に合わせ、差分が残る場合に微調整

### B) 低Pで張り付く「最小α」相当の除去/調整
- 目的: Pを下げたときにSkiaが0へ近づく（またはUWP床に合わせて0になる）
- 観測: `P<=0.001` でも `max_alpha=6/255` 等に張り付く
- 可能性: pressure/alphaのクランプ、あるいは量子化の下限が実装に存在
- 受け入れ基準: P床導入後、`P<=0.0104` は完全0（UWP一致）であることに加え、床の上で `P` を下げたときに `max_alpha` が不自然に一定値へ張り付かず、UWPの統計に整合すること

### C) P→α変換のスケール合わせ
- 目的: `P>=0.0105` の領域で、`max_alpha` や `center4_mean` の段階値と増え方をUWPに近づける
- 観測: Skiaは `P=0.01` 時点で `max_alpha=7/255` と、UWPより大きい
- 受け入れ基準: 同じ(S,P,N)で、Skiaの `max_alpha` / `center4_mean` がUWPの値に近づくこと（まずは `P=0.0105〜0.02` の範囲で確認）

### D) N回重ねの合成規則の一致
- 目的: N=2/10/100での中心αや分布差を縮める
- 観測: 低P域では倍率的に増えているが、SrcOver等の非線形合成との差は今後要確認

### E) 円判定の座標系の一致（微差対策）
- 目的: 円内集計や中心4pxの取り方など、サブピクセル中心（255.5）と整数参照のズレを排除
- 指摘事項: 「円判定中心」「描画点」「GetPixel参照座標」がズレると円内/円外判定がぶれる

## 次の検証手順（推奨）
1. SkiaにP床を入れた版で、同じ `P=0.0100〜0.0200`（0.0001刻み）を再出力する
2. `Sample/Compair/uwp-pre-save-alpha-summary.csv` と突合し、
   - `P<=0.0104` の完全0一致
   - `P=0.0105` 以降の `max_alpha` 立ち上がり
   - `center4_mean` の最初の非ゼロ点
   - （濃度/ゲイン確認）`P=0.0105〜0.0200` の範囲で、同じ(S,P,N)に対する Skia の `max_alpha` / `center4_mean` がUWPに近い段階値になっていること（例: 立ち上がり直後に7/255などの過大値になっていないこと）
   - （濃度/ゲイン確認）`P` を下げたときに Skia の `max_alpha` が不自然に一定値へ張り付かないこと（P床の上での挙動がUWPと整合すること）
   を確認する
