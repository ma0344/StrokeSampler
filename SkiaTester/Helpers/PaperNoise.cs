using SkiaSharp;
using System;
using System.IO;

namespace SkiaTester.Helpers;

public sealed class PaperNoise : IDisposable
{
    public enum EdgeMode
    {
        // 無効ピクセルは 1.0（影響なし）として扱う（既定）
        TreatInvalidAsOne,

        // 無効ピクセルの場合、近傍の有効ピクセルへクランプしてサンプルする
        ClampToValid,
    }

    private readonly SKBitmap _bitmap;
    private readonly double _mean01;
    private readonly double _min01;
    private readonly double _max01;
    private readonly double _stddev01;
    private readonly double _validRatio;

    private PaperNoise(SKBitmap bitmap)
    {
        _bitmap = bitmap;
        var stats = ComputeStats01(bitmap);
        _mean01 = stats.mean;
        _min01 = stats.min;
        _max01 = stats.max;
        _stddev01 = stats.stddev;
        _validRatio = stats.validRatio;
    }

    public int Width => _bitmap.Width;
    public int Height => _bitmap.Height;

    public double Mean01 => _mean01;
    public double Min01 => _min01;
    public double Max01 => _max01;
    public double Stddev01 => _stddev01;
    public double ValidRatio => _validRatio;

    public EdgeMode InvalidEdgeMode { get; set; } = EdgeMode.TreatInvalidAsOne;

