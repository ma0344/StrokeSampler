using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class ImageAlphaRadialProfileExporter
{
    internal static async Task ExportAlphaRadialProfileCsvAsync(MainWindow window, int binSize)
    {
        ArgumentNullException.ThrowIfNull(window);

        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "半径別プロファイルを出力するPNGを選択"
        };
        if (open.ShowDialog(window) != true) return;

        var path = open.FileName;
        if (string.IsNullOrWhiteSpace(path)) return;

        var folder = await PickOutputFolderAsync(window);
        if (folder is null) return;

        await ExportCoreAsync(folder, path, binSize);
    }

    internal static async Task ExportAlphaRadialProfileCsvBatchAsync(MainWindow window, int binSize)
    {
        ArgumentNullException.ThrowIfNull(window);

        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = true,
            Title = "半径別プロファイルを出力するPNG（複数）を選択"
        };
        if (open.ShowDialog(window) != true) return;

        var paths = open.FileNames;
        if (paths is null || paths.Length == 0) return;

        var folder = await PickOutputFolderAsync(window);
        if (folder is null) return;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            await ExportCoreAsync(folder, path, binSize);
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

    private static async Task ExportCoreAsync(StorageFolder folder, string path, int binSize)
    {
        if (binSize <= 0) binSize = 1;

        using var bmp = SKBitmap.Decode(path);
        if (bmp is null) return;

        var thresholds = ImageAlphaRadialProfile.CreateDefaultThresholds();
        var analysis = ImageAlphaRadialProfile.Analyze(bmp, binSize, thresholds);

        await WriteCsvFilesAsync(folder, path, analysis);
    }

    private static async Task WriteCsvFilesAsync(StorageFolder folder, string path, ImageAlphaRadialProfile.AnalysisResult a)
    {
        var baseName = $"radial-alpha-{Path.GetFileNameWithoutExtension(path)}";

        var summary = new StringBuilder(512);
        summary.AppendLine("file,width,height,center_x,center_y,bin_size,alpha_nonzero_count,alpha_nonzero_rate,alpha_sum,alpha_mean");
        summary.Append(Escape(Path.GetFileName(path))).Append(',');
        summary.Append(a.Width.ToString(CultureInfo.InvariantCulture)).Append(',');
        summary.Append(a.Height.ToString(CultureInfo.InvariantCulture)).Append(',');
        summary.Append(a.CenterX.ToString("0.#####", CultureInfo.InvariantCulture)).Append(',');
        summary.Append(a.CenterY.ToString("0.#####", CultureInfo.InvariantCulture)).Append(',');
        summary.Append(a.BinSize.ToString(CultureInfo.InvariantCulture)).Append(',');
        summary.Append(a.AlphaNonZeroPixels.ToString(CultureInfo.InvariantCulture)).Append(',');
        summary.Append(a.AlphaNonZeroRate.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        summary.Append(a.SumAlphaAll.ToString(CultureInfo.InvariantCulture)).Append(',');
        summary.Append(a.MeanAlphaAll01.ToString("0.########", CultureInfo.InvariantCulture));
        summary.AppendLine();

        var summaryFile = await folder.CreateFileAsync($"{baseName}-summary.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(summaryFile, summary.ToString());

        var sb = new StringBuilder(capacity: 1024 * 1024);
        sb.Append("r_bin,px_from,px_to,total,mean_alpha");
        for (var i = 0; i < a.Thresholds.Length; i++)
        {
            sb.Append(",p_ge_");
            sb.Append(a.Thresholds[i].ToString(CultureInfo.InvariantCulture));
        }
        sb.AppendLine();

        for (var bin = 0; bin < a.Bins; bin++)
        {
            var n = a.Total[bin];
            if (n <= 0) continue;

            var meanA01 = (a.SumAlpha[bin] / (double)n) / 255.0;

            sb.Append(bin.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append((bin * a.BinSize).ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(((bin + 1) * a.BinSize).ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(n.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(meanA01.ToString("0.########", CultureInfo.InvariantCulture));

            for (var tIndex = 0; tIndex < a.Thresholds.Length; tIndex++)
            {
                var rate = a.Hits[tIndex][bin] / (double)n;
                sb.Append(',');
                sb.Append(rate.ToString("0.########", CultureInfo.InvariantCulture));
            }

            sb.AppendLine();
        }

        var csvFile = await folder.CreateFileAsync($"{baseName}.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(csvFile, sb.ToString());
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
