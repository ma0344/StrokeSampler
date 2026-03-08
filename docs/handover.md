# 引継ぎ: StrokeSampler（現状の実装と判断）

## 目的
本リポジトリ（`StrokeSampler`）で実装した「鉛筆サンプル生成（Ink→PNG）」の現状と、判明した制約・設計判断を整理し、作り直し（プロジェクト種別変更）に向けて引き継げる状態にする。

## 前提
- 当初想定: UWP（`UseUwp=true`）で `InkCanvas` + Win2D によるPNG書き出し。
- 途中で判明: 公開API上で Hardness（硬度）の変更ができない可能性が高い。
- 出力PNGの要件: 
  - 素材用: 透過・ラベル無し
  - 確認用: 白背景・テキストラベル有り

## 主要な設計判断（Decision Log）
- Hardness は公開APIに存在しないためサンプリング軸から除外（Pressure段階に置換）。
- サンプルは Pressure の固定プリセット `0.2 / 0.5 / 0.8 / 1.0` を縦に並べる（A案）。
- ストローク幅は `InkToolbar` の鉛筆ボタン（`InkToolbarPencilButton`）から `SelectedStrokeWidth` を取得して適用。
- `SelectedBrush` は単色とは限らないため、`SolidColorBrush` の場合のみ `InkDrawingAttributes.Color` に反映。

## 現状のUI
- `MainPage.xaml`
  - `CommandBar`:
    - `生成`
    - `クリア`
    - `素材用PNG（透過）`
    - `確認用PNG（白+ラベル）`
  - `InkToolbar` + `InkCanvas`（`TargetInkCanvas` で接続）
  - 出力サイズ入力: `ExportWidthTextBox` / `ExportHeightTextBox`

## 現状の実装（MainPage.xaml.cs）
### サンプル生成
- `GenerateButton_Click`
  - 既存Strokeをクリア
  - `CreatePencilAttributesFromToolbarBestEffort()` で `InkDrawingAttributes` を作成
  - Pressureプリセット分、水平線ストロークを生成して `StrokeContainer` に追加

### InkToolbarからの属性取得
- `CreatePencilAttributesFromToolbarBestEffort()`
  - `InkToolbar.GetToolButton(InkToolbarTool.Pencil)` を取得
  - `InkToolbarPencilButton` にキャストできれば:
    - `SelectedStrokeWidth` を `InkDrawingAttributes.Size` に反映
    - `SelectedBrush` が `SolidColorBrush` の場合のみ `InkDrawingAttributes.Color` に反映
  - キャストできない環境向けに反射ベストエフォート取得も残している

### PNG書き出し
- `ExportPngAsync(isTransparentBackground, includeLabels, suggestedFileName)`
  - `FileSavePicker` で保存先選択
  - Win2D `CanvasRenderTarget(width,height,96)` に描画
    - 背景: 素材用=透明、確認用=白
    - `ds.DrawInk(strokes)`
    - 確認用のみ `DrawPreviewLabels(ds)`
  - `target.SaveAsync(stream, CanvasBitmapFileFormat.Png)`

### 確認用ラベル
- `DrawPreviewLabels(ds)`
  - Tool名、Pressure一覧、Exportサイズ
  - 最後に生成に使った `InkDrawingAttributes` から StrokeWidth / Color(ARGB) を表示

## 判明した制約・想定差分
- `PencilProperties.Hardness` が存在しない（少なくとも現参照セットでは不可）。
- `InkToolbar.GetInkingAttributes()` も存在しない。
- `InkToolbarPencilButton.SelectedBrush` は `Brush` で、常に `Color` を持つとは限らない。

## 確定事項（Verified Findings）
この節は「今後ひっくり返りにくい検証済みの事実」を集積する。

### 用語（線を描画する際の変化要素と規則性）

変化要素:
- `S`: Size（サイズ）
- `P`: Pressure（筆圧）
- `Op`: Opacity（透明度）
- `No`: OverWrite（重ね回数）
- `L`: Length（DIP長）
- `I`: Interval（更新点間隔。線の座標として有効な間隔）
- `Np`: Npoints（始点からの更新点の数）

