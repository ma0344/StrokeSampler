using System;
using System.Globalization;
using System.Threading.Tasks;

using Microsoft.Graphics.Canvas;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;

namespace StrokeSampler
{
    internal static class RadialFalloffBatchGenerator
    {
        internal readonly struct Item
        {
            public Item(double size, double pressure, int n)
            {
                Size = size;
                Pressure = pressure;
                N = n;
            }

            public double Size { get; }
            public double Pressure { get; }
            public int N { get; }
        }

        public static async Task GenerateOneAsync(
            StorageFolder folder,
            CanvasDevice device,
            float centerX,
            float centerY,
            int canvasSize,
            float dpi,
            Item item,
            Func<double, InkDrawingAttributes> createAttributes,
            Func<float, float, float, InkDrawingAttributes, InkStroke> createDot,
            Func<byte[], int, int, double[]> computeRadialMeanAlpha,
            Func<double[], string> buildRadialFalloffCsv)
        {
            if (folder is null)
            {
                throw new ArgumentNullException(nameof(folder));
            }

            if (device is null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            if (createAttributes is null)
            {
                throw new ArgumentNullException(nameof(createAttributes));
            }

            if (createDot is null)
            {
                throw new ArgumentNullException(nameof(createDot));
            }

            if (computeRadialMeanAlpha is null)
            {
                throw new ArgumentNullException(nameof(computeRadialMeanAlpha));
            }

            if (buildRadialFalloffCsv is null)
            {
                throw new ArgumentNullException(nameof(buildRadialFalloffCsv));
            }

            var attributes = createAttributes(item.Size);

            var pngName = $"dot512-material-S{item.Size:0.##}-P{item.Pressure.ToString("0.####", CultureInfo.InvariantCulture)}-N{item.N}.png";
            var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

            using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
            using (var target = new CanvasRenderTarget(device, canvasSize, canvasSize, dpi))
            {
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(Color.FromArgb(0, 0, 0, 0));

                    for (var i = 0; i < item.N; i++)
                    {
                        var dot = createDot(centerX, centerY, (float)item.Pressure, attributes);
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

            var fr = computeRadialMeanAlpha(dotBytes, canvasSize, canvasSize);
            var csv = buildRadialFalloffCsv(fr);

            var csvName = $"radial-falloff-S{item.Size:0.##}-P{item.Pressure.ToString("0.####", CultureInfo.InvariantCulture)}-N{item.N}.csv";
            var csvFile = await folder.CreateFileAsync(csvName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(csvFile, csv, UnicodeEncoding.Utf8);
        }
    }
}
