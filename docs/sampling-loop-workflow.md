# サンプリング作業ループ（StrokeSampler / DotLab）

目的: UWPの `PencilStroke` 再現に必要な「入力点列 → 描画結果」の関係を、変化要素を増やしながら段階的に観測する。

このドキュメントは「1回の作業ループで何を設定し、何を出力し、何を比較して、次の条件をどう決めるか」を固定化する。

## 前提（確定事項）
- HiResレンダ経路（Win2D `CanvasRenderTarget` + `DrawInk`）の累積合成は **BGRA8（8bit）上の source-over** と見なしてよい。

## 主要な出力物
### StrokeSampler（UWP）
- ストローク描画（InkCanvas）
- HiRes PNG出力（canvas/laststroke）
- InkPointsDump JSON（自動保存）
  - 保存先: `ApplicationData.Current.LocalFolder/InkPointsDump`
  - ファイル名: `stroke_yyyyMMdd-HHmmssfff_..._points.json`
  - `timestanp` は点ごとに固定刻み（既定 4ms）で増加

### DotLab（WPF）
- `Export InkPointsDump Stats (dd/dt CSV)`
  - dumpフォルダを選んで、strokeごとの dd/dt/Δp/Δtilt/short-dt 相関をCSV化
- `Export Alpha Diff (Canvas vs Sim)`
  - 実測PNG vs sim PNG の **αの絶対差** PNG と統計CSVを出力

## 基本ループ（毎回共通）
1. **条件を決める**（S/P/N/step など）
2. StrokeSamplerで **キャンバスをクリア**
3. StrokeSamplerで **ストローク生成**（指定条件）
4. StrokeSamplerで **HiRes出力**（必要なら canvas と laststroke 両方）
5. DotLabで **InkPointsDump解析CSV**を出力
6. DotLabで **α差分**を出力（必要な組だけ）
7. 結果を見て **次の条件を決める**

## 段階別のおすすめ手順（変化要素を増やす順）

### Stage 0: 準備（出力先の把握）
- StrokeSamplerの `LocalFolder/InkPointsDump` の場所を確認する
  - Visual Studioの「ローカルアプリデータ」やデバッグ出力等で確認
- DotLabの2つの機能が動くことを確認する
  - dd/dt CSV が生成される
  - α差分 PNG/CSV が生成される

### Stage 1: 時間（点数）だけを変える（同一座標）
目的: 「同一点でInkPoint数が増える（時間が長い）」ことが描画結果にどう効くかを確認する。

StrokeSampler設定:
- `Dot512 Size` = S を固定（例: 200）
- `Dot512 Pressure` = P を固定（例: 1.0、次に 0.1）
- `Start(X,Y)` を固定
- `LinePts` だけを変える（例: 2 / 3 / 5 / 10 / 50 / 100）

操作:
1. `Draw Hold (Fixed)` を押す（同一座標の点列でストローク生成 + dump自動保存）
2. `Export HiRes PNG (Cropped+Transparent)` を出力（canvas）
   - 必要なら `Export HiRes LastStroke (Cropped+Transparent)` も出力
3. DotLabで dumpフォルダを選び、dd/dt CSV を出力

見るポイント:
- DotLab dd/dt:
  - `dd_zero_ratio` が ほぼ 1.0 になること（同一点）
  - `dt_mode` が 4 付近で安定すること（制御系列のベースクロック）
- HiRes PNG:
  - 点数Nを増やしたときに濃度が増えるか（完全に一致する/増える/頭打ち）

次に進む方向:
- Nを増やして濃度が増える → 同一点の点列も描画に寄与（累積の基本は成立）
- Nを増やしてほぼ変わらない/途中で頭打ち → 同一点は内部で統合/間引きされる可能性

### Stage 2: 距離（点間隔）を入れる（直線）
目的: 点間隔が大きいときに「線が埋まるか/途切れるか」を観測し、補間の有無を推定する。

#### 重要: L（総移動距離）で条件を管理する
Stage 2 の境界探索では、`LineStep(px)` 単体ではなく **総移動距離**

`L = LineStep(px) * (LinePts - 1)`

が支配的なケースがある（S=200,P=0.5 の検証で、描画の出始めが L?18px で揃うことを確認）。
以後の探索は、可能なら **L を固定して** step と pts の組を変えつつ比較する。

