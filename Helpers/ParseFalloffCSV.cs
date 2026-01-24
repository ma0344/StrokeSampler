using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrokeSampler
{
    internal class ParseFalloffCSV
    {
        internal static bool TryParseFalloffCsv(string text, out double[] fr)
        {
            // r,mean_alpha
            fr = Array.Empty<double>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                return false;
            }

            var list = new List<double>(lines.Length);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var cols = line.Split(',');
                if (cols.Length < 2)
                {
                    continue;
                }

                if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                if (!double.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    continue;
                }

                list.Add(v);
            }

            if (list.Count == 0)
            {
                return false;
            }

            fr = list.ToArray();
            return true;
        }

    }
}
