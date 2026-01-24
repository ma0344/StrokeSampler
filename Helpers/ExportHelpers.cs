using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using static StrokeSampler.StrokeHelpers;
using Windows.Foundation;
using Windows.UI.Input.Inking;
namespace StrokeSampler
{
    internal class ExportHelpers
    { 
         internal static async System.Threading.Tasks.Task ExportS200LineAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)
         {

            var _lastGeneratedAttributes = mp._lastGeneratedAttributes;
            var _lastOverwritePressure = mp._lastOverwritePressure;
            var _lastMaxOverwrite = mp._lastMaxOverwrite;
            var _lastDotGridSpacing = mp._lastDotGridSpacing;
            var _lastWasDotGrid = mp._lastWasDotGrid;


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

             var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
             attributes.Size = new Size(200, 200);
             _lastGeneratedAttributes = attributes;
             _lastOverwritePressure = null;
             _lastMaxOverwrite = null;
             _lastDotGridSpacing = null;
             _lastWasDotGrid = false;

             var pressure = 1.0f;

             const int exportSize = 1024;
             const float x0 = 150f;
             const float x1 = 874f;

             var device = CanvasDevice.GetSharedDevice();
             using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
             using (var target = new CanvasRenderTarget(device, exportSize, exportSize, MainPage.Dot512Dpi))
             {
                 using (var ds = target.CreateDrawingSession())
                 {
                     ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                     // 1024x1024内で横線を引く（中心付近）
                     var y = (exportSize - 1) / 2f;

                     var stroke = CreatePencilStroke(x0, x1, y, pressure, attributes);
                     ds.DrawInk(new[] { stroke });

                     if (includeLabels)
                     {
                         DrawingHelpers.DrawS200LineLabels(mp, ds, attributes, pressure, exportSize, x0, x1);
                     }
                 }

                 await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
             }

             await CachedFileManager.CompleteUpdatesAsync(file);
         }

    }
}
