using SkiaSharp;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DotTester.Helpers;

internal static class EdgeScatterExportService
{
    internal sealed class Settings
    {
        public required int AlphaMinByte { get; init; }
        public required int AlphaMaxByte { get; init; }
        public required int StridePx { get; init; }
        public required int MaxRows { get; init; }
    }

    internal static string ExportCsv(
        SKBitmap expected,
        DotReproRenderer.Options opt,
        int kMeanStridePx,
        Settings settings,
        string outCsvPath)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(opt);
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(outCsvPath)) throw new ArgumentException("出力パスが空です。", nameof(outCsvPath));

        if (expected.Width <= 0 || expected.Height <= 0) throw new ArgumentException("Expected のサイズが不正です。", nameof(expected));
        if (expected.Width != expected.Height) throw new ArgumentException("Expected は正方形を想定しています。", nameof(expected));

        if (opt.CanvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(opt.CanvasSizePx));
        if (opt.CanvasSizePx != expected.Width) throw new ArgumentException("Expected と CanvasSizePx が一致していません。", nameof(opt));
        if (!opt.UsePaperNoise || opt.NoiseTile == null) throw new ArgumentException("PaperNoise が無効です。", nameof(opt));

        var aMin = Math.Clamp(settings.AlphaMinByte, 0, 255);
        var aMax = Math.Clamp(settings.AlphaMaxByte, 0, 255);
        if (aMin > aMax) (aMin, aMax) = (aMax, aMin);

        var stride = settings.StridePx;
        if (stride <= 0) stride = 1;

        var maxRows = settings.MaxRows;
        if (maxRows <= 0) maxRows = 200_000;

        var canvas = opt.CanvasSizePx;
        var diameter = opt.DiameterPx;
        if (diameter <= 0) throw new ArgumentOutOfRangeException(nameof(opt.DiameterPx));

        var radiusPx = diameter * 0.5;
        var effectiveRadiusPx = radiusPx + Math.Max(0.0, opt.RadiusPadPx);

        var cx = (canvas - 1) * 0.5;
        var cy = (canvas - 1) * 0.5;

        var falloffRNormScale = opt.FalloffRNormScale;
        if (!double.IsFinite(falloffRNormScale) || falloffRNormScale <= 0) falloffRNormScale = 1.0;

        var falloffGamma = opt.FalloffGamma;
        if (!double.IsFinite(falloffGamma) || falloffGamma <= 0) falloffGamma = 1.0;

        var falloffScale = opt.FalloffScale;
        if (!double.IsFinite(falloffScale) || falloffScale < 0) falloffScale = 0;

        var pressure = opt.Pressure;
        if (!double.IsFinite(pressure) || pressure < 0 || pressure > 1.0) throw new ArgumentOutOfRangeException(nameof(opt.Pressure));

        var evaluator = DotReproRenderer.CreatePointEvaluator(opt, kMeanStridePx);

        var expectedPixels = expected.Pixels;
        var noiseTile = opt.NoiseTile;

        var dir = Path.GetDirectoryName(outCsvPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder(capacity: 8 * 1024 * 1024);
        sb.AppendLine("x,y,dist_px,r_norm,f,base_a01,expected_a8,rendered_a8,n01_nearest,n01_bilinear,n01_bicubic");

        var rows = 0;
        var visited = 0;

        for (var y = 0; y < canvas; y += stride)
        {
            var dy = (y + 0.5) - cy;
            for (var x = 0; x < canvas; x += stride)
            {
                visited++;

                var idx = (y * canvas) + x;
                var expectedA8 = expectedPixels[idx].Alpha;
                if (expectedA8 < aMin || expectedA8 > aMax) continue;

                var dx = (x + 0.5) - cx;
                var dist = Math.Sqrt((dx * dx) + (dy * dy));
                if (dist > effectiveRadiusPx) continue;

                // rNorm: S200基準の0..100
                var rNorm = dist * (200.0 / diameter);
                rNorm *= falloffRNormScale;

                var f = 1.0;
                if (opt.FalloffLut != null)
                {
                    f = opt.FalloffLut.Eval(rNorm);
                }
                f = Math.Pow(Math.Clamp(f, 0.0, 1.0), falloffGamma);
                f = Math.Clamp(f * falloffScale, 0.0, 1.0);

                var baseA01 = f * pressure;

                var nx = ((x + 0.5) + opt.PaperNoiseOffsetX) / opt.PaperNoiseScale;
                var ny = ((y + 0.5) + opt.PaperNoiseOffsetY) / opt.PaperNoiseScale;

                var nNearest = noiseTile.SampleAlpha01(nx, ny, PaperNoiseTile.SamplingMode.Nearest);
                var nBilinear = noiseTile.SampleAlpha01(nx, ny, PaperNoiseTile.SamplingMode.Bilinear);
                var nBicubic = noiseTile.SampleAlpha01(nx, ny, PaperNoiseTile.SamplingMode.Bicubic);

                var renderedA8 = evaluator.EvalAlphaByte(x, y);

                sb.Append(x.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(y.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(dist.ToString("0.#######", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(rNorm.ToString("0.#######", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(f.ToString("0.########", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(baseA01.ToString("0.########", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(expectedA8.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(renderedA8.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(Math.Clamp(nNearest, 0.0, 1.0).ToString("0.########", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(Math.Clamp(nBilinear, 0.0, 1.0).ToString("0.########", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(Math.Clamp(nBicubic, 0.0, 1.0).ToString("0.########", CultureInfo.InvariantCulture));
                sb.AppendLine();

                rows++;
                if (rows >= maxRows) goto Done;
            }
        }

Done:
        File.WriteAllText(outCsvPath, sb.ToString(), Encoding.UTF8);
        return $"Exported edge scatter CSV. rows={rows} visited={visited} a8=[{aMin}..{aMax}] stride={stride} out={outCsvPath}";
    }
}
