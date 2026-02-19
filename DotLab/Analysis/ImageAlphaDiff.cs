using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class ImageAlphaDiff
{
    internal static async Task ExportAlphaDiffAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // WPFなので Win32 ダイアログで2枚選ぶ
        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "PNG(1) を選択"
        };
        if (open.ShowDialog(window) != true) return;
        var canvasPath = open.FileName;

        open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "PNG(2) を選択"
        };
        if (open.ShowDialog(window) != true) return;
        var simPath = open.FileName;

        static (long Size, string Sha256) GetFileSig(string path)
        {
            var fi = new FileInfo(path);
            using var fs = fi.OpenRead();
            var hash = SHA256.HashData(fs);
            return (fi.Length, Convert.ToHexString(hash));
        }

        var canvasSig = GetFileSig(canvasPath);
        var simSig = GetFileSig(simPath);

        using var canvasBmp = SKBitmap.Decode(canvasPath);
        using var simBmp = SKBitmap.Decode(simPath);
        if (canvasBmp == null || simBmp == null) return;
        if (canvasBmp.Width != simBmp.Width || canvasBmp.Height != simBmp.Height) return;

        var w = canvasBmp.Width;
        var h = canvasBmp.Height;

        using var diff = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        using var vis16 = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);
        using var vis32 = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);

        const int roiSize = 128;
        var roiCx = -1;
        var roiCy = -1;

        long sum = 0;
        long sumSq = 0;
        var min = 255;
        var max = 0;
        var hist = new int[256];

        var canvasPixels = canvasBmp.Pixels;
        var simPixels = simBmp.Pixels;

        long canvasAlphaSum = 0;
        long simAlphaSum = 0;
        long canvasAlphaNonZero = 0;
        long simAlphaNonZero = 0;

        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                var idx = row + x;
                var a0 = canvasPixels[idx].Alpha;
                var a1 = simPixels[idx].Alpha;
                var d = Math.Abs(a0 - a1);

                canvasAlphaSum += a0;
                simAlphaSum += a1;
                if (a0 != 0) canvasAlphaNonZero++;
                if (a1 != 0) simAlphaNonZero++;

                if (roiCx < 0 && a0 > 0)
                {
                    roiCx = x;
                    roiCy = y;
                }

                hist[d]++;
                sum += d;
                sumSq += (long)d * d;
                if (d < min) min = d;
                if (d > max) max = d;

                // 差分を1ch画像として保存（ビューア互換性重視）
                diff.SetPixel(x, y, new SKColor((byte)d, (byte)d, (byte)d, 255));

                // 差分を強調して可視化（暗すぎて見えないケースの目視確認用）
                var v16 = (byte)Math.Min(255, d * 16);
                var v32 = (byte)Math.Min(255, d * 32);
                vis16.SetPixel(x, y, new SKColor(v16, v16, v16, 255));
                vis32.SetPixel(x, y, new SKColor(v32, v32, v32, 255));
            }
        }

        // ROI stats around first non-zero alpha pixel of the first image.
        // This boosts S/N when diff is extremely sparse.
        var roiX0 = 0;
        var roiY0 = 0;
        var roiW = 0;
        var roiH = 0;
        long roiSum = 0;
        long roiSumSq = 0;
        long roiNonZero = 0;
        var roiMin = 255;
        var roiMax = 0;
        var roiHist = new int[256];
        if (roiCx >= 0)
        {
            roiX0 = Math.Clamp(roiCx - (roiSize / 2), 0, Math.Max(0, w - roiSize));
            roiY0 = Math.Clamp(roiCy - (roiSize / 2), 0, Math.Max(0, h - roiSize));
            roiW = Math.Min(roiSize, w - roiX0);
            roiH = Math.Min(roiSize, h - roiY0);
            for (var y = roiY0; y < roiY0 + roiH; y++)
            {
                var row = y * w;
                for (var x = roiX0; x < roiX0 + roiW; x++)
                {
                    var idx = row + x;
                    var a0 = canvasPixels[idx].Alpha;
                    var a1 = simPixels[idx].Alpha;
                    var d = Math.Abs(a0 - a1);

                    if (d != 0) roiNonZero++;
                    roiHist[d]++;
                    roiSum += d;
                    roiSumSq += (long)d * d;
                    if (d < roiMin) roiMin = d;
                    if (d > roiMax) roiMax = d;
                }
            }
        }

        var count = (long)w * h;
        var mean = count == 0 ? 0.0 : sum / (double)count;
        var variance = count == 0 ? 0.0 : (sumSq / (double)count) - (mean * mean);
        var stddev = variance <= 0 ? 0.0 : Math.Sqrt(variance);
        var unique = hist.Count(v => v != 0);

        var roiCount = (long)roiW * roiH;
        var roiMean = roiCount == 0 ? 0.0 : roiSum / (double)roiCount;
        var roiVariance = roiCount == 0 ? 0.0 : (roiSumSq / (double)roiCount) - (roiMean * roiMean);
        var roiStddev = roiVariance <= 0 ? 0.0 : Math.Sqrt(roiVariance);
        var roiUnique = roiCx < 0 ? 0 : roiHist.Count(v => v != 0);

        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        folderPicker.FileTypeFilter.Add(".png");
        folderPicker.FileTypeFilter.Add(".csv");

        var hwnd = new WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(folderPicker, hwnd);


        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder is null) return;

        var baseName = $"alpha-diff-{Path.GetFileNameWithoutExtension(canvasPath)}-vs-{Path.GetFileNameWithoutExtension(simPath)}";

        // PNG
        var pngFile = await folder.CreateFileAsync($"{baseName}.png", CreationCollisionOption.ReplaceExisting);
        using (var fs = new FileStream(pngFile.Path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            using var image = SKImage.FromBitmap(diff);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            data.SaveTo(fs);
            fs.Flush(flushToDisk: true);
        }

        // PNG (diff visualization)
        var vis16File = await folder.CreateFileAsync($"{baseName}-vis16.png", CreationCollisionOption.ReplaceExisting);
        using (var fs = new FileStream(vis16File.Path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            using var image = SKImage.FromBitmap(vis16);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            data.SaveTo(fs);
            fs.Flush(flushToDisk: true);
        }

        var vis32File = await folder.CreateFileAsync($"{baseName}-vis32.png", CreationCollisionOption.ReplaceExisting);
        using (var fs = new FileStream(vis32File.Path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            using var image = SKImage.FromBitmap(vis32);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            data.SaveTo(fs);
            fs.Flush(flushToDisk: true);
        }

        // CSV
        var sb = new StringBuilder(1024);
        sb.AppendLine("png1_path,png1_size,png1_sha256,png1_alpha_nonzero_px,png1_alpha_sum,png2_path,png2_size,png2_sha256,png2_alpha_nonzero_px,png2_alpha_sum,width,height,diff_min,diff_max,diff_mean,diff_stddev,diff_unique,roi_found,roi_center_x,roi_center_y,roi_x0,roi_y0,roi_w,roi_h,roi_diff_min,roi_diff_max,roi_diff_mean,roi_diff_stddev,roi_diff_unique,roi_diff_nonzero_px,roi_diff_sum,roi_diff_sum01");
        sb.Append(EscapeForCsv(canvasPath));
        sb.Append(',');
        sb.Append(canvasSig.Size.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(canvasSig.Sha256);
        sb.Append(',');
        sb.Append(canvasAlphaNonZero.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(canvasAlphaSum.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(EscapeForCsv(simPath));
        sb.Append(',');
        sb.Append(simSig.Size.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(simSig.Sha256);
        sb.Append(',');
        sb.Append(simAlphaNonZero.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(simAlphaSum.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(w.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(h.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(min.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(max.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append((mean / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append((stddev / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(unique.ToString(CultureInfo.InvariantCulture));

        sb.Append(',');
        sb.Append((roiCx >= 0 ? 1 : 0).ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(roiCx.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(roiCy.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(roiX0.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(roiY0.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(roiW.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(roiH.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append((roiCx < 0 ? 0 : roiMin).ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append((roiCx < 0 ? 0 : roiMax).ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append((roiMean / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append((roiStddev / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(roiUnique.ToString(CultureInfo.InvariantCulture));

        sb.Append(',');
        sb.Append((roiCx < 0 ? 0 : roiNonZero).ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append((roiCx < 0 ? 0 : roiSum).ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append((roiCx < 0 ? 0 : (roiSum / 255.0)).ToString("0.########", CultureInfo.InvariantCulture));
        sb.AppendLine();

        var csvFile = await folder.CreateFileAsync($"{baseName}.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(csvFile, sb.ToString());
    }

    private static string EscapeForCsv(string s)
    {
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return '"' + s.Replace("\"", "\"\"") + '"';
    }
}
