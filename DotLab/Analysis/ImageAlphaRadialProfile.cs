using SkiaSharp;
using System.Collections.Generic;

namespace DotLab.Analysis;

internal static class ImageAlphaRadialProfile
{
    internal readonly record struct AnalysisResult(
        int Width,
        int Height,
        double CenterX,
        double CenterY,
        int BinSize,
        int[] Thresholds,
        int Bins,
        int[] Total,
        long[] SumAlpha,
        int[][] Hits,
        long TotalPixels,
        long AlphaNonZeroPixels,
        long SumAlphaAll)
    {
        internal double MeanAlphaAll => TotalPixels > 0 ? SumAlphaAll / (double)TotalPixels : 0.0;
        internal double MeanAlphaAll01 => MeanAlphaAll / 255.0;
        internal double AlphaNonZeroRate => TotalPixels > 0 ? AlphaNonZeroPixels / (double)TotalPixels : 0.0;
    }

    internal static int[] CreateDefaultThresholds()
    {
        // StrokeSampler側と同じ閾値列で比較できるようにしておく。
        // 1, 10..250(step10), 255
        var list = new List<int>(27) { 1 };
        for (var t = 10; t <= 250; t += 10)
        {
            list.Add(t);
        }
        list.Add(255);
        return list.ToArray();
    }

    internal static AnalysisResult Analyze(SKBitmap bmp, int binSize, int[] thresholds)
    {
        ArgumentNullException.ThrowIfNull(bmp);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (bmp.Width <= 0) throw new ArgumentOutOfRangeException(nameof(bmp), "画像幅が0以下です。");
        if (bmp.Height <= 0) throw new ArgumentOutOfRangeException(nameof(bmp), "画像高さが0以下です。");
        if (binSize <= 0) throw new ArgumentOutOfRangeException(nameof(binSize), "binSizeは1以上を指定してください。");

        // 比較画像は常に同じ描画位置・同じ切り出し・同じサイズで揃えている前提のため、画像中心を基準にする。
        // （alpha重み付き重心は、P/Opの条件差で中心推定が微妙に動きうるため、比較には不利）
        var cx = (bmp.Width - 1) / 2.0;
        var cy = (bmp.Height - 1) / 2.0;

        var maxR = MaxDistanceToCorners(cx, cy, bmp.Width, bmp.Height);
        var bins = (int)Math.Floor(maxR / binSize) + 1;
        if (bins <= 0) bins = 1;

        var total = new int[bins];
        var sumAlpha = new long[bins];

        var hits = new int[thresholds.Length][];
        for (var i = 0; i < thresholds.Length; i++)
        {
            hits[i] = new int[bins];
        }

        var pixels = bmp.Pixels;
        var w = bmp.Width;
        var h = bmp.Height;

        long nonZero = 0;
        long sumAAll = 0;

        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            var dy = y - cy;
            for (var x = 0; x < w; x++)
            {
                var dx = x - cx;
                var r = Math.Sqrt((dx * dx) + (dy * dy));
                var bin = (int)Math.Floor(r / binSize);
                if ((uint)bin >= (uint)bins) continue;

                var a = pixels[row + x].Alpha;
                sumAAll += a;
                if (a != 0) nonZero++;

                total[bin]++;
                sumAlpha[bin] += a;

                for (var tIndex = 0; tIndex < thresholds.Length; tIndex++)
                {
                    if (a >= thresholds[tIndex])
                    {
                        hits[tIndex][bin]++;
                    }
                }
            }
        }

        var totalPixels = (long)w * h;

        return new AnalysisResult(
            Width: w,
            Height: h,
            CenterX: cx,
            CenterY: cy,
            BinSize: binSize,
            Thresholds: thresholds,
            Bins: bins,
            Total: total,
            SumAlpha: sumAlpha,
            Hits: hits,
            TotalPixels: totalPixels,
            AlphaNonZeroPixels: nonZero,
            SumAlphaAll: sumAAll);
    }

    private static double MaxDistanceToCorners(double cx, double cy, int width, int height)
    {
        // 画像の四隅までの最大距離をmaxRとする（中心がずれていても成立）。
        var x0 = 0.0;
        var y0 = 0.0;
        var x1 = width - 1.0;
        var y1 = height - 1.0;

        var d00 = Distance(cx, cy, x0, y0);
        var d10 = Distance(cx, cy, x1, y0);
        var d01 = Distance(cx, cy, x0, y1);
        var d11 = Distance(cx, cy, x1, y1);

        return Math.Max(Math.Max(d00, d10), Math.Max(d01, d11));
    }

    private static double Distance(double x0, double y0, double x1, double y1)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
