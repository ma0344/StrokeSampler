# SkiaSharp への移植用メモ：UWP InkCanvas Pencil 近似モデル

## ?? 分析結果 Summary:
このリポジトリ（`StrokeSampler`）では、UWP `InkCanvas` の `Pencil` の描画を観測し、以下の2要素に分解して移植可能な形にしました。

- 距離減衰（半径方向の平均アルファ） `F(r)`
- 紙目（ノイズ） `N(x,y)`

SkiaSharp 側では、既存の「ソフト円スタンプ（例：`StampSoftCircle`）」を **距離減衰LUTで置換**し、さらに **紙目ノイズで変調**することで、UWP Pencil の見た目に寄せます。


---

## ? 移植時に受け渡す必要のあるファイル

### 1) 距離減衰LUT（必須）
- `Sample/Compair/CSV/normalized-falloff-S0200-P1-N1.csv`
  - 列: `r_norm,mean_alpha,stddev_alpha,count`
  - Skia側で使うのは基本 `mean_alpha`（`stddev_alpha` は品質指標/任意）
  - `S0=200` を基準にした正規化済みLUT

（参考として、生データのCSV群）
- `Sample/Compair/CSV/radial-falloff-S*-P1-N1.csv`（任意）

### 2) 紙目テクスチャ（必須）
- `paper-noise-estimated-*.png`
  - **このリポジトリ内ではファイル管理していないため、生成したPNGを別途渡す必要があります**
  - 生成元は本アプリの `紙目推定PNG` ボタン


---

## 1. モデル（数式）

### 1.1 距離の正規化
基準サイズ `S0` と、実際のブラシサイズ `S`（px相当）を用いて、中心からの距離 `distance_px` を `r_norm` に変換します。

- `distance_px = sqrt((x - cx)^2 + (y - cy)^2)`
- `r_norm = distance_px * (S0 / S)`

このリポジトリのLUTは `S0=200` 基準で作っています。

### 1.2 距離減衰（LUT）
`normalized-falloff-...csv` の `mean_alpha` を配列 `lut[]` に読み込みます（インデックスが `r_norm`）。

`r_norm` は連続値なので **線形補間**します。

- `i = floor(r_norm)`
- `t = r_norm - i`
- `F(r_norm) = lerp(lut[i], lut[i+1], t)`

範囲外は端でクランプします。

### 1.3 紙目（ノイズ）
紙目画像（グレースケール）を 0..1 に正規化して使います。

- `noise = gray / 255.0`
- 反転が必要なら `noise = 1.0 - noise`

### 1.4 最終アルファ（最小構成）
まずはこの式で十分です。

- `alpha = baseAlpha * F(r_norm) * noise`

`baseAlpha` は当面固定でも良いです。


---

## 2. Skia側で置き換える場所（概念）

SkiaSharp側に、以下のような「スタンプ描画」処理がある前提です。

- `DrawPencilStroke(...)` がサンプル点を生成
- 各サンプル点で `StampSoftCircle(...)` のような関数が
  - `distance` から `mask`（0..1）を作り
  - `mask` を alpha に掛けて塗る

この `mask` 生成部分を、距離減衰LUTに置換します。

- 旧: `mask = SoftCircle(distance, radius, hardness, ...)`
- 新: `mask = F(r_norm)`

そして紙目が使えるなら
- `mask *= N(x,y)`


---

## 3. 紙目テクスチャの扱い（パス/サイズ/タイル規約）

### 3.1 パス（Skia側）
Skia側リポジトリの管理方針に合わせて、以下のどちらかにします。

- A案（推奨）: `Assets/paper-noise-estimated.png` として同梱
- B案: ユーザー指定パス（設定）から読み込み

このMDに添付するべきものは **生成済みPNGファイル**です。

### 3.2 サイズ
- 紙目PNGのピクセルサイズは生成時の入力画像に依存します。
- Skia側は「画像の幅/高さ」を読み取って動作すれば固定値は不要です。

### 3.3 タイル規約（重要）
UWPっぽさを出すなら、紙目は **キャンバス座標（ワールド）固定**で参照します。

- `noise = SamplePaperNoise(canvasX, canvasY)`
- タイルするなら
  - `u = ((canvasX % W)+W)%W`
  - `v = ((canvasY % H)+H)%H`

補間は最初は最近傍でも良いですが、可能なら線形補間推奨です。


---

## 4. 距離減衰LUT（CSV）の読み取り仕様

### 4.1 入力CSV
- `normalized-falloff-S0200-P1-N1.csv`

例（先頭）:
- `# normalized-falloff S0=200 P=1 N=1 count=7`
- `r_norm,mean_alpha,stddev_alpha,count`
- `0,0.593...,0.016...,7`

### 4.2 使用カラム
- まずは `mean_alpha` のみ
- `stddev_alpha` は「不確かさ」なので、後で
  - クリップ範囲決定
  - 品質評価
  に利用可能（任意）


---

## 5. Skia側の実装方式（CPU更新 / Shader）

向こうのコード事情に依存するため、2案を示します。

### 5.1 CPUで `SKBitmap` を更新する方式（実装が分かりやすい）
- 各スタンプについて、影響範囲のピクセルを走査
- `r_norm` を計算して `F(r_norm)` を引く
- `noise` を引く
- `alpha` を計算して既存ピクセルに合成（まずは `SrcOver`）

利点:
- 実装が単純
- LUT/紙目の参照がそのまま書ける

欠点:
- 速度が出にくい（最適化が必要になり得る）

### 5.2 `SKShader` で描画時に評価する方式（高速化しやすい）
- 可能なら
  - 距離（中心からの距離）
  - LUT参照（1Dテクスチャ相当）
  - 紙目サンプル
  をシェーダで行う

注意:
- SkiaSharpでランタイムシェーダが使えるかは環境依存
- まずはCPU方式で正しさを出してから移行でも良い


---

## 6. まず最小で動かす手順（推奨）

1. `normalized-falloff-S0200-P1-N1.csv` をSkia側に同梱する
2. CSVを読み、`mean_alpha` を `double[] lut` にする
3. `StampSoftCircle` の `mask` を `F(r_norm)` に置換する（紙目無し）
4. `paper-noise-estimated.png` を同梱して `mask *= noise` を入れる
5. `S=10` 前後で見た目がUWPに寄るか確認する


---

## 7. 参照実装（擬似コード）

### 7.1 LUT評価
- `EvalFalloff(rNorm)`
  - 線形補間
  - 範囲外クランプ

### 7.2 紙目参照
- `EvalNoise(canvasX, canvasY)`
  - PNGのグレー値を 0..1
  - 必要なら反転
  - タイルはキャンバス座標で行う


---

## 8. 補足（pressure依存について）
この段階では「pressure→直径/濃さ」のテーブルは必須ではありません。
まずは **S固定（例:10）でF(r)と紙目を入れる**と、見た目の“質感”が大きく近づきます。

pressure依存が必要になった時点で:
- `baseAlpha`（濃さ）
- `S`（サイズ）
- `F(r)` の形
のどれがpressureで変わるかを追加測定します。
