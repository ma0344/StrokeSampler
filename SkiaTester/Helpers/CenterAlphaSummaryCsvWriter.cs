using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SkiaTester.Helpers;

internal static class CenterAlphaSummaryCsvWriter
{
    internal readonly record struct Row(int DiameterPx, double Pressure, int StampCount, double CenterAlpha01);

    internal static void Write(string filePath, IReadOnlyList<Row> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(rows);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine("S,P,N,center_alpha");

        foreach (var row in rows)
        {
            sb.Append(row.DiameterPx.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.Pressure.ToString("0.####", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(row.StampCount.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.AppendLine(row.CenterAlpha01.ToString("0.########", CultureInfo.InvariantCulture));
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
