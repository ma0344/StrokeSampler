using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml.Controls;

namespace StrokeSampler
{
    internal static class ExportPngService
    {
        internal static async Task ExportAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)
        {
            var width = UIHelpers.GetExportWidth(mp);
            var height = UIHelpers.GetExportHeight(mp);

            var strokes = mp.InkCanvasControl.InkPresenter.StrokeContainer.GetStrokes();
            if (strokes.Count == 0)
            {
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = suggestedFileName
            };
            picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            CachedFileManager.DeferUpdates(file);

            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var device = CanvasDevice.GetSharedDevice();

                using (var target = new CanvasRenderTarget(device, width, height, 96f))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                        ds.DrawInk(strokes);

                        if (includeLabels)
                        {
                            DrawingHelpers.DrawPreviewLabels(mp, ds);
                        }
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }
            }

            await CachedFileManager.CompleteUpdatesAsync(file);
        }
    }
}
