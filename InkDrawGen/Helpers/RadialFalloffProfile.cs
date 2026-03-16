using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;

namespace InkDrawGen.Helpers
{
  internal sealed class RadialFalloffProfile
  {
    private readonly double[] _radiusPx;
    private readonly double[] _falloff;

    private RadialFalloffProfile(double[] radiusPx, double[] falloff)
    {
      _radiusPx = radiusPx ?? throw new ArgumentNullException(nameof(radiusPx));
      _falloff = falloff ?? throw new ArgumentNullException(nameof(falloff));
      if (_radiusPx.Length != _falloff.Length || _radiusPx.Length == 0)
      {
        throw new ArgumentException("半径配列とfalloff配列が不正です。", nameof(radiusPx));
      }
    }

    internal static async Task<RadialFalloffProfile> LoadAsync(StorageFile csvFile, int scale)
    {
      if (csvFile == null) throw new ArgumentNullException(nameof(csvFile));
      scale = Math.Max(1, scale);

      var text = await FileIO.ReadTextAsync(csvFile, Windows.Storage.Streams.UnicodeEncoding.Utf8);
      if (string.IsNullOrWhiteSpace(text))
      {
        throw new InvalidOperationException("falloff CSVが空です。");
      }

      var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
      var header = string.Empty;
      foreach (var raw in lines)
      {
        var line = raw.Trim();
        if (line.Length == 0) continue;
        if (line.StartsWith("#", StringComparison.Ordinal)) continue;
        header = line;
        break;
      }

      if (header.Length == 0)
      {
        throw new InvalidOperationException("falloff CSVのヘッダが見つかりません。");
      }

      if (header.StartsWith("dx_px", StringComparison.OrdinalIgnoreCase))
      {
        return await LoadFromKernelSweepAsync(text);
      }

      if (header.IndexOf("normalized_falloff01", StringComparison.OrdinalIgnoreCase) >= 0
        || header.IndexOf("kernel01", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        return LoadFromRobustKernelCsv(text);
      }

      if (header.IndexOf("r_norm", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        return LoadFromNormalizedFalloffCsv(text, scale);
      }

      throw new InvalidOperationException("対応していないfalloff CSV形式です。");
    }

    internal double SampleByRadiusPx(double radiusPx)
    {
      if (double.IsNaN(radiusPx) || double.IsInfinity(radiusPx)) return 0.0;
      if (radiusPx <= _radiusPx[0]) return _falloff[0];

      var last = _radiusPx.Length - 1;
      if (radiusPx >= _radiusPx[last]) return _falloff[last];

      var idx = Array.BinarySearch(_radiusPx, radiusPx);
      if (idx >= 0)
      {
        return _falloff[idx];
      }

      var ins = ~idx;
      if (ins <= 0) return _falloff[0];
      if (ins >= _radiusPx.Length) return _falloff[last];

      var x0 = _radiusPx[ins - 1];
      var x1 = _radiusPx[ins];
      var y0 = _falloff[ins - 1];
      var y1 = _falloff[ins];
      if (x1 <= x0) return y0;

      var t = (radiusPx - x0) / (x1 - x0);
      return ((1.0 - t) * y0) + (t * y1);
    }

    internal IReadOnlyList<double> GetRadiusPx() => _radiusPx;

    internal IReadOnlyList<double> GetFalloff() => _falloff;

    private static async Task<RadialFalloffProfile> LoadFromKernelSweepAsync(string text)
    {
      var alphaByDx = new Dictionary<int, double>();
      var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
      foreach (var raw in lines)
      {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
        if (line.StartsWith("dx_px", StringComparison.OrdinalIgnoreCase)) continue;

        var parts = line.Split(',');
        if (parts.Length < 6) continue;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dxPx)) continue;
        if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var a01)) continue;
        alphaByDx[Math.Abs(dxPx)] = Math.Clamp(a01, 0.0, 1.0);
      }

      if (alphaByDx.Count == 0)
      {
        throw new InvalidOperationException("kernel-sweep CSVに有効なデータがありません。");
      }

      if (!alphaByDx.TryGetValue(0, out var a0) || a0 <= 0)
      {
        throw new InvalidOperationException("kernel-sweep CSVの dx=0 が無効です。");
      }

