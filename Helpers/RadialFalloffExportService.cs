using Microsoft.Graphics.Canvas;
using System;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using static StrokeSampler.Helpers;

namespace StrokeSampler
{
    internal static class RadialFalloffExportService
    {
        internal static async Task ExportRadialFalloffBatchAsync(MainPage mp)
        {
            var sizes = UIHelpers.GetRadialFalloffBatchSizes(mp);
            if (sizes.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "距離減衰CSV一括",
                    Content = "Sizes が空です。例: 50,100,150,200",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            folderPicker.FileTypeFilter.Add(".png");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var pressure = UIHelpers.GetDot512Pressure(mp);
            var n = UIHelpers.GetDot512Overwrite(mp);

            var device = CanvasDevice.GetSharedDevice();

            foreach (var size in sizes)
            {
                var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
                attributes.Size = new Size(size, size);

                var cx = (MainPage.Dot512Size - 1) / 2f;
                var cy = (MainPage.Dot512Size - 1) / 2f;

                var pngName = $"dot512-material-S{size:0.##}-P{pressure:0.###}-N{n}.png";
                var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

                using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(Color.FromArgb(0, 0, 0, 0));

                        for (var i = 0; i < n; i++)
                        {
                            var dot = CreatePencilDot(cx, cy, pressure, attributes);
                            ds.DrawInk(new[] { dot });
                        }
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }

                byte[] dotBytes;
                using (var s = await pngFile.OpenAsync(FileAccessMode.Read))
                using (var bmp = await CanvasBitmap.LoadAsync(device, s))
                {
                    dotBytes = bmp.GetPixelBytes();
                }

                var fr = ComputeRadialMeanAlphaD(dotBytes, MainPage.Dot512Size, MainPage.Dot512Size);
                var csv = BuildRadialFalloffCsv(fr);

                var csvName = $"radial-falloff-S{size:0.##}-P{pressure:0.###}-N{n}.csv";
                var csvFile = await folder.CreateFileAsync(csvName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(csvFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }

            var done = new ContentDialog
            {
                Title = "距離減衰CSV一括",
                Content = $"完了: {sizes.Count} サイズ出力しました。",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }
    }
}
