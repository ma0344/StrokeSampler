using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace SkiaTester.Helpers;

public static class CsvWriter
{
    public static void WriteRadialFalloff(string filePath, double[] meanAlphaByR)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(meanAlphaByR);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine("r,mean_alpha");
        for (var r = 0; r < meanAlphaByR.Length; r++)
        {
            sb.Append(r.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.AppendLine(meanAlphaByR[r].ToString("0.########", CultureInfo.InvariantCulture));
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void WriteRadialFalloff(string filePath, double[] meanAlphaByR, double[] stddevAlphaByR)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(meanAlphaByR);
        ArgumentNullException.ThrowIfNull(stddevAlphaByR);
        if (meanAlphaByR.Length != stddevAlphaByR.Length)
        {
            throw new ArgumentException("mean ‚Æ stddev ‚Ì’·‚³‚ªˆê’v‚µ‚Ä‚¢‚Ü‚¹‚ñB");
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine("r,mean_alpha,stddev_alpha");
        for (var r = 0; r < meanAlphaByR.Length; r++)
        {
            sb.Append(r.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(meanAlphaByR[r].ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.AppendLine(stddevAlphaByR[r].ToString("0.########", CultureInfo.InvariantCulture));
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static void WriteNormalizedFalloff(string filePath, double[] meanAlphaByRNorm, double[] stddevAlphaByRNorm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(meanAlphaByRNorm);
        ArgumentNullException.ThrowIfNull(stddevAlphaByRNorm);
        if (meanAlphaByRNorm.Length != stddevAlphaByRNorm.Length)
        {
            throw new ArgumentException("mean ‚Æ stddev ‚Ì’·‚³‚ªˆê’v‚µ‚Ä‚¢‚Ü‚¹‚ñB");
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var sb = new StringBuilder();
        sb.AppendLine("r_norm,mean_alpha,stddev_alpha");
        for (var r = 0; r < meanAlphaByRNorm.Length; r++)
        {
            sb.Append(r.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(meanAlphaByRNorm[r].ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.AppendLine(stddevAlphaByRNorm[r].ToString("0.########", CultureInfo.InvariantCulture));
        }

        File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
