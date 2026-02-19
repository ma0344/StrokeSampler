using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.Storage.Streams;
using Windows.UI;

namespace StrokeSampler
{
    internal static class ExportHighResInk
    {
        private static readonly Color EraseKeyColor = Color.FromArgb(255, 255, 0, 255);
        private const int EraseKeyTolerance = 0;
        internal readonly struct ExportContext
        {
            public ExportContext(double? s, double? p, int? n, int? exportScale, string tag = null)
            {
                S = s;
                P = p;
                N = n;
                ExportScale = exportScale;
                Tag = tag;
            }

            public double? S { get; }
            public double? P { get; }
            public int? N { get; }
            public int? ExportScale { get; }
            public string Tag { get; }
        }

        internal static async Task ExportAsync(MainPage mp, int scale, float dpi, bool transparentBackground, bool cropToBounds)
        {
            if (mp == null) throw new ArgumentNullException(nameof(mp));
            var strokes = mp.InkCanvasControl.InkPresenter.StrokeContainer.GetStrokes();
            await ExportAsync(mp, scale, dpi, transparentBackground, cropToBounds, strokes, default);
        }

        internal static async Task ExportPreSaveAlphaStatsCsvAsync(MainPage mp, int scale, float dpi, bool transparentBackground, bool cropToBounds, IReadOnlyList<Windows.UI.Input.Inking.InkStroke> strokes, ExportContext ctx)
        {
            if (mp == null) throw new ArgumentNullException(nameof(mp));
            if (strokes == null) throw new ArgumentNullException(nameof(strokes));
            if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
            if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));

            var baseWidth = UIHelpers.GetExportWidth(mp);
            var baseHeight = UIHelpers.GetExportHeight(mp);

            if (strokes.Count == 0)
            {
                return;
            }

            Windows.Foundation.Rect bounds;
            if (cropToBounds)
            {
                if (!StrokeHelpers.TryGetStrokesBoundingRect(strokes, out bounds))
                {
                    return;
                }

                if (bounds.X < 0) bounds.X = 0;
                if (bounds.Y < 0) bounds.Y = 0;
                if (bounds.Width <= 0 || bounds.Height <= 0) return;
            }
            else
            {
                bounds = new Windows.Foundation.Rect(0, 0, baseWidth, baseHeight);
            }

            var width = (int)Math.Ceiling(bounds.Width * scale);
            var height = (int)Math.Ceiling(bounds.Height * scale);
            if (width <= 0 || height <= 0) return;

            var meta = BuildMetaSuffix(ctx, scale);

