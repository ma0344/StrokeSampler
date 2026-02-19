using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DotLab.Analysis;

internal static class LineN1VsDotN1BatchMatcher
{
    private const int RoiWidthPx = 18;
    private const int RoiY0 = 435;
    private const int RoiY1Exclusive = 1592;

    internal static string BuildMatchCsv(string lineFolderPath, string dotFolderPath, bool useFullImage)
    {
        var rows = BuildRows(lineFolderPath, dotFolderPath, useFullImage);
        if (rows.Length == 0) return "";

        return BuildFullCsv(rows);
    }

    internal static string BuildSummaryCsv(string lineFolderPath, string dotFolderPath, bool useFullImage)
    {
        var rows = BuildRows(lineFolderPath, dotFolderPath, useFullImage);
        if (rows.Length == 0) return "";

        var best = rows
            .GroupBy(r => r.LineFile, StringComparer.OrdinalIgnoreCase)
            .Select(g => useFullImage
                ? g.OrderBy(r => r.DiffSum01).ThenBy(r => r.DiffNonZeroPx).First()
                : g.OrderBy(r => r.RoiDiffSum01).ThenBy(r => r.RoiDiffNonZeroPx).First())
            .OrderBy(r => r.LinePressure)
            .ToArray();

        return BuildSummaryOnlyCsv(best);
    }

