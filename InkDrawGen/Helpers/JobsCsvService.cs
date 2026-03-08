using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace InkDrawGen.Helpers
{
    internal static class JobsCsvService
    {
        internal sealed class JobRow
        {
            internal string JobType;
            internal double PressureStart;
            internal double PressureEnd;
            internal double PressureStep;

            internal double SStart;
            internal double SEnd;
            internal double SStep;

            internal int NStart;
            internal int NEnd;
            internal int NStep;

            internal int Scale;
            internal double Dpi;
            internal bool Transparent;

            internal double StartX;
            internal double StartY;
            internal double StepX;
            internal double StepY;
            internal int RepeatCount;
            internal double EndX;
            internal double EndY;

            internal bool DotStepFixedCount;
            internal int DotStepCount;

            internal double DotStepStart;
            internal double DotStepEnd;
            internal double DotStepStep;

            internal bool DotStepTwoPoints;

            // 線長を「更新点間隔 I と更新点数 Np」で指定する補助入力
            // - interval: I
            // - npoints: Np
            internal double Interval;
            internal int Npoints;

            internal int DotStepCountStart;
            internal int DotStepCountEnd;
            internal int DotStepCountStep;

            internal double RoiX;
            internal double RoiY;
            internal double RoiW;
            internal double RoiH;

            internal double OpStart;
            internal double OpEnd;
            internal double OpStep;
            internal string OpValues;

            internal string RunTag;

            internal JobRow()
            {
                RunTag = "";
                JobType = "";
                OpValues = "";
            }
        }

        internal static IEnumerable<JobRow> Read(string csvText)
        {
            if (string.IsNullOrWhiteSpace(csvText)) yield break;

            using (var sr = new StringReader(csvText))
            {
                var header = sr.ReadLine();
                if (header == null) yield break;
                var map = BuildHeaderMap(header);

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var cells = SplitCsv(line);

                    var row = new JobRow
                    {
                        JobType = ReadString(cells, map, "jobType", ReadString(cells, map, "job_type", "")),
                        PressureStart = ReadDouble(cells, map, "pressure_start", 1),
                        PressureEnd = ReadDouble(cells, map, "pressure_end", 1),
                        PressureStep = ReadDouble(cells, map, "pressure_step", 0),

                        SStart = ReadDouble(cells, map, "s_start", 200),
                        SEnd = ReadDouble(cells, map, "s_end", 200),
                        SStep = ReadDouble(cells, map, "s_step", 0),

                        NStart = ReadInt(cells, map, "n_start", 1),
                        NEnd = ReadInt(cells, map, "n_end", 1),
                        NStep = ReadInt(cells, map, "n_step", 0),

                        Scale = ReadInt(cells, map, "scale", 10),
                        Dpi = ReadDouble(cells, map, "dpi", 96),
                        Transparent = ReadBool(cells, map, "transparent", true),

                        StartX = ReadDouble(cells, map, "start_x", double.NaN),
                        StartY = ReadDouble(cells, map, "start_y", double.NaN),
                        StepX = ReadDouble(cells, map, "step_x", ReadDouble(cells, map, "stepX", 0)),
                        StepY = ReadDouble(cells, map, "step_y", ReadDouble(cells, map, "stepY", 0)),
                        RepeatCount = ReadInt(cells, map, "repeat_count", ReadInt(cells, map, "repeatCount", 0)),
                        EndX = ReadDouble(cells, map, "end_x", double.NaN),
                        EndY = ReadDouble(cells, map, "end_y", double.NaN),

                        DotStepFixedCount = ReadBool(cells, map, "dot_step_fixed_count", ReadBool(cells, map, "dotStepFixedCount", false)),
                        DotStepCount = ReadInt(cells, map, "dot_step_count", ReadInt(cells, map, "dotStepCount", 2)),

                        DotStepStart = ReadDouble(cells, map, "dot_step_start", ReadDouble(cells, map, "dotStepStart", double.NaN)),
                        DotStepEnd = ReadDouble(cells, map, "dot_step_end", ReadDouble(cells, map, "dotStepEnd", double.NaN)),
                        DotStepStep = ReadDouble(cells, map, "dot_step_step", ReadDouble(cells, map, "dotStepStep", double.NaN)),
                        DotStepTwoPoints = ReadBool(cells, map, "dot_step_two_points", ReadBool(cells, map, "dotStepTwoPoints", false)),

                        // 互換: セルが "I=9" / "Np=14" のような表記でも数値部分を拾う
                        Interval = ReadDoubleLoose(cells, map, "interval", ReadDoubleLoose(cells, map, "i", double.NaN)),
                        // 互換: "Npoits" はユーザーCSVで発生しやすいtypoなので別名として受ける
                        Npoints = ReadIntLoose(cells, map, "npoints",
                            ReadIntLoose(cells, map, "np",
                                ReadIntLoose(cells, map, "n_points",
                                    ReadIntLoose(cells, map, "npoits", 0)))),

                        DotStepCountStart = ReadInt(cells, map, "dot_step_count_start", ReadInt(cells, map, "dotStepCountStart", 0)),
                        DotStepCountEnd = ReadInt(cells, map, "dot_step_count_end", ReadInt(cells, map, "dotStepCountEnd", 0)),
                        DotStepCountStep = ReadInt(cells, map, "dot_step_count_step", ReadInt(cells, map, "dotStepCountStep", 0)),

                        RoiX = ReadDouble(cells, map, "roi_x", 0),
                        RoiY = ReadDouble(cells, map, "roi_y", 0),
                        RoiW = ReadDouble(cells, map, "roi_w", 18),
                        RoiH = ReadDouble(cells, map, "roi_h", 202),

                        // Opacity: range or list
                        OpStart = ReadDouble(cells, map, "op_start", ReadDouble(cells, map, "opacity_start", double.NaN)),
                        OpEnd = ReadDouble(cells, map, "op_end", ReadDouble(cells, map, "opacity_end", double.NaN)),
                        OpStep = ReadDouble(cells, map, "op_step", ReadDouble(cells, map, "opacity_step", double.NaN)),
                        OpValues = ReadString(cells, map, "op_values", ReadString(cells, map, "opacity_values", "")),

                        RunTag = ReadString(cells, map, "runTag", ReadString(cells, map, "run_tag", ReadString(cells, map, "runTag", ""))),
                    };

                    // StrokeSampler互換: start/end が "x,y" の文字列として入っているケース
                    if (double.IsNaN(row.StartX) || double.IsNaN(row.StartY))
                    {
                        var start = ReadString(cells, map, "start", "");
                        if (TryParsePoint(start, out var x, out var y))
                        {
                            row.StartX = x;
                            row.StartY = y;
                        }
                    }
                    if (double.IsNaN(row.EndX) || double.IsNaN(row.EndY))
                    {
                        var end = ReadString(cells, map, "end", "");
                        if (TryParsePoint(end, out var x, out var y))
                        {
                            row.EndX = x;
                            row.EndY = y;
                        }
                    }

                    if (double.IsNaN(row.StartX)) row.StartX = 100;
                    if (double.IsNaN(row.StartY)) row.StartY = 101;
                    if (double.IsNaN(row.EndX)) row.EndX = 500;
                    if (double.IsNaN(row.EndY)) row.EndY = 101;

                    if (double.IsNaN(row.StepX) || double.IsInfinity(row.StepX)) row.StepX = 0;
                    if (double.IsNaN(row.StepY) || double.IsInfinity(row.StepY)) row.StepY = 0;
                    if (row.RepeatCount < 0) row.RepeatCount = 0;
                    if (row.DotStepCount < 1) row.DotStepCount = 1;

                    if (row.DotStepCountStart < 0) row.DotStepCountStart = 0;
                    if (row.DotStepCountEnd < 0) row.DotStepCountEnd = 0;

                    if (double.IsNaN(row.OpStart) || double.IsInfinity(row.OpStart)) row.OpStart = double.NaN;
                    if (double.IsNaN(row.OpEnd) || double.IsInfinity(row.OpEnd)) row.OpEnd = double.NaN;
                    if (double.IsNaN(row.OpStep) || double.IsInfinity(row.OpStep)) row.OpStep = double.NaN;

                    // StrokeSampler互換: out_w/out_h も許容（現在はROI*w/h*scaleで出力を決めるが、ヘッダの存在は許容する）
                    // trials -> n_end として扱う（単発用）
                    var trials = ReadInt(cells, map, "trials", 0);
                    if (trials > 0)
                    {
                        row.NStart = trials;
                        row.NEnd = trials;
                        row.NStep = 0;
                    }

                    yield return row;
                }
            }
        }

        private static bool TryParsePoint(string s, out double x, out double y)
        {
            x = 0;
            y = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;

            var parts = s.Trim().Trim('"').Split(',');
            if (parts.Length != 2) return false;

            double vx;
            double vy;
            if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out vx) &&
                !double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out vx)) return false;
            if (!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out vy) &&
                !double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out vy)) return false;

            x = vx;
            y = vy;
            return true;
        }

        private static Dictionary<string, int> BuildHeaderMap(string headerLine)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var cells = SplitCsv(headerLine);
            for (var i = 0; i < cells.Count; i++)
            {
                var key = (cells[i] ?? "").Trim();
                if (key.Length == 0) continue;
                if (!map.ContainsKey(key)) map.Add(key, i);
            }
            return map;
        }

        private static string ReadString(IReadOnlyList<string> cells, Dictionary<string, int> map, string key, string fallback)
        {
            int i;
            if (!map.TryGetValue(key, out i)) return fallback;
            if (i < 0 || i >= cells.Count) return fallback;
            return (cells[i] ?? "").Trim();
        }

        private static double ReadDouble(IReadOnlyList<string> cells, Dictionary<string, int> map, string key, double fallback)
        {
            var s = ReadString(cells, map, key, "");
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
            return fallback;
        }

        private static int ReadInt(IReadOnlyList<string> cells, Dictionary<string, int> map, string key, int fallback)
        {
            var s = ReadString(cells, map, key, "");
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return v;
            return fallback;
        }

        private static readonly Regex FirstIntRegex = new Regex(@"[-+]?\d+", RegexOptions.Compiled);
        // 少なくとも1桁以上の数字を含むdoubleトークン（例: 9, 9.0, .5, 1e-3）
        private static readonly Regex FirstDoubleRegex = new Regex(@"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.Compiled);

        private static int ReadIntLoose(IReadOnlyList<string> cells, Dictionary<string, int> map, string key, int fallback)
        {
            var s = ReadString(cells, map, key, "");
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim().Trim('"');

            var strict = ReadInt(cells, map, key, int.MinValue);
            if (strict != int.MinValue) return strict;

            var m = FirstIntRegex.Match(s);
            if (!m.Success) return fallback;
            int v;
            if (int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        private static double ReadDoubleLoose(IReadOnlyList<string> cells, Dictionary<string, int> map, string key, double fallback)
        {
            var s = ReadString(cells, map, key, "");
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim().Trim('"');

            var strict = ReadDouble(cells, map, key, double.NaN);
            if (!double.IsNaN(strict)) return strict;

            var m = FirstDoubleRegex.Match(s);
            if (!m.Success) return fallback;

            var token = m.Value;
            if (string.IsNullOrWhiteSpace(token) || token == "+" || token == "-") return fallback;

            double v;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
            return fallback;
        }

        private static bool ReadBool(IReadOnlyList<string> cells, Dictionary<string, int> map, string key, bool fallback)
        {
            var s = ReadString(cells, map, key, "");
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (string.Equals(s, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(s, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        private static List<string> SplitCsv(string line)
        {
            // minimal CSV splitter: handles quotes w/o escapes
            var result = new List<string>();
            if (line == null) return result;

            var cur = "";
            var inQ = false;
            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                if (ch == '"')
                {
                    inQ = !inQ;
                    continue;
                }
                if (ch == ',' && !inQ)
                {
                    result.Add(cur);
                    cur = "";
                    continue;
                }
                cur += ch;
            }
            result.Add(cur);
            return result;
        }
    }
}