確定事項（S=200, P=0.5 / 横方向の制御系列）:
- 判定は `LineStep`/`LinePts` 個別ではなく `L=step*(pts-1)` に強く支配される（`pts=18` でも同判定を確認）
- 描画が「出ない→出る」に切り替わる閾値は `L0 ? 18.0000 ± 0.0001`
  - 区間同定: `L0 ∈ (17.9998952, 18.0000952]`

追加の確定事項（Stage 2 / 直線・等圧・等速の制御系列）:
- **18px周期**で「有効な更新点（スタンプ/セグメントの追加）」が発生するように見える。
  - 18px周期の根拠: `S=18` のDotを描画して周期を測定。
- 入力点密度（`LinePts`）や `LineStep` を変えても、`L` が同一であれば **同一の線**になるケースが確認できた。
  - 例: `P=0.5, step=100, L=1700` で `LinePts=18` と `LinePts=171` が同一
  - 例: `P=0.5, LinePts=39/20, step=25/50, L=950` が同一

周期のスケール換算（補足）:
- HiRes出力で `scale` を変えても、周期をDIP換算した値が揃うことを確認。
  - `scale=8` で `period_px=14` → `period_dip=1.75`
  - `scale=12` で `period_px=21` → `period_dip=1.75`
  - よって、周期は「HiRes上で固定18px」ではなく、**DIP基準で約 1.75** の可能性が高い（scale10では 17.5px 相当のため 18px に見える）。

追加観測（周期のS依存の可能性）:
- Pを変えても周期は変わらない（少なくとも検証範囲ではP非依存）。
- `S=100` では、`period_px=scale`（例: scale8→8px, scale10→10px, scale12→12px）で揃い、`period_dip=period_px/scale=1.0` となった。
  - これは操作として正しく、「周期がDIP基準で一定（1.0DIP）」という観測結果を意味する。
  - よって周期はSに依存して変わる可能性がある（S200で約1.75DIP、S100で約1.0DIP）。

追加観測（小さいS）:
- `S=120`: `period_dip=1.0`
- `S=80`: `period_dip?0.75`（scale10では丸めにより 0.8 寄りになり得る）
- `S=40`: `period_dip=0.5`
- `S=30`: `period_dip?0.25`（scale10では丸めにより 0.3 寄りになり得る）

追加観測（S=150）:
- `S=150` では scale によって最適な `period_px` が 10/12/15 となり、`period_dip=period_px/scale` は概ね **1.2?1.25** に入った。
  - scale8: `period_px=10` → `period_dip=1.25`
  - scale10: `period_px=12` → `period_dip=1.2`
  - scale12: `period_px=15` → `period_dip=1.25`
  - scale10だけ 1.2 になっているため、周期は「厳密な連続値」ではなく、内部でpx/DIPの丸めや量子化が入っている可能性がある。

追加観測（S=180）:
- `S=180` では `period_px=12/15/18 (scale=8/10/12)` が一致し、`period_dip=period_px/scale=1.5` に揃った。

StrokeSampler設定:
- S/P は Stage 1 と同じに固定
- Start/End を水平直線に固定
- `LinePts` 固定、`LineStep(px)` だけを変える
  - 例: step=1/2/4/8/16/32

操作（推奨: 閾値探索ループ）:
1. **環境を固定する**
   - S/P/Start/End/LinePts（点数）は固定
   - 比較用の出力は **`laststroke` を推奨**（キャンバス残留の影響を排除するため）
     - `Export HiRes LastStroke (Cropped+Transparent)`
2. `LineStep(px)` だけを変えて、以下を繰り返す
   1) `Draw Line (Fixed)`（直線点列 + dump自動保存）
   2) `Export HiRes LastStroke (Cropped+Transparent)` を出力
   3) DotLabで `Export InkPointsDump Stats (dd/dt CSV)` を出力（必要ならまとめて最後でも可）
   4) DotLabで差分（α差分）を作る
      - まずは **実測同士**（例: step=0.1 と step=0.2）の比較で「差が0ではなくなる境界」を探す
      - simとの比較は、境界が見えてからでよい