    private static Row[] BuildRows(string lineFolderPath, string dotFolderPath, bool useFullImage)
    {
        if (string.IsNullOrWhiteSpace(lineFolderPath) || !Directory.Exists(lineFolderPath)) return Array.Empty<Row>();
        if (string.IsNullOrWhiteSpace(dotFolderPath) || !Directory.Exists(dotFolderPath)) return Array.Empty<Row>();

        var lineFiles = Directory.EnumerateFiles(lineFolderPath, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();

        var dotFiles = Directory.EnumerateFiles(dotFolderPath, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();

        var lines = lineFiles
            .Select(f => TryParsePressure(f!, out var p) ? new Item(f!, p) : (Item?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .OrderBy(x => x.Pressure)
            .ToArray();

        var dots = dotFiles
            .Select(f => TryParsePressure(f!, out var p) ? new Item(f!, p) : (Item?)null)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .OrderBy(x => x.Pressure)
            .ToArray();

        if (lines.Length == 0 || dots.Length == 0) return Array.Empty<Row>();

        // Dot側はOpacity sweep想定のため、同一Pが複数存在しうる。
        // ここでは全dotのalpha配列（full or ROI帯）を抽出し、line毎に同一Pのdot群と総当たり比較する。
        var dotAlphas = dots
            .Select(d => (d.Pressure, d.FileName, Alpha: TryLoadAlpha(Path.Combine(dotFolderPath, d.FileName), useFullImage)))
            .Where(x => x.Alpha != null)
            .Select(x => new DotAlpha(x.Pressure, x.FileName, x.Alpha!))
            .ToArray();

        if (dotAlphas.Length == 0) return Array.Empty<Row>();

        var rows = new List<Row>(lines.Length * 16);

        foreach (var l in lines)
        {
            var linePath = Path.Combine(lineFolderPath, l.FileName);
            using var bmpLine = SKBitmap.Decode(linePath);
            if (bmpLine == null) continue;

            var lineAlpha = ExtractAlpha(bmpLine, useFullImage);
            if (lineAlpha == null) continue;

            var candidates = dotAlphas.Where(d => Math.Abs(d.Pressure - l.Pressure) < 1e-8).ToArray();
            if (candidates.Length == 0) continue;

            foreach (var d in candidates)
            {
                var dotPath = Path.Combine(dotFolderPath, d.FileName);
                var diff = ComputeAlphaDiff(lineAlpha, d.Alpha, roiForReport: !useFullImage);
                var op = TryParseOpacity(d.FileName, out var opV)
                    ? opV.ToString("0.00000", CultureInfo.InvariantCulture)
                    : "";
                rows.Add(new Row(
                    LinePressure: l.Pressure,
                    LineFile: l.FileName,
                    LinePath: linePath,
                    DotPressure: d.Pressure,
                    DotFile: d.FileName,
                    DotPath: dotPath,
                    DotOpacity: op,
                    Width: bmpLine.Width,
                    Height: bmpLine.Height,
                    RoiW: RoiWidthPx,
                    RoiH: Math.Min(bmpLine.Height, Math.Max(0, RoiY1Exclusive) - Math.Max(0, RoiY0)),
                    DiffMin: diff.DiffMin,
                    DiffMax: diff.DiffMax,
                    DiffMean: diff.DiffMean,
                    DiffStdDev: diff.DiffStdDev,
                    DiffUnique: diff.DiffUnique,
                    DiffNonZeroPx: diff.DiffNonZeroPx,
                    DiffSum: diff.DiffSum,
                    DiffSum01: diff.DiffSum01,
                    RoiFound: diff.RoiFound,
                    RoiCenterX: diff.RoiCenterX,
                    RoiCenterY: diff.RoiCenterY,
                    RoiX0: diff.RoiX0,
                    RoiY0: diff.RoiY0,
                    RoiW2: diff.RoiW,
                    RoiH2: diff.RoiH,
                    RoiDiffMin: diff.RoiDiffMin,
                    RoiDiffMax: diff.RoiDiffMax,
                    RoiDiffMean: diff.RoiDiffMean,
                    RoiDiffStdDev: diff.RoiDiffStdDev,
                    RoiDiffUnique: diff.RoiDiffUnique,
                    RoiDiffNonZeroPx: diff.RoiDiffNonZeroPx,
                    RoiDiffSum: diff.RoiDiffSum,
                    RoiDiffSum01: diff.RoiDiffSum01));
            }

        }

        return rows.ToArray();
    }

    private static string BuildFullCsv(Row[] rows)
    {
        var sb = new StringBuilder(rows.Length * 256);
        sb.AppendLine(string.Join(",", new[]
        {
            "line_pressure","line_file","line_path","dot_pressure","dot_file","dot_path",
            "dot_opacity",
            "width","height","roi_w","roi_h",
            "diff_min","diff_max","diff_mean","diff_stddev","diff_unique",
             "diff_nonzero_px","diff_sum","diff_sum01",
            "roi_found","roi_center_x","roi_center_y","roi_x0","roi_y0","roi_w2","roi_h2",
            "roi_diff_min","roi_diff_max","roi_diff_mean","roi_diff_stddev","roi_diff_unique",
            "roi_diff_nonzero_px","roi_diff_sum","roi_diff_sum01"
        }));

        foreach (var r in rows)
        {
            sb.Append(r.LinePressure.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Escape(r.LineFile)).Append(',');
            sb.Append(Escape(r.LinePath)).Append(',');
            sb.Append(r.DotPressure.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Escape(r.DotFile)).Append(',');
            sb.Append(Escape(r.DotPath)).Append(',');
            sb.Append(r.DotOpacity).Append(',');

            sb.Append(r.Width.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.Height.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiW.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiH.ToString(CultureInfo.InvariantCulture)).Append(',');

            sb.Append(r.DiffMin.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.DiffMax.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.DiffMean.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.DiffStdDev.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.DiffUnique.ToString(CultureInfo.InvariantCulture)).Append(',');

            sb.Append(r.DiffNonZeroPx.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.DiffSum.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.DiffSum01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');

            sb.Append(r.RoiFound ? "1" : "0").Append(',');
            sb.Append(r.RoiCenterX.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiCenterY.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiX0.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiY0.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiW2.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiH2.ToString(CultureInfo.InvariantCulture)).Append(',');

            sb.Append(r.RoiDiffMin.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiDiffMax.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiDiffMean.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiDiffStdDev.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiDiffUnique.ToString(CultureInfo.InvariantCulture)).Append(',');

            sb.Append(r.RoiDiffNonZeroPx.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiDiffSum.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiDiffSum01.ToString("0.########", CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildSummaryOnlyCsv(Row[] rows)
    {
        var sb = new StringBuilder(rows.Length * 128);
        sb.AppendLine(string.Join(",", new[]
        {
            "line_pressure","line_file",
            "best_dot_opacity",
            "best_diff_sum01","best_diff_nonzero_px","best_diff_max",
            "best_roi_diff_sum01","best_roi_diff_nonzero_px","best_roi_diff_max"
        }));

        foreach (var r in rows)
        {
            sb.Append(r.LinePressure.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(Escape(r.LineFile)).Append(',');
            sb.Append(r.DotOpacity).Append(',');

            sb.Append(r.DiffSum01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.DiffNonZeroPx.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.DiffMax.ToString(CultureInfo.InvariantCulture)).Append(',');

            sb.Append(r.RoiDiffSum01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiDiffNonZeroPx.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(r.RoiDiffMax.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Escape(string s)
    {
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
        return '"' + s.Replace("\"", "\"\"") + '"';
    }

    private static bool TryParsePressure(string fileName, out double pressure)
    {
        // 既存の命名: ...-P{p}-...png
        pressure = 0;
        var m = Regex.Match(fileName, @"(?:^|-)P(?<p>\d+(?:\.\d+)?)(?:-|\.png$)", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        var s = m.Groups["p"].Value;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out pressure);
    }

    private static bool TryParseOpacity(string fileName, out double opacity)
    {
        // 命名: ...-Op{value}-...png
        opacity = 0;
        var m = Regex.Match(fileName, @"(?:^|-)Op(?<op>\d+(?:\.\d+)?)(?:-|\.png$)", RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        var s = m.Groups["op"].Value;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity);
    }

    private static byte[]? TryLoadAlpha(string path, bool useFullImage)
    {
        using var bmp = SKBitmap.Decode(path);
        if (bmp == null) return null;
        return ExtractAlpha(bmp, useFullImage);
    }

    private static byte[]? ExtractAlpha(SKBitmap bmp, bool useFullImage)
    {
        return useFullImage ? ExtractFullAlpha(bmp) : ExtractLeftRoiAlpha(bmp);
    }

    private static byte[] ExtractFullAlpha(SKBitmap bmp)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var a = new byte[w * h];
        var pixels = bmp.Pixels;
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                a[row + x] = pixels[row + x].Alpha;
            }
        }
        return a;
    }

    private static byte[]? ExtractLeftRoiAlpha(SKBitmap bmp)
    {
        if (bmp.Width < RoiWidthPx) return null;

        var y0 = Math.Clamp(RoiY0, 0, bmp.Height);
        var y1 = Math.Clamp(RoiY1Exclusive, 0, bmp.Height);
        if (y1 <= y0) return null;

        var h = y1 - y0;
        var roi = new byte[RoiWidthPx * h];
        var pixels = bmp.Pixels;
        var w = bmp.Width;

        for (var y = 0; y < h; y++)
        {
            var sy = y0 + y;
            var row = sy * w;
            for (var x = 0; x < RoiWidthPx; x++)
            {
                roi[y * RoiWidthPx + x] = pixels[row + x].Alpha;
            }
        }

        return roi;
    }

    private readonly record struct Item(string FileName, double Pressure);

    private readonly record struct DotAlpha(double Pressure, string FileName, byte[] Alpha);

    private readonly record struct Row(
        double LinePressure,
        string LineFile,
        string LinePath,
        double DotPressure,
        string DotFile,
        string DotPath,
        string DotOpacity,
        int Width,
        int Height,
        int RoiW,
        int RoiH,
        int DiffMin,
        int DiffMax,
        double DiffMean,
        double DiffStdDev,
        int DiffUnique,
        long DiffNonZeroPx,
        long DiffSum,
        double DiffSum01,
        bool RoiFound,
        int RoiCenterX,
        int RoiCenterY,
        int RoiX0,
        int RoiY0,
        int RoiW2,
        int RoiH2,
        int RoiDiffMin,
        int RoiDiffMax,
        double RoiDiffMean,
        double RoiDiffStdDev,
        int RoiDiffUnique,
        long RoiDiffNonZeroPx,
        long RoiDiffSum,
        double RoiDiffSum01);

    private readonly record struct AlphaDiffResult(
        int DiffMin,
        int DiffMax,
        double DiffMean,
        double DiffStdDev,
        int DiffUnique,
        long DiffNonZeroPx,
        long DiffSum,
        double DiffSum01,
        bool RoiFound,
        int RoiCenterX,
        int RoiCenterY,
        int RoiX0,
        int RoiY0,
        int RoiW,
        int RoiH,
        int RoiDiffMin,
        int RoiDiffMax,
        double RoiDiffMean,
        double RoiDiffStdDev,
        int RoiDiffUnique,
        long RoiDiffNonZeroPx,
        long RoiDiffSum,
        double RoiDiffSum01);

    private static AlphaDiffResult ComputeAlphaDiff(byte[] lineAlpha, byte[] dotAlpha, bool roiForReport)
    {
        if (lineAlpha.Length != dotAlpha.Length)
        {
            throw new InvalidOperationException($"Alpha size mismatch: line={lineAlpha.Length}, dot={dotAlpha.Length}");
        }

        // diff統計（入力全体）
        var len = lineAlpha.Length;
        var uniq = new bool[256];
        var min = 255;
        var max = 0;
        double sum = 0;
        double sum2 = 0;
        long nonZero = 0;
        long sumInt = 0;

        for (var i = 0; i < len; i++)
        {
            var d = Math.Abs(lineAlpha[i] - dotAlpha[i]);
            if (d < min) min = d;
            if (d > max) max = d;
            uniq[d] = true;
            sum += d;
            sum2 += (double)d * d;
            if (d != 0)
            {
                nonZero++;
                sumInt += d;
            }
        }

        var mean = sum / len;
        var var0 = Math.Max(0.0, (sum2 / len) - (mean * mean));
        var std = Math.Sqrt(var0);
        var uniqCount = uniq.Count(x => x);

        var sum01 = sumInt / 255.0;

        // ROI詳細: ROI帯指定時は従来通り、full image時は入力の「先頭の非ゼロαのbbox中心」を使い128x128を切り出す。
        // full imageでもROI統計は参照用に残す（best判定はfull diff側を使う）。
        var w = roiForReport ? RoiWidthPx : 0;
        var h = roiForReport ? (len / RoiWidthPx) : 0;

        if (!roiForReport)
        {
            // full image側は「本当に全体一致か」を見る用途なので、ROI統計は無効値を返す。
            return new AlphaDiffResult(
                DiffMin: min,
                DiffMax: max,
                DiffMean: mean,
                DiffStdDev: std,
                DiffUnique: uniqCount,
                DiffNonZeroPx: nonZero,
                DiffSum: sumInt,
                DiffSum01: sum01,
                RoiFound: false,
                RoiCenterX: 0,
                RoiCenterY: 0,
                RoiX0: 0,
                RoiY0: 0,
                RoiW: 0,
                RoiH: 0,
                RoiDiffMin: 0,
                RoiDiffMax: 0,
                RoiDiffMean: 0,
                RoiDiffStdDev: 0,
                RoiDiffUnique: 0,
                RoiDiffNonZeroPx: 0,
                RoiDiffSum: 0,
                RoiDiffSum01: 0);
        }

        var lx0 = w;
        var ly0 = h;
        var lx1 = -1;
        var ly1 = -1;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var a = lineAlpha[y * w + x];
                if (a == 0) continue;
                if (x < lx0) lx0 = x;
                if (y < ly0) ly0 = y;
                if (x > lx1) lx1 = x;
                if (y > ly1) ly1 = y;
            }
        }

        var found = lx1 >= lx0 && ly1 >= ly0;
        var cx = found ? (lx0 + lx1) / 2 : 0;
        var cy = found ? (ly0 + ly1) / 2 : 0;

        // line側bbox中心を基準に128x128 ROIを切り出す。
        const int roiW = 128;
        const int roiH = 128;
        var rx0 = found ? Math.Clamp(cx - roiW / 2, 0, Math.Max(0, w - roiW)) : 0;
        var ry0 = found ? Math.Clamp(cy - roiH / 2, 0, Math.Max(0, h - roiH)) : 0;

        var rMin = 255;
        var rMax = 0;
        double rSum = 0;
        double rSum2 = 0;
        var rUniq = new bool[256];
        long rNonZero = 0;
        long rSumInt = 0;

        for (var y = 0; y < roiH && (ry0 + y) < h; y++)
        {
            for (var x = 0; x < roiW && (rx0 + x) < w; x++)
            {
                var i = (ry0 + y) * w + (rx0 + x);
                var d = Math.Abs(lineAlpha[i] - dotAlpha[i]);
                if (d < rMin) rMin = d;
                if (d > rMax) rMax = d;
                rUniq[d] = true;
                rSum += d;
                rSum2 += (double)d * d;
                if (d != 0)
                {
                    rNonZero++;
                    rSumInt += d;
                }
            }
        }

        var rCount = Math.Min(roiW, w - rx0) * Math.Min(roiH, h - ry0);
        var rMean = rCount > 0 ? rSum / rCount : 0;
        var rVar0 = rCount > 0 ? Math.Max(0.0, (rSum2 / rCount) - (rMean * rMean)) : 0;
        var rStd = Math.Sqrt(rVar0);
        var rUniqCount = rUniq.Count(x => x);

        var rSum01 = rSumInt / 255.0;

        return new AlphaDiffResult(
            DiffMin: min,
            DiffMax: max,
            DiffMean: mean,
            DiffStdDev: std,
            DiffUnique: uniqCount,
            DiffNonZeroPx: nonZero,
            DiffSum: sumInt,
            DiffSum01: sum01,
            RoiFound: found,
            RoiCenterX: cx,
            RoiCenterY: cy,
            RoiX0: rx0,
            RoiY0: ry0,
            RoiW: roiW,
            RoiH: roiH,
            RoiDiffMin: rMin,
            RoiDiffMax: rMax,
            RoiDiffMean: rMean,
            RoiDiffStdDev: rStd,
            RoiDiffUnique: rUniqCount,
            RoiDiffNonZeroPx: rNonZero,
            RoiDiffSum: rSumInt,
            RoiDiffSum01: rSum01);
    }
}
