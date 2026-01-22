using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace StrokeSampler
{
    internal static class ExportEstimatedPaperNoise
    {
        internal static async Task ExportAsync(MainPage mp)
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

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"paper-noise-estimated-{sourceFile.DisplayName}"
            };
            savePicker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

            var saveFile = await savePicker.PickSaveFileAsync();
            if (saveFile is null)
            {
                return;
            }

            var device = CanvasDevice.GetSharedDevice();

            using (var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read))
            using (var bitmap = await CanvasBitmap.LoadAsync(device, sourceStream))
            {
                var w = (int)bitmap.SizeInPixels.Width;
                var h = (int)bitmap.SizeInPixels.Height;
                var bytes = bitmap.GetPixelBytes();

                var cx = (w - 1) / 2.0;
                var cy = (h - 1) / 2.0;

                var maxR = Math.Sqrt(cx * cx + cy * cy);
                var bins = (int)Math.Floor(maxR) + 1;

                // F(r): 半径方向の平均アルファ（0..1）を推定する
                var sumAlpha = new double[bins];
                var count = new int[bins];

                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var dx = x - cx;
                        var dy = y - cy;
                        var r = Math.Sqrt((dx * dx) + (dy * dy));
                        var bin = (int)Math.Floor(r);
                        if ((uint)bin >= (uint)bins)
                        {
                            continue;
                        }

                        var idx = (y * w + x) * 4;
                        var a = bytes[idx + 3] / 255.0;
                        sumAlpha[bin] += a;
                        count[bin]++;
                    }
                }

                var fr = new double[bins];
                for (var i = 0; i < bins; i++)
                {
                    var m = count[i] > 0 ? (sumAlpha[i] / count[i]) : 0.0;
                    fr[i] = m;
                }

                // 紙目推定: noise = alpha / F(r)
                // 中心付近と外縁は不安定になりやすいので除外して正規化する
                const int rMin = 2;
                var rMax = Math.Max(rMin + 1, bins - 2);
                const double eps = 1e-6;

                var noise = new double[w * h];
                double minN = double.PositiveInfinity;
                double maxN = double.NegativeInfinity;

                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var dx = x - cx;
                        var dy = y - cy;
                        var r = Math.Sqrt((dx * dx) + (dy * dy));
                        if (r < rMin || r >= rMax)
                        {
                            continue;
                        }

                        var bin = (int)Math.Floor(r);
                        if ((uint)bin >= (uint)bins)
                        {
                            continue;
                        }

                        var idx = (y * w + x) * 4;
                        var a = bytes[idx + 3] / 255.0;
                        var den = Math.Max(eps, fr[bin]);
                        var n = a / den;
                        noise[y * w + x] = n;

                        if (n < minN) minN = n;
                        if (n > maxN) maxN = n;
                    }
                }

                if (double.IsNaN(minN) || double.IsInfinity(minN)
                    || double.IsNaN(maxN) || double.IsInfinity(maxN)
                    || Math.Abs(maxN - minN) < eps)
                {
                    minN = 0.0;
                    maxN = 1.0;
                }

                var outBytes = new byte[w * h * 4];

                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var n = noise[y * w + x];
                        var t = (n - minN) / (maxN - minN);
                        t = Math.Clamp(t, 0.0, 1.0);
                        var g = (byte)Math.Round(t * 255.0);

                        var outIdx = (y * w + x) * 4;
                        outBytes[outIdx + 0] = g; // B
                        outBytes[outIdx + 1] = g; // G
                        outBytes[outIdx + 2] = g; // R
                        outBytes[outIdx + 3] = 255;
                    }
                }

                CachedFileManager.DeferUpdates(saveFile);
                using (var outStream = await saveFile.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, w, h, MainPage.Dot512Dpi))
                {
                    target.SetPixelBytes(outBytes);
                    await target.SaveAsync(outStream, CanvasBitmapFileFormat.Png);
                }
                await CachedFileManager.CompleteUpdatesAsync(saveFile);
            }
        }
    }
}
