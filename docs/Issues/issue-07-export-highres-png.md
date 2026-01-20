# Issue: 高解像度透過PNGのレンダリング・書き出しルーチンを実装する

## 概要
`InkStroke` 群を Win2D の `CanvasRenderTarget` に描画し、PNGとして保存できるようにする。

## 目的（完了条件）
- 素材用PNG（透過・ラベル無し）が保存できる。
- 確認用PNG（白背景・テキストラベル有り）が保存できる。
- 出力サイズ（例: 4096x4096）を指定できる（固定値でも可）。

## ロードマップ小項目（割り当て）
- `CanvasRenderTarget(width,height,dpi)` を作成する
- 背景を目的に応じてクリアする（素材用=透明、確認用=白）
- `ds.DrawInk(strokes)` で描画する
- 確認用では文字ラベルも `ds.DrawText(...)` で焼き込む（XAML `TextBlock` ではなく）

## 作業内容
- `CanvasRenderTarget` を生成し、`DrawingSession` で背景をクリアする。
- `DrawInk(strokes)` でストロークを描画する。
- `FileSavePicker` で保存先を選択し、PNGとして保存する。

## 影響範囲
- `MainPage.xaml.cs`
- 必要に応じて `MainPage.xaml`（出力設定UI）

## メモ
- `StrokeContainer.SaveAsync` は画像ではなくインクデータ保存なので使わない。