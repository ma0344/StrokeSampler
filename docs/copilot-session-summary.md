# Copilot 作業サマリ（スレッド共有用）

## スレッド引き継ぎまとめ（2026-03-10）

### 1. このスレッドの目的
- VS Code への移行後も、既存プロジェクト群をビルド・デバッグできる状態に整える。
- UWP InkCanvas Pencil の再現検証を進めるため、従来の「1画素中心Xスイープ」依存の紙目抽出フローを見直し、より頑健な kernel / 紙目抽出基盤を InkDrawGen に追加する。

### 2. 背景と変更に至った経緯
- ユーザーは Visual Studio 2026 Community から VS Code へ移行したため、まず VS Code で `DotLab` をデバッグできるようにする必要があった。
- その後、全プロジェクトを VS Code から扱えるようにしたいという要望があり、WPF と UWP でビルド方式が異なる点を整理して構成を追加した。
- さらに本題として、UWP Pencil の紙目再現で「紙目抽出範囲に対応する正方形だけ差分の出方が違う」問題があり、原因調査を行った。
- 調査の結果、主因は再現式そのものよりも、**紙目タイルの抽出元と falloff 相殺手法の粗さ** にある可能性が高いと判断した。
- 従来の `ExportKernelSweepCsvButton` + `ExportKernelCanceledDotPngButton` は、1画素固定の水平断面から `f(r)` を作る方式であり、外周や周方向の揺らぎ、量子化、紙目の局所偏りを十分に平均化できないという限界があった。
- そのため、以下の方針へ切り替えた。
  - PNG 全周の半径統計から頑健な kernel を作る。
  - 統計量は mean ではなく `P90` または `Median` を選べるようにする。
  - falloff は整数binではなく連続補間で評価する。
  - 複数条件 PNG から共通紙目を推定する。
  - 必要なら簡易な交互最適化で kernel と紙目を再推定する。

### 3. 実施した追加・修正内容

#### 3-1. VS Code デバッグ / ビルド環境の整備
- 追加・修正対象
  - `.vscode/launch.json`
  - `.vscode/tasks.json`
  - [DotLab/Properties/launchSettings.json](DotLab/Properties/launchSettings.json)
  - [DotTester/Properties/launchSettings.json](DotTester/Properties/launchSettings.json)
  - [SkiaTester/Properties/launchSettings.json](SkiaTester/Properties/launchSettings.json)
- 実施内容
  - `DotLab` / `DotTester` / `SkiaTester` の WPF 3件に VS Code 起動構成を追加。
  - `StrokeSampler` / `InkDrawGen` の UWP 2件には VS Code からのビルドタスクを追加。
  - UWP は `dotnet build` ではなく、`vswhere.exe` で検出した Visual Studio Build Tools の `MSBuild.exe` を使う構成にした。
- 検討
  - 初回は `coreclr` を試したが、VS Code 側の debug adapter 解決に失敗したため `dotnet` へ切替。
  - その後も `debug adapter descriptor` 問題が出たが、最終的には拡張機能破損が原因と判断し、ユーザーの再インストールで解消した。
- 結論
  - WPF 3件は VS Code から F5 起動可能。
  - UWP 2件は VS Code から直接デバッグ起動までは未整備だが、ビルドタスク経由で安定してビルド可能。

#### 3-2. 既存の紙目抽出フローの課題整理
- 調査対象
  - [InkDrawGen/Helpers/KernelSweepExportService.cs](InkDrawGen/Helpers/KernelSweepExportService.cs)
  - [InkDrawGen/Helpers/KernelCanceledDotExportService.cs](InkDrawGen/Helpers/KernelCanceledDotExportService.cs)
  - [DotTester/Helpers/PaperNoiseTile.cs](DotTester/Helpers/PaperNoiseTile.cs)
  - [DotTester/Helpers/DotReproRenderer.cs](DotTester/Helpers/DotReproRenderer.cs)
- 抽出した課題
  - `kernel-sweep` は 1画素固定・X方向断面のみで、full-circumference の情報がない。
  - `alpha / f(r)` 相殺時に整数bin参照を使っており、半径方向の変化が段付きになりやすい。
  - 紙目タイルは抽出元の統計や位相の偏りをそのまま持ち込みやすい。
  - 複数条件から共通紙目を抽出する手段がなかった。
- 結論
  - 既存フローは「軽量な試作」には有効だが、最終的な紙目再現の土台としては不十分。

#### 3-3. InkDrawGen に頑健kernel抽出を追加
- 追加対象
  - [InkDrawGen/Helpers/RobustRadialKernelExportService.cs](InkDrawGen/Helpers/RobustRadialKernelExportService.cs)
  - [InkDrawGen/MainPage.xaml](InkDrawGen/MainPage.xaml)
  - [InkDrawGen/MainPage.xaml.cs](InkDrawGen/MainPage.xaml.cs)
- 実施内容
  - `頑健kernel CSV(PNG→CSV)` ボタンを追加。
  - PNG を複数選択し、画像中心 `((w-1)/2,(h-1)/2)` 基準で半径binごとの統計を計算する処理を追加。
  - 統計量は `P90` / `Median` を切替可能にした。
  - `bin内α=0を除外` を追加し、外周のゼロ画素を統計に含めるか制御できるようにした。
  - 画像ごとの中心近傍 `p90` を gain として使い、条件差を吸収してから `normalized_falloff01` を作るようにした。
  - 単調減少を軽く強制し、量子化ノイズで局所的に上振れる bin を抑えるようにした。
- 検討
  - mean では紙目の谷や外れ値に引っ張られやすい。
  - `P90` は「欠けを減らす」方向に強く、`Median` はより保守的な代表値として機能する。
- 結論
  - 従来の断面 kernel よりも、全周情報を使った安定した falloff 推定が可能になった。

#### 3-4. 共通 falloff ローダ / 連続補間を追加
- 追加対象
  - [InkDrawGen/Helpers/RadialFalloffProfile.cs](InkDrawGen/Helpers/RadialFalloffProfile.cs)
  - [InkDrawGen/Helpers/KernelCanceledDotExportService.cs](InkDrawGen/Helpers/KernelCanceledDotExportService.cs)
- 実施内容
  - `kernel-sweep` / `normalized-falloff` / `robust-kernel` の3形式を共通で読めるローダを追加。
  - 半径pxを引数にした線形補間サンプル `SampleByRadiusPx()` を追加。
  - `KernelCanceledDotExportService` は旧来の整数bin参照から、この共通ローダ経由の連続補間へ変更した。
- 検討
  - 整数binの最寄り参照では scale や欠損の影響で段差が出やすい。
  - 今後 falloff 形式が増えても、呼び出し側を共通化しておくと検証の差し替えが容易。
- 結論
  - 相殺処理はより滑らかになり、新旧CSVの互換性も確保できた。

#### 3-5. 複数PNGからの共有紙目抽出を追加
- 追加対象
  - [InkDrawGen/Helpers/SharedPaperTextureExportService.cs](InkDrawGen/Helpers/SharedPaperTextureExportService.cs)
  - [InkDrawGen/MainPage.xaml](InkDrawGen/MainPage.xaml)
  - [InkDrawGen/MainPage.xaml.cs](InkDrawGen/MainPage.xaml.cs)
- 実施内容
  - `共有紙目PNG(複数PNG)` ボタンを追加。
  - 現在の ROI を 1周期タイルサイズとみなし、複数PNGの同一位置から `paper = alpha / (gain * f(r))` を逆算して画素ごとに median 合成する処理を追加。
  - gain は `alpha / f(r)` の `p90` から推定する。
  - 出力タイルは tile 全体の `p90=1` になるよう正規化する。
  - ROI が画像範囲外の場合は説明付きダイアログで中止するようにした。
  - `min kernel` を追加し、`f(r)` が小さすぎる外周は gain 推定 / 紙目推定から除外するようにした。
  - `refine iter` を追加し、抽出した紙目タイルを使って kernel を再推定する簡易交互最適化を実装した。
- 検討
  - 外周では `f(r)` が小さくなり、`alpha / f(r)` が不安定になるため、その領域を除外する必要があった。
  - そのためのしきい値として `min kernel` を導入した。既定値は `0.15`。
  - 交互最適化は本格実装ではなく、まずは「紙目→kernel→紙目」の簡易反復で改善余地を確認する段階に留めた。
- 結論
  - 複数条件の PNG から共通紙目を作る基盤ができた。
  - 単一画像依存よりも、紙目の共通成分を抽出しやすくなった。

#### 3-6. UWP 旧形式 csproj への明示登録
- 追加対象
  - [InkDrawGen/InkDrawGen.csproj](InkDrawGen/InkDrawGen.csproj)
- 実施内容
  - `RadialFalloffProfile.cs`
  - `RobustRadialKernelExportService.cs`
  - `SharedPaperTextureExportService.cs`
  を `Compile Include=...` に追加。
- 結論
  - このプロジェクトは SDK-style ではなく、`.cs` 自動認識ではないため、明示登録が必要だった。

### 4. ビルド・検証結果
- `DotLab` は VS Code / `dotnet build` でビルド成功。
- `DotTester` / `SkiaTester` も `dotnet build` でビルド成功。
- `StrokeSampler` / `InkDrawGen` は `MSBuild.exe /p:Configuration=Debug /p:Platform=x64` でビルド成功。
- `InkDrawGen` の新規実装追加後もビルド成功。
- `InkDrawGen` の警告は既存の nullable 注釈に関するものが中心で、今回追加分の致命的問題は確認していない。

### 5. このスレッドでの最終結論
- VS Code 移行に必要な最小限のデバッグ / ビルド基盤は整備済み。
- 紙目再現の主課題は、現時点では「再現側の式」より先に、**kernel と紙目の抽出基盤の粗さ** にあると判断した。
- そのため、今後の比較・調整は、まず以下を使う前提に切り替えるのがよい。
  1. `頑健kernel CSV(PNG→CSV)` で full-circumference kernel を作る。
  2. `共有紙目PNG(複数PNG)` で複数条件から紙目タイルを抽出する。
  3. 必要なら `refine iter` を 1 以上にして簡易交互最適化を試す。
  4. その後に DotTester / DotLab 側で sampler / offset / k定義 / cutoff を詰める。

### 6. 次スレッドでまず確認すべきこと
- 実データ（scale80 画像群）で、`P90` と `Median` のどちらが under を減らしやすいかを比較する。
- `min kernel` を `0.15 / 0.20 / 0.25` で変えたときの紙目タイルの安定性を比較する。
- `refine iter=0` と `1` で、差分画像の改善有無を確認する。
- 抽出した shared paper tile の周期・位相が、既知の 436px 仮説と整合するかを改めて検証する。

## 追加実装: InkDrawGen に頑健kernel抽出 / 共有紙目抽出を追加（2026-03）
- 目的: 単一点の中心Xスイープではなく、PNG全周の半径統計から `f(r)` を作り、さらに複数条件のPNGから共通の紙目タイルを抽出できるようにする。
- 追加UI: [InkDrawGen/MainPage.xaml](InkDrawGen/MainPage.xaml)
  - `頑健kernel CSV(PNG→CSV)` ボタン
  - `共有紙目PNG(複数PNG)` ボタン
  - `bin(px)` / `P90|Median` / `bin内α=0を除外`
  - `min kernel` / `refine iter`
  - 共有紙目抽出は「現在のROIをタイルサイズとして使う」注記を追加
- 追加実装:
  - [InkDrawGen/Helpers/RobustRadialKernelExportService.cs](InkDrawGen/Helpers/RobustRadialKernelExportService.cs)
    - 複数PNGを選択し、画像中心 `((w-1)/2,(h-1)/2)` 基準で full-circumference の半径bin統計を計算
    - `P90` / `Median` 切替に対応
    - bin内 `α=0` を含める/除外する切替に対応
    - 画像ごとの中心近傍 `p90` を gain として正規化したうえで、`normalized_falloff01` をCSV出力
    - 単調減少を軽く強制して量子化のギザつきを抑制
  - [InkDrawGen/Helpers/RadialFalloffProfile.cs](InkDrawGen/Helpers/RadialFalloffProfile.cs)
    - `kernel-sweep` / `normalized-falloff` / 新しい `robust-kernel` CSV を共通で読めるfalloffローダを追加
    - 半径pxでの線形補間サンプルを提供
  - [InkDrawGen/Helpers/SharedPaperTextureExportService.cs](InkDrawGen/Helpers/SharedPaperTextureExportService.cs)
    - 複数PNG + falloff CSV から、現在のROIを1周期タイルとして `alpha / (gain * f(r))` を取り、画素ごとに median 合成して共有紙目PNGを出力
    - 画像ごとの gain は `alpha / f(r)` の `p90` で推定
    - 出力タイルは最後に tile全体の `p90=1` になるよう正規化
    - `refine iter > 0` のときは、共有紙目タイルを使って頑健kernelを再推定し直す簡易交互最適化を実行し、refined kernel CSV も保存
  - [InkDrawGen/Helpers/KernelCanceledDotExportService.cs](InkDrawGen/Helpers/KernelCanceledDotExportService.cs)
    - falloff読込を `RadialFalloffProfile` 経由へ切替
    - 旧来の整数bin参照ではなく、半径pxでの線形補間サンプルへ更新
    - 既存の `kernel-sweep` に加え、新しい `robust-kernel` / `normalized-falloff` CSV もそのまま使えるようにした
- プロジェクト設定:
  - [InkDrawGen/InkDrawGen.csproj](InkDrawGen/InkDrawGen.csproj) に新規 `.cs` を明示追加した。
  - このUWPプロジェクトは `Compile Include=...` を列挙する旧形式だったため、自動認識ではなくcsproj追記が必要だった。
- 検証:
  - `MSBuild.exe .\InkDrawGen\InkDrawGen.csproj /p:Configuration=Debug /p:Platform=x64` 成功
  - エラー 0 / 警告 5
  - 警告5件は既存ファイルの `?` 注釈に関する既知警告で、今回追加ファイル由来の警告は出ていない

## 追加実装: VS Code から DotLab をデバッグする構成を追加（2026-03）
- 目的: VisualStudio 2026 Community から VS Code へ移行したため、`DotLab\bin\Debug\net8.0-windows10.0.19041.0\DotLab.exe` をワークスペースから直接デバッグできるようにする。
- 追加: `.vscode/launch.json` に `DotLab (.NET 8 WPF)` の起動構成を追加。
  - 初回は `coreclr` を使っていたが、VS Code 側で `Couldn't find a debug adapter descriptor for debug type 'coreclr'` が出たため、`type: dotnet` へ切替。
  - `projectPath`: `DotLab/DotLab.csproj`
  - `preLaunchTask`: `build DotLab`
- 追加: `.vscode/tasks.json` に `dotnet build DotLab/DotLab.csproj -c Debug` を実行する `build DotLab` タスクを追加。
- 追加: `DotLab/Properties/launchSettings.json` を作成し、`workingDirectory` をプロジェクトフォルダ (`.`) に固定。
- 検証: `dotnet build .\DotLab\DotLab.csproj -c Debug` は成功。既存警告3件のみで、Debug出力は生成されることを確認。

## 追加実装: 全プロジェクト向けの VS Code デバッグ/ビルド構成を追加（2026-03）
- `.vscode/launch.json`
  - `DotLab (.NET 8 WPF)`
  - `DotTester (.NET 8 WPF)`
  - `SkiaTester (.NET 8 WPF)`
  を追加し、3つのWPFプロジェクトは VS Code の F5 で起動できるようにした。
- `DotTester/Properties/launchSettings.json` と `SkiaTester/Properties/launchSettings.json` を追加し、作業ディレクトリを各プロジェクト直下 (`.`) に固定した。
- `.vscode/tasks.json`
  - `build DotLab`
  - `build DotTester`
  - `build SkiaTester`
  - `build StrokeSampler (UWP x64)`
  - `build InkDrawGen (UWP x64)`
  - `build All Desktop Projects`
  - `build All Projects`
  を追加した。
- UWP (`StrokeSampler` / `InkDrawGen`) は `dotnet build` ではなく、`vswhere.exe` で検出した Visual Studio Build Tools の `MSBuild.exe` を使うタスクでビルドする方式にした。
- 検証:
  - `dotnet build .\DotTester\DotTester.csproj -c Debug` 成功
  - `dotnet build .\SkiaTester\SkiaTester.csproj -c Debug` 成功
  - `MSBuild.exe .\StrokeSampler.csproj /p:Configuration=Debug /p:Platform=x64` 成功
  - `MSBuild.exe .\InkDrawGen\InkDrawGen.csproj /p:Configuration=Debug /p:Platform=x64` 成功
- 制約:
  - VS Code からの F5 直起動を構成したのは `dotnet` デバッグアダプターで扱える WPF 3件のみ。
  - UWP 2件は VS Code では通常の `dotnet` 起動対象ではないため、現時点では **ビルドタスクまで** を整備し、直接デバッグ起動の設定は追加していない。

## 調査メモ: Dot単発N=1の紙目差分に中心正方形が出る件（2026-03）
- 観測: DotLabの差分で、紙目を取得した範囲に対応する正方形だけ差分の出方が変わる。
- 暫定結論: falloffや合成式より前に、**紙目タイルの作り方（周期/位相/切り出し）** が主因の可能性が高い。
  - 既知の確定事項として、紙目タイル周期は `435px` ではなく `436px`、さらに切り出し位相に `2〜3px` 補正が必要。
  - 正方形の痕跡が出るのは、抽出した有限パッチをそのままタイルとして使っており、抽出元範囲だけ統計や位相が他領域と異なる場合の見え方と整合する。
