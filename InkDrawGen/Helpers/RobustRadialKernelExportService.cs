using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;

namespace InkDrawGen.Helpers
{
  internal static class RobustRadialKernelExportService
  {
    private enum RobustStatisticKind
    {
      Median,
      P90,
    }

    private sealed class AlphaImage
    {
      internal AlphaImage(string name, int width, int height, byte[] bgra)
      {
        Name = name;
        Width = width;
        Height = height;
        Bgra = bgra;
      }

      internal string Name { get; }
      internal int Width { get; }
      internal int Height { get; }
      internal byte[] Bgra { get; }
    }

    internal static async Task ExportRobustKernelCsvAsync(MainPage page)
    {
      if (page == null) throw new ArgumentNullException(nameof(page));

      var state = InkDrawGenUiReader.Read(page);
      var scale = Math.Max(1, state.Scale);
      var binSizePx = Math.Max(1, ReadIntFromTextBox(page, "RobustKernelBinSizePxTextBox", 1));
      var excludeZeroAlpha = ReadBoolFromCheckBox(page, "RobustKernelExcludeZeroCheckBox", true);
      var statistic = ReadStatistic(page);

      var pngFiles = await PickPngFilesAsync();
      if (pngFiles == null || pngFiles.Count == 0)
      {
        return;
      }

      var outFolder = await PickOutputFolderBestEffortAsync(state.OutputFolder);
      if (outFolder is null)
      {
        return;
      }

      var images = new List<AlphaImage>(pngFiles.Count);
      foreach (var file in pngFiles)
      {
        images.Add(await LoadAlphaImageAsync(file));
      }

      EnsureSameImageSize(images);

      var gainSummary = new List<(string Name, double Gain01, int CenterSampleCount)>(images.Count);
      var kernel = EstimateKernel(images, scale, binSizePx, statistic, excludeZeroAlpha, paperTile: null, tileWidth: 0, tileHeight: 0, gainsOut: gainSummary);
      var csvText = BuildKernelCsv(kernel, scale, binSizePx, statistic, excludeZeroAlpha, pngFiles.Select(f => f.Name).ToArray(), gainSummary);

      var firstBaseName = RemoveExtensionSafe(pngFiles[0].Name);
      var statTag = statistic == RobustStatisticKind.P90 ? "p90" : "median";
      var zeroTag = excludeZeroAlpha ? "exclude0" : "include0";
      var outName = $"robust-kernel-{firstBaseName}-count{pngFiles.Count}-scale{scale}-bin{binSizePx}-{statTag}-{zeroTag}.csv";
      var outFile = await outFolder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
      await FileIO.WriteTextAsync(outFile, csvText, Windows.Storage.Streams.UnicodeEncoding.Utf8);

      AppendLog(page, $"頑健kernel CSV: {outFile.Path}\n");
      await new ContentDialog
      {
        Title = "頑健kernel CSV(PNG→CSV)",
        Content = $"完了: CSVを書き出しました。\n\nfile={outFile.Path}\nimages={images.Count} size={images[0].Width}x{images[0].Height}\nscale={scale} bin={binSizePx} statistic={statTag} zero={zeroTag}",
        CloseButtonText = "OK"
      }.ShowAsync();
    }

    internal static KernelSeries EstimateKernel(IReadOnlyList<(string Name, int Width, int Height, byte[] Bgra)> images, int scale, int binSizePx, bool useP90, bool excludeZeroAlpha)
    {
      var wrapped = images.Select(x => new AlphaImage(x.Name, x.Width, x.Height, x.Bgra)).ToArray();
      EnsureSameImageSize(wrapped);
      var kernel = EstimateKernel(wrapped, scale, binSizePx, useP90 ? RobustStatisticKind.P90 : RobustStatisticKind.Median, excludeZeroAlpha, paperTile: null, tileWidth: 0, tileHeight: 0, gainsOut: null);
      return kernel;
    }

    internal static KernelSeries ReEstimateKernel(IReadOnlyList<(string Name, int Width, int Height, byte[] Bgra)> images, int scale, int binSizePx, bool useP90, bool excludeZeroAlpha, double[] paperTile, int tileWidth, int tileHeight)
    {
      var wrapped = images.Select(x => new AlphaImage(x.Name, x.Width, x.Height, x.Bgra)).ToArray();
      EnsureSameImageSize(wrapped);
      var kernel = EstimateKernel(wrapped, scale, binSizePx, useP90 ? RobustStatisticKind.P90 : RobustStatisticKind.Median, excludeZeroAlpha, paperTile, tileWidth, tileHeight, gainsOut: null);
      return kernel;
    }