      var radius = alphaByDx.Keys.OrderBy(v => v).Select(v => (double)v).ToArray();
      var falloff = new double[radius.Length];
      for (var i = 0; i < radius.Length; i++)
      {
        var dx = (int)radius[i];
        var a = alphaByDx.TryGetValue(dx, out var value) ? value : 0.0;
        falloff[i] = Math.Clamp(a / a0, 0.0, 1.0);
      }

      ForceMonotonicNonIncreasing(falloff);
      return new RadialFalloffProfile(radius, falloff);
    }

    private static RadialFalloffProfile LoadFromNormalizedFalloffCsv(string text, int scale)
    {
      var radius = new List<double>();
      var falloff = new List<double>();
      var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
      double center = 0.0;

      foreach (var raw in lines)
      {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
        if (line.StartsWith("r_norm", StringComparison.OrdinalIgnoreCase)) continue;

        var parts = line.Split(',');
        if (parts.Length < 2) continue;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var rNorm)) continue;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var a01)) continue;

        if (radius.Count == 0)
        {
          center = Math.Max(1e-6, a01);
        }

        radius.Add(rNorm * scale);
        falloff.Add(Math.Clamp(a01 / center, 0.0, 1.0));
      }

      if (radius.Count == 0)
      {
        throw new InvalidOperationException("normalized-falloff CSVに有効なデータがありません。");
      }

      var radiusArr = radius.ToArray();
      var falloffArr = falloff.ToArray();
      ForceMonotonicNonIncreasing(falloffArr);
      return new RadialFalloffProfile(radiusArr, falloffArr);
    }

    private static RadialFalloffProfile LoadFromRobustKernelCsv(string text)
    {
      var radius = new List<double>();
      var falloff = new List<double>();
      var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
      var headerIndex = -1;
      string[] header = null;
      foreach (var raw in lines)
      {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
        header = line.Split(',');
        headerIndex = 0;
        break;
      }

      if (headerIndex < 0 || header == null)
      {
        throw new InvalidOperationException("頑健kernel CSVのヘッダが見つかりません。");
      }

      var rPxIndex = FindColumnIndex(header, "r_px");
      var falloffIndex = FindColumnIndex(header, "normalized_falloff01");
      if (falloffIndex < 0)
      {
        falloffIndex = FindColumnIndex(header, "kernel01");
      }
      if (rPxIndex < 0 || falloffIndex < 0)
      {
        throw new InvalidOperationException("頑健kernel CSVの列が不足しています。");
      }

      var headerPassed = false;
      foreach (var raw in lines)
      {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
        if (!headerPassed)
        {
          headerPassed = true;
          continue;
        }

        var parts = line.Split(',');
        if (parts.Length <= Math.Max(rPxIndex, falloffIndex)) continue;
        if (!double.TryParse(parts[rPxIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var rPx)) continue;
        if (!double.TryParse(parts[falloffIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var f01)) continue;

        radius.Add(rPx);
        falloff.Add(Math.Clamp(f01, 0.0, 1.0));
      }

      if (radius.Count == 0)
      {
        throw new InvalidOperationException("頑健kernel CSVに有効なデータがありません。");
      }

      var radiusArr = radius.ToArray();
      var falloffArr = falloff.ToArray();
      ForceMonotonicNonIncreasing(falloffArr);
      return new RadialFalloffProfile(radiusArr, falloffArr);
    }

    private static int FindColumnIndex(string[] header, string name)
    {
      for (var i = 0; i < header.Length; i++)
      {
        if (string.Equals((header[i] ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase))
        {
          return i;
        }
      }

      return -1;
    }

    private static void ForceMonotonicNonIncreasing(double[] values)
    {
      if (values == null || values.Length == 0) return;
      values[0] = Math.Clamp(values[0], 0.0, 1.0);
      for (var i = 1; i < values.Length; i++)
      {
        values[i] = Math.Clamp(values[i], 0.0, 1.0);
        if (values[i] > values[i - 1])
        {
          values[i] = values[i - 1];
        }
      }
    }
  }
}