- 実装上の観点:
  - `DotTester/Helpers/PaperNoiseTile.cs` はタイル全体のαから `mean/stddev` をそのまま算出する。
  - `DotTester/Helpers/DotReproRenderer.cs` はその `mean/stddev` と、`((x+0.5)+offset)/scale` のワールド固定サンプリングを用いて `k` を作る。

## 設計メモ: Pスイープしたkernelの現時点での整理（2026-03）
- `P` によって kernel 曲線は変化する。変化は単なる高さ差ではなく、階段構造・終端位置・強変化点の現れ方にも及ぶ。
- `Kernel01 * P` で見ると全体の包絡は放物線状に近いが、単一の放物線や単一の数式では記述しにくい。特に高P帯では局所的な regime change がある。
- 同一 `P` 内では、観測された `riser_to_next01` は量子化誤差レベルの微差を除けばほぼ一定とみなしてよい。
- `tread_px` は段ごとに変化し、`delta_tread_to_prev` / `tread_ratio_to_prev` / `log2_tread_ratio_to_prev` で追うと、局所的に強く縮むポイントがある。
- `Kernel01 * P` の最初の plateau は一般の踏面列と別扱いにした方がよい。`initial_tread_px`（最初の1段目までの距離）は平均蹴上だけでは説明できず、頂上近傍の局所形状と最初の閾値 crossing に支配されている。
- `P0.1` から `P0.4` までは初期 plateau が非常に長く、`P0.5` 以上では初期 plateau が急に短くなるため、少なくとも低P帯と中高P帯で別 regime を疑うのが自然。
- `P0.5` 以上では、最も強い踏面縮小が出る位置は概ね `r_norm ≈ 36〜40` 付近に集まる一方、そこへ到達する `plateau_index` は `P` が高いほど増える傾向がある。したがって、Pは「崖の半径位置」そのものより「そこへ至る段数」と「崖の鋭さ」に強く効いている可能性がある。
- 実務上は、単純な近似式を急いで作るより、`initial_tread_px` / `second_to_first_tread_ratio` / `first_major_shrink_plateau_index` / `first_major_shrink_r_norm` / `mean_riser01` を指標として保持し、Pごとの差を比較する方針を優先する。
  - したがって、入力タイル自体に「抽出範囲の偏り」や「非周期な境界」が入っていると、その癖が描画全体へ持ち込まれる。
- 次の優先順:
  1. `tileSize=436px` と切り出し位相補正を先に固定する。
  2. 正方形の痕跡が消えてから、offset / sampler / falloff / k定義を詰める。

## 追加: 検証手順書の集約（2026-02）
- 手順書: `docs/pencil-parity-playbook.md`
- 以後の検証手順（InkDrawGenでの生成→DotLabでの差分/集計→目視確認）は上記へ集約する。

## 目的
このスレッドで実施した変更内容・現状を、別スレッド（別担当/別Copilot）に引き継ぐためのメモです。

## 進め方（方針）
## InkCanvas重ね塗り（Dot/S200）: 確定事項の整理（2026-02）
- 目的: 低圧/高圧での見た目差（等高線化、飽和帯、谷が残る等）が「スタンプ生成」か「InkCanvas累積合成」かを切り分け。
- 手順: `DrawDotButton` を同一座標にN回押してInkCanvasに累積 → `ExportHighResPngCroppedTransparentButton` で8bit alpha PNG出力 → ImageMagickでα差分統計。
- 実装: 切り分けのため「最後に追加したStrokeのみ」をHiResで出力するボタンを追加（出力名に `-laststroke` を付与）。
- 確定1: laststroke（1回分）の差分は P=0.1/P=1 ともに N1-N2, N2-N3 が完全一致（mean=0, stddev=0）。
  - 結論: 同一座標・同一筆圧・紙目ワールド固定なら、スタンプ生成は決定的で毎回同一。
- 確定2: canvas（累積）差分は P で挙動が異なる。
  - P=0.1: Δ12=Δ23、ΔΔ=0（増分が完全に一定）。
  - P=1: Δ23 < Δ12、ΔΔ≠0（飽和に近づくほど増分が減る挙動と整合）。
  - 結論: P依存の見た目差は主に InkCanvas 側の累積（合成・飽和・8bit量子化）で生じる。

詳細: `docs/inkcanvas-stack-analysis.md` に確定事項を整理。

## 追加実装: HiResエクスポートの保存前α統計CSV（2026-02）
- 目的: 8bit量子化が「PNG保存時のみ」か「描画ターゲット時点（CanvasRenderTarget BGRA8）」で既に起きているかを観測で切り分ける。
- UI: `Export HiRes PreSave α Stats (Canvas)` / `Export HiRes PreSave α Stats (LastStroke)`
- 出力: `pencil-highres-pre-save-alpha-...-pre-save-alpha-canvas...csv` / `...-pre-save-alpha-laststroke...csv`
- 集計項目: `alpha_min`, `alpha_max`, `alpha_mean(0..1)`, `alpha_stddev(0..1)`, `alpha_unique(0..256)`

## 方針決定: 合成式の推定（2026-02）
- 目的: InkCanvas累積の見た目をなるべく再現する。
- 方針: 合成候補を `source-over` / `add` / `max` の3つで実装し、HiResエクスポート（Win2Dレンダ）の `canvas` 出力と一致度（統計/差分）比較で推定する。

## 追加実装: HiRes Simulated Composite（2026-02）
- UI: `Export HiRes Simulated Composite (SO/Add/Max)`
- 入力: 現在の最後のStroke（`laststroke`）をHiResレンダしてα(BGRA8)を取得
- 出力: 合成式 `source-over` / `add` / `max` を N 回（`Dot512Overwrite`）適用したPNGと、pre-save α統計CSV

## 確定: HiResレンダ経路の累積はBGRA8上のsource-over（2026-02）
- P=1, N=3 で simulated source-over と実測canvas（pre-save）が統計完全一致。
- P=0.1 は N=3 では add と source-over が一致して見えるが、N=50 で add と source-over が分離。
- P=0.1, N=50 および P=1, N=50 で simulated source-over と実測canvas（pre-save）が統計完全一致。
- 結論: HiResエクスポート（Win2D `CanvasRenderTarget` + `DrawInk`）の累積合成は **BGRA8（8bit）** の上で **source-over** と見なしてよい。

## 追加実装: DotLab Analysis（2026-02）
- InkPointsDump解析: `DotLab/Sample/InkPointsDump/stroke_*_points.json` を読み、dd/dt（点間距離/時間差）統計をCSV出力するボタンを追加。
  - UI: `Export InkPointsDump Stats (dd/dt CSV)`
  - 出力: 選択フォルダに `inkpointsdump-dd-dt-stats-YYYYMMDD-HHmmss.csv`
  - 備考: dumpの `timestanp` typo と `timestamp` の両方に対応。
- α差分出力: 実測canvas PNG と sim PNG を選択し、αの絶対差画像（PNG）と統計CSVを出力するボタンを追加。
  - UI: `Export Alpha Diff (Canvas vs Sim)`

## 追加実装: DotLab alignedN系列のモデル検算（2026-02）
- 目的: P0.5で観測された「No依存の増え方（指数型）+ 画素ごとのcap」モデルを、alignedN系列（N1/飽和N/N2/4/8）で機械的に検算できるようにする（PowerQuery不要）。
- UI: `Verify alignedN series model (N1/cap/N2/4/8)`
- 入力: `alignedN{n}` を含むPNGをまとめて選択（最低 `alignedN1` と `alignedN1024` 等の飽和Nが必要）。
- 出力:
  - `alignedN-model-verify-*-summary-*.csv`（Nごとに `floor/round` の誤差統計を出力）
  - best量子化の `pred` / `diffabs` PNG（および `diffabs` の `vis16`）
  - 参照用に `a(N1)` / `cap(Nsat)` のPNGも出力
  - 追記: 8bit量子化がレンダリング時点で入る前提に合わせ、closed-form（最後に量子化）に加えて stepwise（各ステップで量子化）モデルも同一CSVに出力するようにした。
  - 追記: 差分が小さい（max_abs_diffが1〜3など）ケースでも分布を見分けやすいよう、`diffmask`（diff>0の二値）と `vis64` / `visAuto` を追加出力する。
  - 追記: BGRA8のsource-over（255分母）を各ステップで整数演算するモデル（`so255`）と、飽和capでクランプする派生（`so255cap`）も同一CSVに併記し、どのモデルがN2/4/8に最も一致するかを比較できるようにした。
  - 検証メモ: `so255` の `round` が N2/N4/N8 で `rmse=0, max_abs_diff=0, mismatch_px=0` になれば、No累積は「N1αをsrcAとしてBGRA8 source-overを逐次適用（+127丸め）」と見なしてよい。

## 追加実装: 手順書更新と補助ツール（2026-02）
- `docs/pencil-parity-playbook.md` に、No累積の確定モデル（BGRA8 source-over, 255分母, +127丸めの逐次適用）を追記。
- `docs/pencil-parity-playbook.md` に、小さい差分（max_abs_diffが1〜3程度）で `visAuto` が `diffmask` と同じに見える条件と読み方を追記。
- DotLabに `ImageAlphaRadialKernelNoiseExporter` を追加。
  - 入力PNGのαから、半径方向平均（radial mean）kernel画像と、`alpha/kernel` 比の可視化（ratio=1→128, 0..2→0..255）をPNG/CSVで出力。

## 追加実装: DotLab 紙目テクスチャ周期探索（shift self-match）（2026-02）
- 目的: 紙目PNGがタイルとして繰り返されて見える場合に、平行移動の自己一致から周期（タイル幅/高さ）候補を推定する。
- UI: `Period search (shift self-match)` / `Export Alpha Shift Period CSV (PNG)`
- 出力: `shift-period-<input>.csv`（X/Y両方向、MAE/RMSE、valid_rate）
- SkiaTesterに、alignedN1 PNGから `so255/round` で alignedN2/4/8 相当を生成してPNG出力するボタンを追加。

## 修正: NoiseRatioの比較がAlphaDiffで常にdiff=0になる問題（2026-02）
- `noiseRatio-vis2x.png` は可視化用にGray8(不透明)で出力していたため、DotLabの `Export Alpha Diff`（Alpha比較）では常にdiff=0になってしまう。
- 値をAlphaに格納した `*-alpha.png`（`noiseRatio-vis2x-alpha.png` / `kernel-alpha.png`）も追加出力し、AlphaDiffで紙目推定の差分比較ができるようにした。

## 追記: paper noise（noiseRatio推定）のP依存は実用上ほぼ不変（2026-02）
- `P0.25` と `P0.5` の `*-noiseRatio-vis2x-alpha.png` を半径プロファイル化して比較。
- 外周の不安定さを避けるため、`kernel-profile` の `mean_alpha_byte > 32` を満たす範囲（例: `r_bin < 502`）だけを採用して差分統計を算出。
- 結果例（`S200, scale10, r_bin < 502`）:
  - `diff_abs` 平均 `≈ 0.0107`（≈ `2.73/255`）
  - `diff_abs` p95 `≈ 0.0211`（≈ `5.39/255`）
  - `diff_abs` 最大 `≈ 0.0294`（≈ `7.49/255`）
- 解釈: `noiseRatio-vis2x` はクランプ＋8bit丸め済みの可視化量のため差がゼロにはならないが、パターン（山谷の位置）は概ね一致し、風合い再現の観点では「Pに対してほぼ不変」として扱ってよい。

## 追加実装: StrokeSampler 直線ストローク（指定条件）描画（2026-02）
- UI: `Draw Line (Fixed)` を追加（点数/点間隔を指定して直線ストロークを生成）
- 入力: Start/End座標（既存TextBox）、LinePts（点数）、LineStep(px)（点間隔）、P（Dot512 Pressure）、S（Dot512 Size）
- 生成: `InkStrokeBuilder.CreateStrokeFromInkPoints` により、指定点列でPencilStrokeを作成し `StrokeContainer.AddStroke` で描画

## 追加実装: StrokeSampler InkPointsDump自動保存（2026-02）
- `Draw Line (Fixed)` 実行時に、生成したInkPoint列を `ApplicationData.Current.LocalFolder/InkPointsDump` 配下へJSON自動保存する。
- 形式は `DotLab/Sample/InkPointsDump` と互換（キーは `timestanp` のtypoも踏襲）。
- `MainPage.xaml.cs` の各イベントハンドラは、原則として **処理本体をヘルパー/サービスへ移し、UI側は1行委譲**にする。
- 目的は「移動（責務分離）」で、挙動変更や最適化は基本的に行わない。
- ビルドが通ることを都度確認。

## 追加実装: DotLab αトーンカーブ（GIMP .crv）LUTの可視化出力（2026-02）
- 目的: GIMPのトーンカーブをαチャンネルに適用した変換を、Dot側に同じ変換として適用し、線側αとの比較を行う。
- 方針: 3D LUT（`.cube`）は使わず、GIMP `.crv` の `(channel alpha)` にある `samples 256` を 1D LUT（0..1正規化の出力テーブル）として扱う。
- 実装: `DotLab/Analysis/LineN1VsDotN1Matcher.cs` で `DotLab/LUT/Dot P1 LUT.crv` を読み、`Match line N1 vs dot N1` 実行時に `*-lut-*` の可視化PNGを追加出力。
  - 出力例: `lineN1-vs-dotN1-heatmap-lut-th1(-fullw)-P{p}.png`, `lineN1-vs-dotN1-diffmag-lut-th1(-fullw)-P{p}.png`
- 備考: ビルド時に `DotLab.exe` がロックされる場合があるため、実行中のDotLabを終了してからビルドする。

## 追加実装: LUT未検出/読込失敗時の警告ダイアログ（2026-02）
- 目的: 起動方法（作業ディレクトリ）差などで `.crv` が見つからずLUT無効になる場合に、原因調査ができるようにする。
- 実装: `Match line N1 vs dot N1` 実行時に、LUTがロードできない場合は `requested/resolved/error` を含む警告ダイアログを1回表示。
- 確認: LUTファイル名変更で「見つからない」警告が表示されることを確認。

## 観測: LUTは高圧側で改善するが低圧側で悪化し得る（2026-02）
- `th=1` のheatmap/CSVでは、P=0.9〜1.0で over(青)が大きく減る一方、低圧側では under(緑)が増えるケースがある。
- 次アクション: LUT適用の閾値（th）または適用条件（圧力帯域/α帯域）の調査が必要。

## 実施済み（主な委譲/移植）

## 追加実装: DotTester（N=1ドットの目視比較用WPF）とテスト環境整備（2026-02）
- 新規: `DotTester` プロジェクト（WPF/.NET 8）。観測PNG（expected）と再現（rendered）を左右に並べて表示し、`Render (N=1)` 押下で再現側のみ更新する。
- テスト環境: `scale`（InkDrawGenのscale相当）を追加し、`diameterPx = S * scale`、`canvas = diameterPx + pad`（Auto時）で `S200/scale10` → `canvas=2020` を再現側でも作れるようにした。
- 紙目: タイルPNG（alpha）を入力し、`noiseScale = scale / tileScale`（Auto時）で、タイルがどのscale基準で切り出されたか（例: tileScale=10）に追従できるようにした。

## 高速化: PaperNoiseサンプリングとPencilDotRendererのピクセル書き込み（2026-02）
- `PaperNoise.Sample01Bilinear` 等で `SKBitmap.GetPixel` を多用していたため、内部に `_pixels` をキャッシュして配列参照で読むように変更。
- `PencilDotRenderer.Render` の出力を `SetPixel` 多発から `bitmap.Pixels` への配列書き込み＋最後に一括反映へ変更。
- `DotTester` のステータスに `time=...ms` を表示し、2020固定での待ち時間・改善効果を確認しやすくした。

## 追加実装: DotTesterでkernel-profile.csvを減衰LUTとして利用（2026-02）
- `DotTester` の `Falloff CSV` で `normalized-falloff` に加え、`ImageAlphaRadialKernelNoiseExporter` が出力する `*-kernel-profile.csv` を指定できるようにした。
- `kernel-profile` は `mean_alpha(px)` を有効半径で正規化し、`r_norm=0..100` のLUTへ線形補間して `PencilDotRenderer` の `falloffLut` に渡す。
- 形状一致を優先して `PaperNoise` の既定をOFFにした。

## 追加実装: 紙目(B=紙目をαへ乗算)の検証パラメータ（2026-02）
- `PencilDotRenderer.Render` に `paperNoiseKClampMin/Max` を追加し、紙目係数kのクランプ範囲（既定0.5..1.5）を調整可能にした。
- `DotTester` に `Gain` / `kClamp(min..max)` / `Disable kMean norm` を追加し、外周の「まばら化」を紙目パラメータで詰められるようにした。

## 追加実装: 外周floor上昇（cutoff/紙目マスク）仮説の検証UI（2026-02）
- `DotTester` に `Cutoff alpha(byte)` と `noise-dependent cutoff (×k)` を追加し、微小α帯域の0落ちを制御できるようにした。
- `DotTester` に `PaperMask`（Multiply/Soft/Threshold）と `edge falloff`（StrongerAtEdge/ThresholdAtEdge）を追加し、外周ほど厳しく落とす挙動を検証できるようにした。

## 追加実装: カーネル（falloff）全体の明るさ/濃さ調整（2026-02）
- `PencilDotRenderer.Render` に `falloffScale` を追加し、半径減衰 `f` に乗算してクランプすることでカーネル全体を明るく/暗く調整できるようにした。
- `DotTester` に `FalloffScale` を追加し、UIから `falloffScale` を指定してレンダ結果を比較できるようにした。

