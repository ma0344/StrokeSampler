using System;
using System.Collections.Generic;
using System.Globalization;

namespace StrokeSampler
{
    internal class ReadASamplesCSV
    {
        internal static bool TryReadAlphaSamplesFromFalloffCsv(string text, IReadOnlyList<int> rs, out double[] samples)
        {
            samples = Array.Empty<double>();
            if (rs is null || rs.Count == 0)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                return false;
            }

            var map = new Dictionary<int, double>(capacity: Math.Min(lines.Length, rs.Count));
            for (var i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length < 2)
                {
                    continue;
                }

                if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
                {
                    continue;
                }
                if (!double.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                {
                    continue;
                }

                // 必要なrだけ保持
                if (!map.ContainsKey(r))
                {
                    map[r] = a;
                }
            }

            var tmp = new double[rs.Count];
            for (var i = 0; i < rs.Count; i++)
            {
                if (!map.TryGetValue(rs[i], out var v))
                {
                    return false;
                }
                tmp[i] = v;
            }

            samples = tmp;
            return true;
        }
    }
}
