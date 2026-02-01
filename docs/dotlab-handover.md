# DotLab 引き継ぎメモ

## 目的
- `SkiaTester` が検証UI/分岐で肥大化してきたため、Dot再現の最小実験環境として `DotLab`（WPF + SkiaSharp）を新設した。
- 紙目の「高さ」は `NoiseSheet-WhiteBack2-Transparent.png` の **Alpha(0..1)** を使用する。
- ノイズは「ワールド固定（キャンバス座標固定）」として扱う。

## 重要な決定（固定ルール）
- NoiseOffsetの方向
  - `NoiseOffsetX` を増加させると **紙ノイズが右に移動**し（点は左に見える）
  - `NoiseOffsetY` を増加させると **紙ノイズが上に移動**する（点は下に見える）
- オフセットの効きは `NoiseScale` により実質スケールする（大きなスケールには大きなオフセットが必要）。

## DotLab のモデル（壁貫通モデル）
GIMPの手動分解で得た仮説を、実装優先で以下の式として扱う。

- `B = P * f(r)`
- `H = T(x,y)`（紙目の高さ＝alpha）
- `wall = 1 - H`
- `V = clamp((B - wall) / k, 0..1)`
- `outA = 1 - (1 - V)^N`

`k` は「閾値を超えてから 1 へ到達するまでの遊び（階調の幅）」として使う。

## 現状の実装（主要ファイル）
- `DotLab/MainWindow.xaml` - UI（S/P/N/k, NoiseScale/Offset, Preview切替）
- `DotLab/Rendering/DotModel.cs` - 上記式をそのまま実装（`V` と `outA` を生成）
- `DotLab/Rendering/DotLabNoise.cs` - PNGのalphaをタイルとしてサンプル（現状nearest）
- `DotLab/Rendering/Falloff.cs` - 現状は最小で「半径内 f=1」
- `DotLab/Rendering/DotBitmap.cs` - 0..1をグレースケール描画

## 現状の症状（次スレッドでの調査対象）
- NoisePathは適切。
- しかし `Preview: B=P*f(r) / V / outA` が **白い円のみ**になっており、紙目由来のドット化が出ていない。

この症状は、少なくとも以下のいずれかを示唆する：
- `H`（noise alpha）が実際にはほぼ一定（例: ほぼ1.0）
- `noiseScale/noiseOffset` の座標系が意図と違い、同一地点ばかりサンプルしている
- 画像読み込みで alpha が想定と異なる（premul/unpremul/codec経路）
- `Falloff` が現状 f=1固定のため、`B` が常に P（=1なら真っ白）になり、`V` がほぼ 1 に張り付く

## 次の調査の最短手順（推奨）
1. `Preview: H=noiseHeight` を表示し、模様が出ているか確認する。
2. `NoiseOffsetX/Y` を大きく動かして `H` がスライドするか確認する。
3. `NoiseScale` を変えて `H` の見え方（周期）が変わるか確認する。
4. `P` を 1→0.05 などへ落として `B/V/outA` が変化するか確認する。

## 追加改善の候補（確度順）
- ノイズサンプルを nearest → bilinear に変更（GIMP見え寄せに効く可能性が高い）
- `Falloff` に SkiaTester の normalized falloff CSV 読み込みを追加
- `H` の統計（min/max/mean）をオーバーレイ表示して、異常（ほぼ1固定など）を即検出する

## SkiaTester 側で得た学び（バグ回避）
- UIのコンボボックス → enum マッピングは反転しやすいので、`falloffF` のような可視化で常に検証する。
- UI計算と本体計算がズレるため、中間値は「同一ループで計算した配列」を表示する設計が有効。
