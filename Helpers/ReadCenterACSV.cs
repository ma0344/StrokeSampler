using System;
using System.Globalization;

namespace StrokeSampler
{
    internal class ReadCenterACSV
    {
        internal static bool TryReadCenterAlphaFromFalloffCsv(string text, out double centerAlpha)
        {
            // 期待形式:
            // r,mean_alpha
            // 0,0.123...
            centerAlpha = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                return false;
            }

            // 2行目がr=0である前提（本ツールの出力は必ず0から開始）
            var cols = lines[1].Split(',');
            if (cols.Length < 2)
            {
                return false;
            }

            if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) || r != 0)
            {
                return false;
            }

            if (!double.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out centerAlpha))
            {
                return false;
            }

            return true;
        }


    }
}
