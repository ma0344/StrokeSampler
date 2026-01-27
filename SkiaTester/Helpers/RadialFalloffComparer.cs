using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace SkiaTester.Helpers;

public static class RadialFalloffComparer
{
    public static (double mae, double rmse) CompareCsv(string aPath, string bPath)
    {
        var a = ReadMeanAlphaByR(aPath);
        var b = ReadMeanAlphaByR(bPath);

        var n = Math.Min(a.Count, b.Count);
        if (n <= 0) return (double.NaN, double.NaN);

        double sumAbs = 0;
        double sumSq = 0;

        for (var i = 0; i < n; i++)
        {
            var d = a[i] - b[i];
            sumAbs += Math.Abs(d);
            sumSq += d * d;
        }

        return (sumAbs / n, Math.Sqrt(sumSq / n));
    }

    /// <summary>
    /// r,mean_alpha,stddev_alpha 形式のCSV同士を比較し、mean/stddevそれぞれのMAE/RMSEを返します。
    /// </summary>
    public static ((double mae, double rmse) mean, (double mae, double rmse) stddev) CompareCsvWithStddev(string aPath, string bPath)
    {
        var a = ReadMeanAndStddevAlphaByR(aPath);
        var b = ReadMeanAndStddevAlphaByR(bPath);

        var n = Math.Min(a.mean.Count, b.mean.Count);
        n = Math.Min(n, Math.Min(a.stddev.Count, b.stddev.Count));
        if (n <= 0)
        {
            throw new InvalidOperationException("CSVにmean/stddev列が存在しないか、比較可能な行がありません。");
        }

        double meanSumAbs = 0;
        double meanSumSq = 0;
        double stdSumAbs = 0;
        double stdSumSq = 0;

        for (var i = 0; i < n; i++)
        {
            var dm = a.mean[i] - b.mean[i];
            meanSumAbs += Math.Abs(dm);
            meanSumSq += dm * dm;

            var ds = a.stddev[i] - b.stddev[i];
            stdSumAbs += Math.Abs(ds);
            stdSumSq += ds * ds;
        }

        return ((meanSumAbs / n, Math.Sqrt(meanSumSq / n)), (stdSumAbs / n, Math.Sqrt(stdSumSq / n)));
    }

    private static List<double> ReadMeanAlphaByR(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is empty", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var lines = File.ReadAllLines(fullPath);

        var list = new List<double>(capacity: Math.Max(0, lines.Length - 1));

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;
            if (line.StartsWith("r,", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split(',');
            if (parts.Length < 2) continue;

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                continue;
            }

            list.Add(v);
        }

        return list;
    }

    private static (List<double> mean, List<double> stddev) ReadMeanAndStddevAlphaByR(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path is empty", nameof(path));

        var fullPath = Path.GetFullPath(path);
        var lines = File.ReadAllLines(fullPath);

        var mean = new List<double>(capacity: Math.Max(0, lines.Length - 1));
        var stddev = new List<double>(capacity: Math.Max(0, lines.Length - 1));

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("#", StringComparison.Ordinal)) continue;
            if (line.StartsWith("r,", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("r_norm,", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split(',');
            if (parts.Length < 3) continue;

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var m))
            {
                continue;
            }

            if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            {
                continue;
            }

            mean.Add(m);
            stddev.Add(s);
        }

        return (mean, stddev);
    }
}