## 追加実装: InkDrawGenでカーネル断面（中心Xスイープ）CSV出力（2026-03）
- `InkDrawGen` に観測点(px)固定で中心Xを `dx_px` だけ移動させ、同一画素のαを取得するCSV出力（`KernelSweepExportService`）を追加した。
- 2000×2000のPNGを大量生成せず、9×9等の小さなオフスクリーンに描画して中心1pxのαをサンプリングするため、出力は軽量に実行できる。

## 追加実装: InkDrawGenでkernel-sweep CSVを使ったfalloff相殺（紙目ベース単点PNG）出力（2026-03）
- `InkDrawGen` に `KernelCanceledDotExportService` を追加し、kernel-sweep CSVから `f(rNorm)` を構築して単点レンダのαを `alpha/f` で相殺したPNGを出力できるようにした。
- `InkDrawGen/MainPage.xaml` に `紙目ベース単点PNG` ボタンを追加し、CSV選択→PNG出力をUIから実行できるようにした。

## 追加実装: DotTesterで再現（Rendered）PNG出力（2026-03）
- `DotTester` に `Save Rendered PNG...` ボタンを追加し、レンダ結果（再現画像）をPNGで保存できるようにした。

## 修正: Alphaチャンネル紙目タイルの統計に背景(A==0)が混ざって谷が浅くなる問題の対策（2026-03）
- `SkiaTester/Helpers/PaperNoise.cs` の無効背景判定をサンプルチャンネル依存にし、Alpha利用時は `A==0` を無効扱いにすることで、背景が統計に混ざってz正規化が歪むのを防いだ。
- `DotTester` のPaperNoise読み込みを `InvalidPixelMode.Legacy` + `SampleChannel.Alpha` に変更し、dot由来タイル（背景が透明）でも紙目の谷が浅く潰れにくいようにした。

## 修正: PaperNoiseの双線形サンプルが0.5pxずれて極値が平均化される問題の対策（2026-03）
- `SkiaTester/Helpers/PaperNoise.cs` の `Sample01Bilinear` を「ピクセル中心座標(0.5,1.5,...)」基準で扱うようにし、呼び出し側の `(x+0.5)` とtexel中心が一致するよう `-0.5` シフトを入れた。
- これにより、PaperNoiseタイルの「最深画素」が常に近傍平均との差で潰れて見える（谷が浅い）症状を軽減する。

## 追加実装: DotTesterで再現手法をSkiaTesterから切り離して再構築（2026-03）
- SkiaTester式（z正規化）を踏襲する不安があるため、DotTester側に再現の最小要素を1から実装した。
- `normalized-falloff` は `DotTester/Helpers/NormalizedFalloffLut.cs` で読み込み、`kernel-sweep→normalized-falloff` のLUTをそのまま `f(rNorm)` として使用する。
- PaperNoiseは `DotTester/Helpers/PaperNoiseTile.cs` でPNG(α)を読み込み、タイルサンプリングをUIで `Nearest/Bilinear` 切替できるようにした。
- k定義は `DotTester/Helpers/DotReproRenderer.cs` で実装し、UIから A/B/C（`k=n01/mean`, `blend`, `k=n01`）を選択できるようにした。
- 量子化は最後に1回だけ行う（内部はdoubleで `outA` を保持してから8bit化）。
- 旧UIのPaperMaskは互換のため残しつつ、再構築レンダラでは現時点で未適用（コア再現の検証を優先）。

## 修正: DotTesterのk定義(A/B)が同一になっていた問題とEdgePad未適用（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs` で A/B/C のk定義を仕様どおりに分離し、AとBが同じ見た目になる不具合を修正した。
- `DotTester` の `EdgePad(px)` を再構築レンダラへ渡し、外縁の落ち方をUIから調整できるようにした。

## 修正: NoiseDependentCutoffを谷で厳しくする（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs` の `NoiseDependentCutoff` を `cutoff *= k` から `cutoff /= k` に変更し、谷(k<1)で抜けが出やすい挙動にした。

## 追加実装: 外縁で紙目が消える問題の切り分け用に加算モデルとfalloffガンマを追加（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs` に紙目の適用モードを追加（`MultiplyAlpha`/`AddAlpha`）。
  - `AddAlpha` は中心のベースαを基準(aRef)にして `a01 += aRef*(k-1)` を適用し、外縁でベースαが小さくても紙目の差分が量子化で消えにくいようにする。
- `DotTester` のUIに `Apply(Multiply/Add)` と `Falloff Gamma` を追加し、検証時に切り替え可能にした。

## 追加実装: falloffの落ち始め位置を調整するRNormScaleを追加（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs` に `FalloffRNormScale` を追加し、`rNorm` をスケールしてLUT参照位置を調整できるようにした。
  - `RNormScale>1` で同じ距離でも外側の`rNorm`を参照するため、落ち始めが内側へ寄る（より早く落ちる）。
- `DotTester` のUIに `RNormScale` を追加し、見た目合わせで微調整できるようにした。

## 修正: falloff距離計算をピクセル中心基準に変更（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs` の距離計算を `(x+0.5, y+0.5)` のピクセル中心基準へ変更し、falloff CSVの観測系列（dx_pxが整数で増える）と整合しやすくした。

## 追加実装: DotTesterでkernel-sweep CSVをFalloffとして直接読み込み（2026-03）
- `DotTester/MainWindow.xaml.cs` のFalloff CSVローダを拡張し、`kernel-sweep`（ヘッダが`dx_px,...`）を指定した場合でも読み込めるようにした。
  - `dxTarget = r_norm * Scale`（ScaleはDotTesterの`RenderScale`）で `r_norm=0..100` の `mean_alpha` を抽出し、内部の`NormalizedFalloffLut`として使用する。
  - kernel-sweep生成時のscaleとDotTesterのScaleが同一であることが前提。
  - FalloffキャッシュはパスだけでなくScaleも一致した場合のみ再利用する。

## 追加実装: DotTesterの紙目(k)生成をUWP/Skia寄せ（z正規化 + kMean正規化）（2026-03）
- `DotTester/Helpers/PaperNoiseTile.cs`
  - `Stddev01` を追加し、紙目タイルの標準偏差を取得できるようにした（z正規化のため）。
  - サンプル座標を「ピクセル中心基準（x=0.5がtexel0中心）」として扱い、nearest/bilinearとも `-0.5` シフトでSkia側と整合させた。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `KDefinition.ZNormalized` を追加（`z=(n01-mean)/std` を ±3 でクランプし、`k=1+(strength*gain)*z`）。
  - `DisableKMeanNormalization` を追加し、半径内平均で `kMean` を計測して `k/=kMean` する補正をON/OFFできるようにした。
  - `NoiseDependentCutoff` はSkiaTester互換の `cutoff *= k` に揃えた（谷の0落ちを起こしにくくする意図）。
- `DotTester/MainWindow.xaml`
  - `k mode` に `U: ZNorm (k=1+(s*g)*z)` を追加。
  - `noise-dependent cutoff` の表記を `(×k)` に更新（実装に合わせる）。

## 追加実装: DotTesterで紙目タイル統計(mean/stddev/min/max)をステータス表示（2026-03）
- `DotTester/MainWindow.xaml.cs` のステータス文字列に `noise(mean/std/min/max)` を追記し、`Stddev01` の確認がUI上でできるようにした。

## 追加実装: DotTesterのZNormでzクランプ幅を調整可能にする（2026-03）
- 背景: ZNormの`z`を固定で±3にクランプすると、タイルの実分布（例: minが-9σ相当）で谷側が張り付き、谷の階調が消えるケースがあった。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `DotReproRenderer.Options` に `PaperNoiseZClampAbs`（既定3.0）を追加。
  - `KMode=ZNormalized` のときの `z` クランプを `±PaperNoiseZClampAbs` で行い、`kMean` の事前計測側も同じクランプ幅で揃えた。
- `DotTester/MainWindow.xaml` / `DotTester/MainWindow.xaml.cs`
  - `zClamp` 入力UIを追加し、値を `PaperNoiseZClampAbs` に配線した。

## 更新: DotTesterのZNorm zClampを上下別 + ON/OFF（既定: -7/+7）（2026-03）
- 背景: タイルの分布で負側（谷）が深く、対称クランプだと谷側の階調が張り付きやすかったため、`zClamp-` を大きくして階調を残しつつ、`zClamp+` は別に抑えられる必要があった。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `DotReproRenderer.Options` に `EnablePaperNoiseZClamp` と `PaperNoiseZClampNegAbs` / `PaperNoiseZClampPosAbs` を追加。
  - `EnablePaperNoiseZClamp=false` の場合は `z` クランプを行わず、`kClamp` のみで抑制する。
  - 互換のため `PaperNoiseZClampNegAbs/PosAbs` が未指定(<=0)なら `PaperNoiseZClampAbs` を採用。
  - 既定値は `PaperNoiseZClampAbs=7`。
- `DotTester/MainWindow.xaml` / `DotTester/MainWindow.xaml.cs`
  - `zClamp` のON/OFFと `zClamp-` / `zClamp+` を追加（既定 7/7）。OFF時は数値入力を無効化。

## 追加実装: InkDrawGenでkernel-sweep CSVからDotTester用 normalized-falloff CSVを生成（2026-03）
- `InkDrawGen` に `KernelSweepToNormalizedFalloffExportService` を追加し、kernel-sweep CSVの `alpha01` を `r_norm` 整数（= `dx_px/scale`）へ落として `normalized-falloff` 形式CSVを出力できるようにした。
- `InkDrawGen/MainPage.xaml` に `normalized-falloff CSV` ボタンを追加し、DotTesterで読み込むLUTを手作業なしで生成できるようにした。

## 追加実装: DotLab LineN1 vs DotN1 (Opacity sweep) バッチとサマリCSV（2026-02）
- 目的: LineN1フォルダとDot(Opacity sweep)フォルダを別指定し、P一致の組み合わせでAlphaDiff統計を総当たり出力する。
- UI: Analysisに `Line folder` / `Dot (Opacity sweep) folder` / `CSV output folder` と `Run batch and export CSV` を追加。
- 出力: `lineN1-vs-dotN1-opacitysweep-match-YYYYMMDD-HHmmss.csv`（総当たり）と、同一出力フォルダへ
  `lineN1-vs-dotn1-opacitysweep-summary-YYYYMMDD-HHmmss.csv`（line_fileごとに roi_diff_sum01 最小行を抽出）を追加出力。
- CSV列: dot側の `dot_file` から `-Op(value)` をパースして `dot_opacity` 列を追加。

## 修正: InkDrawGen Opのファイル名端数（2026-02）
- 原因: Opを描画用にfloat化した値をそのままファイル名へ出していたため、`0.15f -> 0.150000005...` の表記が出る。
- 対応: 描画用(float)とファイル名用(double, 丸め済み)を分離し、ファイル名には丸め済みのOpを出すように修正。

## 追加実装: StrokeSampler 疑似線(Dot連続)の更新点(DotStep)スイープPNG出力（2026-02）
- 目的: 更新点（点間隔）をレンジ指定でスイープし、他の値を固定したまま疑似線（Dotを並べたスタンプ列）を生成してHiRes PNGを一括出力する。
- UI: `DotStep(px) start/end/step` と `Export PseudoLine (DotStep Sweep)` ボタンを追加。
- 実装: `Helpers/TestMethods.ExportPseudoLineDotStepSweepAsync` を追加し、`MainPage.xaml.cs` から1行委譲。
- 備考: 出力は指定のStart/End（X方向長）に対して `count=floor(len/step)+1` のdot数を自動算出して並べる。ファイル名に `dotstep{step}` を含める。

## 追加実装: InkDrawGen 疑似線(Dot連続)のDotStep(少数)入力と出力（2026-02）
- UI: `InkDrawGen/MainPage.xaml` に `dotStep start/end/step` を追加。
- 状態: `InkDrawGenUiState` に `DotStepX` を追加し、`InkDrawGenUiReader` で読み取る。
- 生成: `RunInkDrawJobsService` の `JobType=Line` で `dotStep>0` の場合、Lineストロークの代わりに Start→End のX方向に Dot を `dotStep` 刻みで並べて疑似線としてレンダ（少数指定可）。
- 命名: `extraSuffix` に `dotstepline-step{dotStep}` を付与してファイル名に残す。
- 追記: DotStepはレンジ指定でスイープし、dotStepごとに疑似線PNGを個別出力する（FirstOrDefaultで固定しない）。

## 修正: InkDrawGen Opスイープ(0.001刻み)とファイル名Op表記の安定化（2026-02）
- `OpacityRangeSpec` の正規化を0.001刻みに統一し、デバッグ残骸を削除。
- `FileNameBuilder` の `-Op` 表記を `0.###` に変更し、過剰桁や揺れを抑止。
- `RunInkDrawJobsService` の `opacityTag` 生成（小数第3位丸め）と意図コメントを整合。

## 修正: InkDrawGen Opスイープ(0.0001刻み)対応（2026-02）
- `OpacityRangeSpec.Normalize` の丸めを小数第4位に変更（0.0001刻み）。
- `FileNameBuilder` の `-Op` 表記を `0.####` に変更。
- `RunInkDrawJobsService` の `opacityTag` 生成（小数第4位丸め）へ変更し整合。

## 追加実装: InkDrawGen 2点疑似線(dot2)モード（2026-02）
- UI: `InkDrawGen/MainPage.xaml` に `2点疑似線（始点+更新点1つ）` チェックを追加。
- 状態: `InkDrawGenUiState` に `DotStepTwoPoints` を追加し、`InkDrawGenUiReader` で読み取る。
- 生成: `JobType=Line` かつ `dotStep>0` のとき、チェックONならDotを常に2点（`(x0,y0)` と `(x0+dotStep,y0)`）だけ描画してPNG出力。
- 命名: `extraSuffix` を `dot2-step{dotStep}` とし、通常のDot連続疑似線(`dotstepline-step`)と区別できるようにした。

## 追加実装: InkDrawGen 線(2点)生成ボタン（2026-02）
- UI: `InkDrawGen/MainPage.xaml` に `線(2点)生成` ボタンを追加。
- ハンドラ: `InkDrawGen/MainPage.xaml.cs` から `RunInkDrawJobsService.RunSingleLine2PointsAsync` に委譲。
- 動作: UIの `startX/Y` を始点、`endX/Y` を終点として2点のLine InkStrokeを生成する（dotStep疑似線モードを無効化して通常Line描画を明示）。

## 変更: InkDrawGenのファイル名へStartX/EndXを付与（2026-02）
- `RunInkDrawJobsService` の `extraSuffix` に `StartX{...}-EndX{...}` を追加し、出力PNGの区別を容易にした。

## 追加実装: InkDrawGen 線(2点) EndXスイープ出力（2026-02）
- UI: `endX sweep start/end/step` と `線(2点) EndXスイープ` ボタンを追加。
- 動作: `JobType=Line` の2点線を強制し、`endX` をレンジで差し替えて複数PNGを出力（dotStep疑似線は無効化）。

## 確定: N1始点ROIはDot Op=0.1795で完全一致（2026-02）
- DotLabのAlphaDiff（同一ROI切り出し）比較で、`S200 P1` の線(alignedN1)始点ROIに対して、単点Dotの `Op=0.1795`（同率で `Op=0.1796`）が `roi_diff_sum01=0` となり完全一致。
- 以降の検証は濃度Opを `0.1795` に固定し、更新点（点列/間隔）由来の差分に集中できる。

## 追加観測: 2点LineのEndXスイープでもN1はDot Opで完全一致できる（2026-02）
- `S200/DPI96/P1` の2点Lineを `Op=1` 固定で描画し、`endX=118..280 step18` をスイープしたところ、各endXごとに単点Dotの `Op` を調整することで `roi_diff_sum01=0`（完全一致）を達成。
- したがって、更新点数/線長に応じてN1の実効濃度スケール（単点Dotに対する必要Op）が変化する（2..12程度で顕著）。

## 追記: 更新点13点目以降でN1の最適Opが0.1795へ定常化（2026-02）
- EndXスイープを `EndX334` まで伸ばすと、`EndX316` / `EndX334` で `best_dot_opacity=0.17950`（完全一致）となり、更新点13点目以降で定常化することを確認。
- `EndX298`（更新点12）では `best_dot_opacity=0.17860`（完全一致）で、ここが定常化直前の遷移域。

## 追加実装: DotLabバッチ比較を全画像(w×h)のAlphaDiffに対応（2026-02）
- `Run batch and export CSV`（`RunLineN1VsDotOpacityBatchButton`）に `Use full image (w×h) AlphaDiff` オプションを追加。
  - ON: 画像全体のAlphaDiff統計で比較し、サマリbest選択は `diff_sum01` / `diff_nonzero_px`。
  - OFF: 従来通り左帯ROI(18px)の比較（N1用途）。

## 修正: InkDrawGen疑似線(dotstep)のスイープで小数stepが同名上書きになる問題（2026-02）
- `dotStep` のファイル名表記が `0.###` 丸めだったため、`step=0.1` などのスイープでサフィックスが同一になり、出力が上書きされて「同じ画像しか出ない」ように見えるケースがあった。
- `InkDrawGen/Helpers/RunInkDrawJobsService.cs` の `dot2-step{...}` / `dotstepline-step{...}` 表記を `0.#####` に拡張して区別できるようにした。

## 修正: InkDrawGen CSVバッチで小数座標が同名上書きになる問題（2026-02）
- CSVバッチは `start_x/end_x` を `double` として受け付けるが、ファイル名の `StartX/EndX` タグをint丸めしていたため、`EndX=108.0` と `108.5` のようなケースで出力が上書きされ「小数が効かない」ように見えることがあった。
- `InkDrawGen/Helpers/RunInkDrawJobsService.cs` の `StartX/EndX` タグを小数も出力する方式に変更し、ファイル名衝突を回避した。

