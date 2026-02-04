using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class AlphaWindowProfileSummaryAnalyzer
{
    internal static async Task AnalyzeAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var open = new OpenFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            Multiselect = false,
            Title = "alpha-window-profile-summary.csv を選択"
        };
        if (open.ShowDialog(window) != true) return;

        var path = open.FileName;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

        var headK = (int)(window.AlphaWindowSummaryHeadKNumberBox?.Value ?? 32);
        if (headK < 1) headK = 1;

        var tailN = (int)(window.AlphaWindowSummaryTailNumberBox?.Value ?? 10);
        if (tailN < 1) tailN = 1;

        var rows = ReadRows(path);
        if (rows.Count == 0) return;

        var groups = rows
            .GroupBy(r => new { r.File, r.Scale, r.PeriodPx, r.PeriodDip, r.RoiX, r.RoiY, r.RoiW, r.RoiH, r.WindowWPx, r.WindowWDip })
            .ToList();

        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add(".csv");
        InitializeWithWindow.Initialize(picker, new System.Windows.Interop.WindowInteropHelper(window).Handle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        var outName = $"alpha-window-profile-analysis-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);

        var sb = new StringBuilder(128 * 1024);
        sb.Append("file,scale,period_px,period_dip,roi_x,roi_y,roi_w,roi_h,window_w_px,window_w_dip,");
        sb.Append("first_nonzero_win_index,first_nonzero_x,steady_mean,steady_stddev,ramp_reach_win_index,ramp_reach_x");

        for (var i = 0; i < headK; i++)
        {
            sb.Append(",mean_").Append(i.ToString(CultureInfo.InvariantCulture));
        }

        sb.AppendLine();

        foreach (var g in groups)
        {
            var ordered = g.OrderBy(r => r.WinIndex).ToList();

            var firstNonZero = ordered.FirstOrDefault(r => r.AlphaNonZeroCount > 0);
            var firstNonZeroIndex = firstNonZero is null ? -1 : firstNonZero.WinIndex;
            var firstNonZeroX = firstNonZero is null ? -1 : firstNonZero.WinX;

            var tail = ordered.Count <= tailN ? ordered : ordered.Skip(Math.Max(0, ordered.Count - tailN)).ToList();
            var steadyMean = tail.Count == 0 ? 0.0 : tail.Average(r => r.AlphaMean01);
            var steadyStd = StdDev01(tail.Select(r => r.AlphaMean01));

            // ramp: steadyMean の 90% に初めて到達する index（開始が薄い/立ち上がりを見たい）
            var rampThreshold = steadyMean * 0.90;
            var ramp = ordered.FirstOrDefault(r => r.AlphaMean01 >= rampThreshold);
            var rampIndex = ramp is null ? -1 : ramp.WinIndex;
            var rampX = ramp is null ? -1 : ramp.WinX;

            sb.Append(Escape(g.Key.File)).Append(',');
            sb.Append(g.Key.Scale.ToString("0.####", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(g.Key.PeriodPx.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(g.Key.PeriodDip.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(g.Key.RoiX.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(g.Key.RoiY.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(g.Key.RoiW.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(g.Key.RoiH.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(g.Key.WindowWPx.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(g.Key.WindowWDip.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(firstNonZeroIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(firstNonZeroX.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(steadyMean.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(steadyStd.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(rampIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(rampX.ToString(CultureInfo.InvariantCulture));

            for (var i = 0; i < headK; i++)
            {
                var row = ordered.FirstOrDefault(r => r.WinIndex == i);
                var v = row is null ? 0.0 : row.AlphaMean01;
                sb.Append(',').Append(v.ToString("0.########", CultureInfo.InvariantCulture));
            }
            sb.AppendLine();
        }

        await FileIO.WriteTextAsync(outFile, sb.ToString());
    }

    private sealed record Row(
        string File,
        double Scale,
        int PeriodPx,
        double PeriodDip,
        int RoiX,
        int RoiY,
        int RoiW,
        int RoiH,
        int WindowWPx,
        double WindowWDip,
        int WinIndex,
        int WinX,
        long AlphaNonZeroCount,
        double AlphaMean01);

    private static List<Row> ReadRows(string path)
    {
        var list = new List<Row>(8192);
        using var sr = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = sr.ReadLine();
        if (header is null) return list;

        while (!sr.EndOfStream)
        {
            var line = sr.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) continue;

            // 本CSVはDotLabが生成する単純CSV（fileにカンマがない前提）
            var p = line.Split(',');
            if (p.Length < 16) continue;

            // file,scale,period_px,period_dip,roi_x,roi_y,roi_w,roi_h,window_w_px,window_w_dip,win_index,win_x,alpha_nonzero_count,alpha_mean,alpha_stddev,alpha_max
            var file = Unescape(p[0]);
            if (!TryParseDouble(p[1], out var scale)) continue;
            if (!int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var periodPx)) continue;
            if (!TryParseDouble(p[3], out var periodDip)) continue;
            if (!int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var roiX)) continue;
            if (!int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out var roiY)) continue;
            if (!int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out var roiW)) continue;
            if (!int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out var roiH)) continue;
            if (!int.TryParse(p[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var windowWPx)) continue;
            if (!TryParseDouble(p[9], out var windowWDip)) continue;
            if (!int.TryParse(p[10], NumberStyles.Integer, CultureInfo.InvariantCulture, out var winIndex)) continue;
            if (!int.TryParse(p[11], NumberStyles.Integer, CultureInfo.InvariantCulture, out var winX)) continue;
            if (!long.TryParse(p[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nonZero)) continue;
            if (!TryParseDouble(p[13], out var mean01)) continue;

            list.Add(new Row(file, scale, periodPx, periodDip, roiX, roiY, roiW, roiH, windowWPx, windowWDip, winIndex, winX, nonZero, mean01));
        }

        return list;
    }

    private static double StdDev01(IEnumerable<double> values)
    {
        var arr = values as double[] ?? values.ToArray();
        if (arr.Length == 0) return 0;
        var mean = arr.Average();
        var var = arr.Select(v => (v - mean) * (v - mean)).Average();
        return Math.Sqrt(var);
    }

    private static bool TryParseDouble(string s, out double v)
    {
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return true;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return true;
        return false;
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

    private static string Unescape(string s)
    {
        // 今回の出力は基本的にクオートなし想定だが、最低限対応
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
        {
            return s[1..^1].Replace("\"\"", "\"");
        }
        return s;
    }
}
