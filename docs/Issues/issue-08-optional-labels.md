# Issue: シート上のラベル描画（任意）を実装する

## 概要
確認用PNGで、サンプルごとのHardness/PressureなどをPNGに直接焼き込む（Win2Dで `DrawText`）。

## 目的（完了条件）
- 書き出した確認用PNG上で、各サンプルのHardness/Pressureが識別できる。

## ロードマップ小項目（割り当て）
- 確認用PNGで、Hardness/Pressure等のテキストラベルを `ds.DrawText(...)` で焼き込む（XAML `TextBlock` ではなく）

## 作業内容
- `CanvasTextFormat` を定義する。
- ストロークのY座標に合わせて `DrawText` する。
- 何を表示するかを固定する（例：Hardness、Pressure、段階数、出力サイズ）。

## 影響範囲
- `MainPage.xaml.cs`

## メモ
- 素材用PNGはラベル無しを前提にする（切り抜き・サンプリングの邪魔になるため）。