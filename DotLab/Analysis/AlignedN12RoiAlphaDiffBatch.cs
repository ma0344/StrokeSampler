using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class AlignedN12RoiAlphaDiffBatch
{
    private const int RoiSize = 128;

    internal static async Task ExportAlignedN1N2RoiAlphaDiffBatchAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

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

        // StorageFolder.Path is available in unpackaged WPF scenarios.
        // Guard anyway.
        var folderPath = folder.Path;
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            System.Windows.MessageBox.Show(window, "Selected folder path is not available.", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var diag = new BatchDiagnostics();
        var rows = AnalyzeFolder(folderPath, diag);

        var fixedDiag = new BatchDiagnostics();
        var fixedRows = AnalyzeFolderFixedRoi(folderPath, fixedDiag);

        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var csvName = $"alignedN1N2-roi-alpha-diff-batch-{ts}.csv";
        var csvFile = await folder.CreateFileAsync(csvName, CreationCollisionOption.ReplaceExisting);

        var sb = new StringBuilder(1024 + (rows.Count * 256));
        sb.AppendLine("pressure,trial,file_n1,file_n2,width,height,roi_center_x,roi_center_y,roi_x0,roi_y0,roi_w,roi_h,roi_diff_min,roi_diff_max,roi_diff_mean,roi_diff_stddev,roi_diff_unique,roi_diff_nonzero_px,roi_diff_sum,roi_diff_sum01");
        foreach (var row in rows.OrderBy(r => r.Pressure).ThenBy(r => r.Trial))
        {
            sb.Append(row.Pressure.ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.Trial.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.FileN1);
            sb.Append(',');
            sb.Append(row.FileN2);
            sb.Append(',');
            sb.Append(row.Width.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.Height.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiCenterX.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiCenterY.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiX0.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiY0.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiW.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiH.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiMin.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiMax.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append((row.RoiMean / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append((row.RoiStddev / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiUnique.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiNonZeroPx.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.RoiSum.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append((row.RoiSum / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        await FileIO.WriteTextAsync(csvFile, sb.ToString());

        var summaryText = AlignedN12RoiAlphaDiffBatchSummary.BuildSummaryCsv(csvFile.Path);
        if (!string.IsNullOrWhiteSpace(summaryText))
        {
            var summaryFile = await folder.CreateFileAsync($"alignedN1N2-roi-alpha-diff-batch-{ts}-summary.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(summaryFile, summaryText);
        }

        // Fixed ROI output (use the first ROI center found at the minimum pressure).
        var fixedCsvFile = await folder.CreateFileAsync($"alignedN1N2-roi-alpha-diff-batch-{ts}-fixedroi.csv", CreationCollisionOption.ReplaceExisting);
        var fixedSb = new StringBuilder(1024 + (fixedRows.Count * 256));
        fixedSb.AppendLine("pressure,trial,file_n1,file_n2,width,height,roi_center_x,roi_center_y,roi_x0,roi_y0,roi_w,roi_h,roi_diff_min,roi_diff_max,roi_diff_mean,roi_diff_stddev,roi_diff_unique,roi_diff_nonzero_px,roi_diff_sum,roi_diff_sum01");
        foreach (var row in fixedRows.OrderBy(r => r.Pressure).ThenBy(r => r.Trial))
        {
            fixedSb.Append(row.Pressure.ToString("0.########", CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.Trial.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.FileN1);
            fixedSb.Append(',');
            fixedSb.Append(row.FileN2);
            fixedSb.Append(',');
            fixedSb.Append(row.Width.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.Height.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiCenterX.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiCenterY.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiX0.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiY0.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiW.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiH.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiMin.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiMax.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append((row.RoiMean / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append((row.RoiStddev / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiUnique.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiNonZeroPx.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append(row.RoiSum.ToString(CultureInfo.InvariantCulture));
            fixedSb.Append(',');
            fixedSb.Append((row.RoiSum / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
            fixedSb.AppendLine();
        }
        await FileIO.WriteTextAsync(fixedCsvFile, fixedSb.ToString());

        var fixedSummaryText = AlignedN12RoiAlphaDiffBatchSummary.BuildSummaryCsv(fixedCsvFile.Path);
        if (!string.IsNullOrWhiteSpace(fixedSummaryText))
        {
            var fixedSummaryFile = await folder.CreateFileAsync($"alignedN1N2-roi-alpha-diff-batch-{ts}-fixedroi-summary.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(fixedSummaryFile, fixedSummaryText);
        }

        System.Windows.MessageBox.Show(window,
            $"Done. rows={rows.Count}\n" +
            $"png_files={diag.PngFiles}\n" +
            $"parsed={diag.ParsedFiles}\n" +
            $"n1_candidates={diag.N1Candidates}\n" +
            $"n2_candidates={diag.N2Candidates}\n" +
            $"paired_keys={diag.PairedKeys}\n" +
            $"drop_decode_fail={diag.DropDecodeFail}\n" +
            $"drop_size_mismatch={diag.DropSizeMismatch}\n" +
            $"drop_alpha_empty={diag.DropAlphaEmpty}\n" +
            $"fixed_rows={fixedRows.Count}\n" +
            $"fixed_png_files={fixedDiag.PngFiles}\n" +
            $"fixed_parsed={fixedDiag.ParsedFiles}\n" +
            $"fixed_n1_candidates={fixedDiag.N1Candidates}\n" +
            $"fixed_n2_candidates={fixedDiag.N2Candidates}\n" +
            $"fixed_paired_keys={fixedDiag.PairedKeys}\n" +
            $"fixed_drop_decode_fail={fixedDiag.DropDecodeFail}\n" +
            $"fixed_drop_size_mismatch={fixedDiag.DropSizeMismatch}\n" +
            $"fixed_drop_alpha_empty={fixedDiag.DropAlphaEmpty}\n" +
            $"file={csvFile.Path}",
            "DotLab",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private static List<Row> AnalyzeFolder(string folderPath, BatchDiagnostics diag)
    {
        var files = Directory.EnumerateFiles(folderPath, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();

        diag.PngFiles = files.Length;

        var n1ByKey = new Dictionary<(double Pressure, int Trial), string>();
        var n2ByKey = new Dictionary<(double Pressure, int Trial), string>();

        foreach (var name in files)
        {
            if (!TryParsePressureNTrial(name!, out var p, out var n, out var trial))
            {
                continue;
            }

            diag.ParsedFiles++;
            var key = (p, trial);
            if (n == 1 && !n1ByKey.ContainsKey(key)) n1ByKey[key] = name!;
            if (n == 2 && !n2ByKey.ContainsKey(key)) n2ByKey[key] = name!;
        }

        diag.N1Candidates = n1ByKey.Count;
        diag.N2Candidates = n2ByKey.Count;

        var keys = n1ByKey.Keys.Intersect(n2ByKey.Keys)
            .OrderBy(k => k.Pressure)
            .ThenBy(k => k.Trial)
            .ToArray();

        diag.PairedKeys = keys.Length;

        var rows = new List<Row>(keys.Length);
        foreach (var key in keys)
        {
            var f1 = n1ByKey[key];
            var f2 = n2ByKey[key];
            var path1 = Path.Combine(folderPath, f1);
            var path2 = Path.Combine(folderPath, f2);

            using var bmp1 = SKBitmap.Decode(path1);
            using var bmp2 = SKBitmap.Decode(path2);
            if (bmp1 == null || bmp2 == null)
            {
                diag.DropDecodeFail++;
                continue;
            }
            if (bmp1.Width != bmp2.Width || bmp1.Height != bmp2.Height)
            {
                diag.DropSizeMismatch++;
                continue;
            }

            var roiCx = FindFirstNonZeroAlpha(bmp1, out var roiCy);
            if (roiCx < 0)
            {
                diag.DropAlphaEmpty++;
                continue;
            }

            var roiX0 = Math.Clamp(roiCx - (RoiSize / 2), 0, Math.Max(0, bmp1.Width - RoiSize));
            var roiY0 = Math.Clamp(roiCy - (RoiSize / 2), 0, Math.Max(0, bmp1.Height - RoiSize));
            var roiW = Math.Min(RoiSize, bmp1.Width - roiX0);
            var roiH = Math.Min(RoiSize, bmp1.Height - roiY0);

            ComputeRoiStats(bmp1, bmp2, roiX0, roiY0, roiW, roiH,
                out var roiMin, out var roiMax, out var roiMean, out var roiStddev, out var roiUnique,
                out var roiNonZero, out var roiSum);

            rows.Add(new Row(
                Pressure: key.Pressure,
                Trial: key.Trial,
                FileN1: f1,
                FileN2: f2,
                Width: bmp1.Width,
                Height: bmp1.Height,
                RoiCenterX: roiCx,
                RoiCenterY: roiCy,
                RoiX0: roiX0,
                RoiY0: roiY0,
                RoiW: roiW,
                RoiH: roiH,
                RoiMin: roiMin,
                RoiMax: roiMax,
                RoiMean: roiMean,
                RoiStddev: roiStddev,
                RoiUnique: roiUnique,
                RoiNonZeroPx: roiNonZero,
                RoiSum: roiSum));
        }

        return rows;
    }

    private static List<Row> AnalyzeFolderFixedRoi(string folderPath, BatchDiagnostics diag)
    {
        var files = Directory.EnumerateFiles(folderPath, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();

        diag.PngFiles = files.Length;

        var n1ByKey = new Dictionary<(double Pressure, int Trial), string>();
        var n2ByKey = new Dictionary<(double Pressure, int Trial), string>();

        foreach (var name in files)
        {
            if (!TryParsePressureNTrial(name!, out var p, out var n, out var trial))
            {
                continue;
            }

            diag.ParsedFiles++;
            var key = (p, trial);
            if (n == 1 && !n1ByKey.ContainsKey(key)) n1ByKey[key] = name!;
            if (n == 2 && !n2ByKey.ContainsKey(key)) n2ByKey[key] = name!;
        }

        diag.N1Candidates = n1ByKey.Count;
        diag.N2Candidates = n2ByKey.Count;

        var keys = n1ByKey.Keys.Intersect(n2ByKey.Keys)
            .OrderBy(k => k.Pressure)
            .ThenBy(k => k.Trial)
            .ToArray();

        diag.PairedKeys = keys.Length;
        var rows = new List<Row>(keys.Length);
        if (keys.Length == 0) return rows;

        // Determine fixed ROI center from the first N1 (in sorted pressure/trial order)
        // that actually contains alpha>0.
        var fixedCx = -1;
        var fixedCy = -1;
        for (var i = 0; i < keys.Length; i++)
        {
            var k = keys[i];
            var anchorN1Path = Path.Combine(folderPath, n1ByKey[k]);
            using var anchorBmp = SKBitmap.Decode(anchorN1Path);
            if (anchorBmp == null)
            {
                diag.DropDecodeFail++;
                continue;
            }

            var cx = FindFirstNonZeroAlpha(anchorBmp, out var cy);
            if (cx >= 0)
            {
                fixedCx = cx;
                fixedCy = cy;
                break;
            }
        }

        if (fixedCx < 0)
        {
            diag.DropAlphaEmpty++;
            return rows;
        }

        foreach (var key in keys)
        {
            var f1 = n1ByKey[key];
            var f2 = n2ByKey[key];
            var path1 = Path.Combine(folderPath, f1);
            var path2 = Path.Combine(folderPath, f2);

            using var bmp1 = SKBitmap.Decode(path1);
            using var bmp2 = SKBitmap.Decode(path2);
            if (bmp1 == null || bmp2 == null)
            {
                diag.DropDecodeFail++;
                continue;
            }
            if (bmp1.Width != bmp2.Width || bmp1.Height != bmp2.Height)
            {
                diag.DropSizeMismatch++;
                continue;
            }

            var roiX0 = Math.Clamp(fixedCx - (RoiSize / 2), 0, Math.Max(0, bmp1.Width - RoiSize));
            var roiY0 = Math.Clamp(fixedCy - (RoiSize / 2), 0, Math.Max(0, bmp1.Height - RoiSize));
            var roiW = Math.Min(RoiSize, bmp1.Width - roiX0);
            var roiH = Math.Min(RoiSize, bmp1.Height - roiY0);

            ComputeRoiStats(bmp1, bmp2, roiX0, roiY0, roiW, roiH,
                out var roiMin, out var roiMax, out var roiMean, out var roiStddev, out var roiUnique,
                out var roiNonZero, out var roiSum);

            rows.Add(new Row(
                Pressure: key.Pressure,
                Trial: key.Trial,
                FileN1: f1,
                FileN2: f2,
                Width: bmp1.Width,
                Height: bmp1.Height,
                RoiCenterX: fixedCx,
                RoiCenterY: fixedCy,
                RoiX0: roiX0,
                RoiY0: roiY0,
                RoiW: roiW,
                RoiH: roiH,
                RoiMin: roiMin,
                RoiMax: roiMax,
                RoiMean: roiMean,
                RoiStddev: roiStddev,
                RoiUnique: roiUnique,
                RoiNonZeroPx: roiNonZero,
                RoiSum: roiSum));
        }

        return rows;
    }

    private sealed class BatchDiagnostics
    {
        internal int PngFiles;
        internal int ParsedFiles;
        internal int N1Candidates;
        internal int N2Candidates;
        internal int PairedKeys;
        internal int DropDecodeFail;
        internal int DropSizeMismatch;
        internal int DropAlphaEmpty;
    }

    private static int FindFirstNonZeroAlpha(SKBitmap bmp, out int yFound)
    {
        yFound = -1;
        for (var y = 0; y < bmp.Height; y++)
        {
            for (var x = 0; x < bmp.Width; x++)
            {
                if (bmp.GetPixel(x, y).Alpha > 0)
                {
                    yFound = y;
                    return x;
                }
            }
        }
        return -1;
    }

    private static void ComputeRoiStats(
        SKBitmap bmp1,
        SKBitmap bmp2,
        int x0,
        int y0,
        int w,
        int h,
        out int min,
        out int max,
        out double mean,
        out double stddev,
        out int unique,
        out long nonZeroPx,
        out long sum)
    {
        sum = 0;
        long sumSq = 0;
        nonZeroPx = 0;
        min = 255;
        max = 0;
        var hist = new int[256];

        for (var y = y0; y < y0 + h; y++)
        {
            for (var x = x0; x < x0 + w; x++)
            {
                var a0 = bmp1.GetPixel(x, y).Alpha;
                var a1 = bmp2.GetPixel(x, y).Alpha;
                var d = Math.Abs(a0 - a1);

                if (d != 0) nonZeroPx++;
                hist[d]++;
                sum += d;
                sumSq += (long)d * d;
                if (d < min) min = d;
                if (d > max) max = d;
            }
        }

        var count = (long)w * h;
        mean = count == 0 ? 0.0 : sum / (double)count;
        var variance = count == 0 ? 0.0 : (sumSq / (double)count) - (mean * mean);
        stddev = variance <= 0 ? 0.0 : Math.Sqrt(variance);
        unique = hist.Count(v => v != 0);
    }

    private static bool TryParsePressureNTrial(string fileName, out double pressure, out int n, out int trial)
    {
        pressure = 0;
        n = 0;
        trial = 0;

        // Example:
        // pencil-highres-324x3636-dpi96-S200-P0.0743-alignedN2-floor-N1to12-t1-scale18-transparent.png
        var m = Regex.Match(fileName, "-P(?<p>[0-9]+(?:\\.[0-9]+)?)-alignedN(?<n>[0-9]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success) return false;

        if (!double.TryParse(m.Groups["p"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out pressure))
        {
            return false;
        }
        if (!int.TryParse(m.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
        {
            return false;
        }

        // Prefer explicit "-t<digits>" (trial) embedded in runTag.
        // Fall back to "-dup<digits>" produced by collision-avoidance naming.
        // Note: runTag may contain other numeric tokens (timestamps), so keep this strict.
        var tm = Regex.Match(fileName, "-t(?<t>[0-9]+)(?:-|\\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (tm.Success)
        {
            _ = int.TryParse(tm.Groups["t"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out trial);
        }
        else
        {
            var dm = Regex.Match(fileName, "-dup(?<d>[0-9]+)(?:-|\\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (dm.Success)
            {
                _ = int.TryParse(dm.Groups["d"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out trial);
            }
        }

        return true;
    }

    private readonly record struct Row(
        double Pressure,
        int Trial,
        string FileN1,
        string FileN2,
        int Width,
        int Height,
        int RoiCenterX,
        int RoiCenterY,
        int RoiX0,
        int RoiY0,
        int RoiW,
        int RoiH,
        int RoiMin,
        int RoiMax,
        double RoiMean,
        double RoiStddev,
        int RoiUnique,
        long RoiNonZeroPx,
        long RoiSum);
}