## 確定: 更新点間隔（dotStep相当）は S の 0.09 倍（2026-02）
- 目的: `S200 -> 18px` を根拠に、各Sで更新点間隔が線形（比例）かを確認。
- 方法: `S=20..180 step20` で最短線の長さ（`EndX-StartX`）を `0.00001` 単位でスイープし、「単点→線（点が増える）」に変化する境界長 `L_threshold` を同定。
- 結果: `L_threshold = 0.09 * S` が明確に成立。
  - 例: `S100 -> 9.0`, `S180 -> 16.2`, `S200 -> 18.0`
  - よって更新点間隔（dotStep相当）は `dotStep(S) = 0.09 * S` とみなせる。

## 追記: 線描画の用語定義（S/P/Op/No/L/I/Np）（2026-02）
- 目的: L（DIP長）と更新点数（Np）が混線して混乱しやすいため、共通言語として定義を固定。
- 追記先: `docs/pencil-parity-playbook.md` と `docs/handover.md`
- 重要式: `I = 0.09 * S`, `Np = floor(L / I) + 1`

## 確定: N1 ROIの `Op(Np)` テーブルはSでほぼ不変（S100..200, 2026-02）
- 目的: `S200` で得た `Op(Np)` を、他の `S` にも共通テーブルとして適用できるか確認。
- 方法: `S=100/140/180/200` で `Np=2,4,5,6,8,10,12,14` の2点Line（`Op=1`）を生成し、Dot側をOp sweepで照合。量子化影響を減らすため `scale` を調整（例: `S100:20`, `S140:15`, `S180:11`, `S200:10`）。
- 結果: best `Op` はS間で同一系列に乗り、差が出ても概ね `1e-3` 程度で、最小となる帯域はほぼ一致。
- 結論: 少なくともこの条件範囲では、N1 ROIの `Op(Np)` は **Sに対してほぼ不変**として扱ってよい。

## 確定: dotN疑似線は2点Lineを線全体で目視再現できる（S10..200, 2026-02）
- `S=10/20/40/80/100/140/180/200` で、各Sの `I=0.09*S` と `Np` を揃えたペア（2点Line vs dotN）を生成して線全体を目視比較。
- 最終出力サイズを揃えるため `scale=2000/S`（例: `S200:10`, `S140:14`, `S100:20`, `S20:100`, `S10:200`）を採用。
- 結果: 上記範囲では、dotN疑似線と2点Lineは線全体としてほぼ同じ見た目となり、形状近似として採用できる。

## 次の課題: ストローク内P変動の再現（2026-02）
- これまでの検証はストローク内の `P` を一定に固定していた。
- 今後はストローク内で `P(t)` を変動させたときの挙動（更新点の寄与/濃度/欠け）を再現し、ISF由来の鉛筆ストロークをSkia等で再現できる状態に持っていく。

## 追加観測: 単点Dotにおける P と Op は同じ効き方ではない（2026-02）
- `Op` を下げると、点の縁が透明化していき見た目の直径が退縮する。
- `P` を下げた場合は透明化は起きるが直径は退縮せず、外周ほど「α>0の画素密度」が下がる（密度が0にはならない）。
- したがって `P -> Op(P)` の単純な置換モデルは成立しない可能性が高く、Skia等での再現では `P` はフォールオフ/密度側のパラメータとして扱う必要がある。

## 追加調査/修正: dot2疑似線でdotStepを変えても出力PNGが同一に見える件（2026-02）
- `dot2 dbg` ログにより、生成段階では `p1=(startX+dotStep, startY)` と `BoundingRect.X` が `17, 17.1, 17.2...` のように変化することを確認。
- レンダ時の座標変換順の不整合の可能性を減らすため、`InkOffscreenRenderService` のROI平行移動を `Scale * Translation` の順に統一した。

## 修正: DotLab ExportAlphaDiff が常にdiff=0になるケース（2026-02）
- `Export Alpha Diff (PNG vs PNG)` のCSVに入力2ファイルの `path/size/SHA256` を出力するようにして、入力が本当に別物か確認できるようにした。
- 入力PNGのSHA256が異なるのに `diff_max=0` となる場合があり、原因は `ImageAlphaDiff` が `SKBitmap.GetPixel().Alpha` に依存していたこと。
  - デコード経路によりAlphaが常に255のように見えるケースがあり、差分が常に0になっていた。
  - `SKBitmap.Pixels` 配列から `Alpha` を参照する方式へ変更して解消した。

## 修正: DotLab バッチ比較(LineN1VsDotN1BatchMatcher)でも同様にdiff=0になるケース（2026-02）
- `LineN1VsDotN1BatchMatcher` の `ExtractFullAlpha` / `ExtractLeftRoiAlpha` も `SKBitmap.GetPixel().Alpha` を使っていたため、同様に `SKBitmap.Pixels` 参照へ切り替えた。

## Verified: S200 dot2疑似線のdotStepは18.00が最適（2026-02）
- 条件: `2180x2020` / `dpi96` / `S200` / `P1` / `N1` / `scale10` / 透過PNG
- DotLabのalpha差分（全画素 `|A1-A2|`）で `diff_sum01` 最小の `dot2-step` を採用。
- スイープ結果より `dot2-step=18.00` が最適（`17.9`〜`18.9` 含む）。
- `dot2-step17.99` vs `dot2-step18.01` の差分可視化では、2つ目ドットの輪郭のみ差が出て円内部のもじゃもじゃは出ないため、残差は主に微小な位置差（平行移動）と解釈できる。

## 追加: InkDrawGenの線(2点) StartXスイープ（2026-02）
- 目的: `EndX` を固定したまま `StartX` を範囲でスイープして複数長さのオリジナル線を生成する。
- UI: `線(2点) StartXスイープ` ボタンを追加（入力欄は `endX sweep start/end/step` を流用）。
- 注意: ROIが `x=0,y=0,w=18,h=202` のように原点周辺のままだと、`StartX` が負の線はROI外になり空画像になる。スイープする線分がROIに入るよう `RoiX/RoiW` を調整する。

## 追加: InkDrawGenのdotN疑似線（StartX基準でN個固定）（2026-02）
- 目的: 指定した `dotStep`（更新値）と `N` 個数でDotを並べた疑似線を生成し、同じ長さのオリジナル線と目視比較する。
- 設定:
  - `JobType = Line`
  - `dotStep start/end/step` に `dotStep` を設定（スイープしたい場合は範囲指定も可）
  - `N個疑似線（StartX基準でN個固定）` をON
  - `dot count` に N を入力
  - `Op` は固定値でよければ `OpStart=OpEnd` にする
- 出力ファイル名: サフィックスに `dotN{N}-step{dotStep}` が付与される

### CSVバッチでの指定
- 列: `dot_step_fixed_count`（true/false）
- 列: `dot_step_count`（N。1以上）
- 別名: `dotStepFixedCount` / `dotStepCount` も使用可

### 個数Nのスイープ
- UI: `dot count start/end/step` を設定すると、Nを範囲でスイープして出力する（`N個疑似線` がONであること）。
- CSV: `dot_step_count_start` / `dot_step_count_end` / `dot_step_count_step` を指定すると、Nを範囲でスイープして出力する。

### 1) Radial 系
- `ExportRadialAlphaCsvButton_Click()`
  - `RadialFalloffExportService.ExportRadialAlphaCsvAsync(MainPage)` を追加。
  - `MainPage.xaml.cs` 側は `await RadialFalloffExportService.ExportRadialAlphaCsvAsync(this);` に委譲。

- `ExportRadialFalloffBatchSizesNsButton_Click()`
  - `RadialFalloffExportService.ExportRadialFalloffBatchSizesNsAsync(MainPage)` を追加。
  - `MainPage.xaml.cs` 側は1行委譲。

- `ExportRadialFalloffBatchPsSizesNsButton_Click()`
  - `RadialFalloffExportService.ExportRadialFalloffBatchPsSizesNsAsync(MainPage)` を追加。
  - `MainPage.xaml.cs` 側は1行委譲。

- `ExportRadialAlphaBatchPsSizesNsButton_Click()`
  - `RadialFalloffExportService.ExportRadialAlphaBatchPsSizesNsAsync(MainPage)` を追加。
  - P×S×Nごとに `dot512-material-...png` と `radial-alpha-...csv`（p_ge_*付き）を一括出力する。
  - CSVファイル名に `-bin{binSize}` を付与して、bin設定の取り違えを避ける。
  - `MainPage.xaml.cs` 側は1行委譲。

- `ExportRadialAlphaKneeSummaryButton_Click()`
  - `RadialFalloffExportService.ExportRadialAlphaKneeSummaryAsync(MainPage)` を追加。
  - 指定フォルダ内の `radial-alpha-*.csv` を走査して、`p_ge_50` / `p_ge_100` が 0.99 / 0.95 / 0.90 を下回る最初の半径（線形補間）を自動抽出し、`radial-alpha-knee-summary.csv` を出力する。
  - 低圧で `p_ge_*` が中心でも 0.99 を満たさず `r=0.5` に張り付くケースがあるため、最大値正規化の交点半径も出力する。
    - `p50_max` / `p100_max`
    - `p50_rMax099` / `p50_rMax095` / `p50_rMax090`
    - `p100_rMax099` / `p100_rMax095` / `p100_rMax090`

- InkDrawGen
  - `RadialAlphaProfileExportService` を追加。
    - `半径αCSV(PNG→CSV)`：選択したPNG（複数可）を半径方向bin集計して `radial-alpha-{png名}-bin{bin}.csv` を出力する（`mean_alpha01` と `p_ge_*` を含む）。
    - `半径α kneeサマリCSV`：フォルダ内の `radial-alpha-*.csv` を走査して交点半径（絶対0.99/0.95/0.90 + 最大値正規化rMax*）を抽出し `radial-alpha-knee-summary.csv` を出力する。maxが絶対閾値に届かない場合は空欄にする。
    - 半径解析の中心は既定で画像中心（(w-1)/2,(h-1)/2）とし、必要な場合のみ `α重心を中心にする` をONにしてα重心へ切替できる。
    - 低圧でMaxαが50未満になるケース（例: P0.4でMaxα=43、P0.1でMaxα=5）に対応するため、閾値に`5`を追加して`p_ge_5`列を生成し、kneeサマリ側も`p_ge_5`/`p_ge_40`を抽出対象に追加した。
    - PNG→CSVには`max_alpha`列も追加し、`p_ge_*`がゼロになる理由（Maxαが閾値未満）をCSVから判別できるようにした。

### 2) Center alpha summary
- `Helpers/ExportCenterAlphaSummary.cs`
  - `ExportCenterAlphaSummary.ExportAsync(MainPage)` を実装（ボタン処理本体を移植）。
- `ExportCenterAlphaSummaryButton_Click()`
  - `await ExportCenterAlphaSummary.ExportAsync(this);` に1行委譲。

### 3) Radial samples summary
- `Helpers/ExportRadialSamplesSummaryButton.cs`
  - 当初 `ExportRadialSamplesSummaryButton` という型名が XAML の `Button` と衝突（CS1061）し得たため、
    **型名を `ExportRadialSamplesSummary` に変更**して衝突回避。
  - `ExportRadialSamplesSummary.ExportAsync(MainPage)` を実装（処理本体を移植）。
- `ExportRadialSamplesSummaryButton_Click()`
  - `await ExportRadialSamplesSummary.ExportAsync(this);` に1行委譲。

### 4) Estimated paper noise
- `Helpers/ExportEstimatedPaperNoise.cs`
  - `ExportEstimatedPaperNoise.ExportAsync(MainPage)` を実装。
  - .NET 5 互換のため `double.IsFinite` は使用せず、`double.IsNaN/IsInfinity` を使用。
- `ExportEstimatedPaperNoiseButton_Click()`
  - `await ExportEstimatedPaperNoise.ExportAsync(this);` に1行委譲。

> 注意: 元コードには `noise` 算出がコメントアウトされた痕跡がありました。現在の実装は「動く形」で `noise` を算出しています。
> 厳密に「コメントアウトを外しただけの挙動」を求める場合は、目的のアルゴリズム（`bin` の扱い等）を再定義する必要があります。

### 5) PaperNoise crop 24
- `Helpers/ExportPaperNoiseCrop24.cs`
  - `ExportPaperNoiseCrop24.ExportAsync(MainPage)` を実装。
  - `IAsyncOperation<T>` を `await` するために `using System;` を追加（CS4036 回避）。
- `ExportPaperNoiseCrop24Button_Click()`
  - `await ExportPaperNoiseCrop24.ExportAsync(this);` に1行委譲。

### 6) Generate 系
- `Helpers/GenerateHelper.cs`
  - `GenerateHelper.Generate(MainPage)`
  - `GenerateHelper.GenerateOverwriteSamples(MainPage)`
  - `GenerateHelper.GenerateDotGrid(MainPage)`
  を実装（それぞれ元のボタン処理本体を移植）。

- `MainPage.xaml.cs`
  - `GenerateButton_Click()` → `GenerateHelper.Generate(this);`
  - `GenerateOverwriteSamplesButton_Click()` → `GenerateHelper.GenerateOverwriteSamples(this);`
  - `GenerateDotGridButton_Click()` → `GenerateHelper.GenerateDotGrid(this);`

### 7) Dot512 export 一式
- `Helpers/ExportDot512.cs`
  - `namespace StrokeSampler.Helpers` の `static class ExportDot512` として、下記4メソッドを**実装移植**:
    - `ExportDot512Async(...)`
    - `ExportDot512BatchAsync(...)`
    - `ExportDot512BatchSizesAsync(...)`
    - `ExportDot512SlideAsync(...)`

- `Helpers/ExportHelpers.cs`
  - 上記4メソッドを `ExportDot512.*` への **1行委譲**に置換（呼び出し互換維持）。

### 8) Normalized falloff

## （追記）SkiaTester / PencilDotRenderer（紙目・マスク・プレビュー強化）

### 目的
- UWPの鉛筆ドットに近い雰囲気（紙目で「乗りやすさ/乗りにくさ」が出る）をSkia側で検証できるようにする。

### 追加した主な機能（SkiaTester側）
- `Preview` の表示モードを増設
  - `8bit` / `float` / `paperMask` / `falloffWeight` / `maskUsed`
  - `invert mask view`（mask系プレビューの反転表示）
- `PaperMask` のマスクモードに `soft(outA)` を追加（しきい値2値ではなく、床付きの連続マスク）
- `MaskFalloff` の方式選択を追加
  - `none` / `gain@edge` / `th@edge`

### 追加した主な機能（PencilDotRenderer側）
- `PaperCapMode.CapOutAlpha`（紙目で outA の上限を作る）
- `BaseShapeMode`（ベース形状の切替）
  - `IdealCircle` / `PaperOnly`（UI上は `paper+falloff`）
- `PaperMaskMode` 拡張
  - `SoftOutAlpha`（連続マスク）
- `PaperMaskFalloffMode` 拡張
  - `StrongerAtEdge`（外縁ほど gain を強める）
  - `ThresholdAtEdge`（外縁ほど threshold を上げる）

### 現状の論点（未解決）
- `paperMask` / `maskUsed` プレビューでは外縁の変化（falloff連動）が確認できるが、`float`（最終 outA）表示では中心/外縁で同タイミングに見えるケースが残っている。
- 次の切り分け候補: `outA` の中間値（mask適用前/後）の可視化を追加して、どの段で差が消えているかを特定する。

### （追記）切り分け強化
- `PencilDotRenderer.RenderOutAlpha01Parts(...)` を追加し、`RenderOutAlpha01` と同一の計算経路で `outA_base`（mask前）/`outA_masked`（mask後）を取得できるようにした。
- `SkiaTester` の `Preview: outA_base/outA_masked` はUI側で再実装せず、本APIの結果を表示するように置換した（UI側再実装のズレを排除）。
- `SkiaTester` の `Preview: paperMask` は `_paperNoise` キャッシュではなく `TryLoadPaperNoiseFromUi()` の結果を使うように変更し、`maskUsed/outA_masked` と同じノイズ取得経路に統一した。

### （追記）paper-only の falloff を閾値化
- `PencilDotRenderer.PaperOnlyFalloffMode` を追加（`None` / `RadiusThreshold`）。
- `BaseShapeMode.PaperOnly` のとき、半径閾値 `PaperOnlyTh`（0..1）で `f(r)` を2値化できるようにした。
- `SkiaTester` UIに `PaperOnlyFalloff` と `PaperOnlyTh` を追加し、`Render/RenderOutAlpha01/RenderOutAlpha01Parts` に渡すようにした。

### DotLab（新WPFプロジェクト）を追加
- SkiaTester が検証UI/分岐で肥大化してきたため、Dot再現の最小実験環境として `DotLab` を新設した。
- SkiaSharp（`SkiaSharp.Views.WPF`）を継続採用し、紙目の高さはPNGのAlpha（0..1）を使用する。
- ノイズはタイルとして繰り返しサンプリングし、オフセット方向は既存検証の合意（X増加でノイズ右、Y増加でノイズ上）に合わせる。

