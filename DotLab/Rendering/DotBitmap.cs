using SkiaSharp;
using System;

namespace DotLab.Rendering
{

    internal static class DotBitmap
    {
        public static SKBitmap BuildGray8(double[] src01, int w, int h)
        {
            ArgumentNullException.ThrowIfNull(src01);
            if (w <= 0) throw new ArgumentOutOfRangeException(nameof(w));
            if (h <= 0) throw new ArgumentOutOfRangeException(nameof(h));
            if (src01.Length != w * h) throw new ArgumentException("配列サイズが不正です。", nameof(src01));

            var bmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var v = src01[y * w + x];
                    v = Math.Clamp(v, 0.0, 1.0);
                    var g = (byte)Math.Clamp((int)Math.Round(v * 255.0), 0, 255);
                    bmp.SetPixel(x, y, new SKColor(g, g, g, 255));
                }
            }
            return bmp;
        }
    }
}