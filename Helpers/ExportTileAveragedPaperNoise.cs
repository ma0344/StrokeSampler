using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;

namespace StrokeSampler
{
    internal static class ExportTileAveragedPaperNoise
    {
        internal static async Task ExportAsync(int tileSize, int offsetX, int offsetY, bool transparentOutput, bool autoOffset, bool flatten, int flattenRadius)
        {
            if (tileSize <= 0) throw new ArgumentOutOfRangeException(nameof(tileSize));
            if (flattenRadius < 1) flattenRadius = 1;

            var sourcePicker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            sourcePicker.FileTypeFilter.Add(".png");
            sourcePicker.FileTypeFilter.Add(".jpg");
            sourcePicker.FileTypeFilter.Add(".jpeg");

            var sourceFile = await sourcePicker.PickSingleFileAsync();
            if (sourceFile == null)
            {
                return;
            }

            var device = CanvasDevice.GetSharedDevice();

            byte[] src;
            int w;
            int h;
            using (var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read))
            using (var bmp = await CanvasBitmap.LoadAsync(device, sourceStream))
            {
                w = (int)bmp.SizeInPixels.Width;
                h = (int)bmp.SizeInPixels.Height;
                src = bmp.GetPixelBytes();
            }

            // タイル分割 ???領域
            var startX = Mod(offsetX, tileSize);
            var startY = Mod(offsetY, tileSize);

            var tilesX = (w - startX) / tileSize;
            var tilesY = (h - startY) / tileSize;
            if (tilesX <= 0 || tilesY <= 0)
            {
                return;
            }

            // Dotの影響が強い場合、背景寄りタイルを優先した方が縫い目が減りやすい。
            // ここでは「平均輝度が白に近い」タイルから上位を採用する。
            var candidates = new List<(double score, int x0, int y0)>();
            for (var ty = 0; ty < tilesY; ty++)
            {
                for (var tx = 0; tx < tilesX; tx++)
                {
                    var x0 = startX + tx * tileSize;
                    var y0 = startY + ty * tileSize;
                    var bgScore = ComputeBackgroundScore(src, w, h, x0, y0, tileSize);
                    candidates.Add((bgScore, x0, y0));
                }
            }

            candidates.Sort((a, b) => b.score.CompareTo(a.score));
            var take = Math.Min(candidates.Count, Math.Max(16, candidates.Count / 4));

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"paper-noise-avg-tile-{tileSize}-off{startX}_{startY}-n{take}" + (transparentOutput ? "-transparent" : "") + (autoOffset ? "-auto" : "") + (flatten ? $"-flatten{flattenRadius}" : "")
            };
            savePicker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

            var saveFile = await savePicker.PickSaveFileAsync();
            if (saveFile == null)
            {
                return;
            }

            var sum = new double[tileSize * tileSize * 4];
            var tileCount = 0;
            for (var i = 0; i < take; i++)
            {
                var c = candidates[i];
                AccumulateTile(src, w, h, c.x0, c.y0, tileSize, sum);
                tileCount++;
            }

            if (tileCount <= 0) return;

            var dst = new byte[tileSize * tileSize * 4];
            for (var i = 0; i < dst.Length; i += 4)
            {
                var b = sum[i + 0] / tileCount;
                var g = sum[i + 1] / tileCount;
                var r = sum[i + 2] / tileCount;
                var a = sum[i + 3] / tileCount;

                if (!transparentOutput)
                {
                    a = 255.0;
                }

                dst[i + 0] = ClampToByte(b);
                dst[i + 1] = ClampToByte(g);
                dst[i + 2] = ClampToByte(r);
                dst[i + 3] = ClampToByte(a);
            }

            if (flatten)
            {
                dst = FlattenBgra(dst, tileSize, tileSize, flattenRadius, transparentOutput);
            }

