using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace DotTester.Helpers;

/// <summary>
/// `normalized-falloff` CSVのLUTを読み込み、r_normで線形補間評価するためのクラスです。
/// </summary>
public sealed class NormalizedFalloffLut
{
    private readonly double[] _mean;

    private NormalizedFalloffLut(double[] mean)
    {
        _mean = mean;
    }

    public static NormalizedFalloffLut CreateFromMeanArray(double[] mean)
    {
        ArgumentNullException.ThrowIfNull(mean);
        if (mean.Length == 0) throw new ArgumentException("mean が空です。", nameof(mean));

        var a = new double[mean.Length];
        Array.Copy(mean, a, mean.Length);
        return new NormalizedFalloffLut(a);
    }

    public static NormalizedFalloffLut LoadFromCsv(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"normalized-falloff CSVが見つかりません: {fullPath}", fullPath);
        }

        var lines = File.ReadAllLines(fullPath);
        var meanByIndex = new List<double>(capacity: Math.Max(256, lines.Length));

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith('#')) continue;
            if (line.StartsWith("r_norm", StringComparison.OrdinalIgnoreCase)) continue;

            // r_norm,mean_alpha,stddev_alpha,count
            var parts = line.Split(',');
            if (parts.Length < 2) continue;

            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
            {
                continue;
            }

            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var mean))
            {
                continue;
            }

            while (meanByIndex.Count <= r)
            {
                meanByIndex.Add(0);
            }

            meanByIndex[r] = mean;
        }

        if (meanByIndex.Count == 0)
        {
            throw new InvalidOperationException($"normalized-falloff CSVの読み取りに失敗しました: {fullPath}");
        }

        return new NormalizedFalloffLut(meanByIndex.ToArray());
    }

    public double Eval(double rNorm)
    {
        if (rNorm <= 0) return _mean[0];

        var i = (int)Math.Floor(rNorm);
        if (i >= _mean.Length - 1) return _mean[^1];

        var t = rNorm - i;
        return (1.0 - t) * _mean[i] + t * _mean[i + 1];
    }
}
