using System;

namespace SkiaTester.Helpers;

public readonly struct AlphaSummary
{
    public AlphaSummary(double maxAlpha, int nonzeroCount)
    {
        MaxAlpha = maxAlpha;
        NonzeroCount = nonzeroCount;
    }

    public double MaxAlpha { get; }
    public int NonzeroCount { get; }

    public static AlphaSummary FromBitmap(SkiaSharp.SKBitmap bitmap)
    {
        if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));

        var maxA = 0;
        var nonzero = 0;

        // SKColor is BGRA 8bit.
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var a = bitmap.GetPixel(x, y).Alpha;
                if (a != 0) nonzero++;
                if (a > maxA) maxA = a;
            }
        }

        return new AlphaSummary(maxA / 255.0, nonzero);
    }
}
