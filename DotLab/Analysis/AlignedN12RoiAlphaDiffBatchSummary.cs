using System.Globalization;
using System.IO;
using System.Text;

namespace DotLab.Analysis;

internal static class AlignedN12RoiAlphaDiffBatchSummary
{
    internal static string BuildSummaryCsv(string batchCsvPath)
    {
        if (string.IsNullOrWhiteSpace(batchCsvPath) || !File.Exists(batchCsvPath))
        {
            return "";
        }

        var lines = File.ReadAllLines(batchCsvPath);
        if (lines.Length <= 1) return "";

        var header = SplitCsv(lines[0]);
        var idxPressure = Array.IndexOf(header, "pressure");
        var idxTrial = Array.IndexOf(header, "trial");
        var idxCx = Array.IndexOf(header, "roi_center_x");
        var idxCy = Array.IndexOf(header, "roi_center_y");
        var idxNonZero = Array.IndexOf(header, "roi_diff_nonzero_px");
        var idxSum = Array.IndexOf(header, "roi_diff_sum");

        if (idxPressure < 0 || idxCx < 0 || idxCy < 0 || idxNonZero < 0 || idxSum < 0) return "";

        var byP = new Dictionary<double, List<Row>>();
        for (var i = 1; i < lines.Length; i++)
        {
            var cols = SplitCsv(lines[i]);
            if (cols.Length != header.Length) continue;

            if (!double.TryParse(cols[idxPressure], NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) continue;

            _ = int.TryParse(idxTrial >= 0 ? cols[idxTrial] : "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var trial);
            _ = int.TryParse(cols[idxCx], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cx);
            _ = int.TryParse(cols[idxCy], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cy);
            _ = long.TryParse(cols[idxNonZero], NumberStyles.Integer, CultureInfo.InvariantCulture, out var nonZero);
            _ = long.TryParse(cols[idxSum], NumberStyles.Integer, CultureInfo.InvariantCulture, out var sum);

            if (!byP.TryGetValue(p, out var list))
            {
                list = new List<Row>();
                byP[p] = list;
            }
            list.Add(new Row(trial, cx, cy, nonZero, sum));
        }

        var sb = new StringBuilder(4096);
        sb.AppendLine("pressure,trials,roi_center_x_mode,roi_center_y_mode,roi_center_x_min,roi_center_x_max,roi_center_y_min,roi_center_y_max,nonzero_min,nonzero_max,nonzero_mean,sum_min,sum_max,sum_mean");

        foreach (var kv in byP.OrderBy(k => k.Key))
        {
            var p = kv.Key;
            var rows = kv.Value;
            if (rows.Count == 0) continue;

            var cxMode = Mode(rows.Select(r => r.Cx));
            var cyMode = Mode(rows.Select(r => r.Cy));

            var cxMin = rows.Min(r => r.Cx);
            var cxMax = rows.Max(r => r.Cx);
            var cyMin = rows.Min(r => r.Cy);
            var cyMax = rows.Max(r => r.Cy);

            var nzMin = rows.Min(r => r.NonZero);
            var nzMax = rows.Max(r => r.NonZero);
            var nzMean = rows.Average(r => r.NonZero);

            var sumMin = rows.Min(r => r.Sum);
            var sumMax = rows.Max(r => r.Sum);
            var sumMean = rows.Average(r => r.Sum);

            sb.Append(p.ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(rows.Count.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(cxMode.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(cyMode.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(cxMin.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(cxMax.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(cyMin.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(cyMax.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(nzMin.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(nzMax.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(nzMean.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(sumMin.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(sumMax.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(sumMean.ToString("0.###", CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string[] SplitCsv(string line)
    {
        // This batch CSV does not contain quoted commas in numeric columns.
        return line.Split(',');
    }

    private static int Mode(IEnumerable<int> values)
    {
        var dict = new Dictionary<int, int>();
        foreach (var v in values)
        {
            dict.TryGetValue(v, out var c);
            dict[v] = c + 1;
        }
        if (dict.Count == 0) return 0;
        return dict.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
    }

    private readonly record struct Row(int Trial, int Cx, int Cy, long NonZero, long Sum);
}