    internal static string BuildKernelCsv(KernelSeries kernel, int scale, int binSizePx, bool useP90, bool excludeZeroAlpha, IReadOnlyList<string> sourceFiles)
    {
      return BuildKernelCsv(kernel, scale, binSizePx, useP90 ? RobustStatisticKind.P90 : RobustStatisticKind.Median, excludeZeroAlpha, sourceFiles, null);
    }

    internal readonly struct KernelSeries
    {
      internal KernelSeries(double[] radiusPx, double[] rawValue01, double[] normalizedFalloff01, int[] countTotal, int[] countUsed)
      {
        RadiusPx = radiusPx;
        RawValue01 = rawValue01;
        NormalizedFalloff01 = normalizedFalloff01;
        CountTotal = countTotal;
        CountUsed = countUsed;
      }

      internal double[] RadiusPx { get; }
      internal double[] RawValue01 { get; }
      internal double[] NormalizedFalloff01 { get; }
      internal int[] CountTotal { get; }
      internal int[] CountUsed { get; }
    }

    private static KernelSeries EstimateKernel(IReadOnlyList<AlphaImage> images, int scale, int binSizePx, RobustStatisticKind statistic, bool excludeZeroAlpha, double[] paperTile, int tileWidth, int tileHeight, List<(string Name, double Gain01, int CenterSampleCount)> gainsOut)
    {
      if (images == null || images.Count == 0)
      {
        throw new InvalidOperationException("画像がありません。");
      }

      var width = images[0].Width;
      var height = images[0].Height;
      var cx = (width - 1) * 0.5;
      var cy = (height - 1) * 0.5;
      var maxRadiusPx = Math.Sqrt((cx * cx) + (cy * cy));
      var binCount = Math.Max(1, (int)Math.Ceiling(maxRadiusPx / binSizePx) + 1);
      var valuesByBin = new List<double>[binCount];
      var countTotal = new int[binCount];
      var countUsed = new int[binCount];

      foreach (var image in images)
      {
        var gainInfo = EstimateImageGain(image, scale, paperTile, tileWidth, tileHeight);
        gainsOut?.Add((image.Name, gainInfo.Gain01, gainInfo.SampleCount));
        var gain = Math.Max(1e-6, gainInfo.Gain01);

        for (var y = 0; y < image.Height; y++)
        {
          var dy = y - cy;
          for (var x = 0; x < image.Width; x++)
          {
            var dx = x - cx;
            var distPx = Math.Sqrt((dx * dx) + (dy * dy));
            var bin = Math.Min(binCount - 1, (int)Math.Floor(distPx / binSizePx));
            countTotal[bin]++;

            var alpha01 = image.Bgra[((y * image.Width) + x) * 4 + 3] / 255.0;
            if (excludeZeroAlpha && alpha01 <= 0)
            {
              continue;
            }

            var corrected = alpha01 / gain;
            if (paperTile != null && tileWidth > 0 && tileHeight > 0)
            {
              var paper = SampleTileNearest01(paperTile, tileWidth, tileHeight, x, y);
              if (paper <= 1e-6)
              {
                continue;
              }

              corrected /= paper;
            }

            if (double.IsNaN(corrected) || double.IsInfinity(corrected))
            {
              continue;
            }

            corrected = Math.Clamp(corrected, 0.0, 1.0);
            var list = valuesByBin[bin];
            if (list == null)
            {
              list = new List<double>();
              valuesByBin[bin] = list;
            }
            list.Add(corrected);
            countUsed[bin]++;
          }
        }
      }

      var radiusPx = new double[binCount];
      var raw = new double[binCount];
      for (var i = 0; i < binCount; i++)
      {
        radiusPx[i] = i * (double)binSizePx;
        var list = valuesByBin[i];
        raw[i] = list == null || list.Count == 0 ? -1.0 : ComputeStatistic(list, statistic);
      }

      FillMissingByCarry(raw);
      var center = raw[0] > 0 ? raw[0] : FindFirstPositive(raw);
      if (center <= 0)
      {
        center = 1.0;
      }

      var normalized = new double[raw.Length];
      for (var i = 0; i < raw.Length; i++)
      {
        var value = raw[i] > 0 ? raw[i] : 0.0;
        normalized[i] = Math.Clamp(value / center, 0.0, 1.0);
      }

      EnforceMonotonicNonIncreasing(normalized);
      return new KernelSeries(radiusPx, raw, normalized, countTotal, countUsed);
    }

