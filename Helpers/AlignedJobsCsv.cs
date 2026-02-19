using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Storage;

namespace StrokeSampler.Helpers;

internal static class AlignedJobsCsv
{
    internal sealed class AlignedJob
    {
        internal string? RunTag { get; set; }
        internal int? Trials { get; set; }
        internal double? PressureStart { get; set; }
        internal double? PressureEnd { get; set; }
        internal double? PressureStep { get; set; }
        internal int? DotCount { get; set; }
        internal string? AlignedMode { get; set; }
        internal int? SingleN { get; set; }
        internal int? ExportScale { get; set; }
        internal double? PeriodStepDip { get; set; }
        internal string? StartXY { get; set; }
        internal string? EndXY { get; set; }
        internal double? LineLengthDip { get; set; }
        internal int? OutWidthDip { get; set; }
        internal int? OutHeightDip { get; set; }
        internal bool? Transparent { get; set; }
    }

    internal static async System.Threading.Tasks.Task<List<AlignedJob>> ReadAsync(StorageFile csvFile)
    {
        if (csvFile is null) throw new ArgumentNullException(nameof(csvFile));

        var text = await FileIO.ReadTextAsync(csvFile, Windows.Storage.Streams.UnicodeEncoding.Utf8);
        var lines = SplitLines(text);

        var result = new List<AlignedJob>();
        if (lines.Count == 0) return result;

        var headerLineIndex = FindHeaderLineIndex(lines);
        if (headerLineIndex < 0) return result;

        var headers = SplitCsvLine(lines[headerLineIndex]);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            var h = headers[i]?.Trim() ?? "";
            if (h.Length == 0) continue;
            map[h] = i;
        }

        for (var li = headerLineIndex + 1; li < lines.Count; li++)
        {
            var line = lines[li];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;

            var cols = SplitCsvLine(line);
            string? Get(string key)
            {
                if (!map.TryGetValue(key, out var idx)) return null;
                if (idx < 0 || idx >= cols.Length) return null;
                var v = cols[idx];
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }

            var job = new AlignedJob
            {
                RunTag = Get("runTag") ?? Get("run_tag"),
                Trials = TryInt(Get("trials")) ?? TryInt(Get("Trials")),
                PressureStart = TryDouble(Get("batchPStart")) ?? TryDouble(Get("pressure_start")) ?? TryDouble(Get("p_start")),
                PressureEnd = TryDouble(Get("batchPEnd")) ?? TryDouble(Get("pressure_end")) ?? TryDouble(Get("p_end")),
                PressureStep = TryDouble(Get("batchPStep")) ?? TryDouble(Get("pressure_step")) ?? TryDouble(Get("p_step")),
                DotCount = TryInt(Get("dotCount")) ?? TryInt(Get("aligned_n")) ?? TryInt(Get("alignedN")),
                AlignedMode = Get("aligned_mode") ?? Get("mode"),
                SingleN = TryInt(Get("single_n")) ?? TryInt(Get("aligned_single_n")),
                ExportScale = TryInt(Get("scale")) ?? TryInt(Get("exportScale")) ?? TryInt(Get("export_scale")),
                PeriodStepDip = TryDouble(Get("alignedPeriodDip")) ?? TryDouble(Get("periodStepDip")) ?? TryDouble(Get("period_step_dip")),
                StartXY = Get("start") ?? Get("startXY") ?? Get("start_xy"),
                EndXY = Get("end") ?? Get("endXY") ?? Get("end_xy"),
                LineLengthDip = TryDouble(Get("lDip")) ?? TryDouble(Get("lineLengthDip")) ?? TryDouble(Get("l_dip")),
                OutWidthDip = TryInt(Get("outWidth")) ?? TryInt(Get("outWidthDip")) ?? TryInt(Get("out_w")),
                OutHeightDip = TryInt(Get("outHeight")) ?? TryInt(Get("outHeightDip")) ?? TryInt(Get("out_h")),
                Transparent = TryBool(Get("transparent"))
            };

            result.Add(job);
        }

