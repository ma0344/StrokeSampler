# Skia移植：スタンプ方式の濃すぎ対策（Q13?Q15の具体アルゴリズム案）

## 背景
LUT方式（`mask = F(r_norm)`）でISFストロークをキャンバスに描画できる段階まで来たが、描画された線が明らかに濃すぎる。

典型的には以下が原因になりやすい。
- スタンプ（サンプル点）が過密で、同一点近傍に過剰に重なる
- `SrcOver` での累積により、短距離移動でαが急激に飽和する

本ドキュメントは `docs/drow-inkcanvas-pencil-from-code-answers.md` の **Q13?Q15** の意図を、Skia側実装に落とし込むための擬似コード案として整理する。

---

## 前提
- 合成は当面 `SrcOver` 前提（必要なら後で差し替え）
- LUTでのマスク生成は維持：`mask = F(r_norm)`
- 紙目ノイズは `noise`（0..1）として乗算（正規/反転は別途選択）
- 画像全域の再計算は避け、ブラシ周辺の小領域のみ更新する

---

## 実装方針（段階的に入れる）
- **Step A（最小）**：距離ベースの間引き（打点密度を下げる）
- **Step B（強化）**：塗り済みマスク（coverage）で差分塗りに近づける（Q13/Q14）
- **Step C（調整）**：重ね塗り感を残すため、禁止ではなく上限付き/減衰付きにする（Q15）

---

## Step A: 距離ベースの間引き（まずこれ）
目的：同一点近傍への過剰スタンプを抑え、濃すぎの大半を低コストで改善する。

```text
radius = S * 0.5
spacingMinPx = max(0.5, k * radius)  // kは0.1?0.3程度から調整

filtered = []
prev = null
for p in strokePoints:
  if prev == null:
    filtered.add(p)
    prev = p
    continue

  d = distance(p.xy, prev.xy)
  if d < spacingMinPx:
    continue

  filtered.add(p)
  prev = p

// filtered をスタンプ列として描画へ
```

メモ：
- まずはこれだけ入れて、濃すぎが改善するか確認する。
- `k` は速度依存（遅いほど密度が上がる）を見て調整対象。

---

## Step B: 塗り済みマスク（coverage）による差分塗り（Q13/Q14）
目的：「既に塗られているピクセルは上書きしない/増えにくい」挙動を作り、結果として差分だけ増える方向へ寄せる。

### データ構造
- `coverage[x,y]`：0..1 の塗り済み度
  - 全画面更新しない（更新はスタンプbboxのみ）

### 描画（mask適用 + coverage更新）
```text
function stamp(xc, yc, S, baseAlpha, F_LUT, noise, coverage):
  radius = S * 0.5
  bbox = [xc-radius .. xc+radius] × [yc-radius .. yc+radius]

  for each pixel (x,y) in bbox:
    r = distance((x,y),(xc,yc))
    if r > radius: continue

    r_norm = r * (S0 / S)
    mask = F_LUT(r_norm)      // 0..1
    n = sampleNoise(x,y)      // 0..1 (invertはここで切替)
    a = baseAlpha * mask * n  // 0..1

    // 差分塗り：塗り済みほど寄与を減らす
    remain = 1 - coverage[x,y]
    a_eff = a * remain

    // SrcOver（αのみの説明）
    dstA = canvasA[x,y]
    outA = dstA + a_eff * (1 - dstA)
    canvasA[x,y] = outA

    // Q14: 描画と同時にマスク更新（bbox範囲だけ）
    coverage[x,y] = clamp01(coverage[x,y] + a_eff)
```

メモ：
- `a_eff = a * (1-coverage)` が Q13 の「差分だけ塗る」の近似。
- bbox単位の更新でコストを抑えるのが Q14 の要点。

---

## Step C: 重ね塗りを許す（Q15）
Step B を強くすると「一切重ならない」寄りになって鉛筆らしさが消えることがある。
その場合は「完全禁止」ではなく上限付き/緩和付きへ。

### C-1) 上限付き（saturate）
```text
coverage[x,y] = clamp01(coverage[x,y] + a_eff * gain)
// gain < 1 で伸びを抑える
```

### C-2) 減衰（時間/距離で緩和）
```text
// 更新範囲だけで良い
coverage[x,y] *= decay  // 0.98?0.999など
```

### C-3) セル単位の近似（軽量化）
ピクセル単位が重い場合、粗いグリッドで近似して上限を設ける。
```text
cell = (floor(x/cellSize), floor(y/cellSize))
if cellStampCount[cell] >= MaxCount:
  a_eff *= 0.2  // またはスキップ
cellStampCount[cell]++
```

---

## 検証（推奨）
- 濃すぎが顕著なストロークで、Step Aのみ→Step B追加→Step C調整の順に適用し、見た目と数値（円内MAE/RMSE）で比較する。
- `representative-sample-subset.md` の観測PNG（dot512-material）と、Skia側が生成した再現dotを直径Sの中心円だけで比較する。

---

## 期待される結果
- 「過密スタンプ」で発生していた過剰累積が緩和され、線がUWP相当の濃度に近づく。
- LUT/紙目/合成（SrcOver）を維持したまま、濃度の主因が打点密度/上書き制御であるかを切り分けられる。
