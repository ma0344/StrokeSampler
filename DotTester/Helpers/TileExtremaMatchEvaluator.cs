using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DotTester.Helpers;

public static class TileExtremaMatchEvaluator
{
    public enum ExpectedAlphaMode
    {
        Auto,
        UseAlpha,
        WhiteBackground255MinusLuma,
    }

    public sealed record Inputs(
        SKBitmap Expected,
        SKBitmap Rendered,
        PaperNoiseTile NoiseTile,
        double NoiseScale,
        double NoiseOffsetX,
        double NoiseOffsetY,
        int DiameterPx,
        double RadiusPadPx,
        int BrightTileX,
        int BrightTileY,
        int DarkTileX,
        int DarkTileY,
        ExpectedAlphaMode ExpectedMode);

    public sealed record PointSample(string Group, int X, int Y, byte ExpectedA, byte RenderedA);

    public sealed record PointLists(List<(int x, int y)> BrightPoints, List<(int x, int y)> DarkPoints)
    {
        public int TotalCount => BrightPoints.Count + DarkPoints.Count;
    }

    public sealed record Result(
        IReadOnlyList<PointSample> Samples,
        int BrightCount,
        int DarkCount,
        double Mae,
        double Rmse,
        int MaxAbsError,
        double MeanExpectedA,
        double MeanRenderedA,
        double MeanExpectedA_Bright,
        double MeanRenderedA_Bright,
        double MeanExpectedA_Dark,
        double MeanRenderedA_Dark)
    {
        public string ToReportText()
        {
            var sb = new StringBuilder(4 * 1024);
            sb.AppendLine("Tile extrema match");
            sb.AppendLine($"points: bright={BrightCount} dark={DarkCount} total={Samples.Count}");
            sb.AppendLine($"MAE={Mae:0.###}  RMSE={Rmse:0.###}  MaxAbs={MaxAbsError}");
            sb.AppendLine($"meanA expected={MeanExpectedA:0.###} rendered={MeanRenderedA:0.###}");
            sb.AppendLine($"meanA bright  expected={MeanExpectedA_Bright:0.###} rendered={MeanRenderedA_Bright:0.###}");
            sb.AppendLine($"meanA dark    expected={MeanExpectedA_Dark:0.###} rendered={MeanRenderedA_Dark:0.###}");

            // 誤差の大きい順に上位を出す
            foreach (var s in Samples
                .OrderByDescending(v => Math.Abs(v.RenderedA - v.ExpectedA))
                .ThenBy(v => v.Group)
                .Take(12))
            {
                var d = s.RenderedA - s.ExpectedA;
                sb.AppendLine($"{s.Group} ({s.X},{s.Y})  exp={s.ExpectedA}  ren={s.RenderedA}  d={d}");
            }

            return sb.ToString();
        }
    }

    public static Result Evaluate(Inputs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Expected);
        ArgumentNullException.ThrowIfNull(input.Rendered);
        ArgumentNullException.ThrowIfNull(input.NoiseTile);

        if (input.Expected.Width != input.Rendered.Width || input.Expected.Height != input.Rendered.Height)
        {
            throw new ArgumentException("Expected/Renderedの画像サイズが一致しません。", nameof(input));
        }

        if (input.NoiseTile.Width <= 0 || input.NoiseTile.Height <= 0)
        {
            throw new ArgumentException("NoiseTileのサイズが不正です。", nameof(input));
        }

