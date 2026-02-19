using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml.Controls;
using static StrokeSampler.StrokeHelpers;

namespace StrokeSampler
{
    internal class TestMethods
    {
        private enum AlphaCompositeMode
        {
            SourceOver,
            Add,
            Max,
        }

        // (removed) duplicate point parser and duplicate DrawLine/Hold fixed helpers

        internal static async Task ExportHiResSimulatedCompositeAsync(MainPage mp, bool transparentBackground, bool cropToBounds)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));

            int scale;
            if (!int.TryParse(mp.ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out scale))
            {
                if (!int.TryParse(mp.ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out scale)) return;
            }

            double dpiD;
            if (!double.TryParse(mp.ExportDpiTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out dpiD))
            {
                if (!double.TryParse(mp.ExportDpiTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out dpiD)) return;
            }

            if (scale <= 0) return;
            if (dpiD <= 0) return;

            var n = UIHelpers.GetDot512Overwrite(mp);
            if (n < 1) n = 1;
            if (n > 10000) n = 10000;

            var pForName = (double)UIHelpers.GetDot512Pressure(mp);
            var pTag = $"p{pForName.ToString("0.####", CultureInfo.InvariantCulture)}";

            var strokes = mp.InkCanvasControl.InkPresenter.StrokeContainer.GetStrokes();
            if (strokes is null || strokes.Count == 0) return;

            var last = strokes[strokes.Count - 1];
            if (last is null) return;

            // laststroke（1回分）をHiResレンダしてα(BGRA8)を取得
            var (stampBgra8, width, height) = await RenderHiResBgra8Async(mp, scale, (float)dpiD, transparentBackground, cropToBounds, new List<InkStroke>(1) { last });
            if (stampBgra8 == null || stampBgra8.Length == 0) return;

            // 保存ダイアログの回数削減のため、最初に保存先フォルダを選んで一括保存する。
            // （キャンセルされた場合は何もしない）
            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            folderPicker.FileTypeFilter.Add(".png");
            folderPicker.FileTypeFilter.Add(".csv");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder == null) return;

            foreach (var mode in new[] { AlphaCompositeMode.SourceOver, AlphaCompositeMode.Add, AlphaCompositeMode.Max })
            {
                var outBgra8 = SimulateCompositeAlpha(stampBgra8, width, height, n, mode);

                // PNG
                await SaveBgra8AsPngToFolderAsync(folder, outBgra8, width, height, (float)dpiD, tag: $"sim-{mode.ToString().ToLowerInvariant()}-n{n}-{pTag}");

                // pre-save α統計CSV
                await SaveAlphaStatsCsvToFolderAsync(folder, outBgra8, width, height, tag: $"sim-{mode.ToString().ToLowerInvariant()}-n{n}-{pTag}");
            }
        }

        private static async Task<(byte[] Bytes, int Width, int Height)> RenderHiResBgra8Async(MainPage mp, int scale, float dpi, bool transparentBackground, bool cropToBounds, IReadOnlyList<InkStroke> strokes)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));
            if (strokes is null) throw new ArgumentNullException(nameof(strokes));

            var baseWidth = UIHelpers.GetExportWidth(mp);
            var baseHeight = UIHelpers.GetExportHeight(mp);

            if (strokes.Count == 0) return (Array.Empty<byte>(), 0, 0);

            Windows.Foundation.Rect bounds;
            if (cropToBounds)
            {
                if (!StrokeHelpers.TryGetStrokesBoundingRect(strokes, out bounds))
                {
                    return (Array.Empty<byte>(), 0, 0);
                }
                if (bounds.X < 0) bounds.X = 0;
                if (bounds.Y < 0) bounds.Y = 0;
                if (bounds.Width <= 0 || bounds.Height <= 0) return (Array.Empty<byte>(), 0, 0);
            }
            else
            {
                bounds = new Windows.Foundation.Rect(0, 0, baseWidth, baseHeight);
            }

            var width = (int)Math.Ceiling(bounds.Width * scale);
            var height = (int)Math.Ceiling(bounds.Height * scale);
            if (width <= 0 || height <= 0) return (Array.Empty<byte>(), 0, 0);

            var device = CanvasDevice.GetSharedDevice();
            using (var target = new CanvasRenderTarget(device, width, height, dpi))
            {
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(transparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                    var translate = Matrix3x2.CreateTranslation((float)-bounds.X, (float)-bounds.Y);
                    var scaleM = Matrix3x2.CreateScale(scale);
                    ds.Transform = translate * scaleM;
                    ds.DrawInk(strokes);
                }
                return (target.GetPixelBytes(), width, height);
            }
        }

        private static byte[] SimulateCompositeAlpha(byte[] stampBgra8, int width, int height, int n, AlphaCompositeMode mode)
        {
            var expected = checked(width * height * 4);
            if (stampBgra8.Length < expected) throw new ArgumentException("スタンプBGRA8の長さが不足しています。", nameof(stampBgra8));

            // RGBは解析用途なので0固定。αだけを合成する。
            // N回の「重ね塗り」は、各view（各画素）で同じstampのαがN回入ることを意味する。
            var outBgra8 = new byte[expected];

            // まず初期化（A=0）
            for (var i = 0; i < expected; i += 4)
            {
                outBgra8[i + 0] = 0;
                outBgra8[i + 1] = 0;
                outBgra8[i + 2] = 0;
                outBgra8[i + 3] = 0;
            }

            // 各回の合成を画素ごとに適用
            for (var k = 0; k < n; k++)
            {
                for (var i = 0; i < expected; i += 4)
                {
                    var sA = stampBgra8[i + 3];
                    var a = outBgra8[i + 3];

                    var next = mode switch
                    {
                        AlphaCompositeMode.SourceOver => a + (sA * (255 - a) + 127) / 255,
                        AlphaCompositeMode.Add => Math.Min(255, a + sA),
                        AlphaCompositeMode.Max => Math.Max(a, sA),
                        _ => a,
                    };

                    outBgra8[i + 3] = (byte)next;
                }
            }

            return outBgra8;
        }

        private static async Task SaveBgra8AsPngToFolderAsync(StorageFolder folder, byte[] bgra8, int width, int height, float dpi, string tag)
        {
            if (folder is null) throw new ArgumentNullException(nameof(folder));
            if (bgra8 is null) throw new ArgumentNullException(nameof(bgra8));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            var file = await folder.CreateFileAsync(
                $"pencil-highres-sim-{width}x{height}-dpi{dpi.ToString("0.##", CultureInfo.InvariantCulture)}-{tag}.png",
                CreationCollisionOption.ReplaceExisting);

            using (var stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var device = CanvasDevice.GetSharedDevice();
                using (var bitmap = CanvasBitmap.CreateFromBytes(device, bgra8, width, height, Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized))
                {
                    await bitmap.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }
            }
        }

        private static async Task SaveAlphaStatsCsvToFolderAsync(StorageFolder folder, byte[] bgra8, int width, int height, string tag)
        {
            if (folder is null) throw new ArgumentNullException(nameof(folder));

            var file = await folder.CreateFileAsync($"pencil-highres-sim-pre-save-alpha-{width}x{height}-{tag}.csv", CreationCollisionOption.ReplaceExisting);
            await WriteAlphaStatsCsvAsync(file, bgra8, width, height);
        }
        internal static async Task WriteAlphaStatsCsvAsync(StorageFile file, byte[] bgra8, int width, int height)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            if (bgra8 == null) throw new ArgumentNullException(nameof(bgra8));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            // BGRA8前提。strideはwidth*4。
            var expected = checked(width * height * 4);
            if (bgra8.Length < expected) throw new ArgumentException("BGRA8バッファ長が不足しています。", nameof(bgra8));

            var hist = new int[256];
            long sum = 0;
            long count = 0;
            var min = 255;
            var max = 0;

            for (var i = 0; i < expected; i += 4)
            {
                var a = bgra8[i + 3];
                hist[a]++;
                sum += a;
                count++;
                if (a < min) min = a;
                if (a > max) max = a;
            }

            var mean = count == 0 ? 0.0 : sum / (double)count;

            double varSum = 0;
            for (var a = 0; a < 256; a++)
            {
                var c = hist[a];
                if (c == 0) continue;
                var d = a - mean;
                varSum += d * d * c;
            }
            var stddev = count == 0 ? 0.0 : Math.Sqrt(varSum / count);

            var unique = 0;
            for (var a = 0; a < 256; a++)
            {
                if (hist[a] != 0) unique++;
            }

            var sb = new StringBuilder(1024);
            sb.AppendLine("width,height,pixel_format,alpha_min,alpha_max,alpha_mean,alpha_stddev,alpha_unique");
            sb.Append(width.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(height.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append("BGRA8");
            sb.Append(',');
            sb.Append(min.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(max.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append((mean / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append((stddev / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(unique.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            await FileIO.WriteTextAsync(file, sb.ToString());
        }
        internal static async Task ExportHiResLastStrokeAsync(MainPage mp, bool transparentBackground, bool cropToBounds)
        {
            if (mp is null)
            {
                throw new ArgumentNullException(nameof(mp));
            }

            int scale;
            if (!int.TryParse(mp.ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out scale))
            {
                if (!int.TryParse(mp.ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out scale))
                {
                    return;
                }
            }

            double dpiD;
            if (!double.TryParse(mp.ExportDpiTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out dpiD))
            {
                if (!double.TryParse(mp.ExportDpiTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out dpiD))
                {
                    return;
                }
            }

            if (scale <= 0) return;
            if (dpiD <= 0) return;

            var strokes = mp.InkCanvasControl.InkPresenter.StrokeContainer.GetStrokes();
            if (strokes is null || strokes.Count == 0)
            {
                return;
            }

            var last = strokes[strokes.Count - 1];
            if (last is null)
            {
                return;
            }

            var s = UIHelpers.GetDot512SizeOrNull(mp);
            var p = (double)UIHelpers.GetDot512Pressure(mp);
            var n = UIHelpers.GetDot512Overwrite(mp);

            // 既存のpencil-highresとファイル名が衝突しないよう suffix を付ける。
            var ctx = new ExportHighResInk.ExportContext(s, p, n, exportScale: scale, tag: "laststroke");
            var one = new List<InkStroke>(1) { last };

            await ExportHighResInk.ExportAsync(mp, scale, (float)dpiD, transparentBackground, cropToBounds, one, ctx);
        }

        internal static async Task ExportHiResPreSaveAlphaStatsAsync(MainPage mp, bool useLastStrokeOnly, bool transparentBackground, bool cropToBounds)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));

            int scale;
            if (!int.TryParse(mp.ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out scale))
            {
                if (!int.TryParse(mp.ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out scale))
                {
                    return;
                }
            }

            double dpiD;
            if (!double.TryParse(mp.ExportDpiTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out dpiD))
            {
                if (!double.TryParse(mp.ExportDpiTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out dpiD))
                {
                    return;
                }
            }

            if (scale <= 0) return;
            if (dpiD <= 0) return;

            var strokes = mp.InkCanvasControl.InkPresenter.StrokeContainer.GetStrokes();
            if (strokes is null || strokes.Count == 0)
            {
                return;
            }

            IReadOnlyList<InkStroke> targetStrokes;
            string tag;

            if (useLastStrokeOnly)
            {
                var last = strokes[strokes.Count - 1];
                targetStrokes = new List<InkStroke>(1) { last };
                tag = "laststroke";
            }
            else
            {
                targetStrokes = strokes;
                tag = "canvas";
            }

            var s = UIHelpers.GetDot512SizeOrNull(mp);
            var p = (double)UIHelpers.GetDot512Pressure(mp);
            var n = UIHelpers.GetDot512Overwrite(mp);

            var ctx = new ExportHighResInk.ExportContext(s, p, n, exportScale: scale, tag: $"pre-save-alpha-{tag}");
            await ExportHighResInk.ExportPreSaveAlphaStatsCsvAsync(mp, scale, (float)dpiD, transparentBackground, cropToBounds, targetStrokes, ctx);
        }

        internal static async Task ExportPseudoLineDotStepSweepAsync(MainPage mp)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));

            int scale;
            if (!int.TryParse(mp.ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out scale) &&
                !int.TryParse(mp.ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out scale))
            {
                return;
            }

            double dpiD;
            if (!double.TryParse(mp.ExportDpiTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out dpiD) &&
                !double.TryParse(mp.ExportDpiTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out dpiD))
            {
                return;
            }

            if (scale <= 0) return;
            if (dpiD <= 0) return;

            var s = UIHelpers.GetDot512SizeOrNull(mp) ?? 200.0;
            var p = UIHelpers.GetDot512Pressure(mp);
            var opacity = UIHelpers.GetPencilOpacity(mp);
            var n = UIHelpers.GetDot512Overwrite(mp);
            var start = UIHelpers.GetStartPosition(mp);
            var end = UIHelpers.GetEndPosition(mp);
            var outW = UIHelpers.GetExportWidth(mp);
            var outH = UIHelpers.GetExportHeight(mp);

            var len = Math.Max(0.0, end.X - start.X);
            if (len <= 0) return;

            var stepRange = UIHelpers.GetDotStepSweepRange(mp);
            var steps = stepRange.Expand().ToArray();
            if (steps.Length == 0) return;

            var folderPicker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary };
            folderPicker.FileTypeFilter.Add(".png");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null) return;

            var isTransparent = mp.S200AlignedTransparentOutputCheckBox?.IsChecked == true;
            var usePen = mp.S200AlignedUsePenCheckBox?.IsChecked == true;
            var attributes = usePen
                ? StrokeHelpers.CreatePenAttributesForComparison(mp)
                : StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);
            attributes.Size = new Size(s, s);

            var device = CanvasDevice.GetSharedDevice();
            var widthPx = checked((int)Math.Ceiling(outW * (double)scale));
            var heightPx = checked((int)Math.Ceiling(outH * (double)scale));
            if (widthPx <= 0 || heightPx <= 0) return;

            foreach (var dotStepPx in steps)
            {
                var step = Math.Abs(dotStepPx);
                if (step <= 0.0f) continue;

                var count = (int)Math.Floor(len / step) + 1;
                count = Math.Clamp(count, 1, 200_000);

                var strokes = new List<InkStroke>(count);
                for (var i = 0; i < count; i++)
                {
                    var x = start.X + (i * step);
                    var pt = new Point(x, start.Y);
                    var points = new List<InkPoint>(1) { new InkPoint(pt, p) };
                    var stroke = StrokeHelpers.CreatePencilStrokeFromInkPoints(points, attributes);
                    strokes.Add(stroke);
                }

                var stepTag = dotStepPx.ToString("0.###", CultureInfo.InvariantCulture);
                var pTag = p.ToString("0.########", CultureInfo.InvariantCulture);
                var opTag = opacity.ToString("0.#####", CultureInfo.InvariantCulture);
                var fileName = $"pencil-highres-{widthPx}x{heightPx}-dpi{dpiD.ToString("0.##", CultureInfo.InvariantCulture)}-S{s}-P{pTag}-dotstep{stepTag}-Op{opTag}-N{n}-scale{scale}" + (isTransparent ? "-transparent" : "") + ".png";

                StorageFile file;
                try
                {
                    file = await folder.CreateFileAsync(fileName, CreationCollisionOption.FailIfExists);
                }
                catch
                {
                    var baseName = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var k = 1;
                    while (true)
                    {
                        var alt = $"{baseName}-dup{k}{ext}";
                        try
                        {
                            file = await folder.CreateFileAsync(alt, CreationCollisionOption.FailIfExists);
                            break;
                        }
                        catch
                        {
                            k++;
                            if (k > 10000) throw;
                        }
                    }
                }

                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, widthPx, heightPx, (float)dpiD))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(isTransparent ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                        ds.Transform = System.Numerics.Matrix3x2.CreateScale(scale);

                        for (var rep = 0; rep < n; rep++)
                        {
                            ds.DrawInk(strokes);
                        }
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }
            }
        }

        internal static void DrawLineStrokeFixed(MainPage mp, string? startText, string? endText, string? pointCountText, string? pointStepText, bool ignorePressure = false)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));

            var startX = 260f;
            var startY = 440f;
            if (TryParsePointTextLocal(startText, out var sx, out var sy))
            {
                startX = sx;
                startY = sy;
            }

            var endX = startX + 1000f;
            var endY = startY;
            if (TryParsePointTextLocal(endText, out var ex, out var ey))
            {
                endX = ex;
                endY = ey;
            }

            var pointCount = UIHelpers.ParseLinePointCount(pointCountText);
            var step = UIHelpers.ParseLinePointStep(pointStepText);
            InkInputProcessingMode processingMode = InkInputProcessingMode.Inking;
            if (mp.EraserRadioButton.IsChecked ?? false) processingMode = InkInputProcessingMode.Erasing;
            string penColorName = "Black";
            if (mp.BlackRadioButton.IsChecked ?? true) penColorName = "Black";
            if (mp.WhiteRadioButton.IsChecked ?? false) penColorName = "White";
            if (mp.TransparentRadioButton.IsChecked ?? false) penColorName = "Transparent";
            if (mp.RedRadioButton.IsChecked ?? false) penColorName = "Red";
            if (mp.GreenRadioButton.IsChecked ?? false) penColorName = "Green";
            if (mp.BlueRadioButton.IsChecked ?? false) penColorName = "Blue";
            Color penColor = penColorName switch
            {
                "Black" => Colors.Black,
                "White" => Colors.White,
                "Transparent" => Colors.Transparent,
                "Red" => Colors.Red,
                "Green" => Colors.Green,
                "Blue" => Colors.Blue,
                _ => Colors.Black
            };
            var attributes = processingMode == InkInputProcessingMode.Erasing
                ? StrokeHelpers.CreateEraseKeyInkAttributes(mp, Color.FromArgb(255, 255, 255, 255))
                : StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);
            attributes.Color = processingMode == InkInputProcessingMode.Erasing
                ? Color.FromArgb(255, 255, 255, 255)
                : penColor;

            if (ignorePressure) attributes.IgnorePressure = true;
            var strokeWidth = UIHelpers.GetDot512SizeOrNull(mp) ?? 200.0;
            attributes.Size = new Size(strokeWidth, strokeWidth);
            var pressure = UIHelpers.GetDot512Pressure(mp);

            // 生成した点列を同時にダンプするため、pointsを生成してstroke化する。
            // 消しゴム代替は「背景色で上書き描画」なので、削除ではなく通常ストロークとして追加する。
            var points = StrokeHelpers.CreateLineInkPointsFixed(startX, startY, endX, endY, pointCount, step, pressure);
            InkStroke stroke;
            if (processingMode == InkInputProcessingMode.Erasing)
            {
                var builder = new InkStrokeBuilder();
                stroke = builder.CreateStrokeFromInkPoints(points, System.Numerics.Matrix3x2.Identity, null, null);
                stroke.DrawingAttributes = attributes;
                
            }
            else
            {
                stroke = StrokeHelpers.CreatePencilStrokeFromInkPoints(points, attributes);
            }
            mp.InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(stroke);

            // ダンプはベストエフォート（失敗しても描画は維持）。
            _ = DumpInkPointsJsonBestEffortAsync(points, attributes);
        }

        internal static void DrawLineStrokeFixedMulti(MainPage mp, string? startText, string? endText, string? pointCountText, string? pointStepText, double stepYDip, int repeatCount, bool ignorePressure = false)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));
            if (repeatCount < 0) repeatCount = 0;
            var drawCount = Math.Max(1, repeatCount + 1);

            for (var i = 0; i < drawCount; i++)
            {
                var dy = (float)(stepYDip * i);
                var startShift = TryShiftPointTextY(startText, dy);
                var endShift = TryShiftPointTextY(endText, dy);
                DrawLineStrokeFixed(mp, startShift, endShift, pointCountText, pointStepText, ignorePressure);
            }
        }

        private static string? TryShiftPointTextY(string? pointText, float dy)
        {
            if (!TryParsePointTextLocal(pointText, out var x, out var y)) return pointText;
            return x.ToString("0.########", CultureInfo.InvariantCulture) + "," + (y + dy).ToString("0.########", CultureInfo.InvariantCulture);
        }

        internal static void DrawHoldStrokeFixed(MainPage mp, string? startText, string? pointCountText, bool ignorePressure = false)
        {
            if (mp is null) throw new ArgumentNullException(nameof(mp));
            
            var x = 260f;
            var y = 440f;
            if (TryParsePointTextLocal(startText, out var sx, out var sy))
            {
                x = sx;
                y = sy;
            }

            var pointCount = UIHelpers.ParseLinePointCount(pointCountText);

            var attributes = StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);
            if (ignorePressure) attributes.IgnorePressure = true;
            var strokeWidth = UIHelpers.GetDot512SizeOrNull(mp) ?? 200.0;
            attributes.Size = new Size(strokeWidth, strokeWidth);
            var pressure = UIHelpers.GetDot512Pressure(mp);

            var points = StrokeHelpers.CreateHoldInkPointsFixed(x, y, pointCount, pressure);
            var stroke = StrokeHelpers.CreatePencilStrokeFromInkPoints(points, attributes);
            mp.InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(stroke);

            _ = DumpInkPointsJsonBestEffortAsync(points, attributes);
        }

        public static async Task<(bool sucsess,string ret)> isDirExists(string path)
        {
            bool isDirectoryExists;
            Exception exception = null;
            try
            {
                Directory.GetFiles(path);

                // フォルダがある
                isDirectoryExists = true;
                exception = null;
            }
            catch (ArgumentNullException e)
            {
                // パスがnull（フォルダがない）
                isDirectoryExists = false;
                exception = e;
            }
            catch (ArgumentException e)
            {
                // パスに禁止文字あり（フォルダがない）
                isDirectoryExists = false;
                exception = e;
            }
            catch (PathTooLongException e)
            {
                // パスが長い（フォルダはあるかもしれない）
                isDirectoryExists = true;
                exception = e;
            }
            catch (DirectoryNotFoundException e)
            {
                // フォルダがない
                isDirectoryExists = false;
                exception = e;
            }
            catch (UnauthorizedAccessException e)
            {
                // 権限がない（フォルダがある）
                isDirectoryExists = true;
                exception = e;
            }
            catch (IOException e)
            {
                // その他（フォルダはあるかもしれない）
                isDirectoryExists = true;
                exception = e;
            }

            if (isDirectoryExists)
            {
                var retVal = exception?.Message ?? string.Empty;
                return (sucsess:true,ret:path);   // フォルダがある（かもしれない）
            }
            else
            {
                // フォルダがない
                return (sucsess:false,ret:exception.Message);
            }
        }

        public async static Task<string> GetGitRootPath(string path = null)
        {
            // 引数がなければ現在の実行ディレクトリを使用
                path ??= Directory.GetCurrentDirectory();
            var directory = new DirectoryInfo(path);
            while (directory != null)
            {
                var dirname = Path.Combine(path, ".git");
                (bool isExists,string retVal) = await isDirExists(dirname);
                if (isExists)
                {
                    return new DirectoryInfo(path).FullName;
                }
                try
                {
                    string newPath = new DirectoryInfo(path).Parent.FullName;
                    path = newPath;
                }
                catch (NullReferenceException)
                {
                    return null;
                }
            }
                return null;
        }
        private static async Task DumpInkPointsJsonBestEffortAsync(IReadOnlyList<InkPoint> points, InkDrawingAttributes attributes)
        {
            try
            {
                var json = BuildInkPointsDumpJson(points);

                var s = attributes.Size.Width;
                var fname = $"stroke_{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}_S{s:0.#}_P{points[0].Pressure:0.####}_N{points.Count}_step{ComputeStepFromPoints(points):0.###}_points.json";
                fname = fname.Replace(':', '_');
                var rootPath = await GetGitRootPath();
                // まずはユーザーが参照しやすいリポジトリルート（StrokeSampler\InkPointsDump）へ保存する。
                // 失敗した場合のみ LocalFolder へフォールバックする。
                StorageFolder? rootFolder = await StorageFolder.GetFolderFromPathAsync(rootPath ?? string.Empty);
                StorageFolder folder;
                try
                {
                    folder = await rootFolder.CreateFolderAsync("InkPointsDump", Windows.Storage.CreationCollisionOption.OpenIfExists);
                }
                catch
                {
                    folder = await Windows.Storage.ApplicationData.Current.LocalFolder.CreateFolderAsync(
                        "InkPointsDump",
                        Windows.Storage.CreationCollisionOption.OpenIfExists);
                }

                var file = await folder.CreateFileAsync(fname, Windows.Storage.CreationCollisionOption.ReplaceExisting);
                await Windows.Storage.FileIO.WriteTextAsync(file, json, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }
            catch
            {
                // 解析補助のためのベストエフォートなので握りつぶす。
            }
        }

        private static double ComputeStepFromPoints(IReadOnlyList<InkPoint> points)
        {
            if (points.Count < 2) return 0;
            var p0 = points[0].Position;
            var p1 = points[1].Position;
            var dx = p1.X - p0.X;
            var dy = p1.Y - p0.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string BuildInkPointsDumpJson(IReadOnlyList<InkPoint> points)
        {
            // DotLab/Sample/InkPointsDump と同形式（timestanp typoも踏襲）
            var sb = new StringBuilder(points.Count * 96);
            sb.AppendLine("[");

            // 制御ストロークのdtが0になって解析できないのを避けるため、固定刻みのtimestampを付与する。
            const long dtMs = 4;
            var baseTs = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[i];
                var pos = p.Position;
                var ts = baseTs + (dtMs * i);

                sb.AppendLine("  {");
                sb.Append("    \"x\": ").Append(pos.X.ToString("0.###############", CultureInfo.InvariantCulture)).AppendLine(",");
                sb.Append("    \"y\": ").Append(pos.Y.ToString("0.###############", CultureInfo.InvariantCulture)).AppendLine(",");
                sb.Append("    \"pressure\": ").Append(p.Pressure.ToString("0.########", CultureInfo.InvariantCulture)).AppendLine(",");
                sb.AppendLine("    \"tiltX\": 0,");
                sb.AppendLine("    \"tiltY\": 0,");
                sb.Append("    \"timestanp\": ").Append(ts.ToString(CultureInfo.InvariantCulture)).AppendLine();
                sb.Append("  }");
                if (i + 1 < points.Count) sb.Append(',');
                sb.AppendLine();
            }
            sb.AppendLine("]");
            return sb.ToString();
        }

        private static bool TryParsePointTextLocal(string? text, out float x, out float y)
        {
            x = 0;
            y = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split(',');
            if (parts.Length != 2) return false;

            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x))
            {
                if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out x)) return false;
            }

            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            {
                if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out y)) return false;
            }

            return true;
        }

        
        internal static void AssertCanParseFalloffFileNameFormats()
        {
            if (!ParseFalloffFilenameService.TryParseFalloffFilename("radial-falloff-S200-P0.1-N50.csv", out var s0, out var p0, out var n0))
            {
                throw new InvalidOperationException("Failed to parse radial-falloff-S200-P0.1-N50.csv");
            }
            if (s0 != 200 || Math.Abs(p0 - 0.1) > 1e-9 || n0 != 50)
            {
                throw new InvalidOperationException("Parsed values mismatch for radial-falloff-S200-P0.1-N50.csv");
            }

            if (!ParseFalloffFilenameService.TryParseFalloffFilename("radial-falloff-hires-S200-P0.1-N50-scale8.csv", out var s1, out var p1, out var n1))
            {
                throw new InvalidOperationException("Failed to parse radial-falloff-hires-S200-P0.1-N50-scale8.csv");
            }
            if (s1 != 200 || Math.Abs(p1 - 0.1) > 1e-9 || n1 != 50)
            {
                throw new InvalidOperationException("Parsed values mismatch for radial-falloff-hires-S200-P0.1-N50-scale8.csv");
            }
        }

        internal static async Task ExportDot512PreSaveAlphaSummaryCsvAsync(MainPage mp)
            => await ExportDot512PreSaveAlphaSummaryCsvWithPressureSweepAsync(mp, pressureStartInclusive: 0.0100f, pressureEndInclusive: 0.0200f, pressureStep: 0.0001f);

        internal static async Task ExportDot512PreSaveAlphaSummaryCsvWithPressureSweepAsync(
            MainPage mp,
            float pressureStartInclusive,
            float pressureEndInclusive,
            float pressureStep)
        {
            if (mp is null)
            {
                throw new ArgumentNullException(nameof(mp));
            }

            var sizes = UIHelpers.GetDot512BatchSizes(mp);
            if (sizes.Count == 0)
            {
                sizes = new[] { 200.0 };
            }

            IReadOnlyList<float> ps;
            {
                var userPs = UIHelpers.GetDot512BatchPs(mp);
                ps = userPs.Count != 0
                    ? userPs
                    : CreatePressureSweep(pressureStartInclusive, pressureEndInclusive, pressureStep);
            }
            var ns = UIHelpers.GetDot512BatchNs(mp);
            if (ns.Count == 0)
            {
                ns = new[] { UIHelpers.GetDot512Overwrite(mp) };
            }

            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            folderPicker.FileTypeFilter.Add(".csv");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var attributesBase = CreatePencilAttributesFromToolbarBestEffort(mp);
            var device = CanvasDevice.GetSharedDevice();

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var sb = new StringBuilder(1024);
            sb.AppendLine("S,P,N,center4_mean,max_alpha,nonzero_count,nonzero_ratio,center4_mean_circle,max_alpha_circle,nonzero_count_circle,nonzero_ratio_circle");

            var fileNameSuffix = "";
            if (sizes.Count == 1 && ns.Count == 1)
            {
                fileNameSuffix += $"-S{sizes[0]}-N{ns[0]}";
            }

            // 圧力範囲が指定されている場合はファイル名に残す（ユーザーP一覧指定時は除外）
            if (UIHelpers.GetDot512BatchPs(mp).Count == 0)
            {
                fileNameSuffix += $"-P{pressureStartInclusive:0.####}-to-{pressureEndInclusive:0.####}-step{pressureStep:0.####}";
            }

            foreach (var s in sizes)
            {
                var attributes = attributesBase;
                attributes.Size = new Size(s, s);

                foreach (var p in ps)
                {
                    foreach (var n in ns)
                    {
                        using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                        {
                            using (var ds = target.CreateDrawingSession())
                            {
                                ds.Clear(Color.FromArgb(0, 0, 0, 0));

                                for (var k = 0; k < n; k++)
                                {
                                    var dot = CreatePencilDot(cx, cy, p, attributes);
                                    ds.DrawInk(new[] { dot });
                                }
                            }

                            var bytes = target.GetPixelBytes();
                            var stats = ComputeAlphaStats(bytes, width: MainPage.Dot512Size, height: MainPage.Dot512Size);
                            var statsCircle = ComputeAlphaStatsCenterCircle(bytes, width: MainPage.Dot512Size, height: MainPage.Dot512Size, diameterPx: (int)Math.Round(s));

                            sb.Append(s.ToString("0.##", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(p.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(n.ToString(CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(stats.Center4Mean.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(stats.MaxAlpha.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(stats.NonZeroCount.ToString(CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(stats.NonZeroRatio.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(statsCircle.Center4Mean.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(statsCircle.MaxAlpha.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(statsCircle.NonZeroCount.ToString(CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(statsCircle.NonZeroRatio.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.AppendLine();
                        }
                    }
                }
            }

            var outFile = await folder.CreateFileAsync($"uwp-pre-save-alpha-summary{fileNameSuffix}.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var dialog = new ContentDialog
            {
                Title = "保存前α観測",
                Content = $"完了: {outFile.Path}",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }

        internal static async Task ExportDot512PreSaveAlphaFloorBySizeCsvAsync(MainPage mp)
        {
            if (mp is null)
            {
                throw new ArgumentNullException(nameof(mp));
            }

            const int sMin = 2;
            const int sMax = 200;

            // まずは既知の傾向に合わせて範囲を固定（S>=2）
            const float pMin = 0.0000f;
            const float pMax = 0.0105f;
            const float pStep = 0.0001f;

            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            folderPicker.FileTypeFilter.Add(".csv");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var attributesBase = CreatePencilAttributesFromToolbarBestEffort(mp);
            attributesBase.Size = new Size(1, 1);

            var device = CanvasDevice.GetSharedDevice();
            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var sb = new StringBuilder(4096);
            sb.AppendLine("S,p_floor,max_alpha,nonzero_count");

            for (var s = sMin; s <= sMax; s++)
            {
                var attributes = attributesBase;
                attributes.Size = new Size(s, s);

                var (found, pFloor, stats) = FindPressureFloorByBinarySearch(
                    device,
                    cx,
                    cy,
                    attributes,
                    pMin,
                    pMax,
                    pStep);

                sb.Append(s.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((found ? pFloor : float.NaN).ToString("0.######", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(stats.MaxAlpha.ToString("0.######", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(stats.NonZeroCount.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
            }

            var outFile = await folder.CreateFileAsync($"uwp-pre-save-alpha-floor-S{sMin}-to-S{sMax}-P{pMin:0.######}-to-{pMax:0.######}-step{pStep:0.######}.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var dialog = new ContentDialog
            {
                Title = "保存前α床（S別）",
                Content = $"完了: {outFile.Path}",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }

        private static (bool Found, float PFlooor, AlphaStats StatsAtFloor) FindPressureFloorByBinarySearch(
            CanvasDevice device,
            float cx,
            float cy,
            Windows.UI.Input.Inking.InkDrawingAttributes attributes,
            float pMin,
            float pMax,
            float pStep)
        {
            // 判定関数: max_alpha > 0 なら描画あり
            (bool HasInk, AlphaStats Stats) Eval(float p)
            {
                using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(Color.FromArgb(0, 0, 0, 0));
                        var dot = CreatePencilDot(cx, cy, p, attributes);
                        ds.DrawInk(new[] { dot });
                    }

                    var bytes = target.GetPixelBytes();
                    var stats = ComputeAlphaStats(bytes, width: MainPage.Dot512Size, height: MainPage.Dot512Size);
                    return (stats.MaxAlpha > 0, stats);
                }
            }

            // 範囲外（maxでも0）の場合は見つからない
            var (hasAtMax, statsAtMax) = Eval(pMax);
            if (!hasAtMax)
            {
                return (false, float.NaN, statsAtMax);
            }

            // pMinが既に非ゼロなら、pMinを返す
            var (hasAtMin, statsAtMin) = Eval(pMin);
            if (hasAtMin)
            {
                return (true, pMin, statsAtMin);
            }

            // 0.0001単位で境界を返すため、整数インデックスで二分探索する
            var minI = 0;
            var maxI = (int)Math.Round((pMax - pMin) / pStep);
            if (maxI < 1)
            {
                return (false, float.NaN, statsAtMax);
            }

            AlphaStats bestStats = statsAtMax;
            var bestI = maxI;

            while (minI + 1 < maxI)
            {
                var midI = (minI + maxI) / 2;
                var p = pMin + (pStep * midI);
                var (has, st) = Eval(p);
                if (has)
                {
                    bestI = midI;
                    bestStats = st;
                    maxI = midI;
                }
                else
                {
                    minI = midI;
                }
            }

            var pFloor = pMin + (pStep * bestI);
            pFloor = (float)Math.Round(pFloor, 4);
            return (true, pFloor, bestStats);
        }

        private static AlphaStats ComputeAlphaStatsCenterCircle(byte[] bgraBytes, int width, int height, int diameterPx)
        {
            // 直径Sの中心円内だけで統計を取る（S違いの面積差が反映される）
            if (diameterPx <= 0)
            {
                return new AlphaStats(double.NaN, double.NaN, 0, double.NaN);
            }

            var cx = (width - 1) * 0.5;
            var cy = (height - 1) * 0.5;
            var radius = diameterPx * 0.5;
            var r2 = radius * radius;

            long total = 0;
            long nonZero = 0;
            var maxA = 0;

            for (var y = 0; y < height; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < width; x++)
                {
                    var dx = x - cx;
                    if ((dx * dx + dy * dy) > r2)
                    {
                        continue;
                    }

                    total++;
                    var a = bgraBytes[(y * width + x) * 4 + 3];
                    if (a != 0)
                    {
                        nonZero++;
                    }

                    if (a > maxA)
                    {
                        maxA = a;
                    }
                }
            }

            // center4は幾何中心の4pxなので、円内/円外で値は変わらない想定だが、列としては同じ方式で出す
            var center4 = SampleCenterAlpha4px(bgraBytes, width, height);

            return new AlphaStats(
                Center4Mean: center4,
                MaxAlpha: maxA / 255.0,
                NonZeroCount: nonZero,
                NonZeroRatio: total > 0 ? (double)nonZero / total : double.NaN);
        }

        private static IReadOnlyList<float> CreatePressureSweep(float startInclusive, float endInclusive, float step)
        {
            if (step <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(step));
            }

            if (endInclusive < startInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(endInclusive));
            }

            var count = (int)Math.Floor((endInclusive - startInclusive) / step) + 1;
            count = Math.Clamp(count, 1, 10000);

            var list = new float[count];
            var digits = step >= 1
                ? 0
                : step >= 0.1f
                    ? 1
                    : step >= 0.01f
                        ? 2
                        : step >= 0.001f
                            ? 3
                            : 4;
            for (var i = 0; i < count; i++)
            {
                var p = startInclusive + (step * i);

                // float誤差の吸収と、CSV上の重複回避
                p = (float)Math.Round(p, digits);
                list[i] = p;
            }
            return list;
        }

        private static AlphaStats ComputeAlphaStats(byte[] bgraBytes, int width, int height)
        {
            // BGRA8 のA(0..255)を対象に簡易統計を取る
            var total = (long)width * height;
            long nonZero = 0;
            var maxA = 0;

            for (var i = 3; i < bgraBytes.Length; i += 4)
            {
                var a = bgraBytes[i];
                if (a != 0)
                {
                    nonZero++;
                }

                if (a > maxA)
                {
                    maxA = a;
                }
            }

            var center4 = SampleCenterAlpha4px(bgraBytes, width, height);
            return new AlphaStats(
                Center4Mean: center4,
                MaxAlpha: maxA / 255.0,
                NonZeroCount: nonZero,
                NonZeroRatio: total > 0 ? (double)nonZero / total : double.NaN);
        }

        private static double SampleCenterAlpha4px(byte[] bgraBytes, int width, int height)
        {
            if (width <= 1 || height <= 1)
            {
                return double.NaN;
            }

            var x0 = (width / 2) - 1;
            var y0 = (height / 2) - 1;
            var x1 = x0 + 1;
            var y1 = y0 + 1;
            if (x0 < 0 || y0 < 0 || x1 >= width || y1 >= height)
            {
                return double.NaN;
            }

            var a00 = bgraBytes[(y0 * width + x0) * 4 + 3] / 255.0;
            var a10 = bgraBytes[(y0 * width + x1) * 4 + 3] / 255.0;
            var a01 = bgraBytes[(y1 * width + x0) * 4 + 3] / 255.0;
            var a11 = bgraBytes[(y1 * width + x1) * 4 + 3] / 255.0;
            return (a00 + a10 + a01 + a11) * 0.25;
        }

        private readonly struct AlphaStats
        {
            internal AlphaStats(double Center4Mean, double MaxAlpha, long NonZeroCount, double NonZeroRatio)
            {
                this.Center4Mean = Center4Mean;
                this.MaxAlpha = MaxAlpha;
                this.NonZeroCount = NonZeroCount;
                this.NonZeroRatio = NonZeroRatio;
            }

            internal double Center4Mean { get; }
            internal double MaxAlpha { get; }
            internal long NonZeroCount { get; }
            internal double NonZeroRatio { get; }
        }
    }
}
