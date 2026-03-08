using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class ImageAlphaRadialKernelNoiseExporter
{
    internal static async Task ExportRadialKernelAndNoiseAsync(MainWindow window, int binSize)
    {
        ArgumentNullException.ThrowIfNull(window);

        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "Radial kernel/noise を出力するPNGを選択"
        };
        if (open.ShowDialog(window) != true) return;

        var path = open.FileName;
        if (string.IsNullOrWhiteSpace(path)) return;

        var folder = await PickOutputFolderAsync(window);
        if (folder is null) return;

        await ExportCoreAsync(folder, path, binSize);
    }

    private static async Task<StorageFolder?> PickOutputFolderAsync(MainWindow window)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".csv");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSingleFolderAsync();
    }

    private static async Task ExportCoreAsync(StorageFolder folder, string path, int binSize)
    {
        if (binSize <= 0) binSize = 1;

        using var bmp = SKBitmap.Decode(path);
        if (bmp is null) return;

        var thresholds = ImageAlphaRadialProfile.CreateDefaultThresholds();
        var analysis = ImageAlphaRadialProfile.Analyze(bmp, binSize, thresholds);

        var kernelByBin = BuildKernelByBin(analysis);

        var w = bmp.Width;
        var h = bmp.Height;
        var n = checked(w * h);

        var srcAlpha = new byte[n];
        var px = bmp.Pixels;
        for (var i = 0; i < n; i++)
        {
            srcAlpha[i] = px[i].Alpha;
        }

        var kernelImg = new byte[n];
        var noiseRatioVis = new byte[n];

        var cx = analysis.CenterX;
        var cy = analysis.CenterY;
        var bins = analysis.Bins;

        long valid = 0;
        double sumRatio = 0;
        double minRatio = double.PositiveInfinity;
        double maxRatio = double.NegativeInfinity;

        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            var dy = y - cy;
            for (var x = 0; x < w; x++)
            {
                var dx = x - cx;
                var r = Math.Sqrt((dx * dx) + (dy * dy));
                var bin = (int)Math.Floor(r / binSize);
                if ((uint)bin >= (uint)bins) bin = bins - 1;
                if (bin < 0) bin = 0;

                var kA = kernelByBin[bin];
                var a = srcAlpha[row + x];

                kernelImg[row + x] = kA;

                if (kA == 0)
                {
                    noiseRatioVis[row + x] = 0;
                    continue;
                }

                var ratio = a / (double)kA;
                if (!double.IsFinite(ratio))
                {
                    noiseRatioVis[row + x] = 0;
                    continue;
                }

                valid++;
                sumRatio += ratio;
                if (ratio < minRatio) minRatio = ratio;
                if (ratio > maxRatio) maxRatio = ratio;

                // 可視化: ratio=1 を 128 に割り当てる（0..2を0..255へ）
                var vis = (int)Math.Round(Math.Clamp(ratio, 0.0, 2.0) * 128.0, MidpointRounding.AwayFromZero);
                noiseRatioVis[row + x] = (byte)Math.Clamp(vis, 0, 255);
            }
        }

        var baseName = $"radial-kernel-noise-{Path.GetFileNameWithoutExtension(path)}";

        // 可視化用（グレースケール値をRGBへ格納）
        await SaveGray8PngAsync(folder, $"{baseName}-kernel.png", w, h, kernelImg);
        await SaveGray8PngAsync(folder, $"{baseName}-noiseRatio-vis2x.png", w, h, noiseRatioVis);

        // 解析用（値をAlphaへ格納）
        // DotLabのImageAlphaDiffはAlpha差分を見るため、こちらを比較に使う。
        await SaveAlpha8PngAsync(folder, $"{baseName}-kernel-alpha.png", w, h, kernelImg);
        await SaveAlpha8PngAsync(folder, $"{baseName}-noiseRatio-vis2x-alpha.png", w, h, noiseRatioVis);

        await WriteKernelProfileCsvAsync(folder, baseName, analysis, kernelByBin);
        await WriteNoiseSummaryCsvAsync(folder, baseName, path, analysis, valid, sumRatio, minRatio, maxRatio);
    }

    private static byte[] BuildKernelByBin(ImageAlphaRadialProfile.AnalysisResult a)
    {
        var kernel = new byte[a.Bins];
        for (var bin = 0; bin < a.Bins; bin++)
        {
            var total = a.Total[bin];
            if (total <= 0)
            {
                kernel[bin] = 0;
                continue;
            }

            var meanA = a.SumAlpha[bin] / (double)total;
            var v = (int)Math.Round(meanA, MidpointRounding.AwayFromZero);
            kernel[bin] = (byte)Math.Clamp(v, 0, 255);
        }
        return kernel;
    }

    private static async Task WriteKernelProfileCsvAsync(StorageFolder folder, string baseName, ImageAlphaRadialProfile.AnalysisResult a, byte[] kernelByBin)
    {
        var sb = new StringBuilder(capacity: 256 * 1024);
        sb.AppendLine("r_bin,px_from,px_to,total,mean_alpha,mean_alpha_byte");

        for (var bin = 0; bin < a.Bins; bin++)
        {
            var n = a.Total[bin];
            if (n <= 0) continue;

            var meanA01 = (a.SumAlpha[bin] / (double)n) / 255.0;

            sb.Append(bin.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append((bin * a.BinSize).ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(((bin + 1) * a.BinSize).ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(n.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(meanA01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(kernelByBin[bin].ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        var csvFile = await folder.CreateFileAsync($"{baseName}-kernel-profile.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(csvFile, sb.ToString());
    }

    private static async Task WriteNoiseSummaryCsvAsync(
        StorageFolder folder,
        string baseName,
        string srcPath,
        ImageAlphaRadialProfile.AnalysisResult a,
        long valid,
        double sumRatio,
        double minRatio,
        double maxRatio)
    {
        var meanRatio = valid > 0 ? sumRatio / valid : 0.0;
        if (!double.IsFinite(minRatio)) minRatio = 0.0;
        if (!double.IsFinite(maxRatio)) maxRatio = 0.0;

        var sb = new StringBuilder(512);
        sb.AppendLine("file,width,height,bin_size,valid_px,ratio_mean,ratio_min,ratio_max");
        sb.Append(Escape(Path.GetFileName(srcPath))).Append(',');
        sb.Append(a.Width.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(a.Height.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(a.BinSize.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(valid.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(meanRatio.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(minRatio.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(maxRatio.ToString("0.########", CultureInfo.InvariantCulture));
        sb.AppendLine();

        var csvFile = await folder.CreateFileAsync($"{baseName}-noise-summary.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(csvFile, sb.ToString());
    }

    private static async Task SaveGray8PngAsync(StorageFolder folder, string fileName, int w, int h, byte[] gray)
    {
        if (folder is null) throw new ArgumentNullException(nameof(folder));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileNameが空です。", nameof(fileName));
        if (w <= 0) throw new ArgumentOutOfRangeException(nameof(w));
        if (h <= 0) throw new ArgumentOutOfRangeException(nameof(h));
        if (gray is null) throw new ArgumentNullException(nameof(gray));
        if (gray.Length != w * h) throw new ArgumentException("grayの長さが一致しません。", nameof(gray));

        using var outBmp = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);

        unsafe
        {
            var dstBase = (byte*)outBmp.GetPixels().ToPointer();
            var rowBytes = outBmp.RowBytes;
            for (var y = 0; y < h; y++)
            {
                var dst = dstBase + (y * rowBytes);
                var srcRow = y * w;
                for (var x = 0; x < w; x++)
                {
                    dst[x] = gray[srcRow + x];
                }
            }
        }

        var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
        using var fs = new FileStream(file.Path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var image = SKImage.FromBitmap(outBmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(fs);
        fs.Flush(flushToDisk: true);
    }

    private static async Task SaveAlpha8PngAsync(StorageFolder folder, string fileName, int w, int h, byte[] alpha)
    {
        if (folder is null) throw new ArgumentNullException(nameof(folder));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileNameが空です。", nameof(fileName));
        if (w <= 0) throw new ArgumentOutOfRangeException(nameof(w));
        if (h <= 0) throw new ArgumentOutOfRangeException(nameof(h));
        if (alpha is null) throw new ArgumentNullException(nameof(alpha));
        if (alpha.Length != w * h) throw new ArgumentException("alphaの長さが一致しません。", nameof(alpha));

        using var outBmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var px = outBmp.Pixels;
        for (var i = 0; i < alpha.Length; i++)
        {
            px[i] = new SKColor(0, 0, 0, alpha[i]);
        }
        outBmp.Pixels = px;

        var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
        using var fs = new FileStream(file.Path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var image = SKImage.FromBitmap(outBmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(fs);
        fs.Flush(flushToDisk: true);
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }
}