    private static (double Gain01, int SampleCount) EstimateImageGain(AlphaImage image, int scale, double[] paperTile, int tileWidth, int tileHeight)
    {
      var cx = (image.Width - 1) * 0.5;
      var cy = (image.Height - 1) * 0.5;
      var refRadiusPx = Math.Max(2.0, scale * 0.5);
      var samples = new List<double>(4096);

      for (var y = 0; y < image.Height; y++)
      {
        var dy = y - cy;
        for (var x = 0; x < image.Width; x++)
        {
          var dx = x - cx;
          var distPx = Math.Sqrt((dx * dx) + (dy * dy));
          if (distPx > refRadiusPx)
          {
            continue;
          }

          var alpha01 = image.Bgra[((y * image.Width) + x) * 4 + 3] / 255.0;
          if (alpha01 <= 0) continue;

          if (paperTile != null && tileWidth > 0 && tileHeight > 0)
          {
            var paper = SampleTileNearest01(paperTile, tileWidth, tileHeight, x, y);
            if (paper <= 1e-6) continue;
            alpha01 /= paper;
          }

          samples.Add(Math.Clamp(alpha01, 0.0, 1.0));
        }
      }

      if (samples.Count == 0)
      {
        return (1.0, 0);
      }

      return (ComputePercentile(samples, 0.90), samples.Count);
    }

    private static double SampleTileNearest01(double[] tile, int width, int height, int x, int y)
    {
      var tx = x % width;
      var ty = y % height;
      if (tx < 0) tx += width;
      if (ty < 0) ty += height;
      return tile[(ty * width) + tx];
    }

    private static double ComputeStatistic(List<double> values, RobustStatisticKind statistic)
    {
      return statistic == RobustStatisticKind.P90 ? ComputePercentile(values, 0.90) : ComputeMedian(values);
    }

    private static double ComputeMedian(List<double> values)
    {
      if (values == null || values.Count == 0) return 0.0;
      values.Sort();
      var mid = values.Count / 2;
      if ((values.Count & 1) == 1) return values[mid];
      return 0.5 * (values[mid - 1] + values[mid]);
    }

    private static double ComputePercentile(List<double> values, double p)
    {
      if (values == null || values.Count == 0) return 0.0;
      values.Sort();
      var idx = (int)Math.Floor(p * (values.Count - 1));
      if (idx < 0) idx = 0;
      if (idx >= values.Count) idx = values.Count - 1;
      return values[idx];
    }

    private static void FillMissingByCarry(double[] values)
    {
      if (values.Length == 0) return;
      var first = FindFirstPositive(values);
      if (first <= 0)
      {
        for (var i = 0; i < values.Length; i++) values[i] = 0;
        return;
      }

      if (values[0] <= 0) values[0] = first;
      for (var i = 1; i < values.Length; i++)
      {
        if (values[i] <= 0)
        {
          values[i] = values[i - 1];
        }
      }
    }

    private static double FindFirstPositive(double[] values)
    {
      for (var i = 0; i < values.Length; i++)
      {
        if (values[i] > 0) return values[i];
      }
      return 0.0;
    }

