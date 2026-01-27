using System;

namespace SkiaTester.Helpers;

internal static class CenterAlphaSummary
{
    internal static double GetCenterAlpha01(double[] alpha01, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(alpha01);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (alpha01.Length != width * height)
        {
            throw new ArgumentException("alpha01 length mismatch", nameof(alpha01));
        }

        var cx = (width - 1) / 2;
        var cy = (height - 1) / 2;
        return alpha01[cy * width + cx];
    }

    internal static double QuantizeTo8Bit01(double a01)
    {
        if (a01 <= 0) return 0;
        if (a01 >= 1) return 1;

        var a8 = (int)Math.Round(a01 * 255.0);
        if (a8 < 0) a8 = 0;
        if (a8 > 255) a8 = 255;
        return a8 / 255.0;
    }
}
