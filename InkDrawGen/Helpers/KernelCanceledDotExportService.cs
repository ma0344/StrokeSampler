using System;
using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace InkDrawGen.Helpers
{
  internal static class KernelCanceledDotExportService
  {
    internal static async Task ExportKernelCanceledDotPngAsync(MainPage page)
    {
      if (page == null) throw new ArgumentNullException(nameof(page));

      var state = InkDrawGenUiReader.Read(page);
      var scale = Math.Max(1, state.Scale);

      // kernel-sweep CSVを選択
      var csvFile = await PickKernelSweepCsvAsync();
      if (csvFile is null)
      {
        return;
      }

      // 出力先フォルダ
      var outFolder = await PickOutputFolderBestEffortAsync(page, state.OutputFolder);
      if (outFolder is null)
      {
        return;
      }

      // S/P/Op/N はUIの start を使用（最短）
      var sDip = state.S.Start;
      var pressure = (float)Math.Clamp(state.P.Start, 0.0, 1.0);
      var opacity = (float)Math.Clamp(state.Opacity.Start, 0.01, 5.0);
      var repeat = Math.Max(1, state.N.Start);

      var dpi = (float)Math.Max(1.0, state.Dpi);

      // 出力キャンバス（px）: S*scale を正方形として扱う
      var canvasPx = Math.Max(1, (int)Math.Round(sDip * scale, MidpointRounding.AwayFromZero));

      // stroke中心（DIP）
      var centerDip = new Point(sDip * 0.5, sDip * 0.5);
      var stroke = InkStrokeBuildService.BuildSDotStroke(centerDip, sDip, pressure, opacity);

      // dot画像（透明背景）をレンダ
      var bmp = await InkOffscreenRenderService.RenderStrokeAsync(stroke, canvasPx, canvasPx, transparent: true, dpi: dpi, exportScale: scale, repeat: repeat);

      // カーネルf(r)（中心で1に正規化）を構築する。
      // kernel-sweep / normalized-falloff / 頑健kernel CSV を共通で受け付ける。
      var falloff = await RadialFalloffProfile.LoadAsync(csvFile, scale);

      // 相殺（alpha / f）して紙目ベース単点を作る
      var canceled = CancelFalloff(bmp, falloff);

      var fileName = BuildOutFileName(csvFile.DisplayName, sDip, pressure, opacity, repeat, scale, canvasPx);
      var outFile = await outFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
      await PngExportService.SaveAsync(canceled, outFile);

      var sText = sDip.ToString("0.####", CultureInfo.InvariantCulture);
      var pText = pressure.ToString("0.####", CultureInfo.InvariantCulture);
      var opText = opacity.ToString("0.####", CultureInfo.InvariantCulture);

      await new ContentDialog
      {
        Title = "紙目ベース単点PNG",
        Content = $"完了: PNGを書き出しました。\n\nfile={outFile.Path}\ncanvas={canvasPx} scale={scale} S={sText} P={pText} Op={opText} N={repeat}\nkernelCsv={csvFile.Name}",
        CloseButtonText = "OK"
      }.ShowAsync();
    }

    internal static async Task ExportKernelCanceledDotOffsetPngAsync(MainPage page)
    {
      if (page == null) throw new ArgumentNullException(nameof(page));

      var state = InkDrawGenUiReader.Read(page);
      var scale = Math.Max(1, state.Scale);

      var csvFile = await PickKernelSweepCsvAsync();
      if (csvFile is null)
      {
        return;
      }

      var outFolder = await PickOutputFolderBestEffortAsync(page, state.OutputFolder);
      if (outFolder is null)
      {
        return;
      }

      var sDip = state.S.Start;
      var pressure = (float)Math.Clamp(state.P.Start, 0.0, 1.0);
      var opacity = (float)Math.Clamp(state.Opacity.Start, 0.01, 5.0);
      var repeat = Math.Max(1, state.N.Start);
      var dpi = (float)Math.Max(1.0, state.Dpi);

      var widthPx = Math.Max(1, state.OutWidthPx);
      var heightPx = Math.Max(1, state.OutHeightPx);

      var centerDip = state.Start;
      var stroke = InkStrokeBuildService.BuildSDotStroke(centerDip, sDip, pressure, opacity);

      var bmp = await InkOffscreenRenderService.RenderStrokeAsync(stroke, widthPx, heightPx, transparent: true, dpi: dpi, exportScale: scale, repeat: repeat);

      var falloff = await RadialFalloffProfile.LoadAsync(csvFile, scale);
      var canceled = CancelFalloffOffset(bmp, falloff, centerDip, scale);

      var fileName = BuildOffsetOutFileName(csvFile.DisplayName, sDip, pressure, opacity, repeat, scale, widthPx, heightPx, centerDip);
      var outFile = await outFolder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
      await PngExportService.SaveAsync(canceled, outFile);

      var sText = sDip.ToString("0.####", CultureInfo.InvariantCulture);
      var pText = pressure.ToString("0.####", CultureInfo.InvariantCulture);
      var opText = opacity.ToString("0.####", CultureInfo.InvariantCulture);

      await new ContentDialog
      {
        Title = "紙目ベース単点PNG(Offset)",
        Content = $"完了: PNGを書き出しました。\n\nfile={outFile.Path}\nsize={widthPx}x{heightPx} scale={scale} S={sText} P={pText} Op={opText} N={repeat}\ncenter=({centerDip.X.ToString("0.####", CultureInfo.InvariantCulture)},{centerDip.Y.ToString("0.####", CultureInfo.InvariantCulture)})\nkernelCsv={csvFile.Name}",
        CloseButtonText = "OK"
      }.ShowAsync();
    }

    private static WriteableBitmap CancelFalloff(WriteableBitmap src, RadialFalloffProfile falloff)
    {
      var w = src.PixelWidth;
      var h = src.PixelHeight;

      var bytes = src.PixelBuffer.ToArray();
      var outBytes = new byte[bytes.Length];

      var cx = (w - 1) * 0.5;
      var cy = (h - 1) * 0.5;

      // 極端な外縁での分母ゼロ/量子化暴れを避ける
      const double eps = 1e-6;

      for (var y = 0; y < h; y++)
      {
        var dy = y - cy;
        for (var x = 0; x < w; x++)
        {
          var dx = x - cx;
          var distPx = Math.Sqrt((dx * dx) + (dy * dy));

          var i = ((y * w) + x) * 4;
          var a = bytes[i + 3] / 255.0;

          // dot外はそのまま透明
          if (a <= 0)
          {
            outBytes[i + 0] = 0;
            outBytes[i + 1] = 0;
            outBytes[i + 2] = 0;
            outBytes[i + 3] = 0;
            continue;
          }

          var den = Math.Max(eps, falloff.SampleByRadiusPx(distPx));
          var aCanceled01 = a / den;
          aCanceled01 = Math.Clamp(aCanceled01, 0.0, 1.0);

          var a8 = (byte)Math.Clamp((int)Math.Round(aCanceled01 * 255.0, MidpointRounding.AwayFromZero), 0, 255);

          // BGRA（黒インク、αだけ意味がある）
          outBytes[i + 0] = 0;
          outBytes[i + 1] = 0;
          outBytes[i + 2] = 0;
          outBytes[i + 3] = a8;
        }
      }

      var dst = new WriteableBitmap(w, h);
      using (var s = dst.PixelBuffer.AsStream())
      {
        s.Write(outBytes, 0, outBytes.Length);
      }
      dst.Invalidate();
      return dst;
    }

    private static WriteableBitmap CancelFalloffOffset(WriteableBitmap src, RadialFalloffProfile falloff, Point centerDip, int scale)
    {
      var w = src.PixelWidth;
      var h = src.PixelHeight;

      var bytes = src.PixelBuffer.ToArray();
      var outBytes = new byte[bytes.Length];

      var cx = centerDip.X * scale;
      var cy = centerDip.Y * scale;

      const double eps = 1e-6;

      for (var y = 0; y < h; y++)
      {
        var dy = y - cy;
        for (var x = 0; x < w; x++)
        {
          var dx = x - cx;
          var distPx = Math.Sqrt((dx * dx) + (dy * dy));

          var i = ((y * w) + x) * 4;
          var a = bytes[i + 3] / 255.0;
          if (a <= 0)
          {
            outBytes[i + 0] = 0;
            outBytes[i + 1] = 0;
            outBytes[i + 2] = 0;
            outBytes[i + 3] = 0;
            continue;
          }

          var den = Math.Max(eps, falloff.SampleByRadiusPx(distPx));
          var aCanceled01 = Math.Clamp(a / den, 0.0, 1.0);
          var a8 = (byte)Math.Clamp((int)Math.Round(aCanceled01 * 255.0, MidpointRounding.AwayFromZero), 0, 255);

          outBytes[i + 0] = 0;
          outBytes[i + 1] = 0;
          outBytes[i + 2] = 0;
          outBytes[i + 3] = a8;
        }
      }

      var dst = new WriteableBitmap(w, h);
      using (var s = dst.PixelBuffer.AsStream())
      {
        s.Write(outBytes, 0, outBytes.Length);
      }
      dst.Invalidate();
      return dst;
    }

    private static async Task<StorageFile> PickKernelSweepCsvAsync()
    {
      var picker = new FileOpenPicker
      {
        SuggestedStartLocation = PickerLocationId.PicturesLibrary
      };
      picker.FileTypeFilter.Add(".csv");

      return await picker.PickSingleFileAsync();
    }

    private static async Task<StorageFolder> PickOutputFolderBestEffortAsync(MainPage page, string outputFolderPath)
    {
      if (!string.IsNullOrWhiteSpace(outputFolderPath))
      {
        try
        {
          return await StorageFolder.GetFolderFromPathAsync(outputFolderPath);
        }
        catch
        {
          // フォルダパスが無効な場合はピッカーにフォールバック
        }
      }

      var picker = new FolderPicker
      {
        SuggestedStartLocation = PickerLocationId.PicturesLibrary,
      };
      picker.FileTypeFilter.Add(".png");

      return await picker.PickSingleFolderAsync();
    }

    private static string BuildOutFileName(string kernelCsvDisplayName, double sDip, float pressure, float opacity, int repeat, int scale, int canvasPx)
    {
      var s = ((int)Math.Round(sDip, MidpointRounding.AwayFromZero)).ToString("D4", CultureInfo.InvariantCulture);
      var p = ((int)Math.Round(pressure * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
      var op = ((int)Math.Round(opacity * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

      var kernelTag = kernelCsvDisplayName;
      if (kernelTag.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
      {
        kernelTag = kernelTag.Substring(0, kernelTag.Length - 4);
      }
      return $"paperbase-dot-kernelcancel-{kernelTag}-S{s}-P{p}-Op{op}-N{repeat}-scale{scale}-canvas{canvasPx}.png";
    }

    private static string BuildOffsetOutFileName(string kernelCsvDisplayName, double sDip, float pressure, float opacity, int repeat, int scale, int widthPx, int heightPx, Point centerDip)
    {
      var s = ((int)Math.Round(sDip, MidpointRounding.AwayFromZero)).ToString("D4", CultureInfo.InvariantCulture);
      var p = ((int)Math.Round(pressure * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
      var op = ((int)Math.Round(opacity * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);

      var kernelTag = kernelCsvDisplayName;
      if (kernelTag.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
      {
        kernelTag = kernelTag.Substring(0, kernelTag.Length - 4);
      }

      var startX = Math.Round(centerDip.X, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture).Replace('.', '_');
      var startY = Math.Round(centerDip.Y, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture).Replace('.', '_');
      return $"paperbase-dot-kernelcancel-offset-{kernelTag}-S{s}-P{p}-Op{op}-N{repeat}-scale{scale}-start{startX}_{startY}-size{widthPx}x{heightPx}.png";
    }
  }
}