規則性（現在の確定/運用）:
- `S`: ストロークの開始値が保持されるため、1ストローク内では不変（検証はS固定系列が前提）。
- `P`: 入力によりストロークポイント単位で変化し得る（検証ではP固定系列も多い）。
- `Op`: （線の開始区間ROIの一致という文脈では）`L` / `Np` によって変化し得るが、`Np` が一定以上になると以後は変化しない（定常化）。
- `No`: 1回分スタンプ自体は `No` を増やしても変化しない（同一条件なら同一）。見た目の変化は主に累積合成（BGRA8上のsource-over）で発生する。
- `I`: `I = 0.09 * S`（`S20..180 step20` の境界スイープで確認）。`S` が一定なら1ストローク内で不変。
- `L`: ストロークの長さ（水平線なら `L = EndX - StartX`）。
- `Np`: `Np = floor(L / I) + 1`

### Rendering / 合成
- HiResレンダ経路（Win2D `CanvasRenderTarget` + `DrawInk`）の累積合成は **BGRA8（8bit）上の source-over** と見なしてよい。
  - 根拠はPNG保存後の観測ではなく、`CanvasRenderTarget.GetPixelBytes()` による **pre-save（保存前BGRA8）** の統計一致。
  - ただし、統計CSV（mean/stddev/unique 等）の算出ロジック自体の信頼性をより盤石にするため、必要に応じて **byte配列のhash一致やヒストグラム一致で再検証することを推奨**。
  - 詳細: `docs/inkcanvas-stack-analysis.md`（合成式推定と N=50 での追認を含む）

#### Verified: alignedN系列のNo累積はBGRA8 source-over（255分母, +127丸め）と完全一致
- alignedN画像（N1/N2/N4/N8）に対し、画素ごとの `srcA = alpha(N=1)` を用いて
  - `outA = dstA + (srcA * (255 - dstA) + 127) / 255` をNo回逐次適用（各ステップで8bit整数演算）
  を行うモデル（DotLab側の検算 `model=so255, quantize=round`）が **差分0で完全一致**した。
- このとき飽和画像（例: alignedN1024）の「capが255に届かない」挙動も、PNG保存ではなく **逐次の8bit丸め**により増分が0になって止まるため自然に説明できる。
- 含意: `cap(x,y)` は独立パラメータというより `a(x,y)=alpha(N1)` から決まる固定点（上限）として扱える可能性が高い。

### Paper noise（`noiseRatio` 推定）はPに対して概ね不変（風合い再現として十分）
- 方法: Dot PNG（P以外同条件）から DotLabの `Export Radial Kernel+Noise PNG/CSV (PNG)` で `kernel-profile` を出力し、`mean_alpha_byte > 32` を満たす半径範囲を採用（例: `r_bin < 502`）。同時に出力される `*-noiseRatio-vis2x-alpha.png` について `Export Alpha Radial Profile CSV (PNG)` を実行し、PowerQueryで `r_bin` 結合→`r_bin < rMax` フィルタ→`mean_alpha` 差分統計を算出。
- 結果例（`S200, scale10, P0.25 vs P0.5`, `r_bin < 502`）:
  - 平均差 `diff_abs ≈ 0.0107`（≈ `2.73/255` 階調）
  - p95 `≈ 0.0211`（≈ `5.39/255` 階調）
  - 最大 `≈ 0.0294`（≈ `7.49/255` 階調）
- 解釈: `noiseRatio-vis2x` は「0..2クランプ + 8bit丸め」の可視化量のため、P変更により飽和率や量子化境界が動く影響が残り得る。パターン（山谷の位置）は概ね揃い、差は人間の目でほぼ知覚できない範囲として扱ってよい。

### Paper noiseタイルの周期と切り出し位相（Dot単発N=1のパリティ）
- InkDrawGen の `紙目ベース単点PNG`（`paperbase-dot-kernelcancel-...-canvas2000.png`）を元にした自己相関解析により、紙目タイルの周期は **435pxではなく436px** であることが判明した。
  - 435pxとしてタイル化すると、位相のドリフトにより差分に規則的な格子（周期線）が出やすい。
- 紙目タイルの切り出し位置（位相）も、従来の切り出しから **縦横で2〜3px程度** の補正が必要だった。
- 上記（`tileSize=436px` + 切り出し補正）を反映して紙目を作り直した結果、DotTesterの基本設定（falloff系が全て1）から出力した単点PNGでも、DotLabの `Export Alpha Diff (PNG vs PNG)` で **規則的な格子が消え、自然なノイズ差分に近い**結果が得られた。
  - 含意: まず紙目の周期と位相を合わせることが最優先で、falloff系の調整はその後段の微調整として扱える。

### InkCanvas 重ね塗り（同一点反復）の切り分け
同一座標・同条件での反復で、低圧/高圧の見た目差がどこから来るかを切り分けた。