    public static PaperNoise LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath is empty", nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);
        using var stream = File.OpenRead(fullPath);
        var bitmap = SKBitmap.Decode(stream);
        if (bitmap == null)
        {
            throw new InvalidOperationException($"PNGの読み込みに失敗しました: {fullPath}");
        }

        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            bitmap.Dispose();
            throw new InvalidOperationException($"PNGのサイズが不正です: {fullPath}");
        }

        return new PaperNoise(bitmap);
    }

    // 0..1 のノイズ値。ユーザー申告で「反転済み」なので、ここでは反転しない。
    public double Sample01(int canvasX, int canvasY)
    {
        var x = Mod(canvasX, _bitmap.Width);
        var y = Mod(canvasY, _bitmap.Height);

        var c = _bitmap.GetPixel(x, y);
        if (IsInvalidBackground(c))
        {
            return InvalidEdgeMode == EdgeMode.ClampToValid
                ? SampleNearestValid01(x, y)
                : 1.0;
        }
        // グレースケール想定（B=G=R）だが、念のため平均を取る
        var g = (c.Red + c.Green + c.Blue) / 3.0;
        return g / 255.0;
    }

    // 0..1 のノイズ値（double座標・双線形補間）。タイルはワールド固定で行う。
    public double Sample01Bilinear(double canvasX, double canvasY)
    {
        var w = _bitmap.Width;
        var h = _bitmap.Height;
        if (w <= 0 || h <= 0) return 1.0;

        // タイル（負値も安全に繰り返す）
        var x0f = canvasX % w;
        if (x0f < 0) x0f += w;
        var y0f = canvasY % h;
        if (y0f < 0) y0f += h;

        var x0 = (int)Math.Floor(x0f);
        var y0 = (int)Math.Floor(y0f);
        var x1 = x0 + 1;
        var y1 = y0 + 1;
        if (x1 >= w) x1 -= w;
        if (y1 >= h) y1 -= h;

        var tx = x0f - x0;
        var ty = y0f - y0;

        double Gray01OrFallback(int x, int y, SKColor c)
        {
            if (!IsInvalidBackground(c)) return (c.Red + c.Green + c.Blue) / (3.0 * 255.0);
            return InvalidEdgeMode == EdgeMode.ClampToValid
                ? SampleNearestValid01(x, y)
                : 1.0;
        }

        var c00 = _bitmap.GetPixel(x0, y0);
        var c10 = _bitmap.GetPixel(x1, y0);
        var c01 = _bitmap.GetPixel(x0, y1);
        var c11 = _bitmap.GetPixel(x1, y1);

        var v00 = Gray01OrFallback(x0, y0, c00);
        var v10 = Gray01OrFallback(x1, y0, c10);
        var v01 = Gray01OrFallback(x0, y1, c01);
        var v11 = Gray01OrFallback(x1, y1, c11);

        var vx0 = (1.0 - tx) * v00 + tx * v10;
        var vx1 = (1.0 - tx) * v01 + tx * v11;
        return (1.0 - ty) * vx0 + ty * vx1;
    }

    private double SampleNearestValid01(int x, int y)
    {
        var w = _bitmap.Width;
        var h = _bitmap.Height;
        if (w <= 0 || h <= 0) return 1.0;

        // 最大半径は小さめで十分（今回の用途は円外=透明領域の穴埋め）
        const int maxR = 8;
        for (var r = 1; r <= maxR; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                var yy = y + dy;
                if (yy < 0) yy += h;
                if (yy >= h) yy -= h;

                var dx0 = -r;
                var dx1 = r;

                var xx0 = x + dx0;
                if (xx0 < 0) xx0 += w;
                if (xx0 >= w) xx0 -= w;
                var c0 = _bitmap.GetPixel(xx0, yy);
                if (!IsInvalidBackground(c0))
                {
                    return (c0.Red + c0.Green + c0.Blue) / (3.0 * 255.0);
                }

                var xx1 = x + dx1;
                if (xx1 < 0) xx1 += w;
                if (xx1 >= w) xx1 -= w;
                var c1 = _bitmap.GetPixel(xx1, yy);
                if (!IsInvalidBackground(c1))
                {
                    return (c1.Red + c1.Green + c1.Blue) / (3.0 * 255.0);
                }
            }

            for (var dx = -r + 1; dx <= r - 1; dx++)
            {
                var xx = x + dx;
                if (xx < 0) xx += w;
                if (xx >= w) xx -= w;

                var yy0 = y - r;
                if (yy0 < 0) yy0 += h;
                if (yy0 >= h) yy0 -= h;
                var c0 = _bitmap.GetPixel(xx, yy0);
                if (!IsInvalidBackground(c0))
                {
                    return (c0.Red + c0.Green + c0.Blue) / (3.0 * 255.0);
                }

                var yy1 = y + r;
                if (yy1 < 0) yy1 += h;
                if (yy1 >= h) yy1 -= h;
                var c1 = _bitmap.GetPixel(xx, yy1);
                if (!IsInvalidBackground(c1))
                {
                    return (c1.Red + c1.Green + c1.Blue) / (3.0 * 255.0);
                }
            }
        }

        return 1.0;
    }

    // 0..1 のノイズ値（double座標）。低周波成分を混ぜて粒の大きさを調整する。
    // mix: 0=元の周波数, 1=低周波のみ
    public double Sample01Mixed(double canvasX, double canvasY, double lowFreqScale, double mix)
    {
        if (mix <= 0) return Sample01Bilinear(canvasX, canvasY);
        if (mix >= 1)
        {
            var s = lowFreqScale <= 0 ? 1.0 : lowFreqScale;
            return Sample01Bilinear(canvasX / s, canvasY / s);
        }

        var hi = Sample01Bilinear(canvasX, canvasY);
        var scale = lowFreqScale <= 0 ? 1.0 : lowFreqScale;
        var lo = Sample01Bilinear(canvasX / scale, canvasY / scale);
        return (1.0 - mix) * hi + mix * lo;
    }

    private static bool IsInvalidBackground(SKColor c)
        => c.Alpha == 0 || (c.Alpha == 255 && c.Red == 0 && c.Green == 0 && c.Blue == 0);

    private static (double min, double max, double mean, double stddev, double validRatio) ComputeStats01(SKBitmap bitmap)
    {
        var w = bitmap.Width;
        var h = bitmap.Height;
        if (w <= 0 || h <= 0) return (0.0, 0.0, 1.0, 0.0, 0.0);

        double sum = 0;
        double sumSq = 0;
        double min = 1.0;
        double max = 0.0;

        var nTotal = (long)w * h;
        long nValid = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (IsInvalidBackground(c))
                {
                    continue;
                }
                var v = (c.Red + c.Green + c.Blue) / (3.0 * 255.0);
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
                sumSq += v * v;
                nValid++;
            }
        }

        if (nValid <= 0)
        {
            return (0.0, 0.0, 1.0, 0.0, 0.0);
        }

        var mean = sum / nValid;
        if (double.IsNaN(mean) || double.IsInfinity(mean) || mean <= 0)
        {
            return (min, max, 1.0, 0.0, nValid / (double)nTotal);
        }

        var var0 = (sumSq / nValid) - (mean * mean);
        if (var0 < 0) var0 = 0;
        return (min, max, mean, Math.Sqrt(var0), nValid / (double)nTotal);
    }

    private static int Mod(int x, int m)
    {
        var r = x % m;
        return r < 0 ? r + m : r;
    }

    public void Dispose()
    {
        _bitmap.Dispose();
    }
}
