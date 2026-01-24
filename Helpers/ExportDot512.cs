using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using static StrokeSampler.StrokeHelpers;

namespace StrokeSampler.Helpers
{
    internal static class ExportDot512
    {
        internal static async Task ExportDot512Async(MainPage mp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)
        {
            var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);

            var dotSize = UIHelpers.GetDot512SizeOrNull(mp);
            if (dotSize is double s)
            {
                attributes.Size = new Size(s, s);
            }

            var pressure = UIHelpers.GetDot512Pressure(mp);
            var n = UIHelpers.GetDot512Overwrite(mp);

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

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var device = CanvasDevice.GetSharedDevice();
            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
            {
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                    for (var i = 0; i < n; i++)
                    {
                        var dot = CreatePencilDot(cx, cy, pressure, attributes);
                        ds.DrawInk(new[] { dot });
                    }

                    if (includeLabels)
                    {
                        DrawingHelpers.DrawDot512Labels(ds, attributes, pressure, n);
                    }
                }

                await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            }

            await CachedFileManager.CompleteUpdatesAsync(file);
        }

        internal static async Task ExportDot512BatchAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string defaultSuffix)
        {
            var count = UIHelpers.GetDot512BatchCount(mp);
            var prefix = UIHelpers.GetDot512BatchPrefixOrDefault(mp, defaultSuffix);
            var jitter = UIHelpers.GetDot512BatchJitter(mp);

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

            var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
            var dotSize = UIHelpers.GetDot512SizeOrNull(mp);
            if (dotSize is double s)
            {
                attributes.Size = new Size(s, s);
            }

            var pressure = UIHelpers.GetDot512Pressure(mp);
            var n = UIHelpers.GetDot512Overwrite(mp);

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var rng = new Random();

            var device = CanvasDevice.GetSharedDevice();

            for (var i = 1; i <= count; i++)
            {
                var dx = (float)((rng.NextDouble() * 2.0 - 1.0) * jitter);
                var dy = (float)((rng.NextDouble() * 2.0 - 1.0) * jitter);
                var x = cx + dx;
                var y = cy + dy;

                var fileName = $"{prefix}-P{pressure:0.###}-N{n}-i{i:0000}.png";
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                        for (var k = 0; k < n; k++)
                        {
                            var dot = CreatePencilDot(x, y, pressure, attributes);
                            ds.DrawInk(new[] { dot });
                        }

                        if (includeLabels)
                        {
                            DrawingHelpers.DrawDot512Labels(ds, attributes, pressure, n);
                        }
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }
            }
        }

        internal static async Task ExportDot512BatchSizesAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string defaultSuffix)
        {
            var sizes = UIHelpers.GetDot512BatchSizes(mp);
            if (sizes.Count == 0)
            {
                // サイズ指定が無い場合は従来の一括生成にフォールバック
                await ExportDot512BatchAsync(mp, isTransparentBackground, includeLabels, defaultSuffix);
                return;
            }

            var count = UIHelpers.GetDot512BatchCount(mp);
            var prefix = UIHelpers.GetDot512BatchPrefixOrDefault(mp, defaultSuffix);
            var jitter = UIHelpers.GetDot512BatchJitter(mp);

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

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var rng = new Random();
            var device = CanvasDevice.GetSharedDevice();

            foreach (var size in sizes)
            {
                var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
                attributes.Size = new Size(size, size);

                for (var i = 1; i <= count; i++)
                {
                    var dx = (float)((rng.NextDouble() * 2.0 - 1.0) * jitter);
                    var dy = (float)((rng.NextDouble() * 2.0 - 1.0) * jitter);
                    var x = cx + dx;
                    var y = cy + dy;

                    var fileName = $"{prefix}-S{size:0.##}-P{pressure:0.###}-N{n}-i{i:0000}.png";
                    var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                    using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                    using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                    {
                        using (var ds = target.CreateDrawingSession())
                        {
                            ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                            for (var k = 0; k < n; k++)
                            {
                                var dot = CreatePencilDot(x, y, pressure, attributes);
                                ds.DrawInk(new[] { dot });
                            }

                            if (includeLabels)
                            {
                                DrawingHelpers.DrawDot512Labels(ds, attributes, pressure, n);
                            }
                        }

                        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                    }
                }
            }
        }

        internal static async Task ExportDot512SlideAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string defaultSuffix)
        {
            var frames = UIHelpers.GetDot512SlideFrames(mp);
            var step = UIHelpers.GetDot512SlideStep(mp);
            var prefix = UIHelpers.GetDot512BatchPrefixOrDefault(mp, $"slide-{defaultSuffix}");

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

            var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
            var dotSize = UIHelpers.GetDot512SizeOrNull(mp);
            if (dotSize is double s)
            {
                attributes.Size = new Size(s, s);
            }

            var pressure = UIHelpers.GetDot512Pressure(mp);
            var n = UIHelpers.GetDot512Overwrite(mp);

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var device = CanvasDevice.GetSharedDevice();

            for (var i = 0; i < frames; i++)
            {
                var x = cx + (float)(step * i);
                var y = cy;

                var fileName = $"{prefix}-P{pressure:0.###}-N{n}-step{step:0.###}-f{i:0000}.png";
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                        for (var k = 0; k < n; k++)
                        {
                            var dot = CreatePencilDot(x, y, pressure, attributes);
                            ds.DrawInk(new[] { dot });
                        }

                        if (includeLabels)
                        {
                            DrawingHelpers.DrawDot512Labels(ds, attributes, pressure, n);
                        }
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }
            }
        }
    }
}
