using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace SkiaTester.Helpers;

internal static class PaperNoiseEstimator
{
    internal static SKBitmap EstimateFromDotAlphaPng(
        string dotPngPath,
        NormalizedFalloffLut falloffLut,
        double epsFalloff = 1.0 / 255.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dotPngPath);
        ArgumentNullException.ThrowIfNull(falloffLut);

        var fullPath = Path.GetFullPath(dotPngPath);
        using var stream = File.OpenRead(fullPath);
        using var dot = SKBitmap.Decode(stream) ?? throw new InvalidOperationException($"PNGの読み込みに失敗しました: {fullPath}");

        if (dot.Width <= 0 || dot.Height <= 0)
        {
            throw new InvalidOperationException($"PNGのサイズが不正です: {fullPath}");
        }

        var w = dot.Width;
        var h = dot.Height;
        var cx = (w - 1) * 0.5;
        var cy = (h - 1) * 0.5;
        var radiusPx = Math.Min(w, h) * 0.5;

        // 1) N_hat = A_obs / F(r_norm) を計算（Fが小さい箇所は無効）
        var valid = new bool[w * h];
        var nHat = new double[w * h];

        var values = new List<double>(capacity: w * h);

        for (var y = 0; y < h; y++)
        {
            var dy = y - cy;
            for (var x = 0; x < w; x++)
            {
                var dx = x - cx;
                var dist = Math.Sqrt(dx * dx + dy * dy);

                // ドット外は無効（透明）として扱う
                if (dist > radiusPx)
                {
                    continue;
                }

                var c = dot.GetPixel(x, y);
                if (c.Alpha == 0)
                {
                    continue;
                }

                var aObs = c.Alpha / 255.0;
                if (aObs <= 0)
                {
                    continue;
                }

                // dot512-material は S0=200 相当の切り出しとして扱う
                // r_norm = dist * (S0 / S) だが S0=200, S=200 とみなして r_norm=dist
                var rNorm = dist;
                var f = falloffLut.Eval(rNorm);
                if (f <= epsFalloff)
                {
                    continue;
                }

                var v = aObs / f;
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    continue;
                }

                var idx = y * w + x;
                valid[idx] = true;
                nHat[idx] = v;
                values.Add(v);
            }
        }

        if (values.Count <= 0)
        {
            throw new InvalidOperationException("有効画素が見つかりません。dotPngPathやepsFalloffを確認してください。");
        }

        // 2) 代表値を推定（中央値）して1.0に合わせる
        values.Sort();
        var median = values[values.Count / 2];
        if (median <= 0 || double.IsNaN(median) || double.IsInfinity(median))
        {
            median = 1.0;
        }

        var invMedian = 1.0 / median;

        // 3) 0..1へクランプしてPNG化（無効画素はalpha=0）
        var outBmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var idx = y * w + x;
                if (!valid[idx])
                {
                    outBmp.SetPixel(x, y, new SKColor(0, 0, 0, 0));
                    continue;
                }

                var v = nHat[idx] * invMedian;
                v = Math.Clamp(v, 0.0, 1.0);
                var g8 = (byte)Math.Clamp((int)Math.Round(v * 255.0), 0, 255);
                outBmp.SetPixel(x, y, new SKColor(g8, g8, g8, 255));
            }
        }

        return outBmp;
     }

    internal static void SavePng(SKBitmap bitmap, string filePath)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
        if (data == null)
        {
            throw new InvalidOperationException("PNGのエンコードに失敗しました。");
        }

        using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        data.SaveTo(stream);
    }
 }
