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

### Rendering / 合成
- HiResレンダ経路（Win2D `CanvasRenderTarget` + `DrawInk`）の累積合成は **BGRA8（8bit）上の source-over** と見なしてよい。
  - 根拠はPNG保存後の観測ではなく、`CanvasRenderTarget.GetPixelBytes()` による **pre-save（保存前BGRA8）** の統計一致。
  - ただし、統計CSV（mean/stddev/unique 等）の算出ロジック自体の信頼性をより盤石にするため、必要に応じて **byte配列のhash一致やヒストグラム一致で再検証することを推奨**。
  - 詳細: `docs/inkcanvas-stack-analysis.md`（合成式推定と N=50 での追認を含む）

### InkCanvas 重ね塗り（同一点反復）の切り分け
同一座標・同条件での反復で、低圧/高圧の見た目差がどこから来るかを切り分けた。

- `laststroke`（1回分のスタンプ）は完全一致する（回数に応じてスタンプ自体が変化しているわけではない）
- 見た目差は主に **InkCanvas側の累積（合成・飽和・8bit量子化）**で発生する
  - ※ここでの「8bit」はレンダーターゲットが BGRA8 であることを指し、PNG保存処理が原因という意味ではない。
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

### N1始点ROI: DotのOp=0.1795でαが完全一致（P=1, S=200）
- 線（alignedN1）の始点ROI（重ね塗り・累積の影響が無い領域）に対し、Dot（単点）の `Op=0.1795` でAlphaDiffが完全一致（ROI差分=0）になった。
  - 同率で `Op=0.1796` もROI差分=0（BGRA8/8bit α量子化により同一出力に落ちる区間がある）。
  - よって、この条件の検証では「濃度（Op）の最適化」は `Op=0.1795` に固定してよい。
  - 根拠: DotLabの `LineN1 vs Dot (Opacity sweep)` の比較CSVで `roi_diff_sum01=0, roi_diff_nonzero_px=0, roi_diff_max=0` を確認。

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
