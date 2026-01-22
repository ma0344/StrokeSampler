using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using static StrokeSampler.StrokeHelpers;

namespace StrokeSampler
{
    internal static class RadialFalloffExportService
    {
        internal static async Task ExportRadialFalloffBatchPsSizesNsAsync(MainPage mp)
        {
            var ps = UIHelpers.GetRadialFalloffBatchPs(mp);
            var sizes = UIHelpers.GetRadialFalloffBatchSizes(mp);
            var ns = UIHelpers.GetRadialFalloffBatchNs(mp);

            if (ps.Count == 0 || sizes.Count == 0 || ns.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "距離減衰CSV一括(P×S×N)",
                    Content = "P一覧 / Sizes / N一覧 のいずれかが空です。例: P=0.05,0.1,...  Sizes=5,12,...  N=1,2,...",
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

            var device = CanvasDevice.GetSharedDevice();

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var total = ps.Count * sizes.Count * ns.Count;
            var doneCount = 0;

            foreach (var p in ps)
            {
                foreach (var size in sizes)
                {
                    var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
                    attributes.Size = new Size(size, size);

                    foreach (var n in ns)
                    {
                        var pngName = $"dot512-material-S{size:0.##}-P{p:0.####}-N{n}.png";
                        var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

                        using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
                        using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                        {
                            using (var ds = target.CreateDrawingSession())
                            {
                                ds.Clear(Color.FromArgb(0, 0, 0, 0));

                                for (var i = 0; i < n; i++)
                                {
                                    var dot = CreatePencilDot(cx, cy, p, attributes);
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
                        var csvName = $"radial-falloff-S{size:0.##}-P{p:0.####}-N{n}.csv";
                        var csvFile = await folder.CreateFileAsync(csvName, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(csvFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);

                        doneCount++;
                    }
                }
            }

            var done = new ContentDialog
            {
                Title = "距離減衰CSV一括(P×S×N)",
                Content = $"完了: {doneCount}/{total} 個出力しました。",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }

        internal static async Task ExportRadialFalloffBatchSizesNsAsync(MainPage mp)
        {
            var ps = UIHelpers.GetRadialFalloffBatchPs(mp);
            var sizes = UIHelpers.GetRadialFalloffBatchSizes(mp);
            var ns = UIHelpers.GetRadialFalloffBatchNs(mp);

            if (ps.Count == 0 || sizes.Count == 0 || ns.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "距離減衰CSV一括",
                    Content = "P一覧 / Sizes / N一覧 のいずれかが空です。例: P=0.05,0.1,...  Sizes=5,12,...  N=1,2,...",
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

            var device = CanvasDevice.GetSharedDevice();

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var total = ps.Count * sizes.Count * ns.Count;
            var doneCount = 0;

            foreach (var p in ps)
            {
                foreach (var size in sizes)
                {
                    var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
                    attributes.Size = new Size(size, size);

                    foreach (var n in ns)
                    {
                        var pngName = $"dot512-material-S{size:0.##}-P{p:0.####}-N{n}.png";
                        var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

                        using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
                        using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                        {
                            using (var ds = target.CreateDrawingSession())
                            {
                                ds.Clear(Color.FromArgb(0, 0, 0, 0));

                                for (var i = 0; i < n; i++)
                                {
                                    var dot = CreatePencilDot(cx, cy, p, attributes);
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
                        var csvName = $"radial-falloff-S{size:0.##}-P{p:0.####}-N{n}.csv";
                        var csvFile = await folder.CreateFileAsync(csvName, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(csvFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);

                        doneCount++;
                    }
                }
            }

            var done = new ContentDialog
            {
                Title = "距離減衰CSV一括",
                Content = $"完了: {doneCount}/{total} 個出力しました。",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }

        internal static async Task ExportRadialAlphaCsvAsync(MainPage mp)
        {
            var sourcePicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            sourcePicker.FileTypeFilter.Add(".png");

            var sourceFile = await sourcePicker.PickSingleFileAsync();
            if (sourceFile is null)
            {
                return;
            }

            var binSize = UIHelpers.GetRadialBinSize(mp);

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"radial-alpha-{sourceFile.DisplayName}"
            };
            savePicker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });

            var saveFile = await savePicker.PickSaveFileAsync();
            if (saveFile is null)
            {
                return;
            }

            var device = CanvasDevice.GetSharedDevice();

            using (var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read))
            using (var bitmap = await CanvasBitmap.LoadAsync(device, sourceStream))
            {
                var bytes = bitmap.GetPixelBytes();
                var width = (int)bitmap.SizeInPixels.Width;
                var height = (int)bitmap.SizeInPixels.Height;

                var analysis = RadialAlphaBinAnalyzer.Analyze(
                    bytes,
                    width,
                    height,
                    binSize,
                    MainPage.RadialAlphaThresholds);

                var csv = RadialAlphaCsvBuilder.Build(
                    analysis.Bins,
                    binSize,
                    MainPage.RadialAlphaThresholds,
                    analysis.Total,
                    analysis.SumAlpha,
                    analysis.Hits);

                await FileIO.WriteTextAsync(saveFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }
        }

        internal static async Task ExportRadialFalloffCsvAsync(MainPage mp)
        {
            var sourcePicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            sourcePicker.FileTypeFilter.Add(".png");

            var sourceFile = await sourcePicker.PickSingleFileAsync();
            if (sourceFile is null)
            {
                return;
            }

            var binSize = UIHelpers.GetRadialBinSize(mp);

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"radial-alpha-{sourceFile.DisplayName}"
            };
            savePicker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });

            var saveFile = await savePicker.PickSaveFileAsync();
            if (saveFile is null)
            {
                return;
            }

            var device = CanvasDevice.GetSharedDevice();

            using (var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read))
            using (var bitmap = await CanvasBitmap.LoadAsync(device, sourceStream))
            {
                var bytes = bitmap.GetPixelBytes();
                var width = (int)bitmap.SizeInPixels.Width;
                var height = (int)bitmap.SizeInPixels.Height;

                var analysis = RadialAlphaBinAnalyzer.Analyze(
                    bytes,
                    width,
                    height,
                    binSize,
                    MainPage.RadialAlphaThresholds);

                var csv = RadialAlphaCsvBuilder.Build(
                    analysis.Bins,
                    binSize,
                    MainPage.RadialAlphaThresholds,
                    analysis.Total,
                    analysis.SumAlpha,
                    analysis.Hits);

                await FileIO.WriteTextAsync(saveFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }
        }

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
