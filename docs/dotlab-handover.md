# DotLab 引き継ぎメモ

> 注: 検証の実行手順は `docs/pencil-parity-playbook.md` に集約した（手順書）。本ファイルは背景・設計・実装メモとして維持し、手順の詳細は手順書を参照する。

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

---

## 追加機能: Line N1 vs Dot N1 マッチング（画像フォルダ解析）

### 目的
- StrokeSamplerが出力したPNG（線: `N1N2` / 単点: `aligned-dot-index`）を同一条件で比較し、
  - 形（2値化マスク; IoU等）
  - 濃さ（αスケール係数）
 について、線1枚に最も近い単点PNGを探索する。

### 実装
- `DotLab/Analysis/LineN1VsDotN1Matcher.cs`
  - フォルダ内の `*.png` から候補抽出
    - line候補: `-alignedN1` かつ `N1N2` を含む
    - dot候補: `aligned-dot-index` / `aligned-dot-index-` / `aligned-dot-index-single` を含む
  - 比較ROI（固定）
    - X: 左端 `18px`
    - Y: `435..1591`
  - 2値化閾値 `th=1,2,3,4` を同時に算出
  - 空っぽ一致対策（候補の自動除外）
    - `union>=200` を要求
    - `coverage>=200px/ROI画素数` を line/dot 双方に要求
  - 濃さ補正
    - `alpha_k`: 線≒k×点 となる最小二乗スケール係数
    - `alpha_l1_scaled`: k適用（0..255にクリップ）後の平均|α差|
  - 可視化（best組み合わせ）
    - 2値形状ヒートマップ（dotのみ青 / lineのみ緑 / 両方ON黒 / 両方OFF白）を thごとに出力
    - α差分強度（|α_line-α_dot|）を赤強度で出力（th=1）
    - どちらも ROI版＋全幅版（`-fullw-`）を出力

### 出力
- CSV: `line_pressure` ごとに th別の best/second を1行へ展開（列数が多いのでフィルタ利用推奨）
- PNG（同一フォルダへ追加出力）
  - `lineN1-vs-dotN1-heatmap-th{th}-P{lineP}.png`
  - `lineN1-vs-dotN1-heatmap-th{th}-fullw-P{lineP}.png`
  - `lineN1-vs-dotN1-diffmag-th1-P{lineP}.png`
  - `lineN1-vs-dotN1-diffmag-th1-fullw-P{lineP}.png`

### 観測（高圧帯域の差）
- ヒートマップで差異は主に輪郭に出る。
- `P<=0.8` では輪郭差で青/緑が混在するが、`P=0.9/1.0` では黒以外が青のみになりやすい（点側が外に出る傾向）。

---

## 追加機能: バッチ比較（別フォルダ）を全画像(w×h)のAlphaDiffに対応

## Verified Findings（確定事項）
### S200 dot2疑似線のdotStep最適値
- 条件: 画像サイズ `2180x2020` / `dpi96` / `S200` / `P1` / `N1` / `scale10` / `transparent`（透過PNG）
- 評価: DotLabのalpha差分（全画素 `|A1-A2|`）で `diff_sum01` 最小を採用
- 結論: `dot2-step=18.00` が最適（`17.9`〜`18.9` を含むスイープでも `18.00` 近傍が底）

### dot2疑似線の残差の見え方
- `dot2-step17.99` vs `dot2-step18.01` の比較では、差分は2つ目ドットの輪郭成分のみ（円内部のもじゃもじゃ無し）。
- したがって、残差は主に「2つ目ドットの中心位置の微小な平行移動（サブピクセル級）」に起因する可能性が高い。

### 目的
- オリジナル画像（line）フォルダと、パラメータスイープ画像（dot / dotstep等）フォルダを総当たりで比較し、どれが最も一致するかをCSVで確認する。

### 使い方
- `Batch: LineN1 vs DotN1 (separate folders)` の `Run batch and export CSV` を使用する。
- `Use full image (w×h) AlphaDiff (instead of ROI 18px)` をONにすると、画像全体で差分統計を計算し、サマリのbest選択も全体指標（`diff_sum01` / `diff_nonzero_px`）になる。
  - OFFのままの場合は従来通り「左帯ROI(18px)」での比較（N1用途）。