            // 命名は既存のpencil-highresに寄せ、pre-saveを明示する
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"pencil-highres-pre-save-alpha-{width}x{height}-dpi{dpi.ToString("0.##", CultureInfo.InvariantCulture)}{meta}" + (transparentBackground ? "-transparent" : "") + (cropToBounds ? "-cropped" : "")
            };
            picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                return;
            }

            CachedFileManager.DeferUpdates(file);

            var device = CanvasDevice.GetSharedDevice();
            using (var target = new CanvasRenderTarget(device, width, height, dpi))
            {
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(transparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                    var translate = System.Numerics.Matrix3x2.CreateTranslation((float)-bounds.X, (float)-bounds.Y);
                    var scaleM = System.Numerics.Matrix3x2.CreateScale(scale);
                    ds.Transform = translate * scaleM;
                    ds.DrawInk(strokes);
                }

                // 保存前（CanvasRenderTargetのBGRA8）を観測
                var bytes = target.GetPixelBytes();
                await TestMethods.WriteAlphaStatsCsvAsync(file, bytes, width, height);
            }

            var status = await CachedFileManager.CompleteUpdatesAsync(file);
            _ = status;
        }

        // (実描画ピクセルに基づくクロップの実装は、ExportAsync内で実装する)

        internal static async Task ExportAsync(MainPage mp, int scale, float dpi, bool transparentBackground, bool cropToBounds, IReadOnlyList<Windows.UI.Input.Inking.InkStroke> strokes, ExportContext ctx)
        {
            if (mp == null) throw new ArgumentNullException(nameof(mp));
            if (strokes == null) throw new ArgumentNullException(nameof(strokes));
            if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
            if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));

            var baseWidth = UIHelpers.GetExportWidth(mp);
            var baseHeight = UIHelpers.GetExportHeight(mp);

            if (strokes.Count == 0)
            {
                return;
            }

            var device = CanvasDevice.GetSharedDevice();

            Rect dipBounds;
            if (cropToBounds)
            {
                using var fullTarget = new CanvasRenderTarget(device, baseWidth * scale, baseHeight * scale, dpi);
                using (var ds = fullTarget.CreateDrawingSession())
                {
                    ds.Clear(transparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                    ds.Transform = System.Numerics.Matrix3x2.CreateScale(scale);
                    ds.DrawInk(strokes);
                }

                var pixels = fullTarget.GetPixelBytes();
                if (!TryGetRenderedPixelBounds(pixels, baseWidth * scale, baseHeight * scale, transparentBackground, out var pxBounds))
                {
                    return;
                }

                // アンチエイリアス等による極薄αの端が切れるのを避けるため、1pxマージンを付ける
                var x0 = Math.Max(0, pxBounds.X - 1);
                var y0 = Math.Max(0, pxBounds.Y - 1);
                var x1 = Math.Min((baseWidth * scale) - 1, pxBounds.X + pxBounds.Width);
                var y1 = Math.Min((baseHeight * scale) - 1, pxBounds.Y + pxBounds.Height);
                var wPx = Math.Max(1, x1 - x0 + 1);
                var hPx = Math.Max(1, y1 - y0 + 1);
                dipBounds = new Rect(x0 / (double)scale, y0 / (double)scale, wPx / (double)scale, hPx / (double)scale);
                if (dipBounds.Width <= 0 || dipBounds.Height <= 0) return;
            }
            else
            {
                dipBounds = new Rect(0, 0, baseWidth, baseHeight);
            }

            checked
            {
                var width = (int)Math.Ceiling(dipBounds.Width * scale);
                var height = (int)Math.Ceiling(dipBounds.Height * scale);
                if (width <= 0 || height <= 0) return;

                var meta = BuildMetaSuffix(ctx, scale);
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    SuggestedFileName = $"pencil-highres-{width}x{height}-dpi{dpi.ToString("0.##", CultureInfo.InvariantCulture)}{meta}" + (transparentBackground ? "-transparent" : "") + (cropToBounds ? "-cropped" : "")
                };
                picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

                var file = await picker.PickSaveFileAsync();
                if (file == null) return;

                CachedFileManager.DeferUpdates(file);

                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    using var target = new CanvasRenderTarget(device, width, height, dpi);
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(transparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                        var translate = System.Numerics.Matrix3x2.CreateTranslation((float)-dipBounds.X, (float)-dipBounds.Y);
                        var scaleM = System.Numerics.Matrix3x2.CreateScale(scale);
                        ds.Transform = translate * scaleM;
                        ds.DrawInk(strokes);
                    }

                    if (transparentBackground)
                    {
                        var bgra = target.GetPixelBytes();
                        ReplaceKeyColorWithTransparentInPlace(bgra, EraseKeyColor, EraseKeyTolerance);
                        await WritePngFromBgraAsync(stream, bgra, width, height, dpi);
                    }
                    else
                    {
                        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                    }
                }

                FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);
                if (status != FileUpdateStatus.Complete)
                {
                    // UI側での通知は呼び出し側に任せる
                }
            }
        }

        private static void ReplaceKeyColorWithTransparentInPlace(byte[] bgra, Color key, int tolerance)
        {
            if (bgra is null) throw new ArgumentNullException(nameof(bgra));
            tolerance = Math.Clamp(tolerance, 0, 255);

            // CanvasRenderTargetはBGRA8
            var keyB = key.B;
            var keyG = key.G;
            var keyR = key.R;

            for (var i = 0; i + 3 < bgra.Length; i += 4)
            {
                var b = bgra[i + 0];
                var g = bgra[i + 1];
                var r = bgra[i + 2];

                if (Math.Abs(b - keyB) <= tolerance && Math.Abs(g - keyG) <= tolerance && Math.Abs(r - keyR) <= tolerance)
                {
                    bgra[i + 3] = 0;
                }
            }
        }

        private static async Task WritePngFromBgraAsync(IRandomAccessStream stream, byte[] bgra, int width, int height, float dpi)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            if (bgra is null) throw new ArgumentNullException(nameof(bgra));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            stream.Seek(0);
            stream.Size = 0;

            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetPixelData(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied,
                (uint)width,
                (uint)height,
                dpi,
                dpi,
                bgra);
            await encoder.FlushAsync();
        }

        private static bool TryGetRenderedPixelBounds(byte[] bgra, int w, int h, bool transparentBackground, out Rect bounds)
        {
            bounds = default;
            if (bgra is null || w <= 0 || h <= 0) return false;
            if (bgra.Length < (w * h * 4)) return false;

            var minX = w;
            var minY = h;
            var maxX = -1;
            var maxY = -1;

            var stride = w * 4;
            for (var y = 0; y < h; y++)
            {
                var row = y * stride;
                for (var x = 0; x < w; x++)
                {
                    var i = row + x * 4;
                    var b = bgra[i + 0];
                    var g = bgra[i + 1];
                    var r = bgra[i + 2];
                    var a = bgra[i + 3];

                    bool hit;
                    if (transparentBackground)
                    {
                        hit = a != 0;
                    }
                    else
                    {
                        hit = !(r == 255 && g == 255 && b == 255);
                    }

                    if (!hit) continue;

                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY) return false;
            bounds = new Rect(minX, minY, (maxX - minX + 1), (maxY - minY + 1));
            return bounds.Width > 0 && bounds.Height > 0;
        }

        internal static async Task ExportAsync(MainPage mp, int scale, float dpi, bool transparentBackground, bool cropToBounds, ExportContext ctx)
        {
            if (mp == null) throw new ArgumentNullException(nameof(mp));
            if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
            if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));

            var baseWidth = UIHelpers.GetExportWidth(mp);
            var baseHeight = UIHelpers.GetExportHeight(mp);

            var strokes = mp.InkCanvasControl.InkPresenter.StrokeContainer.GetStrokes();
            await ExportAsync(mp, scale, dpi, transparentBackground, cropToBounds, strokes, ctx);
        }

        private static string BuildMetaSuffix(ExportContext ctx, int fallbackScale)
        {
            var s = ctx.S;
            var p = ctx.P;
            var n = ctx.N;
            var exportScale = ctx.ExportScale ?? fallbackScale;
            var tag = ctx.Tag;

            if (s == null && p == null && n == null && exportScale <= 0 && string.IsNullOrWhiteSpace(tag))
            {
                return string.Empty;
            }

            var parts = new List<string>(4);
            if (s != null) parts.Add($"S{s.Value.ToString("0.##", CultureInfo.InvariantCulture)}");
            if (p != null) parts.Add($"P{p.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
            if (n != null) parts.Add($"N{n.Value}");
            if (exportScale > 0) parts.Add($"scale{exportScale}");
            if (!string.IsNullOrWhiteSpace(tag)) parts.Add(tag);

            return parts.Count == 0 ? string.Empty : "-" + string.Join("-", parts);
        }
    }
}