## 追加実装: DotTesterの再現レンダラでPaperMaskを適用（2026-03）
- 背景: 外縁部の「起伏/落ち方」が再現側で平坦に見えるケースがあり、SkiaTester側にある `PaperMask`（outAマスク + 外縁フォールオフ）をDotTester側でも適用して調整できるようにする。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `PaperMaskMode` / `PaperMaskFalloffMode` を追加し、`DotReproRenderer.Options` に `PaperMaskMode/PaperMaskThreshold01/PaperMaskGain/PaperMaskFalloffMode` を追加。
  - `paperMask` はノイズ `z=(n01-mean)/std` を0..1へ写像して生成し、`outA` に乗算して適用する。
  - 適用順序は SkiaTester 互換で `outA → paperMask → cutoff`。
- `DotTester/MainWindow.xaml.cs`
  - `PaperMask` UI（mode/th/gain/edge）を読み取り、`DotReproRenderer.Options` へ配線。

## 追加実装: DotTesterで紙目強度を外縁ほど強める（edge boost）（2026-03）
- 背景: `PaperMask` は outA に直接マスクを掛けるため、微小な調整が難しく（小gainだと全域で減衰して中心まで抜けやすい）、外縁部だけを狙って調整できる方式が必要だった。
- 方針: 紙目の `Strength`（および ZNormの `strength*gain`）を、中心→外縁に向かって半径依存でスケールする。
  - 中心は1倍、外縁で `1 + boost` 倍。
  - `gamma` で立ち上がりの形を調整。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `DotReproRenderer.Options` に `EnablePaperNoiseEdgeBoost` / `PaperNoiseEdgeBoost` / `PaperNoiseEdgeBoostGamma` を追加。
  - kMean事前計測と描画の両方で同じスケールを適用し、再正規化の不整合を避けた。
- `DotTester/MainWindow.xaml` / `DotTester/MainWindow.xaml.cs`
  - edge boostのON/OFFと `boost/gamma` 入力を追加してOptionsへ配線。

## 変更: DotTesterのUI変更を即時レンダへ反映（2026-03）
- 背景: パラメータ調整で都度 `Render` ボタンを押すのが手間なため、UI変更時に自動で再レンダしたい。
- 対応: `DotTester/MainWindow.xaml.cs` で主要入力（`NumberBox/ComboBox/CheckBox/TextBox`）の変更イベントを購読し、デバウンス（150ms）で自動レンダをスケジュールする。
  - 自動レンダ時はエラーでダイアログ連発しないよう、Status表示のみ更新。
  - `Render (N=1)` ボタンは従来通り明示実行（エラーはダイアログ表示）。

## 追加実装: DotTesterで紙目タイル極値点の一致度スコアを算出（2026-03）
- 背景: タイル内の最明/最暗画素が周期的に現れる点（例: 明16点・暗18点）の値が一致すれば、紙目の位相/強度が視覚的に近いと判断しやすい。
- 対応: `DotTester` にタイル座標（Bright/Dark）とexpected解釈（Auto/alpha/白背景255-輝度）を指定して、該当点群の誤差（MAE/RMSE/MaxAbs、上位差分）を表示する評価機能を追加。
- `DotTester/Helpers/TileExtremaMatchEvaluator.cs`
  - タイル周期からキャンバス上の該当座標を列挙（texel中心を厳密にサンプルできる点のみ）し、expected/renderedの値を比較してスコア化。
- `DotTester/MainWindow.xaml` / `DotTester/MainWindow.xaml.cs`
  - 座標入力とEvaluateボタンを追加。

## 追加実装: DotTesterでタイル極値点スコアを目的関数にした総当たり探索（2026-03）
- 目的: 紙目の見た目合わせを「目視」から「スコア最小化」へ寄せ、offset/strength/gain等の探索を自動化する。
- `DotTester/MainWindow.xaml`
  - `Sweep` UIを追加（Offset/Strength/Gainの範囲・step、kMean stride、Search/Cancel、Apply）。
- `DotTester/MainWindow.xaml.cs`
  - 非同期で候補を総当たりし、タイル極値点のMAEが最小になる設定を探索。
  - `Apply`有効時は最良値をUIへ反映し、最後にフルレンダでRenderedを更新。

### 追記（2026-03）
- Sweepの進捗が分かるように、`SweepProgressBar` と `SweepProgressTextBlock` を追加。
- 探索対象に `FalloffScale` / `FalloffRNormScale` / `FalloffGamma` も追加し、必要に応じて総当たり可能にした。

### 追記（2026-03）
- Sweepを段階探索（Coarse→Refine）に対応。
  - 粗探索: `SweepCoarseOffsetStepNumberBox`（offsetの間引き）と `SweepCoarseStepMultiplierNumberBox`（各step倍率）で探索点を削減。
  - 局所探索: `SweepRefineTopKNumberBox`（上位K）をseedに、`SweepRefineOffsetRadiusNumberBox`（offset±）と `SweepRefineStepsRadiusNumberBox`（各step±N）で近傍のみを細かく再探索。
- Sweepを並列評価に対応。
  - `SweepParallelDegreeNumberBox` で最大同時実行数を指定（既定4）。
- 反復上限をUIで設定可能に変更。
  - `SweepMaxItersNumberBox`（既定200万）。段階探索では上限内に収まるseed数に自動調整。

### 追記（2026-03）
- Sweep結果の比較上位10件をCSV出力。
  - `sweep-top10-settings-*.csv`: 上位10件の設定値（offset/strength/gain/falloff等）とMAE。
  - `sweep-top10-score-*.csv`: 上位10件のスコア詳細（MAE/RMSE/MaxAbs/平均expected/rendered等）。
  - 出力先はExpected PNGと同じフォルダ配下の `SweepResult`。
  - `sweep-top10-points-*.csv`: 上位10件×34点の点詳細（expected/rendered/diff）を点ごとに1行で出力。

### 追記（2026-03）
- InkDrawGenに「紙目周期解析CSV(PNG)」を追加。
  - PNGのαから自己相関（X/Y方向の相関係数・MAE）を算出して、周期候補をCSV/ダイアログ/ログに出力。

### 追記（2026-03）
- 紙目タイル周期の確定: **435pxではなく436px**。
  - 周期（タイルサイズ）と切り出し位相（開始位置）を正しく合わせると、DotTesterの基本設定（falloff系=1）でもDotLabの差分で規則的な格子が消えることを確認。
  - 切り出し位置は縦横で2〜3px程度の補正が効いた。

### 追記（2026-03）
- DotTesterに edge帯の「Expected α × Noise × Falloff」散布図CSV出力を追加。
  - 目的: 外縁部での「単純乗算モデル」 vs 「閾値/減算/マスク系モデル」や、`Nearest` vs `Bilinear` の影響を当てるための観測データを出す。
  - UI: `Edge scatter`（a8 min/max, stride）→ `Export CSV`
  - 出力: Expected PNGと同じフォルダ配下の `SweepResult/edge-scatter-*.csv`
  - CSV列: `expected_a8` / `rendered_a8` / `n01_nearest` / `n01_bilinear` / `f` / `base_a01`（ほか座標/距離）

### 追記（2026-03）
- DotTesterに `Bicubic` サンプリング（Catmull-Rom）と outA の実験モデル `Wall-through` を追加。
  - 目的: 4) サンプラー差（Nearest/Bilinear/Bicubic）と、1) 合成式差（掛け算系 vs 減算/閾値系）をUI切替で素早く試す。
  - UI:
    - `Sampling: Bicubic`
    - `Model: Wall-through` / `WallK`
  - モデル概要: `wall = 1 - n01`、`outA = clamp((baseA01 - wall) / WallK, 0..1)`（N=1）
  - `edge-scatter-*.csv` に `n01_bicubic` 列を追加し、同一点の `Nearest/Bilinear/Bicubic` を同時に比較できるようにした。
- `DotTester/Helpers/DotReproRenderer.cs`
  - フルレンダを回さず、指定座標のα(8bit)だけ評価できる`PointEvaluator`を追加。
  - ZNormalizedでkMean再正規化が有効な場合、探索用にkMeanをstride指定で粗く計算可能。
- `DotTester/Helpers/TileExtremaMatchEvaluator.cs`
  - 点列挙(`EnumeratePoints`)とexpectedのα換算(`GetExpectedAlphaByte`)を公開し、探索で再利用。

## 修正: kernel-sweep→normalized-falloff変換を線形補間に変更（2026-03）
- 背景: `kernel-sweep`（`dx_px`）から `normalized-falloff`（`r_norm`）へ落とす際に、scaleや欠損の影響で最近傍1点を拾うと落ち方が段付きになりやすい。
- 対応: 変換時に `dxTarget=r*scale` の前後2点（`dx0<dxTarget<dx1`）から線形補間して `alpha(r_norm)` を生成する。
- `InkDrawGen/Helpers/KernelSweepToNormalizedFalloffExportService.cs`
  - `ReadKernelSweepAlphaByRNormAsync` の近傍探索（最寄り点）を、前後2点の線形補間に置換（範囲外は端でクランプ）。

### DotLabの新モデル（壁貫通モデル）
- GIMPの手動分解で得た仮説を実装優先の形に落とし込み、以下の式を `DotLab.Rendering.DotModel` として実装した。
  - `B = P * f(r)`
  - `H = T(x,y)`（紙目の高さ＝alpha）
  - `wall = 1 - H`
  - `V = clamp((B - wall) / k, 0..1)`
  - `outA = 1 - (1 - V)^N`
- まずは中間値 `V/B/H/wall/outA` のプレビュー表示を優先し、SkiaTester側で起きた「UI計算と本体計算のズレ」を避ける（同一ループで算出した配列を表示）。
- `Helpers/ExportNormalizedFalloffService.cs`
  - `ExportNormalizedFalloffService.ExportAsync(MainPage mp)` を実装（旧 `ExportHelpers.ExportNormalizedFalloffAsync` の処理本体を移植）。
  - 内部で使う `TryParseFalloffFilename/TryParseFalloffCsv/SampleLinear/BuildNormalizedFalloffCsv` は `StrokeHelpers` にあるため、`using static StrokeSampler.StrokeHelpers;` を使用。
- `Helpers/ExportHelpers.cs`
  - `ExportNormalizedFalloffAsync(MainPage mp)` を **1行委譲**に変更：`=> ExportNormalizedFalloffService.ExportAsync(mp);`

### 9) ExportPng
- `Helpers/ExportPngService.cs`
  - `ExportPngService.ExportAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)` を実装（旧 `ExportHelpers.ExportPngAsync` の処理本体を移植）。
  - `FileSavePicker`/`CanvasRenderTarget.SaveAsync` の `await` を成立させるため、`System.Runtime.InteropServices.WindowsRuntime` 等の using を追加。
- `Helpers/ExportHelpers.cs`
  - `ExportPngAsync(...)` を **1行委譲**に変更：`=> ExportPngService.ExportAsync(...)`

## ビルド・検証状況
- 変更の節目ごとにビルドを実行し、最終時点でビルド成功。

## よく出た注意点（再発防止）
- 型名が XAML 側の `Button` 名などと衝突すると、`CS1061` になり得る。
  - 例: `ExportRadialSamplesSummaryButton` → `ExportRadialSamplesSummary` にリネーム。
- UWP の `IAsyncOperation<T>` をヘルパー側で `await` する場合、環境により `CS4036` が出ることがある。
  - `using System;` や `System.Runtime.InteropServices.WindowsRuntime` の追加で解消したケースあり。
- `.NET 5` では `double.IsFinite` が使えないため、`IsNaN/IsInfinity` を使う。

## 主な変更ファイル一覧
- `MainPage.xaml.cs`（イベントハンドラの1行委譲化）
- `Helpers/RadialFalloffExportService.cs`（Radial系CSV/Batchの委譲先追加）
- `Helpers/ExportCenterAlphaSummary.cs`（新規/実装追加）
- `Helpers/ExportRadialSamplesSummaryButton.cs`（型名変更＋実装追加）
- `Helpers/ExportEstimatedPaperNoise.cs`（実装追加、.NET5互換修正）
- `Helpers/ExportPaperNoiseCrop24.cs`（実装追加、CS4036対策）
- `Helpers/GenerateHelper.cs`（実装追加）
- `Helpers/ExportDot512.cs`（実装追加）
- `Helpers/ExportHelpers.cs`（Dot512系を1行委譲化）
- `Helpers/ExportNormalizedFalloffService.cs`（実装追加）
- `Helpers/ExportPngService.cs`（実装追加）

## 次に起こり得る作業
- `ExportHelpers` に残る他の export（`ExportPngAsync` など）も同様に個別ファイル化するか検討。
- `ExportEstimatedPaperNoise` のアルゴリズム整合（「意図通りのF(r)・noise推定」）が必要なら仕様を詰めて調整。

---

## Aligned line N1 vs aligned-dot-index N1: 点→線先頭の近似（2026-02）

### 目的
- 線描画（`N1N2` の先頭領域）と、単点（`aligned-dot-index`）が「同じ領域」を切り出せる状態を作り、
  - 形（2値マスク）
  - 濃さ（α値）
 について「最も近い組み合わせ（P対応）」を探索・可視化する。

### 追加実装（StrokeSampler 側）
- 単点（`aligned-dot-index`）を **単一Nのみ** 出力する経路を追加。
  - `Helpers/ExportS200Service.cs`: `ExportAlignedDotIndexSingleAsync(...)`
  - `Helpers/AlignedJobsCsv.cs`: CSV拡張（`aligned_mode`/`single_n`）
  - `MainPage.xaml.cs`: `aligned_mode=dot-index-single` のジョブを解釈して P sweep を回す。
- 運用: `runTag` に `aligned-dot-index` を含めると DotLab 側が単点候補として認識。

### 追加実装（DotLab 側）
- `DotLab/Analysis/LineN1VsDotN1Matcher.cs`: フォルダ内の
  - 線候補（`-alignedN1` かつ `N1N2` を含む）
  - 単点候補（`aligned-dot-index` / `aligned-dot-index-` / `aligned-dot-index-single` を含む）
 から、線1枚ごとに最も近い単点を探索して CSV 化。

#### 比較ROI（確定）
- X: 左端 `18px`（`RoiWidthPx=18`）
- Y: `435..1591`（`RoiY0=435`, `RoiY1Exclusive=1592`）
- αのみ使用（RGBは無視）。

#### 形状比較（2値化）
- 2値化閾値 `th = 1,2,3,4` を同時に算出。
- `IoU`/`mismatch`/`coverage`/`inter`/`union` を出力し、best/second を記録。
- 「空っぽ一致（union=0でIoU=1）」が best にならないよう自動排除を導入。
  - `MinUnionGate=200`
  - `minCov=200/ROI画素数` を line/dot 双方に適用（ON画素が200px未満の候補は除外）

#### 濃さ補正の推定
- 形が近い候補に対して、線≒k×点 となるスケール係数 `alpha_k`（最小二乗）を推定。
- `alpha_l1_scaled`（k適用後の平均|α差|）を出力。
  - 形が近い（IoUが高い）候補ほど、単純なαスケールで見た目が寄る可能性がある。

#### 可視化（ヒートマップ/差分強度）
- best組み合わせについて、ROI内の2値マスク差分を画像化（thごと）。
  - dotのみON: 青 / lineのみON: 緑 / 両方ON: 黒 / 両方OFF: 白
  - 出力: `lineN1-vs-dotN1-heatmap-th{th}-P{lineP}.png`
- さらに **全幅版（180px）**も追加出力（`-fullw-` 付き）。
  - 出力: `lineN1-vs-dotN1-heatmap-th{th}-fullw-P{lineP}.png`
- α差分の大きさ（|α_line-α_dot|）を赤強度で可視化（th=1, ROI版＋全幅版）。
  - 出力: `lineN1-vs-dotN1-diffmag-th1-P{lineP}.png`
  - 出力: `lineN1-vs-dotN1-diffmag-th1-fullw-P{lineP}.png`

### 今後ひっくり返りにくい事実（観測）
- ヒートマップにより、差異は主に輪郭（境界）に出る。
- P<=0.8 では黒以外に青/緑が両方出るが、P=0.9/1.0 では黒以外が青のみ（=点側のON領域が外側に出る傾向）。
  - 高圧帯域のIoU低下は「線が太い」より「点側が外に出る（薄縁/外周ONが残る）」寄りの可能性。

（追記・定量化）
- `th=1` の best について、差分領域を over/under として定量化した。
  - `over_area`: dotのみON（ヒートマップ青）
  - `under_area`: lineのみON（ヒートマップ緑）
  - `over_alpha_median` / `under_alpha_median`: それぞれの領域でのα差中央値
- 代表例: P=0.9/1.0 では `under_area=0` かつ `over_alpha_median=1..2` となり、IoU低下要因が「点側の極薄縁（α=1〜2）の過剰」で説明できる。

（追記・低圧対応）
- 低圧 `P=0.1` は、空っぽ/疎すぎる一致の除外ゲート（union/coverage）が厳しすぎて候補が全落ちしやすかった。
- 対応として、`th=1` だけゲートを緩め（union>=20、ON>=20px相当）、`th=2/3` は従来の厳しいゲート（union>=200、ON>=200px相当）を維持した。

### 次の作業候補
- `th=2/3` で union/coverage が十分な条件での best がどう変わるかを再評価。
- `alpha_k` を適用した後に 2値化/差分強度を再可視化（補正が効くかの確認）。

## 追加実装: DotTester SweepのWall-through拡張 + ROI評価 + CSV出力整理（2026-03）
- `DotTester/Helpers/DotReproRenderer.cs`
  - `OutAlphaModel=WallThrough` でも `Strength`（+`EdgeBoost`）で紙目寄与を調整できるよう、紙目 `n01` を `mean` へブレンドしてから壁貫通計算へ入れるようにした（中心の紙目が立ちやすい問題の緩和）。
