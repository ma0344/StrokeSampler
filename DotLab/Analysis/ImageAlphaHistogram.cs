using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class ImageAlphaHistogram
{
    internal static async Task ExportAlphaHistogramCsvAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "alpha histogram を調べる PNG を選択"
        };
        if (open.ShowDialog(window) != true) return;

        var path = open.FileName;
        if (string.IsNullOrWhiteSpace(path)) return;

        var folder = await PickOutputFolderAsync(window);
        if (folder is null) return;

        await ExportAlphaHistogramCsvCoreAsync(folder, path);
    }

    internal static async Task ExportAlphaHistogramCsvBatchAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = true,
            Title = "alpha histogram を出力する PNG（複数）を選択"
        };
        if (open.ShowDialog(window) != true) return;

        var paths = open.FileNames;
        if (paths is null || paths.Length == 0) return;

        var folder = await PickOutputFolderAsync(window);
        if (folder is null) return;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            await ExportAlphaHistogramCsvCoreAsync(folder, path);
        }
    }

    private static async Task<StorageFolder?> PickOutputFolderAsync(MainWindow window)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".csv");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSingleFolderAsync();
    }

    private static async Task ExportAlphaHistogramCsvCoreAsync(StorageFolder folder, string path)
    {
        using var bmp = SKBitmap.Decode(path);
        if (bmp is null) return;

        var w = bmp.Width;
        var h = bmp.Height;

        var hist = new long[256];
        long nonZero = 0;
        long pixelCount = (long)w * h;
        long sumA = 0;
        long sumA2 = 0;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var a = bmp.GetPixel(x, y).Alpha;
                hist[a]++;
                sumA += a;
                sumA2 += (long)a * a;
                if (a != 0) nonZero++;
            }
        }

        var meanA = pixelCount > 0 ? (sumA / (double)pixelCount) : 0.0;
        var varA = pixelCount > 0 ? (sumA2 / (double)pixelCount) - (meanA * meanA) : 0.0;
        if (varA < 0) varA = 0;
        var stdA = Math.Sqrt(varA);

        await WriteCsvFilesAsync(folder, path, w, h, pixelCount, nonZero, meanA, stdA, hist);
    }

    private static async Task WriteCsvFilesAsync(StorageFolder folder, string path, int w, int h, long pixelCount, long nonZero, double meanA, double stdA, long[] hist)
    {
        var baseName = $"alpha-hist-{Path.GetFileNameWithoutExtension(path)}";

        var summary = new StringBuilder(512);
        summary.AppendLine("file,width,height,pixel_count,alpha_nonzero_count,alpha_mean,alpha_stddev");
        summary.Append(Escape(Path.GetFileName(path))).Append(',');
        summary.Append(w.ToString(CultureInfo.InvariantCulture)).Append(',');
        summary.Append(h.ToString(CultureInfo.InvariantCulture)).Append(',');
        summary.Append(pixelCount.ToString(CultureInfo.InvariantCulture)).Append(',');
        summary.Append(nonZero.ToString(CultureInfo.InvariantCulture)).Append(',');
        summary.Append((meanA / 255.0).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        summary.Append((stdA / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
        summary.AppendLine();

        var summaryFile = await folder.CreateFileAsync($"{baseName}-summary.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(summaryFile, summary.ToString());

        var sb = new StringBuilder(4096);
        sb.AppendLine("alpha,count");
        for (var a = 0; a < 256; a++)
        {
            sb.Append(a.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(hist[a].ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        var histFile = await folder.CreateFileAsync($"{baseName}.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(histFile, sb.ToString());
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
