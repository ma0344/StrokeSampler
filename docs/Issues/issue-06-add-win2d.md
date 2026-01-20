# Issue: Win2D を導入する

## 概要
高解像度PNG書き出しのために Win2D（`Win2D.uwp`）をプロジェクトに追加する。

## 目的（完了条件）
- `StrokeSampler` プロジェクトに Win2D の参照が追加される。
- `dotnet build` が成功し、Win2D の名前空間を解決できる。

## ロードマップ小項目（割り当て）
- NuGetで `Win2D.uwp` を追加する
- ビルドが通ることを確認する（参照解決）

## 作業内容
- NuGet で `Win2D.uwp` を追加する。
- 追加後にビルド確認する。

## 影響範囲
- `StrokeSampler.csproj`

## メモ
- 依存追加は最小限にし、問題が出た場合はバージョン/TFM互換を確認する。