- `laststroke`（1回分のスタンプ）は完全一致する（回数に応じてスタンプ自体が変化しているわけではない）
- 見た目差は主に **InkCanvas側の累積（合成・飽和・8bit量子化）**で発生する
  - ※ここでの「8bit」はレンダーターゲットが BGRA8 であることを指す。
    - PNG保存処理で追加の8bit量子化が入るという意味ではなく、レンダリング時点で既に8bit化されている（保存はその結果を保持するだけ）。
- 低圧域（例: `P=0.1, N=3`）では `add` と `source-over` の差が統計上出ない範囲がある（飽和が小さい）
- `P=1` では `add`/`max` は不一致で、`source-over` が一致する

根拠（詳細）: `docs/inkcanvas-stack-analysis.md`

### Stage 2（距離・点間隔）: 総移動距離 L が支配的
条件（制御系列）:
- `S=200`, `P=0.5`
- 横方向（`Start/End` を水平）
- `Draw Line (Fixed)`

結果:
- 描画が「出ない→出る」に切り替わる判定は `LineStep`/`LinePts` の個別値ではなく、総移動距離

  `L = LineStep(px) * (LinePts - 1)`

  に強く支配される。
- `LinePts` を変えても同一 L で同じ判定になることを確認（例: `LinePts=18` でも同判定）。
- 閾値は `L0 ≈ 18.0000 ± 0.0001`
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
  - よって周期は、HiRes上で固定18pxではなく **DIP基準で約1.75** の可能性が高い（scale10では 17.5px 相当のため 18px に見える）。

追加観測（周期のP非依存・S依存の可能性）:
- Pを変えても周期は変わららない（少なくとも検証範囲ではP非依存）。
- Sを変えると `period_dip` が変化した:
  - `S=120`: `period_dip=1.0`
  - `S=80`: `period_dip≈0.75`（scale10では丸めにより 0.8 寄りになり得る）
  - `S=40`: `period_dip=0.5`
  - `S=30`: `period_dip≈0.25`（scale10では丸めにより 0.3 寄りになり得る）
  - `S=100`: `period_dip=1.0`（scale8/10/12で `period_px=8/10/12`）
  - `S=150`: `period_dip≈1.2〜1.25`（scale8/10/12で `period_px=10/12/15`）

#### 確定: 更新点間隔（dotStep相当）は S の 0.09 倍
- 方法: `S=20..180 step20` で、最短線の長さ（`EndX-StartX`）を `0.00001` 単位でスイープし、「単点→線（点が増える）」に変化する境界長 `L_threshold` を同定。
- 結果: `L_threshold = 0.09 * S` が明確に成立。
  - よって更新点間隔（dotStep相当）は `dotStep(S) = 0.09 * S` とみなせる。
  - 例: `S200 -> dotStep=18.0`（既知の `dot2-step=18.00` と整合）

| S | dotStep (=0.09*S) |
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

### N1始点ROI: DotのOp=0.1795でαが完全一致（P=1, S=200）
- 線（alignedN1）の始点ROI（重ね塗り・累積の影響が無い領域）に対し、Dot（単点）の `Op=0.1795` でAlphaDiffが完全一致（ROI差分=0）になった。
  - 同率で `Op=0.1796` もROI差分=0（BGRA8/8bit α量子化により同一出力に落ちる区間がある）。
  - よって、この条件の検証では「濃度（Op）の最適化」は `Op=0.1795` に固定してよい。
  - 根拠: DotLabの `LineN1 vs Dot (Opacity sweep)` の比較CSVで `roi_diff_sum01=0, roi_diff_nonzero_px=0, roi_diff_max=0` を確認。

### 検証: N1 ROIの `Op(Np)` テーブルはSでほぼ不変（S100..200）
- 目的: `S200` で得られた「開始点ROI(N1)の `Op(Np)` テーブル」が、他の `S` でも同一系列として扱えるかを確認する。
- 方法:
  - `I = 0.09 * S` を用い、`Np` を複数（例: `2,4,5,6,8,10,12,14`）に固定して line（`Op=1`）を生成。
  - 量子化影響を減らすため、ROI内のピクセル数が概ね揃うよう `scale` を調整（例: `S100:20`, `S140:15`, `S180:11`, `S200:10`）。
  - 各 `S` ごとに Dot 側を Op sweep して、DotLabのサマリCSV（主にROI差分）で best を採用。
