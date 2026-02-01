using System.IO;
using SkiaSharp;
using System;
namespace DotLab.Rendering
{

    internal sealed class DotLabNoise
    {
        public int Width { get; }
        public int Height { get; }

        private readonly float[] _alpha01;

        private DotLabNoise(int width, int height, float[] alpha01)
        {
            Width = width;
            Height = height;
            _alpha01 = alpha01;
        }

        public static DotLabNoise LoadFromImageAlpha(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path が空です。", nameof(path));

            var stream = File.OpenRead(path);
            var codec = SKCodec.Create(stream);
            if (codec == null) throw new InvalidOperationException($"画像の読み込みに失敗しました: {path}");

            var info = codec.Info;
            var bmp = new SKBitmap(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            var result = codec.GetPixels(bmp.Info, bmp.GetPixels());
            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
            {
                throw new InvalidOperationException($"画像のデコードに失敗しました: {path} ({result})");
            }

            var a = new float[bmp.Width * bmp.Height];
            for (var y = 0; y < bmp.Height; y++)
            {
                for (var x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    a[y * bmp.Width + x] = c.Alpha / 255f;
                }
            }

            return new DotLabNoise(bmp.Width, bmp.Height, a);
        }

        public float SampleAlpha01(double nx, double ny)
        {
            // ワールド固定（キャンバス座標固定）を前提に、連続座標をタイルで繰り返す。
            // ここでは nearest にしておき、必要なら後でbilinearへ拡張する。
            var x = ModToIndex(nx, Width);
            var y = ModToIndex(ny, Height);
            return _alpha01[y * Width + x];
        }

        private static int ModToIndex(double x, int size)
        {
            if (size <= 0) return 0;
            var xi = (int)Math.Floor(x);
            var m = xi % size;
            if (m < 0) m += size;
            return m;
        }
    }
}