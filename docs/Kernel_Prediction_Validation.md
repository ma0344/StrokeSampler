# Kernel Prediction Validation

## 記録方針
- ユーザーのプロンプトは原則として逐語記録する。
- Copilot の回答は要約記録とする。
- 大量データ貼り付けや反復的なコンテキストは、検証に不要な範囲を省略する。

## セッション記録

### 1. 予測比較CSVの実装依頼
**User**
> 現在、0.05-0.95のカーネル断面CSV（紙目Tile）を0.1刻みで取得しています。
> その後、detail/summaryを作成する予定ですが、予測値と観測値を比較するための仕組み（予測値の作成と、観測値と並べたCSVの出力）を実装してください。

**Copilot 要約**
- `InkDrawGen` に予測比較CSV出力機能を追加する方針を提示した。
- `wide` 集約CSVから観測指標を抽出し、leave-one-out の線形補間で予測値を作る構成で進めた。

### 2. 実装継続の依頼
**User**
> 処理を継続してください。

**Copilot 要約**
- `KernelSweepExportService` に予測比較CSV出力処理を追加した。
- `MainPage.xaml` に `カーネル断面CSV(予測比較)` ボタンを追加した。
- `MainPage.xaml.cs` に1行委譲のクリックハンドラを追加した。
- `docs/copilot-session-summary.md` に作業要約を追記した。
- ワークスペースをビルドし、成功を確認した。

### 3. instruction 更新確認の依頼
**User**
> .github\copilot-instructions.md を修正しました。変更を確認してください。

**Copilot 要約**
- `.github/copilot-instructions.md` の変更を確認した。
- `.github/copilot-instructions.md` を優先参照すること、`kernel` 再現では `P` と相関する値の定量化を優先すること、8bit量子化前提で誤差評価することを理解した。

### 4. 2.0 全般への追加項目の確認
**User**
> 2. 行動原則 の 2.0 全般に以下の項目を追加したのですが、可能ですか？
> - ユーザーとのチャットセッションをMD形式でそのままドキュメントに追記する運用をする（例: `docs/copilot-session-summary.md`）。
> - チャットセッション記録用のMDドキュメントが存在しない場合は、ソリューションのルートから以下の相対パスに `docs/{セッションタイトル}.md`(例. セッションタイトル「InkCanvas Reproduction」/ ファイル名「InkCanvas_Reproduction.md」) を新規作成して追記すること。

**Copilot 要約**
- 上記2項目は運用ルールとして成立すると回答した。
- 記録用MDファイルは `docs/{セッションタイトル}.md` で作成可能と回答した。

### 5. 記録方針の補足
**User**
> 追加項目の
> - ユーザーとのチャットセッションをMD形式でそのままドキュメントに追記する運用をする（例: `docs/copilot-session-summary.md`）。
> は「（例: `docs/copilot-session-summary.md`）」の部分を削除し
> - ユーザーとのチャットセッションをMD形式でそのままドキュメントに追記する運用をする。
> と変更しました。
> 会話の検証に使用する目的なので、Copilotの回答に関しては完全逐語記録ではなく、要約記録でも問題ありません。ユーザーのプロンプトは完全逐語記録でお願いします。
> また、ユーザーのプロンプトに大量のデータの貼り付け等が含まれる場合は略してください。

**Copilot 要約**
- ユーザーのプロンプトは逐語記録、Copilot回答は要約記録、大量データは省略可という運用で今後扱うことを確認した。

### 6. 会話記録用MDファイルの作成依頼
**User**
> 会話記録用のMDファイルを作成してください。

**Copilot 要約**
- セッションタイトルの指定が必要と回答した。

### 7. セッションタイトル指定
**User**
> docs/Kernel_Prediction_Validation.md でお願いします。

**Copilot 要約**
- 本ファイル `docs/Kernel_Prediction_Validation.md` を作成し、現在の会話記録を追記した。

### 8. count20 の比較CSVと summary の分析依頼
**User**
> 最初のスイープ結果も含めて比較CSVを出力しました。検証結果は同じだとは思いますが、こちらのデータの方がサンプル数が多いので、分析メモを追記する際にはこちらのデータの分析結果を追加してください。

**Copilot 要約**
- 今後の分析メモは `DotLab/Kernel/KernelSweep/kernel-sweep-wide-count20-stair-prediction-compare.csv` を優先根拠にすると整理した。
- `count20` により「勾配系は強い・横位置系は弱い・低Pは特例」という前回の結論がより明確になったと整理した。