- 結果（結論）:
  - best `Op` は `S=100/140/180/200` で同一系列に乗り、差が出ても概ね `1e-3` 程度（8bit alpha量子化の影響が支配的）で、最小となる帯域はほぼ一致した。
  - よって少なくともこの条件範囲では、`Op(Np)` は **Sに対してほぼ不変な共通テーブル**として扱ってよい。

### 検証: dotN疑似線と2点Lineは線全体で目視一致（S10..200）
- 目的: `I=0.09*S` と `Np` を揃えたとき、dotN疑似線（DotをN個並べる）で2点Lineの見た目を再現できるか確認する。
- 方法:
  - `S=10/20/40/80/100/140/180/200` を対象に、各Sで `I=0.09*S` を採用。
  - `Np=2..14`（代表値）をスイープし、同一 `S/I/Np` になるように以下のペアを生成して目視比較。
    - オリジナル線: 2点Line（`EndX = StartX + I*(Np-1)`）
    - 疑似線: dotN（`dotStep=I` かつ `dotCount=Np`、StartX基準）
  - 最終出力サイズを揃えるため `scale=2000/S`（例: `S200:10`, `S140:14`, `S100:20`, `S20:100`, `S10:200`）を採用。
  - ROIは線全体が入るように十分広い範囲を使用。
- 結果（結論）:
  - 上記範囲では、dotN疑似線と2点Lineは線全体としてほぼ同じ見た目になり、形状近似として採用できる。

### 追加観測: 単点Dotにおける P と Op は同じ効き方ではない
- 観測:
  - `Op` を下げると、点の縁が透明化していき、見た目の直径が退縮する。
  - `P` を下げた場合も透明化は起きるが、見た目の直径は退縮せず、外周ほど「α>0の画素密度」が下がる一方で密度が0にはならない。
- 含意:
  - `P` を `Op` の単純な置き換え（例: `P1,Op1` 基準で `Op(P)` によるスカラー補正）として扱うモデルは成立しない可能性が高い。
  - Skia等での再現では、`Op` と `P` は別パラメータとして、形状マスク/外周の確率密度（またはフォールオフ）側も含めてモデル化する必要がある。

### 2点Line（Op=1）のEndXスイープ: N1はDotのOpスケールで完全一致まで合わせられる（S200/DPI96/P1）
- 2点で構成した通常Line（`startX=100` 固定、`endX` をスイープ）を `Op=1` 固定で描画した場合でも、N1 ROIは単点Dotの `Op` を調整することで **完全一致（diff=0）** まで合わせられる。
  - 対象: `S=200`, `DPI=96`, `P=1`、`endX=118..280 step18`（更新点数2..11相当の範囲）
  - 結果: 各 `endX`（≒線長/更新点数）ごとに最適 `Op` が存在し、`roi_diff_sum01=0, roi_diff_nonzero_px=0, roi_diff_max=0` が達成できた。
- よって、少なくともこの検証系列では、更新点数/線長によって **N1の実効濃度スケール（単点Dotに対する必要Op）が変化**する（2..12程度で顕著）。

### EndXと更新点数の対応（S200/DPI96/P1, step=18）
- 検証系列（`startX=100` 固定、`endX` を18刻みで変化）において、EndXと「更新点数（始点含む）」の対応は以下であることを確認した。
  - `EndX118` が更新点2
  - `EndX280` が更新点11
  - `EndX298` が更新点12

### 定常化: 更新点13点目以降でN1の最適Opが0.1795に収束（S200/DPI96/P1）
- 上記スイープを `EndX316/334` まで伸ばしたところ、更新点13点目以降に相当する範囲で、N1の最適 `Dot Op` が `0.1795` に定常化することを確認した。
  - 観測: `EndX316` と `EndX334` で `best_dot_opacity=0.17950`（`roi_diff_sum01=0` で完全一致）
  - 遷移域の例: `EndX298`（更新点12）では `best_dot_opacity=0.17860`（完全一致）
  - `S=180`: `period_dip=1.5`（scale8/10/12で `period_px=12/15/18`）
  - `S=200`: `period_dip≈1.75`（scale8/12で `period_px=14/21`）
  - よって周期はSに依存し、さらに内部で丸め/量子化が入っている可能性がある（例: S150でscale10のみ 1.2）。

### 検証: SkiaTesterの紙目モデル（z正規化）では谷が潰れやすい
- `DotTester` でSkiaTester依存の再現をやめ、紙目 `k(x,y)` をタイル値から直接構成（A/B/C切替）するレンダラに置き換えたところ、紙目の谷が「潰れて浅く見える」症状が解消した。
- 含意: 紙目再現で z正規化（`z=(n-mean)/std` + クリップ）を前提にすると、極値が平均側へ寄って谷が潰れるリスクがあるため、まずはタイル値の直接モデルで合わせる方が収束しやすい。

