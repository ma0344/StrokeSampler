using SkiaSharp;
using System;
using System.IO;

namespace DotTester.Helpers;

public sealed class PaperNoiseTile : IDisposable
{
    public enum SamplingMode
    {
        Nearest,
        Bilinear,
        Bicubic,
    }

    private readonly SKBitmap _bitmap;
    private readonly SKColor[] _pixels;
    private readonly int _width;
    private readonly int _height;

    private readonly double _mean01;
    private readonly double _min01;
    private readonly double _max01;
    private readonly double _stddev01;

    private PaperNoiseTile(SKBitmap bitmap)
    {
        _bitmap = bitmap;
        _width = bitmap.Width;
        _height = bitmap.Height;
        _pixels = bitmap.Pixels;

        (_min01, _max01, _mean01, _stddev01) = ComputeStatsAlpha01();
    }

    public int Width => _width;
    public int Height => _height;

    public double Mean01 => _mean01;
    public double Min01 => _min01;
    public double Max01 => _max01;
    public double Stddev01 => _stddev01;

    public static PaperNoiseTile LoadFromFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

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

        return new PaperNoiseTile(bitmap);
    }

    /// <summary>
    /// タイルのαを 0..1 でサンプルします。座標は「タイルのピクセル座標系（ピクセル中心基準）」です。
    /// - x/yはdoubleで、負値でも繰り返し可能です。
    /// - x=0.5 が texel(0) の中心、x=1.5 が texel(1) の中心です。
    /// </summary>
    public double SampleAlpha01(double x, double y, SamplingMode mode)
    {
        return mode switch
        {
            SamplingMode.Nearest => SampleNearest(x, y),
            SamplingMode.Bicubic => SampleBicubic(x, y),
            _ => SampleBilinear(x, y),
        };
    }

    private double SampleNearest(double x, double y)
    {
        // ピクセル中心(0.5, 1.5, ...)をtexel中心(0,1,...)へ合わせる
        var xx = Mod((int)Math.Round(x - 0.5, MidpointRounding.AwayFromZero), _width);
        var yy = Mod((int)Math.Round(y - 0.5, MidpointRounding.AwayFromZero), _height);
        return GetAlpha01Unchecked(xx, yy);
    }

    private double SampleBilinear(double x, double y)
    {
        var w = _width;
        var h = _height;
        if (w <= 0 || h <= 0) return 1.0;

        // ピクセル中心(0.5, 1.5, ...)をtexel座標へ合わせてから双線形する
        x -= 0.5;
        y -= 0.5;

        // タイル（負値も安全に繰り返す）
        var x0f = x % w;
        if (x0f < 0) x0f += w;
        var y0f = y % h;
        if (y0f < 0) y0f += h;

        var x0 = (int)Math.Floor(x0f);
        var y0 = (int)Math.Floor(y0f);
        var x1 = x0 + 1;
        var y1 = y0 + 1;
        if (x1 >= w) x1 -= w;
        if (y1 >= h) y1 -= h;

        var tx = x0f - x0;
        var ty = y0f - y0;

        var v00 = GetAlpha01Unchecked(x0, y0);
        var v10 = GetAlpha01Unchecked(x1, y0);
        var v01 = GetAlpha01Unchecked(x0, y1);
        var v11 = GetAlpha01Unchecked(x1, y1);

        var vx0 = (1.0 - tx) * v00 + tx * v10;
        var vx1 = (1.0 - tx) * v01 + tx * v11;
        return (1.0 - ty) * vx0 + ty * vx1;
    }

    private double SampleBicubic(double x, double y)
    {
        var w = _width;
        var h = _height;
        if (w <= 0 || h <= 0) return 1.0;

        // ピクセル中心(0.5, 1.5, ...)をtexel座標へ合わせてから双三次（Catmull-Rom）する
        x -= 0.5;
        y -= 0.5;

        var xf = x % w;
        if (xf < 0) xf += w;
        var yf = y % h;
        if (yf < 0) yf += h;

        var x1 = (int)Math.Floor(xf);
        var y1 = (int)Math.Floor(yf);
        var tx = xf - x1;
        var ty = yf - y1;

        // 4x4サンプル
        double Row(int yy, int xx0)
        {
            var p0 = GetAlpha01Unchecked(Mod(xx0 - 1, w), Mod(yy, h));
            var p1 = GetAlpha01Unchecked(Mod(xx0 + 0, w), Mod(yy, h));
            var p2 = GetAlpha01Unchecked(Mod(xx0 + 1, w), Mod(yy, h));
            var p3 = GetAlpha01Unchecked(Mod(xx0 + 2, w), Mod(yy, h));
            return CatmullRom(p0, p1, p2, p3, tx);
        }

        var r0 = Row(y1 - 1, x1);
        var r1 = Row(y1 + 0, x1);
        var r2 = Row(y1 + 1, x1);
        var r3 = Row(y1 + 2, x1);
        var v = CatmullRom(r0, r1, r2, r3, ty);
        return Math.Clamp(v, 0.0, 1.0);
    }

    private static double CatmullRom(double p0, double p1, double p2, double p3, double t)
    {
        // Catmull-Rom（tension=0.5、Keysの三次畳み込み a=-0.5 相当）。
        // 0..1の範囲は呼び出し側で最後にクランプする。
        var t2 = t * t;
        var t3 = t2 * t;
        return 0.5 * ((2.0 * p1)
            + (-p0 + p2) * t
            + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
            + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3);
    }

    private double GetAlpha01Unchecked(int x, int y)
    {
        var c = _pixels[(y * _width) + x];
        return c.Alpha / 255.0;
    }

    private (double min, double max, double mean, double stddev) ComputeStatsAlpha01()
    {
        var w = _width;
        var h = _height;
        if (w <= 0 || h <= 0) return (0.0, 0.0, 1.0, 0.0);

        double sum = 0;
        double sumSq = 0;
        double min = 1.0;
        double max = 0.0;

        var n = (long)w * h;
        for (var i = 0; i < n; i++)
        {
            var a01 = _pixels[i].Alpha / 255.0;
            if (a01 < min) min = a01;
            if (a01 > max) max = a01;
            sum += a01;
            sumSq += a01 * a01;
        }

        var mean = sum / n;
        if (!double.IsFinite(mean) || mean <= 0)
        {
            mean = 1.0;
        }

        var var0 = (sumSq / n) - (mean * mean);
        if (var0 < 0) var0 = 0;
        var stddev = Math.Sqrt(var0);
        if (!double.IsFinite(stddev) || stddev < 0) stddev = 0;

        return (min, max, mean, stddev);
    }

    private static int Mod(int x, int m)
    {
        if (m <= 0) return 0;
        var r = x % m;
        return r < 0 ? r + m : r;
    }

    public void Dispose()
    {
        _bitmap.Dispose();
    }
}
