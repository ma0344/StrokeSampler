using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class ImageAlphaWindowProfile
{
    internal static async Task ExportAlphaWindowProfileCsvAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var path = PickSinglePngPath(window);
        if (string.IsNullOrWhiteSpace(path)) return;

        if (!TryReadRoi(window.AlphaWindowRoiTextBox?.Text, out var roiX, out var roiY, out var roiW, out var roiH))
        {
            ShowInvalidRoi(window);
            return;
        }

        var (periodPx, winWPx, scale, _) = ReadParamsFromUi(window);

        var folder = await PickOutputFolderAsync(window);
        if (folder is null) return;

        var exclude1DipMargin = window.AlphaWindowExclude1DipMarginCheckBox?.IsChecked == true;
        await ExportCoreAsync(folder, path, roiX, roiY, roiW, roiH, periodPx, winWPx, scale, exclude1DipMargin, appendSummary: null);
    }

    internal static async Task ExportAlphaWindowProfileCsvBatchAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var paths = PickMultiplePngPaths(window);
        if (paths is null || paths.Length == 0) return;

        if (!TryReadRoi(window.AlphaWindowRoiTextBox?.Text, out var roiX, out var roiY, out var roiW, out var roiH))
        {
            ShowInvalidRoi(window);
            return;
        }

        var (periodPx, winWPx, defaultScale, periodDipGuess) = ReadParamsFromUi(window);

        var folder = await PickOutputFolderAsync(window);
        if (folder is null) return;

        var summarySb = new StringBuilder(64 * 1024);
        summarySb.AppendLine("file,scale,period_px,period_dip,roi_x,roi_y,roi_w,roi_h,window_w_px,window_w_dip,win_index,win_x,alpha_nonzero_count,alpha_mean,alpha_stddev,alpha_max");

        var exclude1DipMargin = window.AlphaWindowExclude1DipMarginCheckBox?.IsChecked == true;

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            var scale = TryParseScaleFromFilename(path) ?? defaultScale;
            var pp = periodPx;
            if (periodDipGuess > 0)
            {
                pp = (int)Math.Round(periodDipGuess * scale, MidpointRounding.AwayFromZero);
                if (pp <= 0) pp = periodPx;
            }

            var ww = winWPx;
            if (ww <= 0 || ww == periodPx)
            {
                // UIが周期幅と同一の場合は、画像ごとのperiodに追従させる
                ww = pp;
            }

            await ExportCoreAsync(folder, path, roiX, roiY, roiW, roiH, pp, ww, scale, exclude1DipMargin, appendSummary: summarySb);
        }

        var summaryFile = await folder.CreateFileAsync("alpha-window-profile-summary.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(summaryFile, summarySb.ToString());
    }

    private static (int PeriodPx, int WindowWPx, double Scale, double PeriodDipGuess) ReadParamsFromUi(MainWindow window)
    {
        var periodPx = (int)(window.AlphaWindowPeriodNumberBox?.Value ?? 18);
        if (periodPx <= 0) periodPx = 18;

        var winWPx = (int)(window.AlphaWindowWidthNumberBox?.Value ?? periodPx);
        if (winWPx <= 0) winWPx = periodPx;

        var scale = window.AlphaWindowScaleNumberBox?.Value ?? 10.0;
        if (scale <= 0) scale = 10.0;

        var periodDipGuess = window.AlphaWindowPeriodDipGuessNumberBox?.Value ?? 0.0;
        if (periodDipGuess < 0) periodDipGuess = 0;

        return (periodPx, winWPx, scale, periodDipGuess);
    }

    private static string? PickSinglePngPath(MainWindow window)
    {
        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "解析する PNG を選択（ROI window profile）"
        };
        return open.ShowDialog(window) == true ? open.FileName : null;
    }

    private static string[]? PickMultiplePngPaths(MainWindow window)
    {
        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = true,
            Title = "解析する PNG（複数）を選択（ROI window profile）"
        };
        return open.ShowDialog(window) == true ? open.FileNames : null;
    }

    private static void ShowInvalidRoi(MainWindow window)
    {
        System.Windows.MessageBox.Show(window, "ROI x,y,w,h の形式が不正です。例: 100,200,640,480", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
    }

    private static async Task<StorageFolder?> PickOutputFolderAsync(MainWindow window)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add(".csv");
        InitializeWithWindow.Initialize(picker, new System.Windows.Interop.WindowInteropHelper(window).Handle);
        return await picker.PickSingleFolderAsync();
    }

    private static async Task ExportCoreAsync(StorageFolder folder, string path, int roiX, int roiY, int roiW, int roiH, int periodPx, int winWPx, double scale, bool exclude1DipMargin, StringBuilder? appendSummary)
    {
        using var bmp = SKBitmap.Decode(path);
        if (bmp is null) return;

        var imgW = bmp.Width;
        var imgH = bmp.Height;

        if (roiW <= 0) roiW = imgW;
        if (roiH <= 0) roiH = imgH;

        var rx = Clamp(roiX, 0, imgW - 1);
        var ry = Clamp(roiY, 0, imgH - 1);
        var rw = Clamp(roiW, 1, imgW - rx);
        var rh = Clamp(roiH, 1, imgH - ry);

        if (exclude1DipMargin)
        {
            var marginPx = (int)Math.Round(scale, MidpointRounding.AwayFromZero);
            if (marginPx < 0) marginPx = 0;

            // ROIが全体を指している（既定0,0,w,h相当）場合は内側へ寄せる
            // そうでない場合でも、解析対象が余白を含んでいることが多いので内側へクリップする。
            var nx = Clamp(rx + marginPx, 0, imgW);
            var ny = Clamp(ry + marginPx, 0, imgH);
            var nw = Clamp((rx + rw) - marginPx - nx, 1, imgW - nx);
            var nh = Clamp((ry + rh) - marginPx - ny, 1, imgH - ny);
            rx = nx;
            ry = ny;
            rw = nw;
            rh = nh;
        }

        if (periodPx <= 0) periodPx = 18;
        if (winWPx <= 0) winWPx = periodPx;
        if (scale <= 0) scale = 10.0;

        var periodDip = periodPx / scale;
        var winWDip = winWPx / scale;

        var baseName = $"alpha-window-profile-{Path.GetFileNameWithoutExtension(path)}";
        var outFile = await folder.CreateFileAsync($"{baseName}.csv", CreationCollisionOption.ReplaceExisting);

        var sb = new StringBuilder(32 * 1024);
        sb.AppendLine("file,roi_x,roi_y,roi_w,roi_h,scale,period_px,window_w_px,period_dip,window_w_dip,win_index,win_x,alpha_nonzero_count,alpha_mean,alpha_stddev,alpha_max");

        var xEndExclusive = rx + rw;
        var maxIndex = (int)Math.Ceiling(rw / (double)periodPx);
        if (maxIndex < 1) maxIndex = 1;

        var fileName = Path.GetFileName(path);

        for (var i = 0; i < maxIndex; i++)
        {
            var cx = rx + i * periodPx;
            if (cx >= xEndExclusive) break;

            var wx0 = cx;
            var wx1 = Math.Min(xEndExclusive, wx0 + winWPx);
            if (wx1 <= wx0) continue;

            long nonZero = 0;
            long count = 0;
            long sumA = 0;
            long sumA2 = 0;
            byte maxA = 0;

            for (var y = ry; y < ry + rh; y++)
            {
                for (var x = wx0; x < wx1; x++)
                {
                    var a = bmp.GetPixel(x, y).Alpha;
                    if (a != 0) nonZero++;
                    if (a > maxA) maxA = a;
                    sumA += a;
                    sumA2 += (long)a * a;
                    count++;
                }
            }

            var meanA = count > 0 ? (sumA / (double)count) : 0.0;
            var varA = count > 0 ? (sumA2 / (double)count) - (meanA * meanA) : 0.0;
            if (varA < 0) varA = 0;
            var stdA = Math.Sqrt(varA);

            sb.Append(Escape(fileName)).Append(',');
            sb.Append(rx.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(ry.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(rw.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(rh.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(scale.ToString("0.####", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(periodPx.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(winWPx.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(periodDip.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(winWDip.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(wx0.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(nonZero.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append((meanA / 255.0).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append((stdA / 255.0).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(maxA.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            if (appendSummary != null)
            {
                appendSummary.Append(Escape(fileName)).Append(',');
                appendSummary.Append(scale.ToString("0.####", CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(periodPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(periodDip.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(rx.ToString(CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(ry.ToString(CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(rw.ToString(CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(rh.ToString(CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(winWPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(winWDip.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(i.ToString(CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(wx0.ToString(CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(nonZero.ToString(CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append((meanA / 255.0).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append((stdA / 255.0).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                appendSummary.Append(maxA.ToString(CultureInfo.InvariantCulture));
                appendSummary.AppendLine();
            }
        }

        await FileIO.WriteTextAsync(outFile, sb.ToString());
    }

    private static bool TryReadRoi(string? text, out int x, out int y, out int w, out int h)
    {
        x = y = w = h = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split(',');
        if (parts.Length != 4) return false;

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out x))
        {
            if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out x)) return false;
        }
        if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out y))
        {
            if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out y)) return false;
        }
        if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out w))
        {
            if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out w)) return false;
        }
        if (!int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out h))
        {
            if (!int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out h)) return false;
        }

        return true;
    }

    private static int Clamp(int v, int min, int max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }

    private static double? TryParseScaleFromFilename(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(name)) return null;

        var key = "-scale";
        var idx = name.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        idx += key.Length;
        var end = idx;
        while (end < name.Length && (char.IsDigit(name[end]) || name[end] == '.')) end++;
        if (end <= idx) return null;

        var s = name.Substring(idx, end - idx);
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0) return v;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v) && v > 0) return v;
        return null;
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }
}