- `DotTester/MainWindow.xaml`
  - Sweep項目に `WallK` / `Wall base×` / `Wall bias` の探索（ON + min/max/step）を追加。
  - Sweepの評価点として ROI(xywh) を指定できるUIを追加（stride、詳細CSV最大行数も指定）。
- `DotTester/MainWindow.xaml.cs`
  - Sweep候補に `WallK/base/bias` を追加し、staged（coarse/refine）も含めて探索できるようにした。
  - Sweepの評価点を「タイル極値点（従来）」または「ROIサンプル（stride間引き）」へ切り替え可能にした。

## 設計メモ追記: Pスイープしたkernelの整理の補正（2026-03）
- regime 分けは、現時点では `P0.1〜0.6` と `P0.7〜1.0` の2群として見る仮説を優先する。
- 高Pほど `plateau_count`（階段の段数）は増え、`mean_tread_px`（平均踏面）は小さくなる。
- 高Pほど `mean_riser01`（平均蹴上）は小さくなる。
- `last_nonzero_r_px` は低P側で観測限界 `8000` に張り付き、高P側でのみ実終端が見え始める。
- 強変化点の半径位置は比較的安定している一方、その `plateau_index`（段番号）は `P` の増加とともに増える傾向がある。
- `second_to_first_tread_ratio` は、初段だけの特殊性を直ちに意味する指標というより、量子化された滑らかなカーブを局所段差群として見たときの「初期区間の階段化のされ方」を表す補助指標として解釈する方針を優先する。
- したがって、初段と2段目の比率は単独で解釈するより、周辺の複数段 (`1〜3段目` や `2〜5段目`) を含む局所系列の一部として扱う方が自然である。

### 優先度付き調査項目（2026-03）
1. 強変化点の位置と `P` の関係を調べる。
   - `r_norm` / `r_px` / `plateau_index` のどれが最も安定な指標かを確認する。
2. 前半直線区間の変化率と `P` の関係を調べる。
   - `P0.1` を除き、初段以降から強変化点の手前までがほぼ直線に見える仮説を確認する。
3. 中間区間の変化率と `P` の関係を調べる。
   - 強変化点前後で勾配がどう切り替わるかを比較する。
4. 後半区間の変化率と `P` の関係を調べる。
   - 終端付近の段差群を、局所的な勾配としてどの程度安定して見られるかを確認する。
5. 初段の長さの法則を調べる。
   - `initial_tread_px` を独立指標として扱い、全体平均ではなく局所閾値 crossing として解釈できるかを確認する。
6. 実終端の法則を調べる。
   - `last_nonzero_r_px` / `last_nonzero_r_norm` が高P帯でどう減少し始めるかを確認する。

### 調査開始メモ（第1優先: 強変化点の位置, 2026-03）
- 入力: `DotLab/Kernel/kernel-sweep-wide-count10-stair-detail.csv`
- `P0.1` を除く各 `P` について、`plateau_index >= 2` の範囲で `log2_tread_ratio_to_prev` が最もマイナス側に大きい行を強変化点候補として見た。
- 暫定観測:
  - `P0.2`: `plateau_index=3`, `r_norm=44.1875..58.125`
  - `P0.3`: `plateau_index=4`, `r_norm=40.55..47.125`
  - `P0.4`: `plateau_index=5`, `r_norm=39.7125..43.475`
  - `P0.5`: `plateau_index=7`, `r_norm=38.0375..40.4625`
  - `P0.6`: `plateau_index=8`, `r_norm=35.9625..38.7375`
  - `P0.7`: `plateau_index=10`, `r_norm=37.95..39.225`
  - `P0.8`: `plateau_index=12`, `r_norm=37.65..38.675`
  - `P0.9`: `plateau_index=13`, `r_norm=37.0875..38.25`
  - `P1.0`: `plateau_index=14`, `r_norm=36.8125..38.1`
- 暫定結論:
  - `P0.5〜1.0` では、強変化点の半径位置は概ね `r_norm ≈ 36.8〜40.5` に集まり、位置自体は比較的安定している。
  - 一方で `plateau_index` は `P` の増加とともに概ね単調増加しており、`P` は強変化点の位置そのものより「そこへ至る段数」に強く効いている可能性が高い。
  - `P0.2〜0.4` は強変化点がやや外側へずれており、`P0.1〜0.6` と `P0.7〜1.0` の regime 仮説と整合する。

### 調査開始メモ（第2優先: 前半直線区間の変化率, 2026-03）
- 入力: `DotLab/Kernel/kernel-sweep-wide-count10-stair-detail.csv`
- 定義:
  - 各 `P` について、`plateau_index=2` から「第1優先で抽出した強変化点の1つ手前」までを前半直線区間の候補とした。
  - 各 plateau の頂点を `(start_r_norm, level_eff01)` とみなし、この区間に対して一次回帰の傾き `slope` を求めた。
- 暫定結果（`P0.2` は前半サンプル不足のため参考外）:
  - `P0.3`: `slope ≈ -0.000944`, `R^2 ≈ 1.000000`
  - `P0.4`: `slope ≈ -0.001279`, `R^2 ≈ 0.999995`
  - `P0.5`: `slope ≈ -0.001548`, `R^2 ≈ 0.999997`
  - `P0.6`: `slope ≈ -0.001821`, `R^2 ≈ 1.000000`
  - `P0.7`: `slope ≈ -0.002050`, `R^2 ≈ 0.999994`
  - `P0.8`: `slope ≈ -0.002187`, `R^2 ≈ 0.999967`
  - `P0.9`: `slope ≈ -0.002257`, `R^2 ≈ 0.999915`
  - `P1.0`: `slope ≈ -0.002216`, `R^2 ≈ 0.999800`
- 暫定結論:
  - `P0.3〜1.0` では、前半直線区間は非常に高い直線性を持つ（`R^2 ≈ 0.9998〜1.0`）。
  - 傾きの絶対値は `P` の増加とともに概ね大きくなり、前半区間の減衰は高Pほど急になる。
  - ただし `P0.9→1.0` ではわずかな頭打ち/揺れがあり、完全な一次比例ではなく、量子化や regime 境界の影響を含む可能性がある。
  - したがって現時点では、「前半直線区間の変化率は `P` に対して概ね単調増加し、少なくとも `P0.3〜0.9` では非常に素直な相関を持つ」が、完全な単一式へ固定するにはまだ保留、という整理が妥当。

### 調査開始メモ（第3優先: 中間区間の変化率, 2026-03）
- 入力: `DotLab/Kernel/kernel-sweep-wide-count10-stair-detail.csv`
- 定義:
  - 各 `P` について、第1優先で抽出した強変化点の直後から始まる plateau 群のうち、`|log2_tread_ratio_to_prev| <= 0.15` を満たす連続区間を中間区間の候補とした。
  - 各 plateau の頂点を `(start_r_norm, level_eff01)` とみなし、この区間に対して一次回帰の傾き `slope` を求めた。
- 暫定結果（`P0.2` は中間サンプル不足のため参考外）:
  - `P0.3`: `middle=5〜14`, `slope ≈ -0.003251`, `R^2 ≈ 0.993935`
  - `P0.4`: `middle=6〜24`, `slope ≈ -0.004865`, `R^2 ≈ 0.993456`
  - `P0.5`: `middle=8〜38`, `slope ≈ -0.006331`, `R^2 ≈ 0.992347`
  - `P0.6`: `middle=10〜55`, `slope ≈ -0.008150`, `R^2 ≈ 0.990878`
  - `P0.7`: `middle=11〜76`, `slope ≈ -0.010018`, `R^2 ≈ 0.988018`
  - `P0.8`: `middle=13〜100`, `slope ≈ -0.011774`, `R^2 ≈ 0.983844`
  - `P0.9`: `middle=15〜125`, `slope ≈ -0.013683`, `R^2 ≈ 0.978115`
  - `P1.0`: `middle=16〜64`, `slope ≈ -0.010137`, `R^2 ≈ 0.994934`
- 暫定結論:
  - 中間区間も一次近似はかなり効くが、前半区間より直線性は少し落ちる（`R^2 ≈ 0.978〜0.994`）。
  - `P0.3〜0.9` では傾きの絶対値は概ね単調増加しており、高Pほど中間区間の減衰も急になる。
  - ただし `P1.0` は `P0.9` より傾きが緩くなっており、中間区間では単純な単調式よりも regime 境界や量子化後の局所構造の影響が強い可能性がある。
  - したがって、中間区間の変化率は「概ねPに相関するが、前半区間ほど素直ではない」という整理が妥当。

### 調査開始メモ（第4優先: 後半区間の変化率, 2026-03）
- 入力: `DotLab/Kernel/kernel-sweep-wide-count10-stair-detail.csv`
- 定義:
  - 各 `P` について、終端の1段手前から逆向きに見て、`|log2_tread_ratio_to_prev| <= 0.07` を満たす terminal plateau 群を後半区間の候補とした。
  - 各 plateau の頂点を `(start_r_norm, level_eff01)` とみなし、この区間に対して一次回帰の傾き `slope` を求めた。
- 暫定結果（`P0.2` は後半サンプル不足のため参考外）:
  - `P0.3`: `tail=10〜14`, `slope ≈ -0.003755`, `R^2 ≈ 0.999751`
  - `P0.4`: `tail=15〜24`, `slope ≈ -0.005627`, `R^2 ≈ 0.999696`
  - `P0.5`: `tail=22〜38`, `slope ≈ -0.007416`, `R^2 ≈ 0.999569`
  - `P0.6`: `tail=30〜55`, `slope ≈ -0.009645`, `R^2 ≈ 0.999444`
  - `P0.7`: `tail=39〜76`, `slope ≈ -0.012140`, `R^2 ≈ 0.999208`
  - `P0.8`: `tail=50〜99`, `slope ≈ -0.014699`, `R^2 ≈ 0.998876`
  - `P0.9`: `tail=59〜124`, `slope ≈ -0.017605`, `R^2 ≈ 0.998228`
  - `P1.0`: `tail=67〜151`, `slope ≈ -0.020838`, `R^2 ≈ 0.997126`
- 暫定結論:
  - 後半区間は、前半・中間よりもさらに素直に `P` と相関しており、`P0.3〜1.0` で傾きの絶対値はほぼ単調増加する。
  - 直線性も高く、`R^2 ≈ 0.997〜0.9998` で、量子化後の plateau 群として見ても一次近似がかなり強く効く。
  - 簡易な線形近似では `tail_slope ≈ -0.0243 * P + 0.0043`（`R^2 ≈ 0.990`）となり、少なくとも観測範囲では「後半区間の減衰勾配はP増加に応じてほぼ線形に急になる」とみなせる可能性が高い。

### 調査開始メモ（第5優先: 初段の長さの法則, 2026-03）
- 入力: `DotLab/Kernel/kernel-sweep-wide-count10-stair-detail.csv`
- 観測対象:
  - `initial_tread_px`（`plateau_index=1` の `tread_px`）
  - `initial_tread_r_norm`（`plateau_index=1` の `end_r_norm`）
  - 第1優先の強変化点位置
  - 第2優先の前半直線区間勾配
- 暫定結果:
  - `initial_tread_r_norm`
    - `P0.2=20.475`
    - `P0.3=10.225`
    - `P0.4=10.4875`
    - `P0.5=0.725`
    - `P0.6=1.875`
    - `P0.7=3.6`
    - `P0.8=1.525`
    - `P0.9=2.0875`
    - `P1.0=1.3375`
  - 強変化点開始半径に対する比 `initial_tread_r_norm / major_start_r_norm`
    - `P0.2≈0.463`
    - `P0.3≈0.252`
    - `P0.4≈0.264`
    - `P0.5≈0.019`
    - `P0.6≈0.052`
    - `P0.7≈0.095`
    - `P0.8≈0.041`
    - `P0.9≈0.056`
    - `P1.0≈0.036`
  - 前半勾配から単純推定した閾値距離 `riser / |front_slope|` と比べると、`initial_tread_r_norm` は概ねそれより短く、特に `P0.5` では比が `≈0.094` と極端に小さい。
- 暫定結論:
  - 初段の長さは `P` に対して単調ではなく、単独の数式へ素直には乗らない。
  - ただし regime はかなり明確で、`P0.2〜0.4` では初段が非常に長く、`P0.5〜1.0` では強変化点に対してごく短い初期区間へ急減する。
  - 初段の長さは、前半直線区間の勾配や平均蹴上だけでは説明しきれず、頂上近傍の局所的な丸まり/平坦化の影響を別に受けている可能性が高い。
  - したがって `initial_tread` は、前半・中間・後半の勾配法則とは別の「初期閾値 crossing 指標」として独立に保持するのが妥当。

### 調査開始メモ（第6優先: 実終端の法則, 2026-03）
- 入力: `DotLab/Kernel/kernel-sweep-wide-count10-stair-detail.csv`
- 観測対象:
  - 各 `P` の最終 plateau（最後の `plateau_index`）の `end_r_norm`
  - `headroom = 100 - end_r_norm`
  - 最終 plateau の `tread_px`
- 暫定結果:
  - `P0.1〜0.7`: `end_r_norm = 100` で観測限界に張り付き、実終端は見えていない。
  - `P0.8`: `end_r_norm = 99.1625`, `headroom = 0.8375`, `last_tread = 39`
  - `P0.9`: `end_r_norm = 98.55`, `headroom = 1.45`, `last_tread = 29`
  - `P1.0`: `end_r_norm = 98.325`, `headroom = 1.675`, `last_tread = 22`
- 暫定結論:
  - 実終端の法則は、低P側では観測限界 `8000px` に打ち切られているため、この範囲からは決められない。
  - 少なくとも高P側 (`P0.8〜1.0`) では、`P` が高いほど `headroom` は増加し、実終端は手前へ寄る。
  - 同時に最終 plateau の `tread_px` は `39 → 29 → 22` と縮んでおり、高Pほど「終端へ落ちる最後の区間」も短くなる。
  - 高P3点だけの簡易近似では `headroom ≈ 4.19 * P - 2.45`（`R^2 ≈ 0.93`）となるが、点数が少ないため法則としては暫定扱いに留める。
  - 現時点では、「実終端は高P側でのみ観測可能であり、高Pほど終端は早く来る」が最も堅い整理である。

### 設計メモ: 実終端の分類整理（2026-03）
- 実終端は、終盤の安定区間の勾配を保ったまま `level_eff01` が 0 に到達する点として考えるのが自然。
- `level_eff01` が 0 に達した後は、その外側も 0 を維持するものとして扱う。
- ただし観測上の終端には、真の終端と観測範囲上限による打ち切り終端が混在するため、両者を分けて扱う必要がある。
- 終端は少なくとも次の3種類に分類して扱う。
  - 真の終端: 終盤安定勾配の延長で実際に 0 に到達した点。
  - 観測打ち切り終端: 実際には 0 に達していないが、観測範囲上限 (`r_norm=100`, `8000px`) で打ち切られた点。
  - 準終端: まだ 0 ではないが、終盤安定区間に入っており、その勾配延長で真の終端が推定できそうな点。
- 現在の観測では、`P0.1〜0.7` は観測打ち切り終端、`P0.8〜1.0` は準終端〜真の終端に近い挙動として扱うのが自然。
- 高P側では、終盤勾配が急になるほど `headroom` が増え、実終端は手前に寄るという理解が最も自然。
- 実務上は終端指標を次のように分けて保持する。
  - `observed_terminal`: 観測上の終端
  - `censored_terminal`: 観測打ち切り終端
  - `estimated_true_terminal`: 終盤勾配から推定した真の終端

### 設計メモ: 初段余り仮説を調べる終端基準指標案（2026-03）
- `observed_terminal_r_px` / `observed_terminal_r_norm`
  - 観測上の終端位置。打ち切り終端も含む。
- `censored_terminal`
  - 観測上限 (`r_norm=100`, `8000px`) に達して打ち切られているかどうか。
- `estimated_true_terminal_r_px` / `estimated_true_terminal_r_norm`
  - 終盤安定区間の一次回帰から `level_eff01=0` を外挿した推定真終端。
- `tail_reference_tread_px`
  - 終盤安定区間の代表踏面。現時点では `median(tread_px)` を優先して使う。
- `terminal_phase_offset_px`
  - `estimated_true_terminal_r_px mod tail_reference_tread_px`。
  - 終端から逆向きに階段を並べたときの位相余りを見る指標。
- `terminal_phase_complement_px`
  - `tail_reference_tread_px - terminal_phase_offset_px`。
  - 余りが先頭側/末尾側どちらへ押し出されるかの補助確認に使う。
- `initial_tread_px`
  - 初段の長さそのもの。余り仮説の説明対象。
- `initial_vs_terminal_phase_error_px`
  - `min(|initial_tread_px - terminal_phase_offset_px|, |initial_tread_px - terminal_phase_complement_px|)`。
  - 単純な終端剰余モデルで初段長を説明できるかを確認する指標。

### 調査開始メモ（終端基準の初段余り仮説, 2026-03）
- 入力: `DotLab/Kernel/kernel-sweep-wide-count10-stair-detail.csv`
- 手順:
  - 第4優先で抽出した終盤安定区間から `estimated_true_terminal_r_px` を一次回帰で外挿した。
  - 同じ終盤安定区間の `median(tread_px)` を `tail_reference_tread_px` とした。
  - `terminal_phase_offset_px = estimated_true_terminal_r_px mod tail_reference_tread_px` を計算し、`initial_tread_px` と比較した。