### 9. グラフ形状からの新たな発見と会話記録追記の依頼
**User**
> 添付のイメージは [C:\Users\ma\source\repos\StrokeSampler\DotLab\Kernel\KernelSweep\kernel-sweep-wide-count20-stair-summary.csv](#c:\users\ma\source\repos\strokesampler\dotlab\kernel\kernelsweep\kernel-sweep-wide-count20-stair-summary.csv-context)  をグラフ化したものですが、グラフ形状から何か新たな発見はありませんか？
> mean_tread_px median_tread_px などはPとかなり強く関連づいているようです。
> major_change_index(P)の低P帯は使える「段差」自体が少ないため、現在使用している算出根拠自体が使用できないのかもしれないとも思います。
> もしそうなら、段差がいくつからなら計算が成り立つのかが「特殊性」の閾値になると思います。
> front_slope24 に 23 の値も入れられますか？
>
> 会話記録用のMDファイルへの追記が行われていないようなので、追記も並行してお願いします。

**Copilot 要約**
- `count20-stair-summary.csv` から、`plateau_count ≈ P / mean_riser01` がかなり強く成立していること、`median_tread_px` が `mean_tread_px` より頑健な横方向代表値候補であること、`max_tread_px` が低P特例の強さを表す候補であることを整理した。
- `last_nonzero_r_px` は `P0.75` から観測限界 `8000` を下回り始めるため、終端 regime の境界は `P≈0.75` 付近とみなせると整理した。
- 強変化点の算出根拠は形式上 `plateau_count >= 3` で成立するが、法則として安定に使うには少なくとも `plateau_count >= 8`（概ね `P>=0.2`）が必要そうだと整理した。
- `front_slope24` に加えて `front_slope23` を観測/予測列として追加することは可能であり、初期局所勾配の補助指標として有効と回答した。
- あわせて、`docs/copilot-session-summary.md` と本ファイルへ今回の分析内容を追記した。

### 10. `front_slope23` 追加の実装依頼
**User**
> front_slope23 を予測比較CSVへ実装追加してください。

**Copilot 要約**
- `KernelSweepExportService` に `front_slope23` の観測抽出・leave-one-out 予測補間・比較CSV列出力を追加した。
- 比較CSVへ `obs_front_slope23` / `pred_front_slope23` / `err_front_slope23` を追加し、既存の `front_slope24` は主列のまま維持した。
- `docs/copilot-session-summary.md` に実装要約を追記した。

### 11. `front_slope23` を含む観測の解釈と関節モデルの相談
**User**
> 23も加えたサマリーを添付します。高P帯ではわずかに差が縮まりますが、中低帯では24の方が良いようです。
> 中間帯の変化曲線の法則がもっと見えると、全体が改善しそうですね。
> 添付のイメージは以前も共有したと思いますが、P0.1～1 step 0.1 の kernel曲線をkernel01_P*でまとめたものです。これを見ていると、P1からP5に向かって 曲線がなだらかになり、さらに0.1に向かって根元から持ち上がる感じの変化ですね。
> 物理的な感覚で表すと、高P=しなやか 低P=堅い というイメージですね。

**Copilot 要約**
- `front_slope23` は高P帯の局所接線に近く、`front_slope24` は中低P帯の安定代表勾配として良い、と整理した。
- 全体形状は「共通骨格 + 根元持ち上がり + 中間のしなやかさ」で捉えるのが自然であり、低P=堅い、高P=しなやかという物理イメージはかなり本質に近いと回答した。
- 次の本丸は中間帯の曲がり方を表す1指標を持つことだと整理した。

### 12. 指標セット提案の依頼
**User**
> 提案をお願いします。
> plateau_count を 「可動域に上限のある関節」 とイメージすると、関節数が多いほどしなやかになるということでしょうか？

**Copilot 要約**
- `plateau_count` を関節数とみなす比喩はかなり自然であり、高Pは「関節数が多く、1関節あたりの動きが小さい」のでしなやか、低Pはその逆で堅いと整理した。
- あわせて `mean_riser01/P` を1関節あたりの可動量、`median_tread_px` を関節間の代表長、`initial_tread/max_tread` を根元ロック量としてみなす考え方を提示した。

### 13. 指標セットの説明と提案依頼
**User**
> 指標セットの説明と提案をお願いします。

**Copilot 要約**
- 関節モデルとして、まず次の基本5指標を持つ案を提示した。
  - `joint_count = plateau_count`
  - `joint_step = mean_riser01 / P`
  - `joint_span = median_tread_px`
  - `root_lock = initial_tread_px / median_tread_px`
  - `terminal_headroom = 100 - last_nonzero_r_norm`
- 中間帯改善用の補助指標として、`mid_flex_ratio` と `curvature_budget` を候補に挙げた。
- 法則化の優先順は `joint_count -> joint_step -> joint_span -> root_lock -> terminal_headroom` とし、その後に中間帯指標を足す構成が自然と整理した。

### 14. ドキュメント追記運用の依頼
**User**
> [C:\Users\ma\source\repos\StrokeSampler\docs\Kernel_Prediction_Validation.md](#c:\users\ma\source\repos\strokesampler\docs\kernel_prediction_validation.md-context) には指示がなくても逐次追記をしてください。
> #file:'C:\Users\ma\source\repos\StrokeSampler\docs\copilot-session-summary.md' には指標セットと説明を追加してください。

**Copilot 要約**
- `docs/Kernel_Prediction_Validation.md` へは今後も逐次追記する運用で対応することを確認した。
- `docs/copilot-session-summary.md` へ関節モデルの指標セットと説明を追加する方針を示した。

### 15. ドキュメント追記の実施依頼
**User**
> 追記および追加をお願いします。

**Copilot 要約**
- `docs/copilot-session-summary.md` に関節モデルの指標セットと説明を追記した。
- 本ファイルへ今回の会話記録を逐次追記した。

### 16. 指標算出元ファイルと compare CSV への落とし込み確認
**User**
> 取り込むファイルは [C:\Users\ma\source\repos\StrokeSampler\DotLab\Kernel\KernelSweep\kernel-sweep-wide-count20-stair-detail.csv](#c:\users\ma\source\repos\strokesampler\dotlab\kernel\kernelsweep\kernel-sweep-wide-count20-stair-detail.csv-context) ですよね？そして、算出した値をcompare CSVにどう落とし込むかということですね。具体化してください。

**Copilot 要約**
- 指標算出の主入力は `kernel-sweep-wide-count20-stair-detail.csv` でよいが、`plateau_count` / `median_tread_px` / `last_nonzero_r_norm` などは `stair-summary.csv` を併用する方が安全と整理した。
- compare CSV は 1行1P を維持し、`obs_* / pred_* / err_*` の3列組で `joint_count` / `joint_step` / `joint_span` / `root_lock` / `terminal_headroom` などを追加する方針を具体化した。

### 17. compare CSV への関節モデル指標追加の実装依頼
**User**
> ではその案で実装を開始してください。

**Copilot 要約**
- `KernelSweepExportService` を修正し、`カーネル断面CSV(予測比較)` で `*-stair-detail.csv` を選択した場合に sibling の `*-stair-summary.csv` を自動解決して compare CSV を生成できるようにした。
- `stair-detail.csv` から plateau を復元し、`stair-summary.csv` と併用して `joint_count` / `joint_step` / `joint_span` / `root_lock` / `root_lock_alt` / `terminal_headroom` / `curvature_budget` を観測指標として追加した。
- compare CSV へ各指標の `obs_* / pred_* / err_*` 列を末尾追加し、既存列順は維持した。

### 18. 実測定量化の意味の確認
**User**
> いま行っている実測を定量化する作業は、要するに「ベクトルの変化を数式化する」作業という理解であっていますか？

**Copilot 要約**
- 大筋では近いが、より正確には「kernel 形状の変化を、P に従って動く複数の観測指標ベクトルとして分解し、それぞれを数式・テーブル・区分法則へ落とす作業」と整理した。
- 単純な幾何ベクトル場そのものより、`riser`・`tread`・`major_change`・`tail`・`terminal` などの成分ごとの変形法則を作っている、という説明を行った。

### 19. 勾配ベクトルを共通尺度で並べる案の確認
**User**
> 添付は新たに出力された比較結果のCSVです。
> 実測からどの基準でベクトルを抽出するかが重要だと思うのですが、例えば、２段目以降の格段における変化勾配のベクトルを何かを基準にして同じ尺度で並べると良いような気がしますがどうでしょうか？それはいま行っていること？

**Copilot 要約**
- その発想は有効であり、特に中間帯の法則抽出には「2段目以降の局所勾配列を共通尺度へ正規化して並べる」方法がかなり有望と整理した。
- ただし現在の compare CSV で行っているのは `front_slope23/24` や `tail_slope` など一部の局所勾配を代表値として抜き出している段階であり、2段目以降の勾配ベクトル列全体を共通尺度へ並べているわけではないと説明した。
- 今後の具体案として、`plateau_index`、累積相対落差、強変化点/終端基準の正規化座標などを共通軸にして勾配列を再サンプリングし、`P -> 勾配ベクトル` の形で比較する方針が有力と整理した。

### 20. 勾配ベクトル列の実装と列説明の依頼
**User**
> 列設計もお任せするので、実装まで行ったうえで、追加した列の説明と解釈を教えてください。

**Copilot 要約**
- `KernelSweepExportService` を修正し、`plateau_index >= 2` の隣接 plateau 間から局所勾配 `d(level_eff01)/d(radius_px)` を抽出して compare CSV へ追加した。
- 共通尺度は `u=(P-level)/P` とし、各局所勾配を固定アンカー `u={0.10,0.25,0.40,0.55,0.70,0.85}` へ線形補間で再サンプリングする列設計を採用した。
- compare CSV へ `obs_local_slope_u100`〜`obs_local_slope_u850` と、それぞれに対応する `pred_*` / `err_*` を追加した。
- これにより、従来の `front_slope23/24` と `tail_slope` が代表点比較だったのに対し、中間帯を含む局所勾配ベクトルを共通尺度で比較できるようになった。

### 21. `count20` compare CSV の分析依頼（局所勾配ベクトル追加後）
**User**
> 新たに出力された比較結果のCSVを添付します。分析をお願いします。

**Copilot 要約**
- `joint_count` は全域で誤差 `0` か `1` に収まり、非常に強い指標と整理した。
- `joint_step` も中高Pでかなり安定し、低Pを除けば法則化しやすいと整理した。
- 新規追加した `local_slope_u*` 系列では、`u250〜u850` が全体にかなり安定し、特に中間帯〜後半帯の変形を捉える主力候補と整理した。
- 一方 `local_slope_u100` は頂上近傍の局所性・量子化・サンプル不足の影響を受けやすく、診断列寄りで主指標にはしにくいと整理した。
- `root_lock` / `root_lock_alt` は依然として誤差が大きく、初段由来の不安定さが強いことを再確認した。
- `terminal_headroom` は高P側でかなり良く、`curvature_budget` も概ね有望であるため、次段は `local_slope_u250〜u850` と組み合わせて中間帯の法則を見る方針が有力と整理した。

### 22. `stair-detail.csv` へ前値からのベクター情報を追加する案
**User**
> [C:\Users\ma\source\repos\StrokeSampler\DotLab\Kernel\KernelSweep\kernel-sweep-wide-count20-stair-detail.csv](#c:\users\ma\source\repos\strokesampler\dotlab\kernel\kernelsweep\kernel-sweep-wide-count20-stair-detail.csv-context) の出力に前の値からのベクター情報を追加してはどうでしょうか？

**Copilot 要約**
- その案は有効であり、特に `stair-detail.csv` を後段で解析するときの中間表現としてはかなり扱いやすいと回答した。
- ただし独立した新情報というより、既存の `tread` と `riser` を「前 plateau から current plateau への2次元遷移ベクトル」として明示し直す意味が強いと整理した。
- 追加候補として、`start_r_px` 基準の前値差分から `prev_vec_dx_px` / `prev_vec_dx_norm` / `prev_vec_dy01` / `prev_vec_dy_relP` / `prev_vec_slope01_per_px` / `prev_vec_slope_relP_per_rnorm` を提案した。
- 単位の違う `px` と `level01` をそのまま長さ・角度化すると解釈しにくいため、主に「成分」と「正規化勾配」を持つ方がよいと整理した。

### 23. グラフ曲線をベクター描画として捉える案の相談
**User**
> 考えを詰めたいのですが、グラフ曲線を再現することができれば、定量化できるわけですよね？とすると、グラフの各線をベクター描画の線のようなイメージでとらえるとやりやすくならないでしょうか？

**Copilot 要約**
- その発想は有効であり、曲線再現が十分できれば実質的に定量化できていると考えてよい、と回答した。
- ただし通常の2Dベクター線そのものというより、`制御点 + 接線 + 区間長 + 関節密度` を持つ path モデルとして捉える方が実務上は扱いやすいと整理した。
- raw の角度や長さは軸の単位差に引きずられるため、`x` を正規化半径または `u=(P-level)/P` に合わせ、`y` を `level_eff01` または相対落差で正規化した上で接線ベクトルとして扱うのが自然と整理した。
- モデルの階層として、`骨格アンカー(start/initial_end/major_change/terminal) + 局所接線(local_slope_u*) + 関節数/間隔(joint_count/joint_span)` の構成が有力と整理した。

## 関連変更の要約
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - `wide` 集約CSVから観測指標を抽出し、予測値と観測値を並べた `*-stair-prediction-compare.csv` を出力する機能を追加した。
- `InkDrawGen/MainPage.xaml`
  - `カーネル断面CSV(予測比較)` ボタンを追加した。
- `InkDrawGen/MainPage.xaml.cs`
  - 予測比較CSV出力のクリックハンドラを追加した。
- `docs/copilot-session-summary.md`
  - 予測比較CSV出力機能と `P` による kernel 変化法則モデルの要約を追記した。
