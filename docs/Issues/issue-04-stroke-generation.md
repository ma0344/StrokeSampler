# Issue: 鉛筆ストローク生成ルーチンを実装する

## 概要
コードから鉛筆（pencil）相当の `InkStroke` を生成し、`InkCanvas` 上に表示できるようにする。

## 目的（完了条件）
- Pressure を固定/段階値にしたストロークを生成できる。
- 生成したストロークが `InkCanvas.InkPresenter.StrokeContainer` に追加され、表示される。

## ロードマップ小項目（割り当て）
- `InkDrawingAttributes.CreateForPencil()` を使用する
- `InkStrokeBuilder` で `InkPoint` 列（pressure付き）から `CreateStrokeFromInkPoints(...)` で生成する
- 生成した `InkStroke` を `InkPresenter.StrokeContainer` に投入する

## 作業内容
- `InkDrawingAttributes.CreateForPencil()` をベースに色/太さなどを設定する。
- `InkStrokeBuilder` / `InkPoint` を使い stroke を生成する。
- ストローク生成の入力（pressure, 太さ, 色, 始点/終点 など）を最小のメソッドにまとめる。

## 影響範囲
- `MainPage.xaml.cs`

## メモ
- Hardnessは公開APIで変更できない可能性があるため初期版では扱わない。