    private static void EnforceMonotonicNonIncreasing(double[] values)
    {
      if (values.Length == 0) return;
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

    private static string BuildKernelCsv(KernelSeries kernel, int scale, int binSizePx, RobustStatisticKind statistic, bool excludeZeroAlpha, IReadOnlyList<string> sourceFiles, List<(string Name, double Gain01, int CenterSampleCount)> gains)
    {
      var sb = new StringBuilder(capacity: Math.Max(4096, kernel.RadiusPx.Length * 96));
      sb.Append("# robust-radial-kernel scale=").Append(scale.ToString(CultureInfo.InvariantCulture))
          .Append(" bin_px=").Append(binSizePx.ToString(CultureInfo.InvariantCulture))
          .Append(" statistic=").Append(statistic == RobustStatisticKind.P90 ? "p90" : "median")
          .Append(" zero_policy=").Append(excludeZeroAlpha ? "exclude0" : "include0")
          .AppendLine();
      sb.Append("# sources=").AppendLine(string.Join(";", sourceFiles));
      if (gains != null)
      {
        foreach (var gain in gains)
        {
          sb.Append("# gain ").Append(gain.Name)
              .Append("=").Append(gain.Gain01.ToString("0.########", CultureInfo.InvariantCulture))
              .Append(" center_samples=").Append(gain.CenterSampleCount.ToString(CultureInfo.InvariantCulture))
              .AppendLine();
        }
      }
      sb.AppendLine("r_px,r_norm,raw_value01,normalized_falloff01,count_total,count_used");
      for (var i = 0; i < kernel.RadiusPx.Length; i++)
      {
        sb.Append(kernel.RadiusPx[i].ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
        sb.Append((kernel.RadiusPx[i] / scale).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(kernel.RawValue01[i].ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(kernel.NormalizedFalloff01[i].ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(kernel.CountTotal[i].ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(kernel.CountUsed[i].ToString(CultureInfo.InvariantCulture)).AppendLine();
      }
      return sb.ToString();
    }

    private static async Task<List<StorageFile>> PickPngFilesAsync()
    {
      var picker = new FileOpenPicker
      {
        SuggestedStartLocation = PickerLocationId.PicturesLibrary
      };
      picker.FileTypeFilter.Add(".png");
      var files = await picker.PickMultipleFilesAsync();
      if (files == null || files.Count == 0) return null;
      return files.ToList();
    }

    private static async Task<StorageFolder> PickOutputFolderBestEffortAsync(string outputFolderPath)
    {
      if (!string.IsNullOrWhiteSpace(outputFolderPath))
      {
        try
        {
          return await StorageFolder.GetFolderFromPathAsync(outputFolderPath);
        }
        catch
        {
          // 既定パスが無効な場合はピッカーへフォールバックする。
        }
      }

      var picker = new FolderPicker
      {
        SuggestedStartLocation = PickerLocationId.PicturesLibrary,
      };
      picker.FileTypeFilter.Add(".csv");
      return await picker.PickSingleFolderAsync();
    }

    private static async Task<AlphaImage> LoadAlphaImageAsync(StorageFile file)
    {
      using (var stream = await file.OpenReadAsync())
      {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var pixelData = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        var bytes = pixelData.DetachPixelData();
        return new AlphaImage(file.Name, (int)decoder.PixelWidth, (int)decoder.PixelHeight, bytes);
      }
    }

    private static void EnsureSameImageSize(IReadOnlyList<AlphaImage> images)
    {
      var width = images[0].Width;
      var height = images[0].Height;
      foreach (var image in images)
      {
        if (image.Width != width || image.Height != height)
        {
          throw new InvalidOperationException("PNGのサイズが揃っていません。複数条件の共有抽出では同一サイズ・同一位置の画像を選択してください。");
        }
      }
    }

    private static RobustStatisticKind ReadStatistic(MainPage page)
    {
      var combo = page.FindName("RobustKernelStatisticComboBox") as ComboBox;
      if (combo?.SelectedItem is ComboBoxItem item)
      {
        var text = (item.Content ?? string.Empty).ToString();
        if (string.Equals(text, "Median", StringComparison.OrdinalIgnoreCase))
        {
          return RobustStatisticKind.Median;
        }
      }

      return RobustStatisticKind.P90;
    }

    private static bool ReadBoolFromCheckBox(MainPage page, string name, bool fallback)
    {
      var cb = page.FindName(name) as CheckBox;
      return cb?.IsChecked ?? fallback;
    }

    private static int ReadIntFromTextBox(MainPage page, string name, int fallback)
    {
      var tb = page.FindName(name) as TextBox;
      var s = (tb?.Text ?? string.Empty).Trim();
      if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
      if (int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return v;
      return fallback;
    }

    private static string RemoveExtensionSafe(string name)
    {
      return Path.GetFileNameWithoutExtension(name ?? string.Empty);
    }

    private static void AppendLog(MainPage page, string s)
    {
      var tb = page.FindName("LogTextBox") as TextBox;
      if (tb == null) return;
      tb.Text = (tb.Text ?? string.Empty) + s;
    }
  }
}
