using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
namespace DotLab.Rendering
{
    internal static class Falloff
    {
        public static float[] CreateFromNormalizedFalloffCsv(int canvasSizePx, int diameterPx, string csvPath)
        {
            if (canvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(canvasSizePx));
            if (diameterPx <= 0) throw new ArgumentOutOfRangeException(nameof(diameterPx));
            if (string.IsNullOrWhiteSpace(csvPath)) throw new ArgumentException("CSVパスが空です。", nameof(csvPath));
            if (!File.Exists(csvPath)) throw new FileNotFoundException("CSVファイルが見つかりません。", csvPath);

            var lut = ReadNormalizedFalloffCsv(csvPath);
            if (lut.Count < 2) throw new InvalidOperationException("normalized falloff CSV の有効データが2点未満です。");

            var outF = new float[canvasSizePx * canvasSizePx];
            var cx = (canvasSizePx - 1) * 0.5;
            var cy = (canvasSizePx - 1) * 0.5;
            var radius = diameterPx * 0.5;
            if (radius <= 0) return outF;

            for (var y = 0; y < canvasSizePx; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < canvasSizePx; x++)
                {
                    var dx = x - cx;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > radius) continue;

                    var r01 = dist / radius;
                    outF[y * canvasSizePx + x] = (float)SampleMeanAlphaByR01(lut, r01);
                }
            }

            return outF;
        }

        private static List<(double R01, double MeanAlpha)> ReadNormalizedFalloffCsv(string csvPath)
        {
            var lut = new List<(double R01, double MeanAlpha)>();

            foreach (var rawLine in File.ReadLines(csvPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("r_norm", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(',');
                if (parts.Length < 2) continue;

                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rNorm)) continue;
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var meanAlpha)) continue;

                // r_norm は 0..100 を想定。r01 に変換。
                lut.Add((rNorm / 100.0, meanAlpha));
            }

            lut.Sort(static (a, b) => a.R01.CompareTo(b.R01));
            return lut;
        }

        private static double SampleMeanAlphaByR01(List<(double R01, double MeanAlpha)> lut, double r01)
        {
            if (lut.Count == 0) return 0;
            if (r01 <= lut[0].R01) return lut[0].MeanAlpha;
            if (r01 >= lut[lut.Count - 1].R01) return lut[lut.Count - 1].MeanAlpha;

            for (var i = 1; i < lut.Count; i++)
            {
                var (r1, v1) = lut[i];
                if (r01 <= r1)
                {
                    var (r0, v0) = lut[i - 1];
                    if (r1 <= r0) return v1;

                    var t = (r01 - r0) / (r1 - r0);
                    return v0 + (v1 - v0) * t;
                }
            }

            return lut[lut.Count - 1].MeanAlpha;
        }

        public static float[] CreateIdealCircle(int canvasSizePx, int diameterPx)
        {
            if (canvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(canvasSizePx));
            if (diameterPx <= 0) throw new ArgumentOutOfRangeException(nameof(diameterPx));

            var outF = new float[canvasSizePx * canvasSizePx];
            var cx = (canvasSizePx - 1) * 0.5;
            var cy = (canvasSizePx - 1) * 0.5;
            var radius = diameterPx * 0.5;
            if (radius <= 0) return outF;

            for (var y = 0; y < canvasSizePx; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < canvasSizePx; x++)
                {
                    var dx = x - cx;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > radius) continue;

                    // ひとまず切り分け用: 中心=1、外縁=0 の線形 falloff。
                    // 目標のNormalized falloff(LUT/CSV)が入るまでは、勾配が出る実装にして f(r) の効きを確認する。
                    outF[y * canvasSizePx + x] = (float)Math.Clamp(1.0 - (dist / radius), 0.0, 1.0);
                }
            }

            return outF;
        }

        public static float[] CreateFlat(int canvasSizePx)
        {
            if (canvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(canvasSizePx));

            var outF = new float[canvasSizePx * canvasSizePx];
            Array.Fill(outF, 1f);
            return outF;
        }
    }

}

