using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class ImageAlphaBounds
{
    internal static async Task ExportAlphaBoundsCsvAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "alpha bounds ‚ð’²‚×‚é PNG ‚ð‘I‘ð"
        };
        if (open.ShowDialog(window) != true) return;
        var path = open.FileName;

        using var bmp = SKBitmap.Decode(path);
        if (bmp is null) return;

        var w = bmp.Width;
        var h = bmp.Height;

        var minX = w;
        var minY = h;
        var maxX = -1;
        var maxY = -1;
        long nonZero = 0;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var a = bmp.GetPixel(x, y).Alpha;
                if (a == 0) continue;

                nonZero++;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        var hasAlpha = nonZero > 0 && maxX >= minX && maxY >= minY;
        var boundsW = hasAlpha ? (maxX - minX + 1) : 0;
        var boundsH = hasAlpha ? (maxY - minY + 1) : 0;

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".csv");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null) return;

        var baseName = $"alpha-bounds-{Path.GetFileNameWithoutExtension(path)}";
        var csvFile = await folder.CreateFileAsync($"{baseName}.csv", CreationCollisionOption.ReplaceExisting);

        var sb = new StringBuilder(512);
        sb.AppendLine("file,width,height,has_alpha,alpha_nonzero_count,alpha_min_x,alpha_min_y,alpha_max_x,alpha_max_y,alpha_bounds_w,alpha_bounds_h");
        sb.Append(Escape(Path.GetFileName(path))).Append(',');
        sb.Append(w.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(h.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(hasAlpha ? "1" : "0").Append(',');
        sb.Append(nonZero.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(hasAlpha ? minX.ToString(CultureInfo.InvariantCulture) : "").Append(',');
        sb.Append(hasAlpha ? minY.ToString(CultureInfo.InvariantCulture) : "").Append(',');
        sb.Append(hasAlpha ? maxX.ToString(CultureInfo.InvariantCulture) : "").Append(',');
        sb.Append(hasAlpha ? maxY.ToString(CultureInfo.InvariantCulture) : "").Append(',');
        sb.Append(boundsW.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(boundsH.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();

        await FileIO.WriteTextAsync(csvFile, sb.ToString());
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }
}
