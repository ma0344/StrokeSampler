using Microsoft.Win32;
using SkiaSharp;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class ImageAlphaShiftPngExporter
{
    internal sealed record Options(int ShiftX, int ShiftY, bool Wrap);

    internal static async Task ExportShiftedPngAsync(MainWindow window, Options options)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(options);

        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "平行移動してPNGを出力する元PNGを選択"
        };
        if (open.ShowDialog(window) != true) return;

        var path = open.FileName;
        if (string.IsNullOrWhiteSpace(path)) return;

        var folder = await PickOutputFolderAsync(window);
        if (folder is null) return;

        var outFile = await ExportCoreAsync(folder, path, options);

        System.Windows.MessageBox.Show(
            window,
            $"Done.\nfile={outFile.Path}",
            "DotLab",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Information);
    }

    private static async Task<StorageFolder?> PickOutputFolderAsync(MainWindow window)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".png");

        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSingleFolderAsync();
    }

    private static async Task<StorageFile> ExportCoreAsync(StorageFolder folder, string path, Options options)
    {
        using var src = SKBitmap.Decode(path);
        if (src is null) throw new InvalidOperationException("PNGのデコードに失敗しました。");

        var w = src.Width;
        var h = src.Height;
        if (w <= 0 || h <= 0) throw new InvalidOperationException("PNGのサイズが不正です。");

        var shiftX = options.ShiftX;
        var shiftY = options.ShiftY;
        var wrap = options.Wrap;

        // 定義: shiftX>0 で内容が右へ、shiftY>0 で内容が下へ動く
        // dest(x,y) = src(x-shiftX, y-shiftY)
        var dst = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        for (var y = 0; y < h; y++)
        {
            var sy = y - shiftY;
            if (wrap)
            {
                sy %= h;
                if (sy < 0) sy += h;
            }

            for (var x = 0; x < w; x++)
            {
                var sx = x - shiftX;
                if (wrap)
                {
                    sx %= w;
                    if (sx < 0) sx += w;
                }

                if (!wrap && ((uint)sx >= (uint)w || (uint)sy >= (uint)h))
                {
                    dst.SetPixel(x, y, SKColors.Transparent);
                    continue;
                }

                var c = src.GetPixel(sx, sy);
                dst.SetPixel(x, y, c);
            }
        }

        var baseName = Path.GetFileNameWithoutExtension(path);
        var tag = string.Format(CultureInfo.InvariantCulture, "shiftX{0}-shiftY{1}-{2}", shiftX, shiftY, wrap ? "wrap" : "nowrap");
        var outName = $"{baseName}-{tag}.png";

        var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
        using var fs = await outFile.OpenStreamForWriteAsync();
        using var image = SKImage.FromBitmap(dst);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        data.SaveTo(fs);

        dst.Dispose();
        return outFile;
    }
}