        return result;
    }

    // leaf: in-process tooling only
    internal static List<AlignedJob> Read(string csvPath)
    {
        var lines = File.ReadAllLines(csvPath);
        if (lines.Length == 0) return new List<AlignedJob>();
        // legacy path: reuse parser by joining
        var text = string.Join("\n", lines);
        return ReadFromText(text);
    }

    private static List<AlignedJob> ReadFromText(string text)
    {
        var lines = SplitLines(text);
        var result = new List<AlignedJob>();
        if (lines.Count == 0) return result;

        var headerLineIndex = FindHeaderLineIndex(lines);
        if (headerLineIndex < 0) return result;

        var headers = SplitCsvLine(lines[headerLineIndex]);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Length; i++)
        {
            var h = headers[i]?.Trim() ?? "";
            if (h.Length == 0) continue;
            map[h] = i;
        }

        for (var li = headerLineIndex + 1; li < lines.Count; li++)
        {
            var line = lines[li];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;

            var cols = SplitCsvLine(line);
            string? Get(string key)
            {
                if (!map.TryGetValue(key, out var idx)) return null;
                if (idx < 0 || idx >= cols.Length) return null;
                var v = cols[idx];
                return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }

            var job = new AlignedJob
            {
                RunTag = Get("runTag") ?? Get("run_tag"),
                Trials = TryInt(Get("trials")) ?? TryInt(Get("Trials")),
                PressureStart = TryDouble(Get("batchPStart")) ?? TryDouble(Get("pressure_start")) ?? TryDouble(Get("p_start")),
                PressureEnd = TryDouble(Get("batchPEnd")) ?? TryDouble(Get("pressure_end")) ?? TryDouble(Get("p_end")),
                PressureStep = TryDouble(Get("batchPStep")) ?? TryDouble(Get("pressure_step")) ?? TryDouble(Get("p_step")),
                DotCount = TryInt(Get("dotCount")) ?? TryInt(Get("aligned_n")) ?? TryInt(Get("alignedN")),
                AlignedMode = Get("aligned_mode") ?? Get("mode"),
                SingleN = TryInt(Get("single_n")) ?? TryInt(Get("aligned_single_n")),
                ExportScale = TryInt(Get("scale")) ?? TryInt(Get("exportScale")) ?? TryInt(Get("export_scale")),
                PeriodStepDip = TryDouble(Get("alignedPeriodDip")) ?? TryDouble(Get("periodStepDip")) ?? TryDouble(Get("period_step_dip")),
                StartXY = Get("start") ?? Get("startXY") ?? Get("start_xy"),
                EndXY = Get("end") ?? Get("endXY") ?? Get("end_xy"),
                LineLengthDip = TryDouble(Get("lDip")) ?? TryDouble(Get("lineLengthDip")) ?? TryDouble(Get("l_dip")),
                OutWidthDip = TryInt(Get("outWidth")) ?? TryInt(Get("outWidthDip")) ?? TryInt(Get("out_w")),
                OutHeightDip = TryInt(Get("outHeight")) ?? TryInt(Get("outHeightDip")) ?? TryInt(Get("out_h")),
                Transparent = TryBool(Get("transparent"))
            };

            result.Add(job);
        }

        return result;
    }

    private static int FindHeaderLineIndex(List<string> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
            return i;
        }
        return -1;
    }

    private static List<string> SplitLines(string text)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;

        using var sr = new StringReader(text);
        while (true)
        {
            var line = sr.ReadLine();
            if (line is null) break;
            lines.Add(line);
        }
        return lines;
    }

    // 最小限・引用符なし想定（この用途では十分）。必要になったら強化する。
    private static string[] SplitCsvLine(string line)
    {
        return line.Split(',');
    }

    private static int? TryInt(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return v;
        return null;
    }

    private static double? TryDouble(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return v;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
        return null;
    }

    private static bool? TryBool(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        if (bool.TryParse(s, out var b)) return b;
        if (string.Equals(s, "1", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(s, "0", StringComparison.OrdinalIgnoreCase)) return false;
        return null;
    }
}
