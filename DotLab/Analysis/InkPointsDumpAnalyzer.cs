using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class InkPointsDumpAnalyzer
{
    private sealed record InkPointDump(
        [property: JsonPropertyName("x")] double X,
        [property: JsonPropertyName("y")] double Y,
        [property: JsonPropertyName("pressure")] double Pressure,
        [property: JsonPropertyName("tiltX")] double TiltX,
        [property: JsonPropertyName("tiltY")] double TiltY,
        // dump側typo吸収: timestanp
        [property: JsonPropertyName("timestanp")] long? Timestanp,
        [property: JsonPropertyName("timestamp")] long? Timestamp);

    internal static async Task ExportInkPointsDumpStatsCsvAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        folderPicker.FileTypeFilter.Add(".json");

        var hwnd = new WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(folderPicker, hwnd);

        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder is null) return;

        var files = await folder.GetFilesAsync();
        var targets = files
            .Where(f => f.Name.StartsWith("stroke_", StringComparison.OrdinalIgnoreCase)
                        && f.Name.EndsWith("_points.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (targets.Length == 0)
        {
            return;
        }

        var sb = new StringBuilder(32 * 1024);
        sb.AppendLine("file,point_count,dt_mode,dt_mode_ratio,dt_unique,dt_min,dt_max,dt_mean,dd_min,dd_max,dd_mean,dd_zero_ratio,dp_abs_mean,dp_abs_max,dtilt_abs_mean,dtilt_abs_max,short_dt_ratio,short_dt_dd_mean,short_dt_dp_abs_mean,short_dt_dtilt_abs_mean");

        foreach (var file in targets)
        {
            var json = await FileIO.ReadTextAsync(file);
            var points = JsonSerializer.Deserialize<InkPointDump[]>(json);
            if (points is null || points.Length < 2)
            {
                sb.AppendLine($"{Escape(file.Name)},0,,,,,,,,,,,,,,,,,,");
                continue;
            }

            // dtヒストグラム（mode推定用）
            var dtHist = new Dictionary<long, int>();

            long dtMin = long.MaxValue;
            long dtMax = long.MinValue;
            double dtSum = 0;
            double ddMin = double.PositiveInfinity;
            double ddMax = double.NegativeInfinity;
            double ddSum = 0;
            var ddZero = 0;

            double dpAbsSum = 0;
            double dpAbsMax = 0;
            double dtiltAbsSum = 0;
            double dtiltAbsMax = 0;

            var count = 0;
            for (var i = 1; i < points.Length; i++)
            {
                var p0 = points[i - 1];
                var p1 = points[i];

                var t0 = p0.Timestamp ?? p0.Timestanp;
                var t1 = p1.Timestamp ?? p1.Timestanp;
                if (t0 is null || t1 is null) continue;

                var dt = t1.Value - t0.Value;
                if (dt < 0) continue;

                if (dtHist.TryGetValue(dt, out var c0))
                {
                    dtHist[dt] = c0 + 1;
                }
                else
                {
                    dtHist[dt] = 1;
                }

                var dx = p1.X - p0.X;
                var dy = p1.Y - p0.Y;
                var dd = Math.Sqrt(dx * dx + dy * dy);

                var dpAbs = Math.Abs(p1.Pressure - p0.Pressure);
                dpAbsSum += dpAbs;
                if (dpAbs > dpAbsMax) dpAbsMax = dpAbs;

                // tiltはMicrosoft仕様で-90..+90度の平面角度。
                // 絶対的な意味は別として、差分はそのまま「変化量」として扱う。
                var dtiltAbs = Math.Abs(p1.TiltX - p0.TiltX) + Math.Abs(p1.TiltY - p0.TiltY);
                dtiltAbsSum += dtiltAbs;
                if (dtiltAbs > dtiltAbsMax) dtiltAbsMax = dtiltAbs;

                dtMin = Math.Min(dtMin, dt);
                dtMax = Math.Max(dtMax, dt);
                dtSum += dt;

                ddMin = Math.Min(ddMin, dd);
                ddMax = Math.Max(ddMax, dd);
                ddSum += dd;

                if (dd == 0) ddZero++;

                count++;
            }

            if (count == 0)
            {
                sb.AppendLine($"{Escape(file.Name)},0,,,,,,,,,,,,,,,,,,");
                continue;
            }

            var (dtMode, dtModeCount) = FindMode(dtHist);
            var dtUnique = dtHist.Count;
            var dtModeRatio = dtModeCount / (double)count;

            var dtMean = dtSum / count;
            var ddMean = ddSum / count;
            var ddZeroRatio = (double)ddZero / count;

            var dpAbsMean = dpAbsSum / count;
            var dtiltAbsMean = dtiltAbsSum / count;

            // short-dt（割り込み候補）: dt <= dtMode/2（modeが0の場合は無効）
            var shortDtThreshold = dtMode > 0 ? dtMode / 2 : 0;
            var shortDtCount = 0;
            double shortDdSum = 0;
            double shortDpAbsSum = 0;
            double shortDTiltAbsSum = 0;

            if (shortDtThreshold > 0)
            {
                for (var i = 1; i < points.Length; i++)
                {
                    var p0 = points[i - 1];
                    var p1 = points[i];

                    var t0 = p0.Timestamp ?? p0.Timestanp;
                    var t1 = p1.Timestamp ?? p1.Timestanp;
                    if (t0 is null || t1 is null) continue;

                    var dt = t1.Value - t0.Value;
                    if (dt < 0 || dt > shortDtThreshold) continue;

                    var dx = p1.X - p0.X;
                    var dy = p1.Y - p0.Y;
                    var dd = Math.Sqrt(dx * dx + dy * dy);

                    var dpAbs = Math.Abs(p1.Pressure - p0.Pressure);
                    var dtiltAbs = Math.Abs(p1.TiltX - p0.TiltX) + Math.Abs(p1.TiltY - p0.TiltY);

                    shortDtCount++;
                    shortDdSum += dd;
                    shortDpAbsSum += dpAbs;
                    shortDTiltAbsSum += dtiltAbs;
                }
            }

            var shortDtRatio = shortDtCount / (double)count;
            var shortDdMean = shortDtCount == 0 ? 0.0 : shortDdSum / shortDtCount;
            var shortDpAbsMean = shortDtCount == 0 ? 0.0 : shortDpAbsSum / shortDtCount;
            var shortDTiltAbsMean = shortDtCount == 0 ? 0.0 : shortDTiltAbsSum / shortDtCount;

            sb.Append(Escape(file.Name));
            sb.Append(',');
            sb.Append(points.Length.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(dtMode.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(dtModeRatio.ToString("0.######", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(dtUnique.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(dtMin.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(dtMax.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(dtMean.ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(ddMin.ToString("0.######", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(ddMax.ToString("0.######", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(ddMean.ToString("0.######", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(ddZeroRatio.ToString("0.######", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(dpAbsMean.ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(dpAbsMax.ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(dtiltAbsMean.ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(dtiltAbsMax.ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(shortDtRatio.ToString("0.######", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(shortDdMean.ToString("0.######", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(shortDpAbsMean.ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(shortDTiltAbsMean.ToString("0.########", CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        var outFile = await folder.CreateFileAsync(
            $"inkpointsdump-dd-dt-stats-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(outFile, sb.ToString());
    }

    private static (long Mode, int Count) FindMode(Dictionary<long, int> hist)
    {
        long bestKey = 0;
        var bestCount = 0;
        foreach (var kv in hist)
        {
            if (kv.Value > bestCount)
            {
                bestKey = kv.Key;
                bestCount = kv.Value;
            }
        }
        return (bestKey, bestCount);
    }

    private static string Escape(string value)
        => value.Contains(',') ? '"' + value.Replace("\"", "\"\"") + '"' : value;
}
