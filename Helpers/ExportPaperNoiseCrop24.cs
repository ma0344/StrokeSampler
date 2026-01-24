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
                cropped = CropRgba(bytes, w, h, x0, y0, MainPage.PaperNoiseCropSize, MainPage.PaperNoiseCropSize);
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

        internal static byte[] CropRgba(byte[] srcRgba, int srcW, int srcH, int x0, int y0, int cropW, int cropH)
        {
            var dst = new byte[cropW * cropH * 4];

            for (var y = 0; y < cropH; y++)
            {
                var sy = y0 + y;
                for (var x = 0; x < cropW; x++)
                {
                    var sx = x0 + x;

                    var dstIdx = (y * cropW + x) * 4;

                    // 範囲外は透明で埋める（安全側）
                    if ((uint)sx >= (uint)srcW || (uint)sy >= (uint)srcH)
                    {
                        dst[dstIdx + 0] = 0;
                        dst[dstIdx + 1] = 0;
                        dst[dstIdx + 2] = 0;
                        dst[dstIdx + 3] = 0;
                        continue;
                    }

                    var srcIdx = (sy * srcW + sx) * 4;
                    dst[dstIdx + 0] = srcRgba[srcIdx + 0];
                    dst[dstIdx + 1] = srcRgba[srcIdx + 1];
                    dst[dstIdx + 2] = srcRgba[srcIdx + 2];
                    dst[dstIdx + 3] = srcRgba[srcIdx + 3];
                }
            }

            return dst;
        }

    }

}
