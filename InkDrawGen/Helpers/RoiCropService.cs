using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.UI.Xaml.Media.Imaging;

namespace InkDrawGen.Helpers
{
    internal static class RoiCropService
    {
        internal static WriteableBitmap Crop(WriteableBitmap src, Windows.Foundation.Rect roi, int scale)
        {
            var x0 = Math.Max(0, (int)Math.Round(roi.X * scale));
            var y0 = Math.Max(0, (int)Math.Round(roi.Y * scale));
            var w = Math.Max(1, (int)Math.Round(roi.Width * scale));
            var h = Math.Max(1, (int)Math.Round(roi.Height * scale));

            if (x0 + w > src.PixelWidth) w = Math.Max(1, src.PixelWidth - x0);
            if (y0 + h > src.PixelHeight) h = Math.Max(1, src.PixelHeight - y0);

            var dst = new WriteableBitmap(w, h);

            var srcBuf = src.PixelBuffer.ToArray();
            var dstBuf = new byte[w * h * 4];

            var srcStride = src.PixelWidth * 4;
            var dstStride = w * 4;

            for (var y = 0; y < h; y++)
            {
                var srcOff = (y0 + y) * srcStride + x0 * 4;
                var dstOff = y * dstStride;
                Array.Copy(srcBuf, srcOff, dstBuf, dstOff, dstStride);
            }

            using (var s = dst.PixelBuffer.AsStream())
            {
                s.Write(dstBuf, 0, dstBuf.Length);
            }
            dst.Invalidate();

            return dst;
        }
    }
}
