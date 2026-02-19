using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Foundation;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;

namespace StrokeSampler
{
    internal class ExportS200Service
    {
        internal static async Task ExportAlignedDotIndexSingleAsync(
            MainPage mp,
            StorageFolder folder,
            bool isTransparentBackground,
            float pressure,
            int exportScale,
            int n,
            double periodStepDip,
            double startXDip,
            double startYDip,
            double lDip,
            int outWidthDip,
            int outHeightDip,
            string? runTag)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));
            if (folder is null) throw new ArgumentNullException(nameof(folder));
            if (exportScale <= 0) throw new ArgumentOutOfRangeException(nameof(exportScale));
            if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
            if (periodStepDip <= 0) throw new ArgumentOutOfRangeException(nameof(periodStepDip));
            if (lDip <= 0) throw new ArgumentOutOfRangeException(nameof(lDip));
            if (outWidthDip <= 0 || outHeightDip <= 0) throw new ArgumentOutOfRangeException(nameof(outWidthDip));

            var usePen = mp.S200AlignedUsePenCheckBox?.IsChecked == true;
            var attributes = usePen
                ? StrokeHelpers.CreatePenAttributesForComparison(mp)
                : StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);
            attributes.Size = new Size(200, 200);

            var runTagPart = string.IsNullOrWhiteSpace(runTag) ? "" : $"-{runTag}";

            var shift = (n - 1) * (periodStepDip * exportScale);
            var x0 = startXDip - shift;
            var y0 = startYDip;
            var x1 = x0 + lDip;
            var y1 = y0;

            IReadOnlyList<InkPoint> points = new List<InkPoint>
            {
                new InkPoint(new Point(x0, y0), pressure),
                new InkPoint(new Point(x1, y1), pressure),
            };
            var stroke = StrokeHelpers.CreatePencilStrokeFromInkPoints(points, attributes);

            var device = CanvasDevice.GetSharedDevice();
            var widthPx = checked((int)Math.Ceiling(outWidthDip * (double)exportScale));
            var heightPx = checked((int)Math.Ceiling(outHeightDip * (double)exportScale));

            var pTag = pressure.ToString("0.########", CultureInfo.InvariantCulture);
            var fileName = $"pencil-highres-{widthPx}x{heightPx}-dpi96-S200-P{pTag}-alignedN{n}{runTagPart}-aligned-dot-index-single-scale{exportScale}" + (isTransparentBackground ? "-transparent" : "") + ".png";

            var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.GenerateUniqueName);

            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            using (var target = new CanvasRenderTarget(device, widthPx, heightPx, 96))
            {
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                    ds.Transform = System.Numerics.Matrix3x2.CreateScale(exportScale);
                    ds.DrawInk(new[] { stroke });
                }
                await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            }
        }

        internal static async Task ExportAlignedDotIndexSeriesAsync(
            MainPage mp,
            bool isTransparentBackground,
            float pressure,
            int exportScale,
            int dotCount,
            double periodStepDip,
            double startXDip,
            double startYDip,
            double lDip,
            int outWidthDip,
            int outHeightDip,
            double roiLeftDip,
            double roiTopDip)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));
            if (exportScale <= 0) throw new ArgumentOutOfRangeException(nameof(exportScale));
            if (dotCount <= 0) throw new ArgumentOutOfRangeException(nameof(dotCount));
            if (periodStepDip <= 0) throw new ArgumentOutOfRangeException(nameof(periodStepDip));
            if (lDip <= 0) throw new ArgumentOutOfRangeException(nameof(lDip));
            if (outWidthDip <= 0 || outHeightDip <= 0) throw new ArgumentOutOfRangeException(nameof(outWidthDip));

            var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            folderPicker.FileTypeFilter.Add(".png");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null) return;

            await ExportAlignedDotIndexSeriesAsync(
                mp,
                folder,
                isTransparentBackground,
                pressure,
                exportScale,
                dotCount,
                periodStepDip,
                startXDip,
                startYDip,
                lDip,
                outWidthDip,
                outHeightDip,
                roiLeftDip,
                roiTopDip,
                runTag: null);
        }

        internal static async Task ExportAlignedDotIndexSeriesAsync(
            MainPage mp,
            bool isTransparentBackground,
            float pressure,
            int exportScale,
            int dotCount,
            double periodStepDip,
            double startXDip,
            double startYDip,
            double lDip,
            int outWidthDip,
            int outHeightDip,
            double roiLeftDip,
            double roiTopDip,
            string? runTag)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));
            if (exportScale <= 0) throw new ArgumentOutOfRangeException(nameof(exportScale));
            if (dotCount <= 0) throw new ArgumentOutOfRangeException(nameof(dotCount));
            if (periodStepDip <= 0) throw new ArgumentOutOfRangeException(nameof(periodStepDip));
            if (lDip <= 0) throw new ArgumentOutOfRangeException(nameof(lDip));
            if (outWidthDip <= 0 || outHeightDip <= 0) throw new ArgumentOutOfRangeException(nameof(outWidthDip));

            var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            folderPicker.FileTypeFilter.Add(".png");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null) return;

            await ExportAlignedDotIndexSeriesAsync(
                mp,
                folder,
                isTransparentBackground,
                pressure,
                exportScale,
                dotCount,
                periodStepDip,
                startXDip,
                startYDip,
                lDip,
                outWidthDip,
                outHeightDip,
                roiLeftDip,
                roiTopDip,
                runTag);
        }

        internal static async Task ExportAlignedDotIndexSeriesAsync(
            MainPage mp,
            StorageFolder folder,
            bool isTransparentBackground,
            float pressure,
            int exportScale,
            int dotCount,
            double periodStepDip,
            double startXDip,
            double startYDip,
            double lDip,
            int outWidthDip,
            int outHeightDip,
            double roiLeftDip,
            double roiTopDip,
            string? runTag)
        {
            if (folder is null) throw new ArgumentNullException(nameof(folder));

            var usePen = mp.S200AlignedUsePenCheckBox?.IsChecked == true;
            var attributes = usePen
                ? StrokeHelpers.CreatePenAttributesForComparison(mp)
                : StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);
            attributes.Size = new Size(200, 200);

            var ctxBase = new ExportHighResInk.ExportContext(200, pressure, 1, exportScale, tag: "aligned-dot-index");

            // n番目の更新点が同一点（startX/startY）に来るように、開始点を左にずらして線を引く。
            // 1pxマージン（DIP）を維持するために、開始点は r+1 の利用を想定。
            var runTagPart = string.IsNullOrWhiteSpace(runTag) ? "" : $"-{runTag}";

            for (var n = 1; n <= dotCount; n++)
            {
                // 内部の更新点間隔がpx基準の量子化を含むため、HiRes出力scale分を掛けたシフト量で揃える。
                // periodStepDip は「1ステップのDIP量」。実際の入力座標はpx量子化の影響を受けるため、scaleを掛ける。
                var shift = (n - 1) * (periodStepDip * exportScale);
                var x0 = startXDip - shift;
                var y0 = startYDip;
                var x1 = x0 + lDip;
                var y1 = y0;

                // pointCount=2 の場合、CreateLineInkPointsFixed は start + step にしかならず end(x1)を保証しないため
                // ここでは明示的に2点を与える（サブピクセル含むDIP座標を維持）。
                IReadOnlyList<InkPoint> points = new List<InkPoint>
                {
                    new InkPoint(new Point(x0, y0), pressure),
                    new InkPoint(new Point(x1, y1), pressure),
                };
                var stroke = StrokeHelpers.CreatePencilStrokeFromInkPoints(points, attributes);

                var strokes = new[] { stroke };
                var ctx = new ExportHighResInk.ExportContext(200, pressure, n, exportScale, tag: "aligned");

                // 画像サイズはDIPで固定し、exportScaleでHiRes化する。
                var device = CanvasDevice.GetSharedDevice();
                var widthPx = checked((int)Math.Ceiling(outWidthDip * (double)exportScale));
                var heightPx = checked((int)Math.Ceiling(outHeightDip * (double)exportScale));

                var pTag = pressure.ToString("0.########", CultureInfo.InvariantCulture);
                var fileName = $"pencil-highres-{widthPx}x{heightPx}-dpi96-S200-P{pTag}-alignedN{n}{runTagPart}-scale{exportScale}" + (isTransparentBackground ? "-transparent" : "") + ".png";

                StorageFile file;
                try
                {
                    file = await folder.CreateFileAsync(fileName, CreationCollisionOption.FailIfExists);
                }
                catch
                {
                    var baseName = System.IO.Path.GetFileNameWithoutExtension(fileName);
                    var ext = System.IO.Path.GetExtension(fileName);
                    var i = 1;
                    while (true)
                    {
                        var alt = $"{baseName}-dup{i}{ext}";
                        try
                        {
                            file = await folder.CreateFileAsync(alt, CreationCollisionOption.FailIfExists);
                            break;
                        }
                        catch
                        {
                            i++;
                            if (i > 10000) throw;
                        }
                    }
                }

                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, widthPx, heightPx, 96))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                        // ユーザー指定の座標系（Start(X,Y)）をそのまま維持してHiRes化する。
                        // roiLeft/roiTop は将来的なROI切り出し用に残すが、ここでは適用しない。
                        ds.Transform = System.Numerics.Matrix3x2.CreateScale(exportScale);
                        ds.DrawInk(strokes);
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }

                _ = ctx;
                _ = ctxBase;
            }
        }

        internal static async Task ExportAlignedDotIndexSeriesRepeatedAsync(
            MainPage mp,
            StorageFolder folder,
            bool isTransparentBackground,
            float pressure,
            int exportScale,
            int dotCount,
            int repeat,
            double periodStepDip,
            double startXDip,
            double startYDip,
            double lDip,
            int outWidthDip,
            int outHeightDip,
            double roiLeftDip,
            double roiTopDip)
        {
            await ExportAlignedDotIndexSeriesRepeatedAsync(
                mp,
                folder,
                isTransparentBackground,
                pressure,
                exportScale,
                dotCount,
                repeat,
                periodStepDip,
                startXDip,
                startYDip,
                lDip,
                outWidthDip,
                outHeightDip,
                roiLeftDip,
                roiTopDip,
                runTag: null);
        }

        internal static async Task ExportAlignedDotIndexSeriesRepeatedAsync(
            MainPage mp,
            StorageFolder folder,
            bool isTransparentBackground,
            float pressure,
            int exportScale,
            int dotCount,
            int repeat,
            double periodStepDip,
            double startXDip,
            double startYDip,
            double lDip,
            int outWidthDip,
            int outHeightDip,
            double roiLeftDip,
            double roiTopDip,
            string? runTag)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));
            if (folder is null) throw new ArgumentNullException(nameof(folder));
            if (exportScale <= 0) throw new ArgumentOutOfRangeException(nameof(exportScale));
            if (dotCount <= 0) throw new ArgumentOutOfRangeException(nameof(dotCount));
            if (repeat <= 0) throw new ArgumentOutOfRangeException(nameof(repeat));
            if (periodStepDip <= 0) throw new ArgumentOutOfRangeException(nameof(periodStepDip));
            if (lDip <= 0) throw new ArgumentOutOfRangeException(nameof(lDip));
            if (outWidthDip <= 0 || outHeightDip <= 0) throw new ArgumentOutOfRangeException(nameof(outWidthDip));

            var usePen = mp.S200AlignedUsePenCheckBox?.IsChecked == true;
            var attributes = usePen
                ? StrokeHelpers.CreatePenAttributesForComparison(mp)
                : StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);
            attributes.Size = new Size(200, 200);

            var device = CanvasDevice.GetSharedDevice();
            var widthPx = checked((int)Math.Ceiling(outWidthDip * (double)exportScale));
            var heightPx = checked((int)Math.Ceiling(outHeightDip * (double)exportScale));

            var runTagPart = string.IsNullOrWhiteSpace(runTag) ? "" : $"-{runTag}";

            for (var n = 1; n <= dotCount; n++)
            {
                var shift = (n - 1) * (periodStepDip * exportScale);
                var x0 = startXDip - shift;
                var y0 = startYDip;
                var x1 = x0 + lDip;
                var y1 = y0;

                IReadOnlyList<InkPoint> points = new List<InkPoint>
                {
                    new InkPoint(new Point(x0, y0), pressure),
                    new InkPoint(new Point(x1, y1), pressure),
                };
                var stroke = StrokeHelpers.CreatePencilStrokeFromInkPoints(points, attributes);

                var pTag = pressure.ToString("0.########", CultureInfo.InvariantCulture);
                var fileName = $"pencil-highres-{widthPx}x{heightPx}-dpi96-S200-P{pTag}-alignedN{n}{runTagPart}-scale{exportScale}-repeat{repeat}" + (isTransparentBackground ? "-transparent" : "") + ".png";
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, widthPx, heightPx, 96))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                        ds.Transform = System.Numerics.Matrix3x2.CreateScale(exportScale);

                        for (var r = 0; r < repeat; r++)
                        {
                            ds.DrawInk(new[] { stroke });
                        }
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }

                _ = roiLeftDip;
                _ = roiTopDip;
            }
        }

        internal static async System.Threading.Tasks.Task ExportAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)
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

            var attributes =  StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);
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

                    var stroke = StrokeHelpers.CreatePencilStroke(x0, x1, y, pressure, attributes);
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