- 暫定結果:
  - `estimated_true_terminal_r_px`
    - `P0.3≈9272.6`, `P0.4≈8772.6`, `P0.5≈8448.1`, `P0.6≈8220.7`, `P0.7≈8069.4`, `P0.8≈7980.4`, `P0.9≈7941.8`, `P1.0≈7939.0`
  - `tail_reference_tread_px`
    - `P0.3=350`, `P0.4=201`, `P0.5=127`, `P0.6=85`, `P0.7=59.5`, `P0.8=43`, `P0.9=32`, `P1.0=25`
  - `initial_vs_terminal_phase_error_px`
    - `P0.3≈641.6`, `P0.4≈710.4`, `P0.5≈1.9`, `P0.6≈90.3`, `P0.7≈252.1`, `P0.8≈97.6`, `P0.9≈141.8`, `P1.0≈94.0`
- 暫定結論:
  - 単純な終端剰余モデル（`initial_tread = estimated_true_terminal mod tail_reference_tread`）は、大半の `P` では成立しない。
  - `P0.5` 付近だけ誤差が小さいが、全体傾向から見ると偶然一致の可能性が高い。
  - 終端基準の見方自体は有効で、`estimated_true_terminal` と `tail_reference_tread` は保持すべき指標である。
  - ただし初段長は、終端位相だけではなく、前半〜中盤の非一様な踏面配分を含んだ「複数区間の余り」として見る方が自然である。

### 設計メモ: 強変化点を起点にした逆算モデルの見通し（2026-03）
- 強変化点を断定的に算出できる法則が見つかれば、終端側からの逆算経路はかなり組み立てやすくなる。
- 現時点でも、終端側については `estimated_true_terminal` と終盤勾配の法則候補があり、さらに強変化点については「半径位置は比較的安定」「段番号はPとともに増える」「強変化点手前までの累積相対変化量は概ね `0.10P` 前後」という観測が得られている。
- このため、少なくとも
  - 終端 → 強変化点
  - 強変化点 → 前半直線区間
  の骨格は、法則化できる可能性が高い。
- 一方で初段は、終端剰余だけでも前半勾配だけでも説明しきれず、最後に残る位相/余り/局所丸まりの補正項として扱う可能性が高い。
- 実務上は、まず
  - 終端勾配
  - 強変化点位置
  - 強変化点手前までの累積相対変化量
  を固定し、その後で初段の補正を別モデルとして重ねる方針が自然である。

### 調査開始メモ（初段終端は前半直線の閾値crossingか, 2026-03）
- 入力: `DotLab/Kernel/kernel-sweep-wide-count10-stair-detail.csv`
- 仮説:
  - 初段を除く前半区間はほぼ直線なので、その一次近似 `f_front(r)` を中心側へ延長したとき、初段終端は最初の量子化閾値crossingで決まる。
- 比較した閾値候補:
  - `f_front(r) = P - riser`
  - `f_front(r) = P - riser/2`
- 暫定結果（実測の初段終端 `actualEndPx` との比較）:
  - `P0.3`: `actual=818`, `x(P-riser)=819`（誤差 `1px`）, `x(P-riser/2)=112.5`（誤差 `705.5px`）
  - `P0.4`: `actual=839`, `x(P-riser)=838.829`（誤差 `0.171px`）, `x(P-riser/2)=392.076`（誤差 `446.924px`）
  - `P0.5`: `actual=58`, `x(P-riser)=57.397`（誤差 `0.603px`）, `x(P-riser/2)=-250.204`（誤差 `308.204px`）
  - `P0.6`: `actual=150`, `x(P-riser)=150.857`（誤差 `0.857px`）, `x(P-riser/2)=-76.372`（誤差 `226.372px`）
  - `P0.7`: `actual=288`, `x(P-riser)=291.909`（誤差 `3.909px`）, `x(P-riser/2)=114.539`（誤差 `173.461px`）
  - `P0.8`: `actual=122`, `x(P-riser)=130.812`（誤差 `8.812px`）, `x(P-riser/2)=-15.520`（誤差 `137.520px`）
  - `P0.9`: `actual=167`, `x(P-riser)=180.300`（誤差 `13.300px`）, `x(P-riser/2)=52.685`（誤差 `114.315px`）
  - `P1.0`: `actual=107`, `x(P-riser)=128.534`（誤差 `21.534px`）, `x(P-riser/2)=9.780`（誤差 `97.220px`）
  - `P0.2` は前半サンプル不足で比較対象外。
- 暫定結論:
  - `P0.3〜0.7` では `f_front(r)=P-riser` が初段終端を非常に高精度で再現し、`P-riser/2` は明確に不適切。
  - `P0.8〜1.0` でも `P-riser` の方がはるかに近く、誤差は増えるが主仮説としては維持できる。
  - したがって現時点では、**初段終端は「前半直線を中心側へ延長したときの `P-riser` 閾値crossing」で近似できる**、が最有力仮説である。
  - 高P側の残差は、前半直線のわずかな曲がり・量子化・頂上近傍の局所丸まりを補正項として別扱いするのが自然。

### 調査開始メモ（初段終端の局所直線モデル比較, 2026-03）
- 入力: `DotLab/Kernel/kernel-sweep-wide-count10-stair-detail.csv`
- 比較対象:
  - `2-3段目` のみで作る局所直線
  - `2-4段目` で作る局所直線
  - 強変化点手前までの `全前半` 直線
- `P-riser` 閾値crossingでの初段終端誤差（px）:
  - `2-3段目`: 平均 `1.0`, 中央 `1.0`, 最大 `1.0`
  - `2-4段目`: 平均 `7.972`, 中央 `1.0`, 最大 `56.782`
  - `全前半`: 平均 `6.273`, 中央 `2.455`, 最大 `21.534`
- 解釈:
  - `2-3段目` は見かけ上もっとも高精度だが、2段目の開始点自体が `level_eff01 = P-riser` を持つため、交点がほぼ自明に `2段目開始位置` へ落ちる。したがって説明力の高い独立検証とは言いにくい。
  - 実務上の比較対象としては `2-4段目` と `全前半` の比較が妥当であり、高P側 (`P0.8〜1.0`) では `2-4段目` の方が明らかに精度が良い。
  - したがって、初段終端の予測には「前半全体の平均勾配」より「初期数段の局所勾配」を使う方が自然で、特に高P側では `2-4段目` ベースの局所直線が有力候補となる。

### 調査開始メモ（`2-3段` と `3-4段` の局所勾配差とPの相関, 2026-03）
- 入力: `DotLab/Kernel/kernel-sweep-wide-count10-stair-detail.csv`
- 定義:
  - `s23 = (level4? ではなく) 3段目頂点と2段目頂点を結ぶ局所勾配`
  - `s34 = 4段目頂点と3段目頂点を結ぶ局所勾配`
  - 比較指標として `delta_s = s34 - s23`、`ratio_s = s34 / s23`、`delta_theta = atan(s34) - atan(s23)` を見た。
- 暫定結果:
  - `s23` と `P` の相関は強い（Pearson `≈ -0.953`, Spearman `≈ -0.950`）。
  - `s34` と `P` の相関も強い（Pearson `≈ -0.928`, Spearman `≈ -0.883`）。
  - 一方で `delta_s` や `ratio_s` の `P` との相関は弱い。
    - `corr(P, delta_s)`: Pearson `≈ 0.684`, Spearman `≈ 0.100`
    - `corr(P, ratio_s)`: Pearson `≈ -0.701`, Spearman `≈ -0.100`
- 解釈:
  - `2-3段` と `3-4段` の各勾配そのものは `P` と強く相関するが、両者の差は `P0.2〜0.3` の低P帯で大きく、`P0.4〜0.9` ではほぼ 0 に張り付く。
  - したがって、`2-3段` と `3-4段` の角度差そのものを `P` の連続関数として使うより、
    - 低P帯では「初期曲率あり」
    - 中高P帯では「ほぼ同一勾配」
    という regime 指標として使う方が自然である。
  - 実務的には、初段終端の補正量としては `delta_s` 単独より、`2-4段目` の局所勾配そのものを使う方が安定していそうである。

### 設計メモ: 再現モデル案 v1（2026-03）
- 目的は、観測値の物理的な完全説明ではなく、`P` と相関する量をテーブル・定数・数式で定量化し、kernel再現に使える形へ整理すること。
- この `v1` は、`P` による kernel の変化法則モデルとして扱う。入力 `P` から区分ごとの再現パラメータ群を引き、最終的な半径プロファイルを再構成する。
- 現在の観測値は 8bit 量子化の影響を強く受けているため、各区間の誤差や矛盾に見える差も量子化誤差内で説明できる可能性を前提に扱う。
- したがって再現モデルは、厳密一致よりも「量子化誤差内で整合するか」を評価基準にする。
- 再現モデル案 `v1` は、全域1本の式ではなく、`P -> パラメータ群` を用いた区分モデルとして構成する。
- `P` からまず定量化する対象は次の5群とする。
  - `riser(P)`
  - 前半局所勾配 `front_local_slope(P)`（当面は `2-4段目` ベースを優先）
  - 強変化点 `major_change(P)`（半径位置 / 段番号 / 累積相対変化量）
  - 後半勾配 `tail_slope(P)`
  - 終端 `terminal(P)`（`observed_terminal` / `estimated_true_terminal` を区別）
- 高さ方向は、少なくとも一次近似では `start_level=P` と `riser(P)` により
  - `P`
  - `P-riser`
  - `P-2*riser`
  - ...
  の段列として構成する。
- 初段終端は、当面の主仮説として
  - 前半局所直線を中心側へ延長し
  - `P-riser` 閾値crossing を取る
 ことで与える。
- 強変化点以降は、終端側から得られる `tail_slope(P)` と `estimated_true_terminal(P)` を使い、まず終端→強変化点の骨格を固定する。
- その上で、強変化点手前までの累積相対変化量（概ね `0.10P` 前後）を使って、前半→中間の接続位置を決める。
- 初段の残差は、現時点では独立した微小補正項として扱う。候補は
  - 局所勾配残差
  - 位相/余り
  - 頂上近傍の局所丸まり
  であり、`v1` では必要最小限の補正に留める。
- 実務上は、まず `P -> {riser, front_local_slope, major_change, tail_slope, terminal}` を確定し、最後に初段補正を重ねる順序を優先する。
- 手順の骨格は次の通りとする。
  1. `P` から `riser(P)`、`front_local_slope(P)`、`major_change(P)`、`tail_slope(P)`、`terminal(P)` を求める。
  2. 高さ列を `P, P-riser, P-2*riser, ...` として構成する。
  3. 初段終端を、前半局所直線の `P-riser` 閾値crossing で与える。
  4. 強変化点までは前半局所勾配で接続し、強変化点以降は中間区間を経て終端勾配へ遷移させる。
  5. 終端では `estimated_true_terminal` もしくは `observed_terminal` を使い、`level_eff01<=0` 以降は 0 に固定する。
  - Sweep結果CSVを Top10の `summary`（候補パラメータ+スコア詳細+ROI設定）と `detail`（点/ROIのサンプル差分）に整理した。ROI詳細は行数上限で抑制。

## 追加実装: InkDrawGenのカーネル断面CSVを多角度kernel抽出へ更新（2026-03）
- `InkDrawGen/MainPage.xaml`
  - `angle step(deg)` を追加し、0度から360度未満を任意stepで掃引できるようにした（例: 60 → 0/60/120/180/240/300）。
- `InkDrawGen/Helpers/InkDrawGenUiState.cs` / `InkDrawGen/Helpers/InkDrawGenUiReader.cs`
  - `KernelAngleStepDeg` を追加し、UI値を状態へ取り込めるようにした。
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - 固定観測点に対して描画中心を複数角度・半径へ移動し、各角度断面を取得する方式へ更新。
  - `α=0` は欠損扱いとし、内側の欠損は線形補間、外周は `effectiveRadiusPx = 0.5 * size * scale` の `±1px` を基準に終端扱いにした。
  - 各角度断面は `r <= max(2, scale * 0.05)` の安定領域の代表値で正規化し、半径ごとにMedianで統合した `kernel01` を出力する。
  - 出力CSVは `r_px,r_norm,kernel01,valid_angle_count,mean01,min01,max01,stddev01` を持つ最終kernel用形式へ変更した。
- `InkDrawGen/Helpers/RadialFalloffProfile.cs`
  - 新しい多角度kernel CSVの `kernel01` 列を、そのままfalloffとして読み込めるようにした。

## 追加実装: InkDrawGenのkernel sweep観測条件を確認するdebug PNG出力を追加（2026-03）
- `InkDrawGen/MainPage.xaml`
  - `debug r(px)` / `debug angle(deg)` 入力と `debug PNG` ボタンを追加した。
- `InkDrawGen/Helpers/InkDrawGenUiState.cs` / `InkDrawGen/Helpers/InkDrawGenUiReader.cs`
  - debug PNG用の半径・角度設定を保持/読取できるようにした。
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - 現在の `obs x/y`・`sample canvas(px)`・`scale`・`S/P/Op`・指定した `r/angle` 条件でオフスクリーン描画したBGRA8結果を、そのままPNG保存する `ExportKernelDebugPngAsync` を追加した。

## 追加実装: InkDrawGen に kernel予測比較CSV出力を追加（2026-03）
- 目的: `P` による kernel 変化法則モデル（再現モデル案 `v1`）の予測値を、現在の `wide` 観測CSVから抽出した観測値と並べて比較できるようにする。
- 追加UI: `InkDrawGen/MainPage.xaml`
  - `カーネル断面CSV(予測比較)` ボタンを追加。
- 追加実装: `InkDrawGen/MainPage.xaml.cs`
  - `ExportKernelSweepPredictionComparisonButton_Click()` を追加し、`KernelSweepExportService.ExportKernelSweepPredictionComparisonAsync(this)` へ1行委譲するようにした。
