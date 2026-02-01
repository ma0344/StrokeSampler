using System;
namespace DotLab.Rendering
{
    internal static class DotModel
    {
        internal readonly record struct DotResult(double[] OutA, double[] V, double[] B, double[] H, double[] Wall);

        public static DotResult RenderDot(
            int canvasSizePx,
            int diameterPx,
            double pressure01,
            int stampCount,
            double softnessK,
            float[] falloffF01,
            DotLabNoise noise,
            double noiseScale,
            double noiseOffsetX,
            double noiseOffsetY)
        {
            if (canvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(canvasSizePx));
            if (diameterPx <= 0) throw new ArgumentOutOfRangeException(nameof(diameterPx));
            if (pressure01 < 0 || pressure01 > 1) throw new ArgumentOutOfRangeException(nameof(pressure01));
            if (stampCount <= 0) throw new ArgumentOutOfRangeException(nameof(stampCount));
            if (softnessK <= 0) throw new ArgumentOutOfRangeException(nameof(softnessK));
            if (noiseScale <= 0) throw new ArgumentOutOfRangeException(nameof(noiseScale));
            ArgumentNullException.ThrowIfNull(falloffF01);
            ArgumentNullException.ThrowIfNull(noise);
            if (falloffF01.Length != canvasSizePx * canvasSizePx) throw new ArgumentException("falloffF01 のサイズが不正です。", nameof(falloffF01));

            var radiusPx = diameterPx * 0.5;
            var cx = (canvasSizePx - 1) * 0.5;
            var cy = (canvasSizePx - 1) * 0.5;

            var v = new double[canvasSizePx * canvasSizePx];
            var b = new double[canvasSizePx * canvasSizePx];
            var h = new double[canvasSizePx * canvasSizePx];
            var wall = new double[canvasSizePx * canvasSizePx];
            var outA = new double[canvasSizePx * canvasSizePx];

            for (var y = 0; y < canvasSizePx; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < canvasSizePx; x++)
                {
                    var dx = x - cx;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > radiusPx) continue;

                    var idx = y * canvasSizePx + x;
                    var f = falloffF01[idx];
                    var B = Math.Clamp(pressure01 * f, 0.0, 1.0);

                    // NOTE: 既知の仕様に合わせて、Offsetの向きはSkiaTester側の前提に寄せる。
                    // NoiseOffsetXを増加 => ノイズが右へ（点は左へ）
                    // NoiseOffsetYを増加 => ノイズが上へ（点は下へ）
                    var nx = ((x + 0.5) + noiseOffsetX) / noiseScale;
                    var ny = ((y + 0.5) + noiseOffsetY) / noiseScale;
                    var H = noise.SampleAlpha01(nx, ny);

                    var wall01 = 1.0 - H;
                    var V = (B - wall01) / softnessK;
                    V = Math.Clamp(V, 0.0, 1.0);

                    b[idx] = B;
                    h[idx] = H;
                    wall[idx] = wall01;
                    v[idx] = V;

                    // N回重ね: outA = 1 - (1 - V)^N
                    outA[idx] = 1.0 - Math.Pow(1.0 - V, stampCount);
                }
            }

            return new DotResult(outA, v, b, h, wall);
        }
    }
}
