using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml.Media.Imaging;

namespace InkDrawGen.Helpers
{
    internal static class InkOffscreenRenderService
    {
        internal static Task<WriteableBitmap> RenderStrokeCroppedAsync(InkStroke stroke, int outWidth, int outHeight, Rect roiDip, bool transparent, float dpi, int exportScale, int repeat)
        {
            if (stroke == null) throw new ArgumentNullException(nameof(stroke));
            return RenderStrokesCroppedAsync(new[] { stroke }, outWidth, outHeight, roiDip, transparent, dpi, exportScale, repeat);
        }

        internal static Task<WriteableBitmap> RenderStrokesCroppedAsync(InkStroke[] strokes, int outWidth, int outHeight, Rect roiDip, bool transparent, float dpi, int exportScale, int repeat)
        {
            if (strokes == null) throw new ArgumentNullException(nameof(strokes));
            if (outWidth <= 0) throw new ArgumentOutOfRangeException(nameof(outWidth));
            if (outHeight <= 0) throw new ArgumentOutOfRangeException(nameof(outHeight));
            if (exportScale <= 0) throw new ArgumentOutOfRangeException(nameof(exportScale));
            if (repeat <= 0) throw new ArgumentOutOfRangeException(nameof(repeat));
            if (roiDip.Width <= 0) throw new ArgumentOutOfRangeException(nameof(roiDip));
            if (roiDip.Height <= 0) throw new ArgumentOutOfRangeException(nameof(roiDip));

            var cropW = Math.Max(1, (int)Math.Round(roiDip.Width * exportScale));
            var cropH = Math.Max(1, (int)Math.Round(roiDip.Height * exportScale));
            var cropX = (int)Math.Round(roiDip.X * exportScale);
            var cropY = (int)Math.Round(roiDip.Y * exportScale);

            // ROIが出力キャンバスを超える設定の場合でも例外にしない（可能な範囲で切り出す）。
            cropW = Math.Min(cropW, outWidth);
            cropH = Math.Min(cropH, outHeight);

            // outWidth/outHeight がROIサイズ（切り出し後サイズ）と同じ場合は、
            // 物理的な切り出しではなく、描画時に平行移動してROIを原点に持ってくる。
            var translateInsteadOfCrop = outWidth == cropW && outHeight == cropH;

            if (!translateInsteadOfCrop)
            {
                cropX = Math.Clamp(cropX, 0, Math.Max(0, outWidth - cropW));
                cropY = Math.Clamp(cropY, 0, Math.Max(0, outHeight - cropH));
            }

            var device = CanvasDevice.GetSharedDevice();
            using (var target = new CanvasRenderTarget(device, outWidth, outHeight, dpi))
            {
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(transparent ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                    // ROIを(0,0)へ持ってきてからスケールする。
                    // outWidth/outHeightがROIサイズと異なる場合でも「ROIの見えている範囲」は平行移動で一致させ、
                    // その上でbytes配列からcropX/cropYで切り出す（合成すると従来の挙動と互換）。
                    ds.Transform = System.Numerics.Matrix3x2.CreateScale(exportScale)
                        * System.Numerics.Matrix3x2.CreateTranslation(-(float)roiDip.X, -(float)roiDip.Y);
                    for (var i = 0; i < repeat; i++)
                    {
                        ds.DrawInk(strokes);
                    }
                }

                var bytes = target.GetPixelBytes();

                if (translateInsteadOfCrop)
                {
                    var bmp0 = new WriteableBitmap(outWidth, outHeight);
                    using (var s = bmp0.PixelBuffer.AsStream())
                    {
                        s.Write(bytes, 0, bytes.Length);
                    }
                    bmp0.Invalidate();
                    return Task.FromResult(bmp0);
                }

                var stride = outWidth * 4;
                var cropped = new byte[cropW * cropH * 4];
                var copiedAllRows = true;
                for (var y = 0; y < cropH; y++)
                {
                    var srcOffset = ((cropY + y) * stride) + (cropX * 4);
                    var dstOffset = y * cropW * 4;
                    // 念のため境界チェック（GetPixelBytesのサイズ不一致や丸め誤差での例外を防ぐ）
                    if (srcOffset < 0 || (srcOffset + (cropW * 4)) > bytes.Length)
                    {
                        copiedAllRows = false;
                        break;
                    }
                    Buffer.BlockCopy(bytes, srcOffset, cropped, dstOffset, cropW * 4);
                }

                if (!copiedAllRows)
                {
                    // ROIが不正でクロップできない場合は落とさず、キャンバス全体を返す。
                    var bmp0 = new WriteableBitmap(outWidth, outHeight);
                    using (var s = bmp0.PixelBuffer.AsStream())
                    {
                        s.Write(bytes, 0, bytes.Length);
                    }
                    bmp0.Invalidate();
                    return Task.FromResult(bmp0);
                }

                var bmp = new WriteableBitmap(cropW, cropH);
                using (var s = bmp.PixelBuffer.AsStream())
                {
                    s.Write(cropped, 0, cropped.Length);
                }
                bmp.Invalidate();
                return Task.FromResult(bmp);
            }
        }

        internal static Task<WriteableBitmap> RenderStrokeAsync(InkStroke stroke, int width, int height, bool transparent, float dpi, int exportScale, int repeat)
        {
            if (stroke == null) throw new ArgumentNullException(nameof(stroke));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (exportScale <= 0) throw new ArgumentOutOfRangeException(nameof(exportScale));
            if (repeat <= 0) throw new ArgumentOutOfRangeException(nameof(repeat));

            var device = CanvasDevice.GetSharedDevice();
            using (var target = new CanvasRenderTarget(device, width, height, dpi))
            {
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(transparent ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                    ds.Transform = System.Numerics.Matrix3x2.CreateScale(exportScale);
                    for (var i = 0; i < repeat; i++)
                    {
                        ds.DrawInk(new[] { stroke });
                    }
                }

                var bytes = target.GetPixelBytes();
                var bmp = new WriteableBitmap(width, height);
                using (var s = bmp.PixelBuffer.AsStream())
                {
                    s.Write(bytes, 0, bytes.Length);
                }
                bmp.Invalidate();
                return Task.FromResult(bmp);
            }
        }

        internal static Task<WriteableBitmap> RenderStrokesAsync(InkStroke[] strokes, int width, int height, bool transparent, float dpi, int exportScale, int repeat)
        {
            if (strokes == null) throw new ArgumentNullException(nameof(strokes));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (exportScale <= 0) throw new ArgumentOutOfRangeException(nameof(exportScale));
            if (repeat <= 0) throw new ArgumentOutOfRangeException(nameof(repeat));

            var device = CanvasDevice.GetSharedDevice();
            using (var target = new CanvasRenderTarget(device, width, height, dpi))
            {
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(transparent ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                    ds.Transform = System.Numerics.Matrix3x2.CreateScale(exportScale);

                    for (var i = 0; i < repeat; i++)
                    {
                        ds.DrawInk(strokes);
                    }
                }

                var bytes = target.GetPixelBytes();
                var bmp = new WriteableBitmap(width, height);
                using (var s = bmp.PixelBuffer.AsStream())
                {
                    s.Write(bytes, 0, bytes.Length);
                }
                bmp.Invalidate();
                return Task.FromResult(bmp);
            }
        }
    }
}