- 追加実装: `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - `wide` 集約CSVを読み込み、既存の plateau 生成ロジックから各 `P` の観測指標を抽出する処理を追加。
  - 観測指標として、少なくとも以下を抽出するようにした。
    - `riser01`
    - `front_slope24` / `front_intercept24`
    - `initial_end_r_px` / `initial_end_r_norm`
    - `major_change_index` / `major_change_r_px` / `major_relative_drop`
    - `tail_slope`
    - `estimated_true_terminal_r_px`
    - `observed_terminal_r_px` / `censored_terminal`
  - 各 `P` を1件ずつ外した leave-one-out の線形補間で、`P -> パラメータ群` の予測値を作る処理を追加。
  - 観測値と予測値を1行に並べた `*-stair-prediction-compare.csv` を出力するようにした。
- 出力CSVの用途:
  - 現在の既知 `P` 群では、既存法則がどの程度そのまま再現に使えるかを確認する。
  - 今後 `0.05, 0.15, ...` の中間 `P` サンプルを追加取得した際に、同じ列構成で予測誤差を直接比較できるようにする。

## 追加運用: セッション記録用MDを作成（2026-03）
- `docs/Kernel_Prediction_Validation.md`
  - 本セッションの会話記録用MDを新規作成した。
  - ユーザープロンプトは原則逐語、Copilot回答は要約、大量データは省略可という方針を反映した。

## 分析メモ: count20 予測比較CSVと階段summaryから見えたこと（2026-03）
- 優先根拠は `DotLab/Kernel/KernelSweep/kernel-sweep-wide-count20-stair-prediction-compare.csv` と `DotLab/Kernel/KernelSweep/kernel-sweep-wide-count20-stair-summary.csv` とする。
- `riser(P)`、`front_slope24(P)`、`tail_slope(P)` は引き続き強い法則候補であり、`count20` でも安定している。
- `major_change_index(P)` は低P帯では不安定だが、中高P帯では `0` か `±1 plateau` 程度に収まるため、段番号法則としては使える寄りである。
- `major_change_r_px(P)` と `initial_end_r_px(P)` は依然として弱く、勾配法則そのものより「横位置への変換」が主な誤差源である。
- `kernel-sweep-wide-count20-stair-summary.csv` から、`plateau_count ≈ P / mean_riser01` がかなり強く成立している。特に `P0.8=100`, `P0.9=125`, `P1.0=152` は `P / mean_riser01` と一致する。
- `mean_tread_px` は `plateau_count` の増加に応じて滑らかに減少しており、実質的には利用可能半径を段数で割った代表幅として振る舞う。これは有用だが、定義上の従属性も強いため独立法則としては扱いすぎない方がよい。
- `median_tread_px` は `mean_tread_px` よりも滑らかで、長い初段や低P帯の極端な plateau に引っ張られにくい。横方向スケールの代表値としては `mean_tread_px` より `median_tread_px` の方が頑健候補である。
- `max_tread_px` は低P帯で急激に大きく、中高P帯で急減してから緩やかになるため、初段/低P特例の強さを表す指標として使える可能性がある。
- `last_nonzero_r_px` は `P0.7` までは `8000` に張り付き、`P0.75` から実終端が見え始める。したがって終端 regime の実用境界は `P≈0.75` 付近とみなすのが自然である。
- 強変化点の算出根拠は形式上 `plateau_count >= 3` で成立するが、法則として安定に扱えるのは少なくとも `plateau_count >= 8`（概ね `P>=0.2`）以降とみなすのが自然である。`P<=0.15` は低P特例として別扱いする方が安全である。
- `front_slope24` に加えて `front_slope23` を観測/予測列として追加することは可能であり、初期局所勾配の変化を補助的に追う候補として有効である。

## 追加実装: 予測比較CSVへ `front_slope23` を追加（2026-03）
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - `plateau_index=2..3` の局所勾配 `front_slope23` を観測抽出へ追加した。
  - leave-one-out 線形補間による `pred_front_slope23` を追加し、比較CSVへ `obs_front_slope23` / `pred_front_slope23` / `err_front_slope23` を出力するようにした。
  - 既存の `front_slope24` は主列のまま維持し、`front_slope23` は初期局所勾配の補助列として扱う。
  - 中心画素のalpha値をダイアログとファイル名で確認できるようにした。

## 設計メモ: 関節モデルの指標セット案（2026-03）
- kernel の階段列は、`plateau_count` を「可動域に上限のある関節数」とみなす関節モデルで整理すると理解しやすい。
- この見方では、高Pは「関節数が多い・1関節あたりの動きが小さい・根元ロックが弱い」ためしなやか、低Pはその逆で堅いと解釈できる。
- まず持つべき基本指標は次の5つとする。
  - `joint_count = plateau_count`
    - 関節数。多いほど連続曲線へ近づき、しなやかさの基本自由度になる。
  - `joint_step = mean_riser01 / P`
    - 1関節あたりの相対可動量。小さいほど段ごとの変化が細かく、しなやか。
  - `joint_span = median_tread_px`
    - 関節間の代表長。`mean_tread_px` より外れ値に強く、横方向スケールの代表値として頑健。
  - `root_lock = initial_tread_px / median_tread_px`
    - 根元ロック量。大きいほど初段保持が強く、堅い。
    - 低P補助として `max_tread_px / median_tread_px` も候補にする。
  - `terminal_headroom = 100 - last_nonzero_r_norm`
    - 終端がどれだけ手前へ来るか。高P側の閉じ方の強さを見る指標。
- 中間帯の「しなやかさ」を改善する補助指標として、次の2つを候補にする。
  - `mid_flex_ratio = |middle_slope| / ((|front_slope24| + |tail_slope|) / 2)`
    - 前半・後半の平均に対して中間帯がどれだけ柔らかいかを表す。
  - `curvature_budget = major_relative_drop / joint_step`
    - 強変化点までに何関節分の可動域を消費しているかを見る。
- 実務上の優先順は
  1. `joint_count(P)`
  2. `joint_step(P)`
  3. `joint_span(P)`
  4. `root_lock(P)`
  5. `terminal_headroom(P)`
  の順で法則化し、その後に `mid_flex_ratio(P)` と `curvature_budget(P)` を足す。
- したがって、再現モデルの次段では「勾配を増やす/減らす」よりも、まず `joint_count`・`root_lock`・`mid_flex_ratio` の3軸で堅さ/しなやかさを分離して持つ方が自然である。

## 追加実装: compare CSV を stair detail/summary 入力と関節モデル指標へ対応（2026-03）
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - `カーネル断面CSV(予測比較)` は従来の `wide` CSV に加え、`*-stair-detail.csv` を選択した場合に対応する `*-stair-summary.csv` を自動解決して compare CSV を生成できるようにした。
  - `stair-detail.csv` から plateau 行を復元し、`stair-summary.csv` の `plateau_count` / `median_tread_px` / `max_tread_px` / `last_nonzero_r_norm` を併用して関節モデル指標を組み立てるようにした。
  - compare CSV へ次の列を末尾追加した。
    - `obs_joint_count` / `pred_joint_count` / `err_joint_count`
    - `obs_joint_step` / `pred_joint_step` / `err_joint_step`
    - `obs_joint_span` / `pred_joint_span` / `err_joint_span`
    - `obs_root_lock` / `pred_root_lock` / `err_root_lock`
    - `obs_root_lock_alt` / `pred_root_lock_alt` / `err_root_lock_alt`
    - `obs_terminal_headroom` / `pred_terminal_headroom` / `err_terminal_headroom`
    - `obs_curvature_budget` / `pred_curvature_budget` / `err_curvature_budget`
  - 既存 compare CSV の列順は維持し、新規指標は末尾追加に留めた。

## 分析メモ: 次段の有力候補は「局所勾配ベクトルの共通尺度化」（2026-03）
- 現在の compare CSV は `front_slope23/24` と `tail_slope` など、局所勾配の代表値を点で抜き出している段階である。
- 一方で、中間帯の法則をより直接に見るには、`plateau_index >= 2` の各段での局所勾配列を共通尺度へ正規化して並べる方が自然である。
- 共通軸の候補は
  - `plateau_index` 正規化
  - 累積相対落差 `u=(P-level)/P`
  - 強変化点基準または終端基準の正規化半径
  の3系統であり、特に中間帯比較では `u` または強変化点基準が有望である。
- したがって次段の課題は、`P -> scalar 指標群` だけでなく `P -> 勾配ベクトル` を比較可能な形へ落とすことである。

## 追加実装: `u=(P-level)/P` 基準の局所勾配ベクトル列を compare CSV へ追加（2026-03）
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - `plateau_index >= 2` の各隣接 plateau 間について、`d(level_eff01)/d(radius_px)` を局所勾配として抽出する処理を追加した。
  - 各局所勾配は、その2点の中間 level に対する `u=(P-level)/P` を共通尺度として持たせ、固定アンカー `u={0.10,0.25,0.40,0.55,0.70,0.85}` へ線形補間で再サンプリングするようにした。
  - compare CSV へ次の列を追加した。
    - `obs_local_slope_u100` / `pred_local_slope_u100` / `err_local_slope_u100`
    - `obs_local_slope_u250` / `pred_local_slope_u250` / `err_local_slope_u250`
    - `obs_local_slope_u400` / `pred_local_slope_u400` / `err_local_slope_u400`
    - `obs_local_slope_u550` / `pred_local_slope_u550` / `err_local_slope_u550`
    - `obs_local_slope_u700` / `pred_local_slope_u700` / `err_local_slope_u700`
    - `obs_local_slope_u850` / `pred_local_slope_u850` / `err_local_slope_u850`
  - これにより、従来の `front_slope23/24` と `tail_slope` が「代表点比較」だったのに対し、中間帯を含む局所勾配ベクトルを共通尺度で比較できるようになった。

## 分析メモ: 局所勾配ベクトル追加後の `count20` compare CSV（2026-03）
- 優先根拠は `DotLab/Kernel/KernelSweep/kernel-sweep-wide-count20-stair-prediction-compare.csv` とする。
- `joint_count` は全域で `err=0 or 1` に収まり、関節数モデルは非常に強い。
- `joint_step` も中高Pではかなり安定し、低Pを除けば法則化しやすい。
- 新規の `local_slope_u*` 系列は、`u250〜u850` が全体にかなり安定しており、中間帯〜後半帯の変形を捉える主力候補である。
- `local_slope_u100` は頂上近傍の局所性・量子化・サンプル不足の影響を受けやすく、主指標というより診断列として扱う方が自然である。
- `root_lock` / `root_lock_alt` は依然として誤差が大きく、初段由来の不安定さが強い。
- `terminal_headroom` は高P側でかなり良く、`curvature_budget` も概ね有望である。
- したがって次段は、`root_lock` 改善を急ぐよりも `local_slope_u250〜u850` と `curvature_budget` を組み合わせ、中間帯の法則を先に詰める方が効果的である。

## 設計メモ: `stair-detail.csv` に前値からのベクター情報を追加する案（2026-03）
- `stair-detail.csv` へ「前 plateau から current plateau への遷移ベクトル」を追加する案は有効である。
- ただしこれは完全な新情報というより、既存の `tread` と `riser` を2次元遷移として明示し直す意味が強い。
- 列候補は次の通り。
  - `prev_vec_dx_px` = `start_r_px(i) - start_r_px(i-1)`
  - `prev_vec_dx_norm` = `start_r_norm(i) - start_r_norm(i-1)`
  - `prev_vec_dy01` = `level_eff01(i) - level_eff01(i-1)`
  - `prev_vec_dy_relP` = `prev_vec_dy01 / p_value`
  - `prev_vec_slope01_per_px` = `prev_vec_dy01 / prev_vec_dx_px`
  - `prev_vec_slope_relP_per_rnorm` = `prev_vec_dy_relP / prev_vec_dx_norm`
- `px` と `level01` は単位が異なるため、長さや角度へ直接まとめるより、まずは成分と正規化勾配を持つ方が解釈しやすい。
- 実務上は compare CSV の主系列というより、detail CSV から後段で `u` 基準ベクトルや regime 解析へ渡すための中間表現として有用である。

## 修正: InkDrawGenのROI変換順序がsample canvas依存の観測ずれを生んでいた問題（2026-03）
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - kernel sweep / debug PNG の `DrawInk` 前の変換行列を `Scale * Translation` から `Translation * Scale` へ修正した。
- `InkDrawGen/Helpers/InkOffscreenRenderService.cs`
  - 同じくROI原点化→scaleの順になるよう変換順序を修正した。
- 背景
  - debug PNGで `sample canvas` を大きくすると観測中心が `sampleCanvasPx/(2*scale)` 相当だけずれる現象があり、コメントの「ROIを(0,0)へ持ってきてからscale」と実際の行列順が一致していなかった。

## 修正: InkDrawGenのkernel sweepを早期打ち切りなし・補間なしの実測統合へ変更（2026-03）
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - 各角度断面で `α=0` に当たっても早期打ち切りせず、理論半径まで最後まで評価するようにした。
  - 断面内の補間は一旦行わず、半径ごとに「観測できた角度の実測値だけ」をMedian統合する方針へ切り替えた。
  - CSVメタ情報の `zero_policy` を `keep_zero_as_missing, no_interpolation` へ更新した。

## 追加実装: InkDrawGenのkernel sweepに角度ごとのRaw観測CSV出力を追加（2026-03）
- `InkDrawGen/MainPage.xaml`
  - `raw CSV` ボタンを追加した。
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - 各角度・各半径について、中心画素の `alpha_byte` / `alpha01` / `is_observed` をそのまま出力する `ExportKernelRawCsvAsync` を追加した。
  - これにより、統合後CSVだけでは見えない「どの角度がどの半径で0になったか」を切り分けられるようにした。

## 追加実装: InkDrawGenにオフセット用の紙目ベース単点PNGボタンを追加（2026-03）
- `InkDrawGen/MainPage.xaml`
  - 既存の中央固定版と混同しないよう、`紙目ベース単点PNG(Offset)` ボタンを追加した。
- `InkDrawGen/Helpers/KernelCanceledDotExportService.cs`
  - `StartX/StartY` をドット中心、`OutWidthPx/OutHeightPx` を出力サイズとして使う `ExportKernelCanceledDotOffsetPngAsync` を追加した。
  - 相殺時の半径距離も、画像中心ではなくオフセット中心 (`StartX/StartY`) 基準で計算するようにした。
- `InkDrawGen/MainPage.xaml.cs`
  - `紙目ベース単点PNG(Offset)` ボタンから新しいオフセット版出力処理へ1行委譲するハンドラを追加した。

## 追加実装: InkDrawGenに紙目タイル基準のkernel取得を追加（2026-03）
- `InkDrawGen/MainPage.xaml`
  - `カーネル断面CSV(紙目Tile)` ボタンを追加した。
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - 実行時に紙目PNGを選択し、現在の `obs x/y` を種点として、紙目9x9近傍の中央値近傍座標を求める処理を追加した。
  - 続けて、その紙目由来の仮地点を使って実画像9x9近傍をレンダし、中央値近傍の本地点へ微調整したうえで、その本地点を観測点にした多角度kernel sweepを実行するようにした。
  - 出力CSVに `paper_tile` / `obs_seed_px` / `obs_paper_tile_px` / `obs_actual_px` / `obs_window_px` のメタ情報を追記するようにした。
- `InkDrawGen/MainPage.xaml.cs`
  - `カーネル断面CSV(紙目Tile)` ボタンから新しい紙目タイル基準のkernel取得処理へ1行委譲するハンドラを追加した。

## 修正: `カーネル断面CSV(紙目Tile)` をPスイープ対応へ拡張（2026-03）
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - `P start/end/step` を展開し、`S` 固定・`P` 違いのkernel CSVを一括出力できるようにした。
  - 紙目タイル内の種点選定は1回だけ行い、その後は各Pごとに実画像9x9近傍の本地点を再計算してCSVを書き出すようにした。
  - 出力CSVメタ情報に `p_sweep_count` を追加し、ダイアログでも各Pの `actualObs` とファイル名をまとめて確認できるようにした。

## 修正: kernel sweepを並列処理化して実行コア数指定に対応（2026-03）
- `InkDrawGen/MainPage.xaml`
  - kernel sweep設定に `parallel cores` 入力を追加した。`0` はCPUコア数をそのまま使う自動設定。
- `InkDrawGen/Helpers/InkDrawGenUiState.cs` / `InkDrawGen/Helpers/InkDrawGenUiReader.cs`
  - kernel sweep用の最大並列度設定を保持/読取できるようにした。
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - 角度断面の計算を `Task.Run + Parallel.For` でバックグラウンド並列実行する共通処理へ変更した。
  - 通常版の `カーネル断面CSV` と `カーネル断面CSV(紙目Tile)` の両方で、角度ごとに独立した `CanvasRenderTarget` を使って並列計算するようにした。
  - 出力CSVメタ情報と完了ダイアログに `max_parallelism` / `parallel_mode=cpu-angle` を追記した。

## 修正: Win2D並列描画で共有CanvasDeviceを使うと`ds.DrawInk`でAccessViolationが出る問題を安定化（2026-03）
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - 並列workerが `CanvasDevice.GetSharedDevice()` を共有しないようにし、各workerで独立した `CanvasDevice` を使うよう修正した。
  - これにより、GPU経由のWin2D描画は維持しつつ、共有デバイス競合による `ds.DrawInk` のメモリアクセス違反を避ける方針にした。

## 修正: Win2D `DrawInk` 自体が並列不可と判断し、kernel sweepを非同期直列へ戻した（2026-03）
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - 独立 `CanvasDevice` を使っても `ds.DrawInk` で `AccessViolationException` が再発したため、UWP/Win2D の `DrawInk` 呼び出し自体が並列バックグラウンド実行に耐えないと判断した。
  - kernel sweep の角度断面計算は `Task.Run` 上の直列実行へ戻し、UIブロックを避ける非同期性だけを維持する方針へ切り替えた。
  - `parallel cores` は requested 値として保持しつつ、この経路では `effective_parallelism=1` をCSVメタ情報と完了ダイアログへ明示するようにした。

## 追加実装: kernel sweep CSV群をP横持ちwide形式へ集約する機能を追加（2026-03）
- `InkDrawGen/MainPage.xaml`
  - `カーネル断面CSV(wide集約)` ボタンを追加した。
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - 選択した複数のkernel CSVを読み込み、`r_px` / `r_norm` を基準に同名列をP違いで横持ちするwide集約CSV出力を追加した。
  - 出力ヘッダーは `kernel01_p0_1` のように、元ファイル名から抽出したP値を付加する仕様にした。
  - 元CSVファイルはファイル単位で複数選択し、wide集約CSVは出力フォルダへ `kernel-sweep-wide-count{n}.csv` として保存するようにした。
- `InkDrawGen/MainPage.xaml.cs`
  - `カーネル断面CSV(wide集約)` ボタンから新しいwide集約処理へ1行委譲するハンドラを追加した。

## 追加実装: kernel wide CSVから踏面・蹴上を集約する階段解析を追加（2026-03）
- `InkDrawGen/MainPage.xaml`
  - `カーネル断面CSV(階段解析)` ボタンを追加した。
- `InkDrawGen/Helpers/KernelSweepExportService.cs`
  - wide CSV内の `kernel01_p*` 列を読み取り、各Pについて `Kernel01 * P` のplateauを抽出する階段解析処理を追加した。
  - 詳細CSVでは `plateau_index` / `tread_px` / `level_eff01` / `riser_to_next01` / `delta_tread_to_prev` / `tread_ratio_to_prev` / `log2_tread_ratio_to_prev` を出力するようにした。
  - サマリCSVでは `plateau_count` / `transition_count` / `mean_tread_px` / `median_tread_px` / `max_tread_px` / `mean_riser01` / `std_riser01` / `last_nonzero_r_px` を出力するようにした。
- `InkDrawGen/MainPage.xaml.cs`
  - `カーネル断面CSV(階段解析)` ボタンから新しい階段解析処理へ1行委譲するハンドラを追加した。

## 追加実装: DotTesterでInkDrawGenの最新kernel/tileをScale整合付きで再現へ取り込み対応（2026-03）
- `DotTester/MainWindow.xaml.cs`
  - InkDrawGenの `r_px,r_norm,kernel01` 形式CSVを、DotTesterのfalloff LUTとして読み込めるようにした。
  - falloff CSVの `# scale=` メタ情報、またはPNG/CSVファイル名の `scaleNN` からソースscaleを推定する処理を追加した。
  - `Tile PNG` 選択時は推定したscaleを `TileScale` へ自動反映し、`Falloff CSV` 選択時は推定scaleを内部保持して従来の `dx_px` 形式読込でもソースscale優先でLUT化するようにした。

## 追加実装: DotTesterにMultiply(k)専用Biasを追加（2026-03）
- `DotTester/MainWindow.xaml`
  - 既存の `Cutoff(alpha)` とは別に、`multiply bias(01)` 入力を追加した。
- `DotTester/MainWindow.xaml.cs`
  - `multiply bias(01)` を読み取り、`DotReproRenderer.Options` へ渡すようにした。
  - 値変更時に自動再描画されるよう監視対象にも追加した。
- `DotTester/Helpers/DotReproRenderer.cs`
  - `MultiplyCutoffBias01` を追加し、`OutAlphaModel=MultiplyK` のときだけ最終cutoffへ加算する専用Biasとして実装した。
  - 既存の `AlphaCutoff01` と `NoiseDependentCutoff` は維持しつつ、Wall-throughのbiasとは別目的で外周の落ちやすさを調整できるようにした。
