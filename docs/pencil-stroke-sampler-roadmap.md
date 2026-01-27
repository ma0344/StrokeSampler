# 鉛筆ストローク・サンプル作成ロードマップ

## Goal
- UWP（`net9.0-windows10.0.26100.0`）アプリで、鉛筆ストロークのサンプルシートを生成し、高解像度の透過PNGとして書き出せる状態にする。

## 前提（初期DoD）
- 配布品質は目標にせず、デバッグ実行でサンプル生成とPNG書き出しが一通り動作する品質を目指す。
- 例外処理・設定保存・UIの磨き込みは最小限とし、必要になった段階で拡張する。

## Non-goals
- 自前ブラシエンジン（スタンプ/coverage/上限付き加算等）の実装
- `InkToolbar` の状態取得・同期の完全対応（初期版では後回し）
- サンプル画像からの逆解析（閾値マップ生成等）

## Steps
1. プロジェクト構造を把握する
   - `net9.0-windows...` / `UseUwp=true` のUWPアプリであることを確認する
   - 既存UI/ロジックが空であることを前提に設計する
2. `MainPage` のUI骨組みを追加する
   - `MainPage.xaml` に `InkCanvas` を配置する
   - 操作ボタン（例：Generate / Clear / Export PNG）を配置する
   - `InkToolbar` を配置し、鉛筆ツールの色/サイズ選択を行えるようにする
3. サンプリング仕様（Pressure/線形状/配置）を確定する
   - Pressure を `0.2 / 0.5 / 0.8 / 1.0` の4段階に固定する
   - Stroke形状を直線（水平線）に統一する
   - 縦方向に重ならないよう `spacing` を付ける
   - 太さ/色は `InkToolbar` の選択値を使用する（取得できない環境では固定値にフォールバックする）
4. 鉛筆ストローク生成ルーチンを実装する
   - `InkDrawingAttributes.CreateForPencil()` を使用する
   - `InkPoint` 列（pressure付き）から `CreateStrokeFromInkPoints(...)` で生成する
   - 生成した `InkStroke` を `InkPresenter.StrokeContainer` に投入する
5. サンプルシートのレイアウトルーチンを実装する
   - 4段階のPressureサンプルが縦方向に並ぶように配置する
   - PNG書き出し時に“同じ並び”で描画できるよう、`InkPresenter.StrokeContainer.GetStrokes()` を使用する
6. Win2D を導入する
   - NuGetで `Win2D.uwp` を追加する
   - ビルドが通ることを確認する（参照解決）
7. 高解像度PNGのレンダリング・書き出しルーチンを実装する
   - 素材用PNG（透過・ラベル無し）を書き出せるようにする
   - 確認用PNG（白背景・テキストラベル有り）を書き出せるようにする
   - `CanvasRenderTarget(width,height,dpi)` を作成する
   - 背景のクリアを目的に応じて切り替える（素材用=透明、確認用=白）
   - `ds.DrawInk(strokes)` で描画する
   - 確認用では文字ラベルも `ds.DrawText(...)` で焼き込む（XAML `TextBlock` ではなく）
8. シート上のラベル描画（任意）を実装する
   - 確認用PNGで、Pressure/出力サイズ等のテキストラベルを `ds.DrawText(...)` で焼き込む
9. 生成・クリア・書き出しのUI配線を実装する
   - 素材用（透過・ラベル無し）/確認用（白背景・ラベル有り）の書き出しを選べるようにする（ボタン分割またはトグル等）
   - 最初は `FileSavePicker` でユーザーに保存場所を選ばせる（PicturesLibrary権限依存を避ける）
   - 保存形式は PNG 固定にする
   - （任意）export解像度（例：4096/8192）のUIを追加する
10. ソリューションをビルドする
   - アプリのビルドが成功することを確認する
11. 書き出したPNGを別アプリで開いて検証する
   - 画面で生成して表示確認する
   - ExportしてPNGを別アプリで開き、品質確認（透明背景/白背景、解像度、ラベル位置など）を行う

## Files
- `MainPage.xaml` (modify) - `InkCanvas` と操作UI（生成/クリア/書き出し）の追加
- `MainPage.xaml.cs` (modify) - サンプル生成と書き出しロジックの追加
- `StrokeSampler.csproj` (modify) - Win2D 参照（必要時）
- `docs/pencil-stroke-sampler-roadmap.md` (new) - 本ロードマップ

## Validation
- build: `dotnet build`
- tests: n/a
- manual:
  1. アプリを起動する
  2. `InkToolbar` で鉛筆の色/サイズを選ぶ
  3. 生成ボタンで Pressure 4段階のサンプル線が重ならずに表示されることを確認する
  4. 素材用（透過・ラベル無し）のPNGが保存できることを確認する
  5. 確認用（白背景・テキストラベル有り）のPNGが保存できることを確認する
  6. 書き出したPNGを別アプリで開き、透明背景/白背景・解像度・ラベル位置を確認する

## Risks
- Win2D の導入・参照解決が環境依存で失敗する - 依存追加は最小限にし、導入直後に `dotnet build` で確認する
- 書き出しの保存フローがUWPのストレージ制約に引っかかる - 初期版は `FileSavePicker` を使い、特定フォルダ権限（PicturesLibrary等）を前提にしない
- 画面表示（`InkCanvas`）とPNG出力（Win2D）の見た目が一致しない - DPI/スケーリング（DIPとピクセル、`CanvasRenderTarget` のDPI値）を明示し、手動検証で調整する
- 高解像度（例: 4096x4096以上）出力でメモリ使用量が増える - 解像度をUIで調整可能にし、初期値を控えめにする