            CachedFileManager.DeferUpdates(saveFile);
            using (IRandomAccessStream outStream = await saveFile.OpenAsync(FileAccessMode.ReadWrite))
            using (var target = new CanvasRenderTarget(device, tileSize, tileSize, 96f))
            {
                target.SetPixelBytes(dst);
                await target.SaveAsync(outStream, CanvasBitmapFileFormat.Png);
            }
            await CachedFileManager.CompleteUpdatesAsync(saveFile);
        }

        private static byte[] FlattenBgra(byte[] bgra, int w, int h, int radius, bool transparentOutput)
        {
            // 簡易ボックスブラー（分離せずにそのまま実装。半径は小さめ想定）
            // 低周波(グラデーション)を推定し、除去して平均輝度へ戻す。

            var blurred = BoxBlurBgra(bgra, w, h, radius);

            // 元/blurの比でフラット化（乗算モデル）。暗いところを持ち上げ、明るいところを抑える。
            // 出力の平均輝度は維持する。
            double meanSrc = 0;
            double meanOut = 0;
            var n = w * h;
            for (var i = 0; i < bgra.Length; i += 4)
            {
                meanSrc += (bgra[i + 2] + bgra[i + 1] + bgra[i + 0]) / 3.0;
            }
            meanSrc /= n;

            var dst = new byte[bgra.Length];
            for (var i = 0; i < bgra.Length; i += 4)
            {
                var b = bgra[i + 0];
                var g = bgra[i + 1];
                var r = bgra[i + 2];

                var bb = blurred[i + 0];
                var bg = blurred[i + 1];
                var br = blurred[i + 2];

                // blurが極端に小さいと割り算が暴れるので下限を設ける
                var db = b / Math.Max(1.0, bb);
                var dg = g / Math.Max(1.0, bg);
                var dr = r / Math.Max(1.0, br);

                // 0..255スケールへ
                var ob = db * meanSrc;
                var og = dg * meanSrc;
                var orr = dr * meanSrc;

                dst[i + 0] = ClampToByte(ob);
                dst[i + 1] = ClampToByte(og);
                dst[i + 2] = ClampToByte(orr);
                dst[i + 3] = transparentOutput ? bgra[i + 3] : (byte)255;

                meanOut += (dst[i + 2] + dst[i + 1] + dst[i + 0]) / 3.0;
            }

            meanOut /= n;
            if (meanOut <= 0) return dst;

            // 微調整: 出力平均が元に一致するようにスケール
            var k = meanSrc / meanOut;
            for (var i = 0; i < dst.Length; i += 4)
            {
                dst[i + 0] = ClampToByte(dst[i + 0] * k);
                dst[i + 1] = ClampToByte(dst[i + 1] * k);
                dst[i + 2] = ClampToByte(dst[i + 2] * k);
            }

            return dst;
        }

        private static byte[] BoxBlurBgra(byte[] bgra, int w, int h, int radius)
        {
            var dst = new byte[bgra.Length];
            var size = radius * 2 + 1;
            var area = size * size;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    int sumB = 0, sumG = 0, sumR = 0, sumA = 0;
                    for (var oy = -radius; oy <= radius; oy++)
                    {
                        var sy = y + oy;
                        if (sy < 0) sy = 0;
                        if (sy >= h) sy = h - 1;
                        for (var ox = -radius; ox <= radius; ox++)
                        {
                            var sx = x + ox;
                            if (sx < 0) sx = 0;
                            if (sx >= w) sx = w - 1;
                            var si = (sy * w + sx) * 4;
                            sumB += bgra[si + 0];
                            sumG += bgra[si + 1];
                            sumR += bgra[si + 2];
                            sumA += bgra[si + 3];
                        }
                    }

                    var di = (y * w + x) * 4;
                    dst[di + 0] = (byte)(sumB / area);
                    dst[di + 1] = (byte)(sumG / area);
                    dst[di + 2] = (byte)(sumR / area);
                    dst[di + 3] = (byte)(sumA / area);
                }
            }

            return dst;
        }

        private static void FindBestOffset(byte[] src, int w, int h, int tileSize, out int bestX, out int bestY)
        {
            // 簡易: 0..tileSize-1 を粗ステップで探索して縫い目(左右+上下)が最小のものを採用
            // 後で必要なら二段階探索に拡張できる。
            var step = Math.Max(1, tileSize / 64); // tileSize=348なら5あたり
            bestX = 0;
            bestY = 0;
            var bestScore = double.PositiveInfinity;

            for (var oy = 0; oy < tileSize; oy += step)
            {
                for (var ox = 0; ox < tileSize; ox += step)
                {
                    var score = ComputeSeamScore(src, w, h, tileSize, ox, oy);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestX = ox;
                        bestY = oy;
                    }
                }
            }
        }

        private static double ComputeSeamScore(byte[] src, int w, int h, int tileSize, int startX, int startY)
        {
            var tilesX = (w - startX) / tileSize;
            var tilesY = (h - startY) / tileSize;
            if (tilesX <= 0 || tilesY <= 0) return double.PositiveInfinity;

            // 端の比較に使うタイルを少数だけサンプル（左上のdot影響を避けるため外周は避ける）
            var sampleCount = 0;
            double acc = 0;

            var stride = w * 4;

            for (var ty = 0; ty < tilesY; ty++)
            {
                for (var tx = 0; tx < tilesX; tx++)
                {
                    // 画像左上のDotが強いので、左上寄りはスキップしがち
                    if (tx == 0 && ty == 0) continue;

                    var x0 = startX + tx * tileSize;
                    var y0 = startY + ty * tileSize;

                    // 左右の縫い目: x=0 と x=tileSize-1
                    for (var y = 0; y < tileSize; y += 8)
                    {
                        var sy = y0 + y;
                        if ((uint)sy >= (uint)h) break;
                        var leftIdx = sy * stride + (x0 + 0) * 4;
                        var rightIdx = sy * stride + (x0 + (tileSize - 1)) * 4;
                        if ((uint)(rightIdx + 3) >= (uint)src.Length) break;

                        var dl = Luma(src, leftIdx);
                        var dr = Luma(src, rightIdx);
                        var d = dl - dr;
                        acc += d * d;
                        sampleCount++;
                    }

                    // 上下の縫い目: y=0 と y=tileSize-1
                    for (var x = 0; x < tileSize; x += 8)
                    {
                        var sx = x0 + x;
                        if ((uint)sx >= (uint)w) break;
                        var topIdx = (y0 + 0) * stride + sx * 4;
                        var botIdx = (y0 + (tileSize - 1)) * stride + sx * 4;
                        if ((uint)(botIdx + 3) >= (uint)src.Length) break;

                        var dt = Luma(src, topIdx);
                        var db = Luma(src, botIdx);
                        var d = dt - db;
                        acc += d * d;
                        sampleCount++;
                    }
                }
            }

            if (sampleCount <= 0) return double.PositiveInfinity;
            return acc / sampleCount;
        }

        private static double Luma(byte[] bgra, int idx)
        {
            // BGRA
            var b = bgra[idx + 0];
            var g = bgra[idx + 1];
            var r = bgra[idx + 2];
            return (r + g + b) / (3.0 * 255.0);
        }

        private static void AccumulateTile(byte[] src, int w, int h, int x0, int y0, int tileSize, double[] sum)
        {
            var stride = w * 4;

            for (var y = 0; y < tileSize; y++)
            {
                var sy = y0 + y;
                if ((uint)sy >= (uint)h) break;

                var rowBase = sy * stride;
                for (var x = 0; x < tileSize; x++)
                {
                    var sx = x0 + x;
                    if ((uint)sx >= (uint)w) break;

                    var srcIdx = rowBase + sx * 4;
                    var dstIdx = (y * tileSize + x) * 4;

                    sum[dstIdx + 0] += src[srcIdx + 0];
                    sum[dstIdx + 1] += src[srcIdx + 1];
                    sum[dstIdx + 2] += src[srcIdx + 2];
                    sum[dstIdx + 3] += src[srcIdx + 3];
                }
            }
        }

        private static int Mod(int x, int m)
        {
            if (m <= 0) return 0;
            var r = x % m;
            return r < 0 ? r + m : r;
        }

        private static byte ClampToByte(double v)
        {
            if (v <= 0) return 0;
            if (v >= 255) return 255;
            return (byte)(int)Math.Round(v);
        }

        private static double ComputeBackgroundScore(byte[] src, int w, int h, int x0, int y0, int tileSize)
        {
            // 白背景に近いほど高スコア。
            // タイル内の少数サンプル点の輝度を平均して評価する。
            var stride = w * 4;
            double acc = 0;
            var cnt = 0;

            for (var y = 0; y < tileSize; y += 16)
            {
                var sy = y0 + y;
                if ((uint)sy >= (uint)h) break;
                var rowBase = sy * stride;
                for (var x = 0; x < tileSize; x += 16)
                {
                    var sx = x0 + x;
                    if ((uint)sx >= (uint)w) break;
                    var idx = rowBase + sx * 4;
                    if ((uint)(idx + 3) >= (uint)src.Length) break;
                    acc += Luma(src, idx);
                    cnt++;
                }
            }

            if (cnt <= 0) return 0;
            return acc / cnt;
        }
    }
}
