using Microsoft.Graphics.Canvas;
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace StrokeSampler
{
    internal static class ExportPaperNoiseCrop24
    {
        internal static async Task ExportAsync(MainPage mp)
        {
            var sourcePicker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            sourcePicker.FileTypeFilter.Add(".png");

            var sourceFile = await sourcePicker.PickSingleFileAsync();
            if (sourceFile is null)
            {
                return;
            }

            var dx = UIHelpers.GetPaperNoiseCropDx(mp);
            var dy = UIHelpers.GetPaperNoiseCropDy(mp);

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"crop24-{sourceFile.DisplayName}-dx{dx}-dy{dy}"
            };
            savePicker.FileTypeChoices.Add("PNG", new[] { ".png" });

            var saveFile = await savePicker.PickSaveFileAsync();
            if (saveFile is null)
            {
                return;
            }

            var device = CanvasDevice.GetSharedDevice();
            byte[] cropped;

            using (var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read))
            using (var src = await CanvasBitmap.LoadAsync(device, sourceStream))
            {
                var w = (int)src.SizeInPixels.Width;
                var h = (int)src.SizeInPixels.Height;
                var bytes = src.GetPixelBytes();

                var cx = (w - 1) / 2;
                var cy = (h - 1) / 2;

                var cropCx = cx + dx;
                var cropCy = cy + dy;

                var x0 = cropCx - MainPage.PaperNoiseCropHalf;
                var y0 = cropCy - MainPage.PaperNoiseCropHalf;
                cropped = StrokeHelpers.CropRgba(bytes, w, h, x0, y0, MainPage.PaperNoiseCropSize, MainPage.PaperNoiseCropSize);
            }

            CachedFileManager.DeferUpdates(saveFile);
            using (var outStream = await saveFile.OpenAsync(FileAccessMode.ReadWrite))
            using (var target = new CanvasRenderTarget(device, MainPage.PaperNoiseCropSize, MainPage.PaperNoiseCropSize, MainPage.Dot512Dpi))
            {
                target.SetPixelBytes(cropped);
                await target.SaveAsync(outStream, CanvasBitmapFileFormat.Png);
            }
            await CachedFileManager.CompleteUpdatesAsync(saveFile);
        }
    }
}
