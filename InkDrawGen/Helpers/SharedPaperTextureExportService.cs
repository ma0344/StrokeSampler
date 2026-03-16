using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace InkDrawGen.Helpers
{
  internal static class SharedPaperTextureExportService
  {
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

    private sealed class GainRow
    {
      internal string Name = string.Empty;
      internal double Gain01;
      internal int GainSamples;
    }

    internal static async Task ExportSharedPaperTextureAsync(MainPage page)
    {
      if (page == null) throw new ArgumentNullException(nameof(page));

      var state = InkDrawGenUiReader.Read(page);
      var scale = Math.Max(1, state.Scale);
      var binSizePx = Math.Max(1, ReadIntFromTextBox(page, "RobustKernelBinSizePxTextBox", 1));
      var excludeZeroAlpha = ReadBoolFromCheckBox(page, "RobustKernelExcludeZeroCheckBox", true);
      var useP90 = !IsMedianSelected(page);
      var minKernel = ReadDoubleFromTextBox(page, "SharedPaperMinKernelTextBox", 0.15);
      minKernel = Math.Clamp(minKernel, 0.001, 1.0);
      var refineIterations = Math.Max(0, ReadIntFromTextBox(page, "SharedPaperRefineIterationsTextBox", 0));

      var roiPx = GetTileRoiPx(state.Roi, scale);
      if (roiPx.Width <= 0 || roiPx.Height <= 0)
      {
        await new ContentDialog
        {
          Title = "共有紙目PNG(複数PNG)",
          Content = "ROIの幅と高さを1以上にしてください。",
          CloseButtonText = "OK"
        }.ShowAsync();
        return;
      }

      var kernelCsv = await PickKernelCsvAsync();
      if (kernelCsv is null)
      {
        return;
      }

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
            try
            {
              ValidateRoiFits(images[0].Width, images[0].Height, roiPx);

            }
            catch(InvalidOperationException ex)
            {
                 await new ContentDialog
              {
                Title = "共有紙目PNG(複数PNG)",
                Content = "ROIが画像範囲をはみ出しています。共有紙目抽出では現在のROIがそのままタイルサイズになります。ROIを調整するか、画像サイズに合ったROIを選択してください。",
                CloseButtonText = "OK"
              }.ShowAsync();
              return;
            }
      var currentKernelProfile = await RadialFalloffProfile.LoadAsync(kernelCsv, scale);
      var currentKernelSeries = BuildKernelSeriesFromProfile(currentKernelProfile, binSizePx, images[0].Width, images[0].Height);
      double[] paperTile = null;
      GainRow[] gains = null;

      paperTile = EstimateSharedPaperTile(images, roiPx, currentKernelProfile, minKernel, out gains);

      for (var iter = 0; iter < refineIterations; iter++)
      {
        currentKernelSeries = RobustRadialKernelExportService.ReEstimateKernel(
            images.Select(x => (x.Name, x.Width, x.Height, x.Bgra)).ToArray(),
            scale,
            binSizePx,
            useP90,
            excludeZeroAlpha,
            paperTile,
            roiPx.Width,
            roiPx.Height);

        var refinedKernelText = RobustRadialKernelExportService.BuildKernelCsv(
            currentKernelSeries,
            scale,
            binSizePx,
            useP90,
            excludeZeroAlpha,
            pngFiles.Select(f => f.Name).ToArray());

        var tempKernelFile = await outFolder.CreateFileAsync($"shared-paper-refined-kernel-iter{iter + 1}.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(tempKernelFile, refinedKernelText, Windows.Storage.Streams.UnicodeEncoding.Utf8);
        currentKernelProfile = await RadialFalloffProfile.LoadAsync(tempKernelFile, scale);
        paperTile = EstimateSharedPaperTile(images, roiPx, currentKernelProfile, minKernel, out gains);
      }

      var normalizedTile = NormalizeTileByP90(paperTile);
      var tileBitmap = BuildAlphaBitmap(normalizedTile, roiPx.Width, roiPx.Height);

      var baseName = RemoveExtensionSafe(pngFiles[0].Name);
      var pngOutName = $"shared-paper-tile-{baseName}-count{pngFiles.Count}-scale{scale}-roi{roiPx.X}_{roiPx.Y}_{roiPx.Width}_{roiPx.Height}.png";
      var pngOutFile = await outFolder.CreateFileAsync(pngOutName, CreationCollisionOption.ReplaceExisting);
      await PngExportService.SaveAsync(tileBitmap, pngOutFile);

      var summaryOutName = $"shared-paper-summary-{baseName}-count{pngFiles.Count}-scale{scale}-roi{roiPx.X}_{roiPx.Y}_{roiPx.Width}_{roiPx.Height}.csv";
      var summaryOutFile = await outFolder.CreateFileAsync(summaryOutName, CreationCollisionOption.ReplaceExisting);
      await FileIO.WriteTextAsync(summaryOutFile, BuildSummaryCsv(gains ?? Array.Empty<GainRow>(), normalizedTile), Windows.Storage.Streams.UnicodeEncoding.Utf8);

      StorageFile refinedKernelOutFile = null;
      if (refineIterations > 0)
      {
        var refinedKernelCsv = RobustRadialKernelExportService.BuildKernelCsv(
            currentKernelSeries,
            scale,
            binSizePx,
            useP90,
            excludeZeroAlpha,
            pngFiles.Select(f => f.Name).ToArray());
        var refinedName = $"shared-paper-refined-kernel-{baseName}-count{pngFiles.Count}-scale{scale}-iter{refineIterations}.csv";
        refinedKernelOutFile = await outFolder.CreateFileAsync(refinedName, CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(refinedKernelOutFile, refinedKernelCsv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
      }

      AppendLog(page, $"共有紙目PNG: {pngOutFile.Path}\n");
      await new ContentDialog
      {
        Title = "共有紙目PNG(複数PNG)",
        Content = $"完了: 共有紙目タイルを書き出しました。\n\npng={pngOutFile.Path}\nsummary={summaryOutFile.Path}\nrefineIterations={refineIterations}\n" +
                    (refinedKernelOutFile == null ? string.Empty : $"refinedKernel={refinedKernelOutFile.Path}\n"),
        CloseButtonText = "OK"
      }.ShowAsync();
    }

    private static RobustRadialKernelExportService.KernelSeries BuildKernelSeriesFromProfile(RadialFalloffProfile profile, int binSizePx, int width, int height)
    {
      var cx = (width - 1) * 0.5;
      var cy = (height - 1) * 0.5;
      var maxRadiusPx = Math.Sqrt((cx * cx) + (cy * cy));
      var binCount = Math.Max(1, (int)Math.Ceiling(maxRadiusPx / binSizePx) + 1);
      var radius = new double[binCount];
      var raw = new double[binCount];
      var normalized = new double[binCount];
      var countTotal = new int[binCount];
      var countUsed = new int[binCount];
      for (var i = 0; i < binCount; i++)
      {
        var r = i * (double)binSizePx;
        radius[i] = r;
        normalized[i] = profile.SampleByRadiusPx(r);
        raw[i] = normalized[i];
      }
      return new RobustRadialKernelExportService.KernelSeries(radius, raw, normalized, countTotal, countUsed);
    }

    private static double[] EstimateSharedPaperTile(IReadOnlyList<AlphaImage> images, RectInt roiPx, RadialFalloffProfile kernel, double minKernel, out GainRow[] gains)
    {
      var tileLength = roiPx.Width * roiPx.Height;
      var valuesByPixel = new List<double>[tileLength];
      var gainList = new List<GainRow>(images.Count);
      var cx = (images[0].Width - 1) * 0.5;
      var cy = (images[0].Height - 1) * 0.5;

      foreach (var image in images)
      {
        var gain = EstimateGainFromKernel(image, kernel, minKernel);
        gainList.Add(new GainRow { Name = image.Name, Gain01 = gain.Gain01, GainSamples = gain.SampleCount });
        var gain01 = Math.Max(1e-6, gain.Gain01);

        for (var y = 0; y < roiPx.Height; y++)
        {
          var gy = roiPx.Y + y;
          var dy = gy - cy;
          for (var x = 0; x < roiPx.Width; x++)
          {
            var gx = roiPx.X + x;
            var dx = gx - cx;
            var distPx = Math.Sqrt((dx * dx) + (dy * dy));
            var falloff = kernel.SampleByRadiusPx(distPx);
            if (falloff < minKernel) continue;

            var alpha01 = image.Bgra[((gy * image.Width) + gx) * 4 + 3] / 255.0;
            if (alpha01 <= 0) continue;

            var paper = alpha01 / (gain01 * Math.Max(1e-6, falloff));
            if (double.IsNaN(paper) || double.IsInfinity(paper)) continue;

            var idx = (y * roiPx.Width) + x;
            var list = valuesByPixel[idx];
            if (list == null)
            {
              list = new List<double>();
              valuesByPixel[idx] = list;
            }
            list.Add(Math.Clamp(paper, 0.0, 1.5));
          }
        }
      }

      gains = gainList.ToArray();
      var tile = new double[tileLength];
      for (var i = 0; i < tile.Length; i++)
      {
        var list = valuesByPixel[i];
        tile[i] = list == null || list.Count == 0 ? 0.0 : ComputeMedian(list);
      }
      return tile;
    }

    private static (double Gain01, int SampleCount) EstimateGainFromKernel(AlphaImage image, RadialFalloffProfile kernel, double minKernel)
    {
      var samples = new List<double>(16384);
      var cx = (image.Width - 1) * 0.5;
      var cy = (image.Height - 1) * 0.5;
      for (var y = 0; y < image.Height; y++)
      {
        var dy = y - cy;
        for (var x = 0; x < image.Width; x++)
        {
          var dx = x - cx;
          var distPx = Math.Sqrt((dx * dx) + (dy * dy));
          var falloff = kernel.SampleByRadiusPx(distPx);
          if (falloff < minKernel) continue;

          var alpha01 = image.Bgra[((y * image.Width) + x) * 4 + 3] / 255.0;
          if (alpha01 <= 0) continue;

          var value = alpha01 / Math.Max(1e-6, falloff);
          samples.Add(Math.Clamp(value, 0.0, 1.5));
        }
      }

      if (samples.Count == 0)
      {
        return (1.0, 0);
      }

      samples.Sort();
      var idx = (int)Math.Floor(0.90 * (samples.Count - 1));
      if (idx < 0) idx = 0;
      if (idx >= samples.Count) idx = samples.Count - 1;
      return (samples[idx], samples.Count);
    }

    private static double[] NormalizeTileByP90(double[] tile)
    {
      var valid = new List<double>(tile.Length);
      for (var i = 0; i < tile.Length; i++)
      {
        if (tile[i] > 0)
        {
          valid.Add(tile[i]);
        }
      }

      if (valid.Count == 0)
      {
        return tile.Select(_ => 0.0).ToArray();
      }

      valid.Sort();
      var idx = (int)Math.Floor(0.90 * (valid.Count - 1));
      if (idx < 0) idx = 0;
      if (idx >= valid.Count) idx = valid.Count - 1;
      var p90 = Math.Max(1e-6, valid[idx]);

      var normalized = new double[tile.Length];
      for (var i = 0; i < tile.Length; i++)
      {
        normalized[i] = Math.Clamp(tile[i] / p90, 0.0, 1.0);
      }
      return normalized;
    }

    private static WriteableBitmap BuildAlphaBitmap(double[] tile, int width, int height)
    {
      var bmp = new WriteableBitmap(width, height);
      var bytes = new byte[width * height * 4];
      for (var i = 0; i < tile.Length; i++)
      {
        var a = (byte)Math.Clamp((int)Math.Round(tile[i] * 255.0, MidpointRounding.AwayFromZero), 0, 255);
        var p = i * 4;
        bytes[p + 0] = 0;
        bytes[p + 1] = 0;
        bytes[p + 2] = 0;
        bytes[p + 3] = a;
      }

      using (var stream = bmp.PixelBuffer.AsStream())
      {
        stream.Write(bytes, 0, bytes.Length);
      }
      bmp.Invalidate();
      return bmp;
    }

    private static string BuildSummaryCsv(IReadOnlyList<GainRow> gains, double[] normalizedTile)
    {
      var sb = new StringBuilder(capacity: Math.Max(2048, gains.Count * 80));
      sb.AppendLine("section,name,gain01,gain_samples,value");
      foreach (var gain in gains)
      {
        sb.Append("gain,")
            .Append(EscapeCsv(gain.Name)).Append(',')
            .Append(gain.Gain01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',')
            .Append(gain.GainSamples.ToString(CultureInfo.InvariantCulture)).Append(',')
            .AppendLine();
      }

      var valid = normalizedTile.Where(v => v > 0).OrderBy(v => v).ToArray();
      var mean = valid.Length == 0 ? 0.0 : valid.Average();
      var median = valid.Length == 0 ? 0.0 : valid[valid.Length / 2];
      var p90 = valid.Length == 0 ? 0.0 : valid[(int)Math.Floor(0.90 * (valid.Length - 1))];
      sb.Append("tile_stats,,,mean,").Append(mean.ToString("0.########", CultureInfo.InvariantCulture)).AppendLine();
      sb.Append("tile_stats,,,median,").Append(median.ToString("0.########", CultureInfo.InvariantCulture)).AppendLine();
      sb.Append("tile_stats,,,p90,").Append(p90.ToString("0.########", CultureInfo.InvariantCulture)).AppendLine();
      return sb.ToString();
    }

    private static double ComputeMedian(List<double> values)
    {
      if (values == null || values.Count == 0) return 0.0;
      values.Sort();
      var mid = values.Count / 2;
      if ((values.Count & 1) == 1) return values[mid];
      return 0.5 * (values[mid - 1] + values[mid]);
    }

    private static RectInt GetTileRoiPx(Rect roiDip, int scale)
    {
      var x = Math.Max(0, (int)Math.Round(roiDip.X * scale, MidpointRounding.AwayFromZero));
      var y = Math.Max(0, (int)Math.Round(roiDip.Y * scale, MidpointRounding.AwayFromZero));
      var w = Math.Max(1, (int)Math.Round(roiDip.Width * scale, MidpointRounding.AwayFromZero));
      var h = Math.Max(1, (int)Math.Round(roiDip.Height * scale, MidpointRounding.AwayFromZero));
      return new RectInt(x, y, w, h);
    }

    private static void ValidateRoiFits(int imageWidth, int imageHeight, RectInt roi)
    {
      if (roi.X < 0 || roi.Y < 0 || roi.Right > imageWidth || roi.Bottom > imageHeight)
      {
        throw new InvalidOperationException("ROIが画像範囲をはみ出しています。共有紙目抽出では現在のROIがそのままタイルサイズになります。");
      }
    }

    private static async Task<StorageFile> PickKernelCsvAsync()
    {
      var picker = new FileOpenPicker
      {
        SuggestedStartLocation = PickerLocationId.PicturesLibrary
      };
      picker.FileTypeFilter.Add(".csv");
      return await picker.PickSingleFileAsync();
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
      picker.FileTypeFilter.Add(".png");
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
          throw new InvalidOperationException("PNGのサイズが揃っていません。共有抽出では同一サイズ・同一位置の画像を選択してください。");
        }
      }
    }

    private static bool IsMedianSelected(MainPage page)
    {
      var combo = page.FindName("RobustKernelStatisticComboBox") as ComboBox;
      if (combo?.SelectedItem is ComboBoxItem item)
      {
        return string.Equals((item.Content ?? string.Empty).ToString(), "Median", StringComparison.OrdinalIgnoreCase);
      }

      return false;
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

    private static double ReadDoubleFromTextBox(MainPage page, string name, double fallback)
    {
      var tb = page.FindName(name) as TextBox;
      var s = (tb?.Text ?? string.Empty).Trim();
      if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
      if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
      return fallback;
    }

    private static string RemoveExtensionSafe(string name)
    {
      return Path.GetFileNameWithoutExtension(name ?? string.Empty);
    }

    private static string EscapeCsv(string s)
    {
      if (s == null) return string.Empty;
      if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return s;
      return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private static void AppendLog(MainPage page, string s)
    {
      var tb = page.FindName("LogTextBox") as TextBox;
      if (tb == null) return;
      tb.Text = (tb.Text ?? string.Empty) + s;
    }

    private readonly struct RectInt
    {
      internal RectInt(int x, int y, int width, int height)
      {
        X = x;
        Y = y;
        Width = width;
        Height = height;
      }

      internal int X { get; }
      internal int Y { get; }
      internal int Width { get; }
      internal int Height { get; }
      internal int Right => X + Width;
      internal int Bottom => Y + Height;
    }
  }
}
