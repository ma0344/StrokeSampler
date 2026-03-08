using Microsoft.Win32;
using SkiaSharp;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class ImageAlphaShiftPeriodExporter
{
    internal sealed record Options(
        int ShiftMin,
        int ShiftMax,
        int ShiftStep,
        int SampleStep,
        int MarginPx,
        byte AlphaMin,
        bool Wrap);

    internal static async Task ExportAlphaShiftPeriodCsvAsync(MainWindow window, Options options)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);

        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "周期探索（平行移動の自己一致）を行うPNGを選択"
        };
        if (open.ShowDialog(window) != true) return;

        var path = open.FileName;
        if (string.IsNullOrWhiteSpace(path)) return;

        var folder = await PickOutputFolderAsync(window);
        if (folder is null) return;

        var (outFile, best) = await ExportCoreAsync(folder, path, options);

        var msg = new StringBuilder(256);
        msg.AppendLine("Done.");
        msg.AppendLine($"file={outFile.Path}");
        msg.AppendLine($"X best shift={best.XShift} (mae_byte={best.XMaeByte:0.###})");
        msg.AppendLine($"Y best shift={best.YShift} (mae_byte={best.YMaeByte:0.###})");
        System.Windows.MessageBox.Show(window, msg.ToString(), "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private static async Task<StorageFolder?> PickOutputFolderAsync(MainWindow window)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".csv");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSingleFolderAsync();
    }

    private static async Task<(StorageFile OutFile, BestResult Best)> ExportCoreAsync(StorageFolder folder, string path, Options options)
    {
        using var bmp = SKBitmap.Decode(path);
        if (bmp is null) throw new InvalidOperationException("PNGのデコードに失敗しました。");

        var w = bmp.Width;
        var h = bmp.Height;
        if (w <= 0 || h <= 0) throw new InvalidOperationException("PNGのサイズが不正です。");

        var shiftMin = Math.Max(0, options.ShiftMin);
        var shiftMax = Math.Max(shiftMin, options.ShiftMax);
        var shiftStep = Math.Max(1, options.ShiftStep);

        var sampleStep = Math.Max(1, options.SampleStep);
        var margin = Math.Max(0, options.MarginPx);
        var alphaMin = options.AlphaMin;
        var wrap = options.Wrap;

        // shift が画像サイズを超えると同一点比較になりやすいので抑止する
        shiftMax = Math.Min(shiftMax, Math.Max(0, Math.Max(w, h) - 1));

        var px = bmp.Pixels;
        if (px is null || px.Length != w * h) throw new InvalidOperationException("ピクセル配列の取得に失敗しました。");

        var baseName = $"shift-period-{Path.GetFileNameWithoutExtension(path)}";

        var csv = BuildCsv(px, w, h, shiftMin, shiftMax, shiftStep, sampleStep, margin, alphaMin, wrap, out var best);
        var outFile = await folder.CreateFileAsync($"{baseName}.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(outFile, csv);

        return (outFile, best);
    }

    private sealed record BestResult(int XShift, double XMaeByte, int YShift, double YMaeByte);

    private static string BuildCsv(
        SKColor[] px,
        int w,
        int h,
        int shiftMin,
        int shiftMax,
        int shiftStep,
        int sampleStep,
        int margin,
        byte alphaMin,
        bool wrap,
        out BestResult best)
    {
        var sb = new StringBuilder(capacity: 256 * 1024);
        sb.AppendLine("axis,wrap,shift,tested_px,valid_px,valid_rate,mae,rmse,mae_byte,rmse_byte");

        (int bestShift, double bestMaeByte) bestX = (-1, double.PositiveInfinity);
        (int bestShift, double bestMaeByte) bestY = (-1, double.PositiveInfinity);

        for (var shift = shiftMin; shift <= shiftMax; shift += shiftStep)
        {
            var rX = CompareShiftX(px, w, h, shift, sampleStep, margin, alphaMin, wrap);
            AppendRow(sb, axis: 'x', wrap, shift, rX);
            if (rX.ValidPixels > 0 && rX.MaeByte < bestX.bestMaeByte)
            {
                bestX = (shift, rX.MaeByte);
            }

            var rY = CompareShiftY(px, w, h, shift, sampleStep, margin, alphaMin, wrap);
            AppendRow(sb, axis: 'y', wrap, shift, rY);
            if (rY.ValidPixels > 0 && rY.MaeByte < bestY.bestMaeByte)
            {
                bestY = (shift, rY.MaeByte);
            }
        }

        if (!double.IsFinite(bestX.bestMaeByte)) bestX = (-1, 0.0);
        if (!double.IsFinite(bestY.bestMaeByte)) bestY = (-1, 0.0);

        best = new BestResult(bestX.bestShift, bestX.bestMaeByte, bestY.bestShift, bestY.bestMaeByte);
        return sb.ToString();
    }

    private readonly record struct ShiftCompareResult(long TestedPixels, long ValidPixels, double MaeByte, double RmseByte)
    {
        public double Mae01 => MaeByte / 255.0;
        public double Rmse01 => RmseByte / 255.0;
        public double ValidRate => TestedPixels > 0 ? (ValidPixels / (double)TestedPixels) : 0.0;
    }

    private static void AppendRow(StringBuilder sb, char axis, bool wrap, int shift, ShiftCompareResult r)
    {
        sb.Append(axis);
        sb.Append(',');
        sb.Append(wrap ? '1' : '0');
        sb.Append(',');
        sb.Append(shift.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(r.TestedPixels.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(r.ValidPixels.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(r.ValidRate.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(r.Mae01.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(r.Rmse01.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(r.MaeByte.ToString("0.###", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(r.RmseByte.ToString("0.###", CultureInfo.InvariantCulture));
        sb.AppendLine();
    }

    private static ShiftCompareResult CompareShiftX(SKColor[] px, int w, int h, int shift, int sampleStep, int margin, byte alphaMin, bool wrap)
    {
        if (shift < 0) return default;
        if (!wrap && shift >= w) return default;
        if (wrap && w <= 0) return default;

        var xStart = Math.Clamp(margin, 0, Math.Max(0, w - 1));
        var yStart = Math.Clamp(margin, 0, Math.Max(0, h - 1));
        var yEnd = Math.Clamp(h - margin, 0, h);

        var xEnd = w - margin;
        if (!wrap)
        {
            xEnd = w - margin - shift;
        }
        xEnd = Math.Clamp(xEnd, 0, w);

        if (xEnd <= xStart || yEnd <= yStart) return default;

        long tested = 0;
        long valid = 0;
        double sumAbs = 0;
        double sumSq = 0;

        for (var y = yStart; y < yEnd; y += sampleStep)
        {
            var row = y * w;
            for (var x = xStart; x < xEnd; x += sampleStep)
            {
                tested++;

                var idx0 = row + x;
                var x1 = wrap ? (x + shift) % w : (x + shift);
                var idx1 = row + x1;

                var a0 = px[idx0].Alpha;
                var a1 = px[idx1].Alpha;
                if (a0 < alphaMin || a1 < alphaMin) continue;

                valid++;
                var d = a0 - (int)a1;
                var ad = Math.Abs(d);
                sumAbs += ad;
                sumSq += (double)d * d;
            }
        }

        if (valid <= 0) return new ShiftCompareResult(tested, 0, 0, 0);

        var maeByte = sumAbs / valid;
        var rmseByte = Math.Sqrt(sumSq / valid);
        return new ShiftCompareResult(tested, valid, maeByte, rmseByte);
    }

    private static ShiftCompareResult CompareShiftY(SKColor[] px, int w, int h, int shift, int sampleStep, int margin, byte alphaMin, bool wrap)
    {
        if (shift < 0) return default;
        if (!wrap && shift >= h) return default;
        if (wrap && h <= 0) return default;

        var xStart = Math.Clamp(margin, 0, Math.Max(0, w - 1));
        var xEnd = Math.Clamp(w - margin, 0, w);

        var yStart = Math.Clamp(margin, 0, Math.Max(0, h - 1));
        var yEnd = h - margin;
        if (!wrap)
        {
            yEnd = h - margin - shift;
        }
        yEnd = Math.Clamp(yEnd, 0, h);

        if (xEnd <= xStart || yEnd <= yStart) return default;

        long tested = 0;
        long valid = 0;
        double sumAbs = 0;
        double sumSq = 0;

        for (var y = yStart; y < yEnd; y += sampleStep)
        {
            var row0 = y * w;
            var y1 = wrap ? (y + shift) % h : (y + shift);
            var row1 = y1 * w;

            for (var x = xStart; x < xEnd; x += sampleStep)
            {
                tested++;

                var idx0 = row0 + x;
                var idx1 = row1 + x;

                var a0 = px[idx0].Alpha;
                var a1 = px[idx1].Alpha;
                if (a0 < alphaMin || a1 < alphaMin) continue;

                valid++;
                var d = a0 - (int)a1;
                var ad = Math.Abs(d);
                sumAbs += ad;
                sumSq += (double)d * d;
            }
        }

        if (valid <= 0) return new ShiftCompareResult(tested, 0, 0, 0);

        var maeByte = sumAbs / valid;
        var rmseByte = Math.Sqrt(sumSq / valid);
        return new ShiftCompareResult(tested, valid, maeByte, rmseByte);
    }
}
