using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class ImageAlphaDiff
{
    internal static async Task ExportAlphaDiffAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // WPFなので Win32 ダイアログで2枚選ぶ
        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "実測 canvas PNG を選択"
        };
        if (open.ShowDialog(window) != true) return;
        var canvasPath = open.FileName;

        open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "sim-sourceover PNG を選択"
        };
        if (open.ShowDialog(window) != true) return;
        var simPath = open.FileName;

        using var canvasBmp = SKBitmap.Decode(canvasPath);
        using var simBmp = SKBitmap.Decode(simPath);
        if (canvasBmp == null || simBmp == null) return;
        if (canvasBmp.Width != simBmp.Width || canvasBmp.Height != simBmp.Height) return;

        var w = canvasBmp.Width;
        var h = canvasBmp.Height;

        using var diff = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);

        long sum = 0;
        long sumSq = 0;
        var min = 255;
        var max = 0;
        var hist = new int[256];

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var a0 = canvasBmp.GetPixel(x, y).Alpha;
                var a1 = simBmp.GetPixel(x, y).Alpha;
                var d = Math.Abs(a0 - a1);

                hist[d]++;
                sum += d;
                sumSq += (long)d * d;
                if (d < min) min = d;
                if (d > max) max = d;

                // 差分を1ch画像として保存（ビューア互換性重視）
                diff.SetPixel(x, y, new SKColor((byte)d, (byte)d, (byte)d, 255));
            }
        }

        var count = (long)w * h;
        var mean = count == 0 ? 0.0 : sum / (double)count;
        var variance = count == 0 ? 0.0 : (sumSq / (double)count) - (mean * mean);
        var stddev = variance <= 0 ? 0.0 : Math.Sqrt(variance);
        var unique = hist.Count(v => v != 0);

        var folderPicker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        folderPicker.FileTypeFilter.Add(".png");
        folderPicker.FileTypeFilter.Add(".csv");

        var hwnd = new WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(folderPicker, hwnd);


        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder is null) return;

        var baseName = $"alpha-diff-{Path.GetFileNameWithoutExtension(canvasPath)}-vs-{Path.GetFileNameWithoutExtension(simPath)}";

        // PNG
        var pngFile = await folder.CreateFileAsync($"{baseName}.png", CreationCollisionOption.ReplaceExisting);
        using (var fs = new FileStream(pngFile.Path, FileMode.Create, FileAccess.Write, FileShare.Read))
        {
            using var image = SKImage.FromBitmap(diff);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            data.SaveTo(fs);
            fs.Flush(flushToDisk: true);
        }

        // CSV
        var sb = new StringBuilder(1024);
        sb.AppendLine("width,height,diff_min,diff_max,diff_mean,diff_stddev,diff_unique");
        sb.Append(w.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(h.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(min.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(max.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append((mean / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append((stddev / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(unique.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();

        var csvFile = await folder.CreateFileAsync($"{baseName}.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(csvFile, sb.ToString());
    }
}
