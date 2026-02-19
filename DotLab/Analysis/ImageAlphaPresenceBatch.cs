using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class ImageAlphaPresenceBatch
{
    internal static async Task ExportAlphaPresenceCsvBatchAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = true,
            Title = "alpha>0 ‚Ì—L–³‚ð’²‚×‚é PNG ‚ð•¡”‘I‘ð"
        };
        if (open.ShowDialog(window) != true) return;

        var paths = open.FileNames
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .ToArray();
        if (paths.Length == 0) return;

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add(".csv");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        var firstName = Path.GetFileNameWithoutExtension(paths[0]);
        var baseName = $"alpha-presence-batch-{firstName}-{DateTime.Now:yyyyMMdd-HHmmss}";
        baseName = SanitizeFileName(baseName);
        var csvFile = await folder.CreateFileAsync($"{baseName}.csv", CreationCollisionOption.ReplaceExisting);
        var summaryFile = await folder.CreateFileAsync($"{baseName}-summary.csv", CreationCollisionOption.ReplaceExisting);

        var sb = new StringBuilder(16 * 1024);
        sb.AppendLine("file,width,height,has_alpha,alpha_nonzero_count,alpha_max,first_nonzero_x,first_nonzero_y,decode_ok");

        var details = new List<Row>(paths.Length);

        foreach (var path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(path);

            try
            {
                using var bmp = SKBitmap.Decode(path);
                if (bmp is null)
                {
                    sb.Append(Escape(fileName)).Append(",,,,,,,,0").AppendLine();
                    continue;
                }

                var w = bmp.Width;
                var h = bmp.Height;

                long nonZero = 0;
                byte maxA = 0;
                int? firstX = null;
                int? firstY = null;
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var a = bmp.GetPixel(x, y).Alpha;
                        if (a == 0) continue;
                        nonZero++;
                        if (a > maxA) maxA = a;
                        if (firstX is null)
                        {
                            firstX = x;
                            firstY = y;
                        }
                    }
                }

                var hasAlpha = nonZero > 0;
                var row = new Row(
                    fileName,
                    w,
                    h,
                    hasAlpha,
                    nonZero,
                    maxA,
                    firstX,
                    firstY,
                    DecodeOk: true);
                details.Add(row);

                AppendDetail(sb, row);
            }
            catch
            {
                var row = new Row(fileName, 0, 0, HasAlpha: false, AlphaNonZeroCount: 0, AlphaMax: 0, FirstNonZeroX: null, FirstNonZeroY: null, DecodeOk: false);
                details.Add(row);
                AppendDetail(sb, row);
            }
        }

        await FileIO.WriteTextAsync(csvFile, sb.ToString());

        var summary = BuildSummary(details);
        await FileIO.WriteTextAsync(summaryFile, summary);
    }

    private static void AppendDetail(StringBuilder sb, Row row)
    {
        sb.Append(Escape(row.File)).Append(',');
        sb.Append(row.Width.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(row.Height.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(row.HasAlpha ? "1" : "0").Append(',');
        sb.Append(row.AlphaNonZeroCount.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(row.AlphaMax.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(row.FirstNonZeroX?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',');
        sb.Append(row.FirstNonZeroY?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',');
        sb.Append(row.DecodeOk ? "1" : "0");
        sb.AppendLine();
    }

    private static string BuildSummary(List<Row> details)
    {
        var sb = new StringBuilder(16 * 1024);
        sb.AppendLine("pressure,aligned_n,file_count,decode_ok_count,has_alpha_count,has_alpha_rate,mean_alpha_nonzero_count,mode_first_nonzero_x,mode_first_nonzero_y");

        var groups = details
            .Select(r => (r, meta: ParseMeta(r.File)))
            .GroupBy(x => new { x.meta.Pressure, x.meta.AlignedN })
            .OrderBy(g => g.Key.Pressure ?? double.PositiveInfinity)
            .ThenBy(g => g.Key.AlignedN ?? int.MaxValue);

        foreach (var g in groups)
        {
            var rows = g.Select(x => x.r).ToList();
            var fileCount = rows.Count;
            var decodeOkCount = rows.Count(r => r.DecodeOk);
            var hasAlphaCount = rows.Count(r => r.DecodeOk && r.HasAlpha);
            var hasAlphaRate = fileCount > 0 ? (double)hasAlphaCount / fileCount : 0.0;

            var meanNonZero = rows.Count > 0 ? rows.Average(r => (double)r.AlphaNonZeroCount) : 0.0;

            var modeX = Mode(rows.Select(r => r.FirstNonZeroX));
            var modeY = Mode(rows.Select(r => r.FirstNonZeroY));

            sb.Append(g.Key.Pressure?.ToString("0.########", CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(g.Key.AlignedN?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(fileCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(decodeOkCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(hasAlphaCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(hasAlphaRate.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(meanNonZero.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(modeX?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(modeY?.ToString(CultureInfo.InvariantCulture) ?? "");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static int? Mode(IEnumerable<int?> src)
    {
        return src
            .Where(v => v.HasValue)
            .GroupBy(v => v!.Value)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Select(g => (int?)g.Key)
            .FirstOrDefault();
    }

    private static (double? Pressure, int? AlignedN) ParseMeta(string fileName)
    {
        // e.g. pencil-highres-...-P0.07429-alignedN1-...png
        double? pressure = null;
        int? n = null;

        try
        {
            var pIdx = fileName.IndexOf("-P", StringComparison.OrdinalIgnoreCase);
            if (pIdx >= 0)
            {
                var start = pIdx + 2;
                var end = fileName.IndexOf('-', start);
                if (end > start)
                {
                    var pText = fileName.Substring(start, end - start);
                    if (double.TryParse(pText, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    {
                        pressure = p;
                    }
                }
            }

            var nIdx = fileName.IndexOf("-alignedN", StringComparison.OrdinalIgnoreCase);
            if (nIdx >= 0)
            {
                var start = nIdx + "-alignedN".Length;
                var end = fileName.IndexOf('-', start);
                if (end > start)
                {
                    var nText = fileName.Substring(start, end - start);
                    if (int.TryParse(nText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nn))
                    {
                        n = nn;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return (pressure, n);
    }

    private readonly record struct Row(
        string File,
        int Width,
        int Height,
        bool HasAlpha,
        long AlphaNonZeroCount,
        int AlphaMax,
        int? FirstNonZeroX,
        int? FirstNonZeroY,
        bool DecodeOk);

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}
