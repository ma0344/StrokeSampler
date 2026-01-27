using SkiaSharp;
using System;

namespace SkiaTester.Helpers;

public static class RadialFalloff
{
    public static double[] ComputeMeanAlphaByRadius(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var w = bitmap.Width;
        var h = bitmap.Height;
        if (w <= 0 || h <= 0) return Array.Empty<double>();

        // UWP‘¤(StrokeHelpers.ComputeRadialMeanAlphaD)‚Æ“¯ˆê‚Ì’è‹`
        var cx = (w - 1) / 2.0;
        var cy = (h - 1) / 2.0;

        var maxR = Math.Sqrt(cx * cx + cy * cy);
        var bins = (int)Math.Floor(maxR) + 1;
        var sum = new double[bins];
        var count = new int[bins];

        for (var y = 0; y < h; y++)
        {
            var dy = y - cy;
            for (var x = 0; x < w; x++)
            {
                var dx = x - cx;
                var r = Math.Sqrt(dx * dx + dy * dy);
                var bin = (int)Math.Floor(r);
                if ((uint)bin >= (uint)bins) continue;

                var a = bitmap.GetPixel(x, y).Alpha;
                sum[bin] += a / 255.0;
                count[bin]++;
            }
        }

        var mean = new double[bins];
        for (var r = 0; r < mean.Length; r++)
        {
            mean[r] = count[r] == 0 ? 0.0 : (sum[r] / count[r]);
        }

        return mean;
    }

    public static (double[] mean, double[] stddev) ComputeMeanAndStddevAlphaByRadius(SKBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        var w = bitmap.Width;
        var h = bitmap.Height;
        if (w <= 0 || h <= 0) return (Array.Empty<double>(), Array.Empty<double>());

        // UWP‘¤(StrokeHelpers.ComputeRadialMeanAlphaD)‚Æ“¯ˆê‚Ì’è‹`
        var cx = (w - 1) / 2.0;
        var cy = (h - 1) / 2.0;

        var maxR = Math.Sqrt(cx * cx + cy * cy);
        var bins = (int)Math.Floor(maxR) + 1;
        var sum = new double[bins];
        var sumSq = new double[bins];
        var count = new int[bins];

        for (var y = 0; y < h; y++)
        {
            var dy = y - cy;
            for (var x = 0; x < w; x++)
            {
                var dx = x - cx;
                var r = Math.Sqrt(dx * dx + dy * dy);
                var bin = (int)Math.Floor(r);
                if ((uint)bin >= (uint)bins) continue;

                var a = bitmap.GetPixel(x, y).Alpha / 255.0;
                sum[bin] += a;
                sumSq[bin] += a * a;
                count[bin]++;
            }
        }

        var mean = new double[bins];
        var stddev = new double[bins];
        for (var i = 0; i < bins; i++)
        {
            var n = count[i];
            if (n <= 0)
            {
                mean[i] = 0.0;
                stddev[i] = 0.0;
                continue;
            }

            var m = sum[i] / n;
            // E[x^2] - (E[x])^2
            var v = (sumSq[i] / n) - (m * m);
            if (v < 0) v = 0; // •‚“®¬”Œë·‘Îô

            mean[i] = m;
            stddev[i] = Math.Sqrt(v);
        }

        return (mean, stddev);
    }

    public static (double[] mean, double[] stddev) ComputeMeanAndStddevAlphaByRadius(double[] alpha01, int w, int h)
    {
        ArgumentNullException.ThrowIfNull(alpha01);
        if (w <= 0 || h <= 0) return (Array.Empty<double>(), Array.Empty<double>());
        if (alpha01.Length != w * h) throw new ArgumentException("alpha01‚Ì’·‚³‚ªw*h‚Æˆê’v‚µ‚Ä‚¢‚Ü‚¹‚ñB", nameof(alpha01));

        var cx = (w - 1) / 2.0;
        var cy = (h - 1) / 2.0;

        var maxR = Math.Sqrt(cx * cx + cy * cy);
        var bins = (int)Math.Floor(maxR) + 1;
        var sum = new double[bins];
        var sumSq = new double[bins];
        var count = new int[bins];

        for (var y = 0; y < h; y++)
        {
            var dy = y - cy;
            for (var x = 0; x < w; x++)
            {
                var dx = x - cx;
                var r = Math.Sqrt(dx * dx + dy * dy);
                var bin = (int)Math.Floor(r);
                if ((uint)bin >= (uint)bins) continue;

                var a = alpha01[y * w + x];
                sum[bin] += a;
                sumSq[bin] += a * a;
                count[bin]++;
            }
        }

        var mean = new double[bins];
        var stddev = new double[bins];
        for (var i = 0; i < bins; i++)
        {
            var n = count[i];
            if (n <= 0)
            {
                mean[i] = 0.0;
                stddev[i] = 0.0;
                continue;
            }

            var m = sum[i] / n;
            var v = (sumSq[i] / n) - (m * m);
            if (v < 0) v = 0;
            mean[i] = m;
            stddev[i] = Math.Sqrt(v);
        }

        return (mean, stddev);
    }
}
