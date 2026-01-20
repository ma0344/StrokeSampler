# Issue: `MainPage` のUI骨組みを追加する

## 概要
`InkCanvas` と操作UI（生成/クリア/書き出し）を `MainPage` に追加する。

## 目的（完了条件）
- 画面上に `InkCanvas` が表示される。
- 「生成」「クリア」「書き出し」などの操作ボタンが表示され、クリックイベントが配線されている。

## ロードマップ小項目（割り当て）
- `MainPage.xaml` に `InkCanvas` を配置する
- 操作ボタン（例：Generate / Clear / Export PNG）を配置する
- `InkToolbar` は「後で任意追加」扱いとして、初期版は固定パラメータで進める

## 作業内容
- `MainPage.xaml` にレイアウト（`InkCanvas`・ボタン・必要なら入力欄）を追加する。
- `MainPage.xaml.cs` にイベントハンドラの雛形を追加する。

## 影響範囲
- `MainPage.xaml`
- `MainPage.xaml.cs`

## メモ
- 初期版は `InkToolbar` の組み込みは必須ではない（後回し）。