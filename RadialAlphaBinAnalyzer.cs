using System;

namespace StrokeSampler
{
    internal static class RadialAlphaBinAnalyzer
    {
        internal readonly struct AnalysisResult
        {
            public AnalysisResult(int bins, int[] total, long[] sumAlpha, int[][] hits)
            {
                Bins = bins;
                Total = total;
                SumAlpha = sumAlpha;
                Hits = hits;
            }

            public int Bins { get; }
            public int[] Total { get; }
            public long[] SumAlpha { get; }
            public int[][] Hits { get; }
        }

        public static AnalysisResult Analyze(
            byte[] rgba,
            int width,
            int height,
            int binSize,
            int[] radialAlphaThresholds)
        {
            if (rgba is null)
            {
                throw new ArgumentNullException(nameof(rgba));
            }

            if (radialAlphaThresholds is null)
            {
                throw new ArgumentNullException(nameof(radialAlphaThresholds));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            if (binSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(binSize));
            }

            if (rgba.Length < width * height * 4)
            {
                throw new ArgumentException("RGBAバッファのサイズが画像サイズに対して不足しています。", nameof(rgba));
            }

            var cx = (width - 1) / 2.0;
            var cy = (height - 1) / 2.0;

            var maxR = Math.Sqrt(cx * cx + cy * cy);
            var bins = (int)Math.Floor(maxR / binSize) + 1;

            var total = new int[bins];
            var sumAlpha = new long[bins];

            var hits = new int[radialAlphaThresholds.Length][];
            for (var i = 0; i < radialAlphaThresholds.Length; i++)
            {
                hits[i] = new int[bins];
            }

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var r = Math.Sqrt((dx * dx) + (dy * dy));
                    var bin = (int)Math.Floor(r / binSize);
                    if ((uint)bin >= (uint)bins)
                    {
                        continue;
                    }

                    var idx = (y * width + x) * 4;
                    var a = rgba[idx + 3];

                    total[bin]++;
                    sumAlpha[bin] += a;

                    for (var tIndex = 0; tIndex < radialAlphaThresholds.Length; tIndex++)
                    {
                        if (a >= radialAlphaThresholds[tIndex])
                        {
                            hits[tIndex][bin]++;
                        }
                    }
                }
            }

            return new AnalysisResult(bins, total, sumAlpha, hits);
        }
    }
}