### HiRes LastStroke のクロップ
- `InkStroke.BoundingRect` ベースのクロップは、点列由来で範囲だけが広がり「透明余白」が増えることがある。
- そのため `Export HiRes LastStroke (Cropped+Transparent)` のクロップは、**実描画ピクセル（透明背景なら alpha>0）**から最小矩形を取り、そこへ 1px マージンを付けて切り出す方式に変更した。

### DotLab: PNG出力の互換性
- `ExportAlphaDiffAsync` の出力PNGが Gimp / Windows ビューアで「破損」扱いになるケースがあった。
- 対応として、PNG書き込みを `IRandomAccessStream` への直接書き込みから **`FileStream` への書き込み**に変更。
- 併せて差分画像は **Gray8（1ch）+ 不透明**で保存する（ビューア互換性を優先）。

### Line先頭N1 vs 単点（aligned-dot-index）: 形状/濃さ近似（新規）
- 目的: 直線ストローク（`N1N2`）の先頭領域と単点出力（`aligned-dot-index`）を同一ROIで比較し、
  - 形（2値マスク）
  - 濃さ（α値のスケール）
 について「最も近い組み合わせ（P対応）」を探索する。
- StrokeSampler側で単点出力ルート（`aligned_mode=dot-index-single`）を追加。
- DotLab側でフォルダ内のPNGから自動で best/second をマッチングしてCSV出力し、ヒートマップ/差分強度PNGも出力する。
- 詳細な手順・確定事項は `docs/copilot-session-summary.md` の "Aligned line N1 vs aligned-dot-index N1" 節を参照。

### InkPointsDump: 保存先
- `InkPointsDump` の保存先は、まず `KnownFolders.PicturesLibrary/StrokeSampler/InkPointsDump` を試し、失敗時は `ApplicationData.Current.LocalFolder/InkPointsDump` にフォールバックする。

### UWP: 保存先をコードで指定する場合の権限（要注意）
- ファイル/フォルダ選択ダイアログ（Picker）を使わずに保存先をコードで固定すると、UWPの制約により **権限エラー**になり得る。
- 回避には `appxmanifest` の capability 設定だけでなく、環境によっては **Windowsの設定で当該アプリにファイルシステムアクセスを許可**する必要がある。
- 「保存先が内部ディレクトリ（LocalFolder）になる」問題も、この許可設定が未反映だと発生し得る。

### Hold（同一点列）の退化対策
- 全点が完全に同一座標の `InkPoint` 列だと、ストロークが退化して描画されないケースがある。
- `Draw Hold (Fixed)` の点列生成では、サブピクセルの微小オフセット（例: x+0.5）を混ぜて退化を回避する。

### 紙目（ノイズ）の固定性（High confidence observation）
- 解析・再現モデル上、紙目（ノイズ）が **ワールド座標に固定**されている（描画位置に応じて位相が決まる）挙動が強く示唆される。
- ただしこれはAPI仕様として明文化できていないため、ここでは「高確度の観測/仮説」として扱う。

## 依存関係
- NuGet: `Win2D.uwp`（`StrokeSampler.csproj` に `PackageReference` 追加済み）

## 作り直し時の推奨方針
1. プロジェクト種別を確定する（例: WinUI 3 / WPF / UWP継続 など）。
2. 目的のAPI（Hardness相当、ブラシ粒子パラメータ変更）が扱えるフレームワーク/ライブラリを選定する。
3. 既存コードから移植しやすい部分:
   - PNG書き出しの2系統（素材用/確認用）という要件
   - Pressure段階サンプル生成の概念
   - 確認用ラベル焼き込み

## 手動検証（現プロジェクト）
1. アプリ起動
2. `InkToolbar` で鉛筆の色/サイズを選択
3. `生成` を押して4本のサンプル線が描画されることを確認
4. `素材用PNG（透過）` を保存し、透明背景であることを確認
5. `確認用PNG（白+ラベル）` を保存し、白背景とラベルを確認

## 主要ファイル
- `MainPage.xaml` (modify)
- `MainPage.xaml.cs` (modify)
- `StrokeSampler.csproj` (modify) - `Win2D.uwp`
- `docs/pencil-stroke-sampler-roadmap.md` (modify)
- `docs/Issues/*` (modify/new)