        if (!double.IsFinite(input.NoiseScale) || input.NoiseScale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(input.NoiseScale));
        }

        var w = input.Rendered.Width;
        var h = input.Rendered.Height;

        var cx = (w - 1) * 0.5;
        var cy = (h - 1) * 0.5;

        var radiusPx = input.DiameterPx * 0.5;
        var effectiveRadiusPx = radiusPx + Math.Max(0.0, input.RadiusPadPx);

        var points = EnumeratePoints(new EnumerateInputs(
            CanvasW: w,
            CanvasH: h,
            DiameterPx: input.DiameterPx,
            RadiusPadPx: input.RadiusPadPx,
            NoiseTileW: input.NoiseTile.Width,
            NoiseTileH: input.NoiseTile.Height,
            NoiseScale: input.NoiseScale,
            NoiseOffsetX: input.NoiseOffsetX,
            NoiseOffsetY: input.NoiseOffsetY,
            BrightTileX: input.BrightTileX,
            BrightTileY: input.BrightTileY,
            DarkTileX: input.DarkTileX,
            DarkTileY: input.DarkTileY));

        var brightPoints = points.BrightPoints;
        var darkPoints = points.DarkPoints;

        var samples = new List<PointSample>(capacity: brightPoints.Count + darkPoints.Count);

        var expectedPixels = input.Expected.Pixels;
        var renderedPixels = input.Rendered.Pixels;

        void AddSamples(string group, List<(int x, int y)> pts)
        {
            foreach (var (x, y) in pts)
            {
                var idx = (y * w) + x;
                var expectedA = GetExpectedAlphaByte(expectedPixels[idx], input.ExpectedMode);
                var renderedA = renderedPixels[idx].Alpha;
                samples.Add(new PointSample(group, x, y, expectedA, renderedA));
            }
        }

        AddSamples("bright", brightPoints);
        AddSamples("dark", darkPoints);

        if (samples.Count == 0)
        {
            throw new InvalidOperationException("評価点が0件です（NoiseScale/Offsetや座標設定を確認してください）。");
        }

        double sumAbs = 0;
        double sumSq = 0;
        var maxAbs = 0;

        double sumExp = 0;
        double sumRen = 0;

        double sumExpBright = 0;
        double sumRenBright = 0;

        double sumExpDark = 0;
        double sumRenDark = 0;

        foreach (var s in samples)
        {
            var d = s.RenderedA - s.ExpectedA;
            var abs = Math.Abs(d);
            sumAbs += abs;
            sumSq += d * d;
            if (abs > maxAbs) maxAbs = abs;

            sumExp += s.ExpectedA;
            sumRen += s.RenderedA;

            if (s.Group == "bright")
            {
                sumExpBright += s.ExpectedA;
                sumRenBright += s.RenderedA;
            }
            else
            {
                sumExpDark += s.ExpectedA;
                sumRenDark += s.RenderedA;
            }
        }

        var n = samples.Count;
        var mae = sumAbs / n;
        var rmse = Math.Sqrt(sumSq / n);

        var meanExp = sumExp / n;
        var meanRen = sumRen / n;

        var brightCount = brightPoints.Count;
        var darkCount = darkPoints.Count;

        var meanExpBright = brightCount > 0 ? (sumExpBright / brightCount) : 0.0;
        var meanRenBright = brightCount > 0 ? (sumRenBright / brightCount) : 0.0;

        var meanExpDark = darkCount > 0 ? (sumExpDark / darkCount) : 0.0;
        var meanRenDark = darkCount > 0 ? (sumRenDark / darkCount) : 0.0;

        return new Result(
            Samples: samples,
            BrightCount: brightCount,
            DarkCount: darkCount,
            Mae: mae,
            Rmse: rmse,
            MaxAbsError: maxAbs,
            MeanExpectedA: meanExp,
            MeanRenderedA: meanRen,
            MeanExpectedA_Bright: meanExpBright,
            MeanRenderedA_Bright: meanRenBright,
            MeanExpectedA_Dark: meanExpDark,
            MeanRenderedA_Dark: meanRenDark);
    }

    public sealed record EnumerateInputs(
        int CanvasW,
        int CanvasH,
        int DiameterPx,
        double RadiusPadPx,
        int NoiseTileW,
        int NoiseTileH,
        double NoiseScale,
        double NoiseOffsetX,
        double NoiseOffsetY,
        int BrightTileX,
        int BrightTileY,
        int DarkTileX,
        int DarkTileY);

    public static PointLists EnumeratePoints(EnumerateInputs input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.CanvasW <= 0) throw new ArgumentOutOfRangeException(nameof(input.CanvasW));
        if (input.CanvasH <= 0) throw new ArgumentOutOfRangeException(nameof(input.CanvasH));
        if (input.DiameterPx <= 0) throw new ArgumentOutOfRangeException(nameof(input.DiameterPx));
        if (input.NoiseTileW <= 0 || input.NoiseTileH <= 0) throw new ArgumentOutOfRangeException(nameof(input.NoiseTileW));
        if (!double.IsFinite(input.NoiseScale) || input.NoiseScale <= 0) throw new ArgumentOutOfRangeException(nameof(input.NoiseScale));

        var cx = (input.CanvasW - 1) * 0.5;
        var cy = (input.CanvasH - 1) * 0.5;

        var radiusPx = input.DiameterPx * 0.5;
        var effectiveRadiusPx = radiusPx + Math.Max(0.0, input.RadiusPadPx);

        var brightPoints = EnumerateTilePointsWithinCircle(
            input.CanvasW,
            input.CanvasH,
            cx,
            cy,
            effectiveRadiusPx,
            input.NoiseTileW,
            input.NoiseTileH,
            input.NoiseScale,
            input.NoiseOffsetX,
            input.NoiseOffsetY,
            input.BrightTileX,
            input.BrightTileY);

        var darkPoints = EnumerateTilePointsWithinCircle(
            input.CanvasW,
            input.CanvasH,
            cx,
            cy,
            effectiveRadiusPx,
            input.NoiseTileW,
            input.NoiseTileH,
            input.NoiseScale,
            input.NoiseOffsetX,
            input.NoiseOffsetY,
            input.DarkTileX,
            input.DarkTileY);

        return new PointLists(brightPoints, darkPoints);
    }

    public static byte GetExpectedAlphaByte(SKColor c, ExpectedAlphaMode mode)
    {
        return GetExpectedAlpha(c, mode);
    }

    private static List<(int x, int y)> EnumerateTilePointsWithinCircle(
        int canvasW,
        int canvasH,
        double cx,
        double cy,
        double effectiveRadiusPx,
        int tileW,
        int tileH,
        double noiseScale,
        double offsetX,
        double offsetY,
        int tileX,
        int tileY)
    {
        if (tileX < 0 || tileX >= tileW) throw new ArgumentOutOfRangeException(nameof(tileX));
        if (tileY < 0 || tileY >= tileH) throw new ArgumentOutOfRangeException(nameof(tileY));

        var xs = EnumerateAxisMatches(canvasW, tileW, noiseScale, offsetX, tileX);
        var ys = EnumerateAxisMatches(canvasH, tileH, noiseScale, offsetY, tileY);

        var list = new List<(int x, int y)>(capacity: Math.Max(16, xs.Count * ys.Count));
        foreach (var y in ys)
        {
            var dy = (y + 0.5) - cy;
            foreach (var x in xs)
            {
                var dx = (x + 0.5) - cx;
                var dist = Math.Sqrt((dx * dx) + (dy * dy));
                if (dist > effectiveRadiusPx) continue;

                list.Add((x, y));
            }
        }

        return list;
    }

    private static List<int> EnumerateAxisMatches(
        int canvasSize,
        int tileSize,
        double noiseScale,
        double offset,
        int tileIndex)
    {
        // ((x+0.5)+offset)/noiseScale = tileIndex+0.5 + k*tileSize
        // x = noiseScale*(tileIndex+0.5+k*tileSize) - offset - 0.5
        var list = new List<int>(capacity: Math.Max(16, canvasSize / tileSize + 4));

        var a = (offset + 0.5) / noiseScale - (tileIndex + 0.5);
        var b = (offset + (canvasSize - 0.5)) / noiseScale - (tileIndex + 0.5);

        var kMin = (int)Math.Floor(a / tileSize) - 2;
        var kMax = (int)Math.Ceiling(b / tileSize) + 2;

        const double eps = 1e-6;

        for (var k = kMin; k <= kMax; k++)
        {
            var xExact = (noiseScale * (tileIndex + 0.5 + (k * (double)tileSize))) - offset - 0.5;
            if (!double.IsFinite(xExact)) continue;

            var xi = (int)Math.Round(xExact, MidpointRounding.AwayFromZero);
            if (xi < 0 || xi >= canvasSize) continue;

            // 「texel中心」をちょうどサンプルできる点だけを採用する。
            if (Math.Abs(xExact - xi) > eps) continue;

            list.Add(xi);
        }

        list.Sort();
        return list;
    }

    private static byte GetExpectedAlpha(SKColor c, ExpectedAlphaMode mode)
    {
        if (mode == ExpectedAlphaMode.UseAlpha)
        {
            return c.Alpha;
        }

        if (mode == ExpectedAlphaMode.WhiteBackground255MinusLuma)
        {
            return (byte)(255 - GetLumaByte(c));
        }

        // Auto
        if (c.Alpha < 255)
        {
            return c.Alpha;
        }

        // 白背景（不透明）を想定
        return (byte)(255 - GetLumaByte(c));
    }

    private static byte GetLumaByte(SKColor c)
    {
        // グレースケール画像前提だが、保険で輝度へ
        var l = (0.2126 * c.Red) + (0.7152 * c.Green) + (0.0722 * c.Blue);
        var g = (int)Math.Round(l, MidpointRounding.AwayFromZero);
        if (g < 0) g = 0;
        if (g > 255) g = 255;
        return (byte)g;
    }
}