推奨の探索ステップ（2倍刻み → 境界詰め）:
- 粗探索（2倍刻み）: `0.1 → 0.2 → 0.4 → 0.8 → 1.6 → 3.2 → 6.4 → 12.8`
- 差が出始めたら、その前後だけ細かくして境界を詰める（例: `0.8/1.0/1.2`）

記録（最低限）:
- `S,P,LinePts,LineStep,Start/End,L=step*(pts-1)`
- 出力ファイル名（dump/PNG）
- 目視所見（途切れ/埋まり/濃度など）
- DotLab差分の統計CSV（mean/stddev/unique）

見るポイント:
- step を大きくしても線が埋まる → 補間（点増し/連続形状化）の可能性
- step を大きくすると途切れが出る → 離散点列の影響が強い

補足:
- `LinePts`（点数）を変える実験は Stage 1（Hold）で先に済ませ、Stage 2 では原則固定する。
- もし `LineStep` を小さくしても結果が全く変わらない場合、量子化/統合が強い可能性があるので、次の段階で `LineStep` をさらに大きくしていく。

#### L固定で試す組み合わせ（例）
以下は、L を固定して `LineStep` / `LinePts` を組み替えるためのテンプレ。

使い方:
- まず目標の L を決める（例: 16 / 17.5 / 18 / 18.5 / 20 / 24）
- 表から `(LineStep, LinePts)` を選んで実行する
- 可能であれば、同一Lで複数組を試して「結果が一致するか」を確認する

目標L=18px（境界付近）:
- step=0.50, pts=37   （0.50*(37-1)=18.0）
- step=0.45, pts=41   （0.45*(41-1)=18.0）
- step=0.40, pts=46   （0.40*(46-1)=18.0）
- step=0.375, pts=49  （0.375*(49-1)=18.0）
- step=0.36, pts=51   （0.36*(51-1)=18.0）
- step=0.30, pts=61   （0.30*(61-1)=18.0）

目標L=17.5px（境界の手前）:
- step=0.50, pts=36   （0.50*(36-1)=17.5）
- step=0.35, pts=51   （0.35*(51-1)=17.5）
- step=0.25, pts=71   （0.25*(71-1)=17.5）

目標L=18.5px（境界の先）:
- step=0.50, pts=38   （0.50*(38-1)=18.5）
- step=0.37, pts=51   （0.37*(51-1)=18.5）
- step=0.25, pts=75   （0.25*(75-1)=18.5）

目標L=24px（十分先・挙動確認用）:
- step=0.50, pts=49   （0.50*(49-1)=24.0）
- step=0.40, pts=61   （0.40*(61-1)=24.0）
- step=0.30, pts=81   （0.30*(81-1)=24.0）

### Stage 3: 圧力（P）を変える（Hold/Lineのどちらかを固定）
目的: 点列の幾何（Hold/Line）を固定したまま、Pだけで描画がどう変わるかを観測する。

推奨:
- まず Hold（同一点）でPを振る（例: 0.05/0.1/0.25/0.5/1.0）
- 次に Line（直線）でPを振る

見るポイント:
- Pが低い領域で add/source-over が近似的に振る舞う（過去の観測と整合するか）
- Pを上げたときの飽和・頭打ちがsource-overの期待通りか

### Stage 4: 傾き（tilt）を入れる（必要になったら）
目的: tiltが点列生成（サンプリング）や描画結果に寄与しているかを確認する。

注意:
- `InkPoint.TiltX/TiltY` は -90..+90 度（Microsoft Learn）。
- 制御系列（Hold/Line）にも tilt を入れて比較できるようにするのが理想だが、必要になった段階で機能追加する。

## 記録テンプレ（おすすめ）
各サンプルについて、最低限これだけ残すと後で追えます。

- 条件: `S=?, P=?, Hold/Line, LinePts=?, LineStep=?, Start/End=?`
- 出力:
  - dump: `stroke_..._points.json`
  - png: `pencil-highres-...-canvas-...png`（必要なら laststroke）
  - dotlab: `inkpointsdump-dd-dt-stats-....csv`
  - diff: `alpha-diff-...png/csv`（必要な組だけ）
- 観測メモ:
  - 見た目: 途切れ/埋まり/濃度増分/頭打ち
  - dd/dt: `dt_mode`, `dt_mode_ratio`, `dd_zero_ratio`, `short_dt_ratio` の所感
