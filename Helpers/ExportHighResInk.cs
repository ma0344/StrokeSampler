using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;
using Windows.Storage.Streams;
using Windows.UI;

namespace StrokeSampler
{
    internal static class ExportHighResInk
    {
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
            => await ExportAsync(mp, scale, dpi, transparentBackground, cropToBounds, default);

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

            Windows.Foundation.Rect bounds;
            if (cropToBounds)
            {
                if (!StrokeHelpers.TryGetStrokesBoundingRect(strokes, out bounds))
                {
                    return;
                }

                // 範囲外になったケースへの保護
                if (bounds.X < 0) bounds.X = 0;
                if (bounds.Y < 0) bounds.Y = 0;
                if (bounds.Width <= 0 || bounds.Height <= 0) return;
            }
            else
            {
                bounds = new Windows.Foundation.Rect(0, 0, baseWidth, baseHeight);
            }

            checked
            {
                var width = (int)Math.Ceiling(bounds.Width * scale);
                var height = (int)Math.Ceiling(bounds.Height * scale);
                if (width <= 0 || height <= 0) return;

                var meta = BuildMetaSuffix(ctx, scale);

                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    SuggestedFileName = $"pencil-highres-{width}x{height}-dpi{dpi.ToString("0.##", CultureInfo.InvariantCulture)}{meta}" + (transparentBackground ? "-transparent" : "") + (cropToBounds ? "-cropped" : "")
                };
                picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

                var file = await picker.PickSaveFileAsync();
                if (file == null)
                {
                    return;
                }

                CachedFileManager.DeferUpdates(file);

                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var device = CanvasDevice.GetSharedDevice();

                    using (var target = new CanvasRenderTarget(device, width, height, dpi))
                    {
                        using (var ds = target.CreateDrawingSession())
                        {
                            ds.Clear(transparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                            // 既定がDIP座標で描かれているストロークを、ピクセル数を増やしたターゲットへ拡大して描く
                            var translate = System.Numerics.Matrix3x2.CreateTranslation((float)-bounds.X, (float)-bounds.Y);
                            var scaleM = System.Numerics.Matrix3x2.CreateScale(scale);
                            ds.Transform = translate * scaleM;
                            ds.DrawInk(strokes);
                        }

                        // 解析用: 保存前のピクセル（BGRA8）を取得して統計用に使う。
                        // 実際の保存（PNG化）前にどう見えているかを観測するためのフック。
                        // 本処理は呼び出し側が必要なときのみ行う。

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

        internal static async Task ExportAsync(MainPage mp, int scale, float dpi, bool transparentBackground, bool cropToBounds, ExportContext ctx)
        {
            if (mp == null) throw new ArgumentNullException(nameof(mp));
            if (scale <= 0) throw new ArgumentOutOfRangeException(nameof(scale));
            if (dpi <= 0) throw new ArgumentOutOfRangeException(nameof(dpi));

            var baseWidth = UIHelpers.GetExportWidth(mp);
            var baseHeight = UIHelpers.GetExportHeight(mp);

            var strokes = mp.InkCanvasControl.InkPresenter.StrokeContainer.GetStrokes();
            await ExportAsync(mp, scale, dpi, transparentBackground, cropToBounds, strokes, ctx);
            return;

            Windows.Foundation.Rect bounds;
            if (cropToBounds)
            {
                if (!StrokeHelpers.TryGetStrokesBoundingRect(strokes, out bounds))
                {
                    return;
                }

                // 境界が負になるケースへの保険
                if (bounds.X < 0) bounds.X = 0;
                if (bounds.Y < 0) bounds.Y = 0;
                if (bounds.Width <= 0 || bounds.Height <= 0) return;
            }
            else
            {
                bounds = new Windows.Foundation.Rect(0, 0, baseWidth, baseHeight);
            }

            checked
            {
                var width = (int)Math.Ceiling(bounds.Width * scale);
                var height = (int)Math.Ceiling(bounds.Height * scale);
                if (width <= 0 || height <= 0) return;

                var meta = BuildMetaSuffix(ctx, scale);

                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    SuggestedFileName = $"pencil-highres-{width}x{height}-dpi{dpi.ToString("0.##", CultureInfo.InvariantCulture)}{meta}" + (transparentBackground ? "-transparent" : "") + (cropToBounds ? "-cropped" : "")
                };
                picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

                var file = await picker.PickSaveFileAsync();
                if (file == null)
                {
                    return;
                }

                CachedFileManager.DeferUpdates(file);

                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var device = CanvasDevice.GetSharedDevice();

                    using (var target = new CanvasRenderTarget(device, width, height, dpi))
                    {
                        using (var ds = target.CreateDrawingSession())
                        {
                            ds.Clear(transparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                            // 元のDIP座標で描かれているストロークを、ピクセル数を増やしたターゲットへ拡大して描画する
                            var translate = System.Numerics.Matrix3x2.CreateTranslation((float)-bounds.X, (float)-bounds.Y);
                            var scaleM = System.Numerics.Matrix3x2.CreateScale(scale);
                            ds.Transform = translate * scaleM;
                            ds.DrawInk(strokes);
                        }

                        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                    }
                }

                FileUpdateStatus status = await CachedFileManager.CompleteUpdatesAsync(file);
                if (status != FileUpdateStatus.Complete)
                {
                    // UI側での通知は呼び出し元に任せる
                }
            }
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
