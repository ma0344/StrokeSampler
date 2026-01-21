using System;
using System.Globalization;
using System.Text;

namespace StrokeSampler
{
    internal static class RadialAlphaCsvBuilder
    {
        public static string Build(
            int bins,
            int binSize,
            int[] radialAlphaThresholds,
            int[] total,
            long[] sumAlpha,
            int[][] hits)
        {
            if (radialAlphaThresholds is null)
            {
                throw new ArgumentNullException(nameof(radialAlphaThresholds));
            }

            if (total is null)
            {
                throw new ArgumentNullException(nameof(total));
            }

            if (sumAlpha is null)
            {
                throw new ArgumentNullException(nameof(sumAlpha));
            }

            if (hits is null)
            {
                throw new ArgumentNullException(nameof(hits));
            }

            if (bins <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bins));
            }

            if (binSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(binSize));
            }

            if (total.Length < bins || sumAlpha.Length < bins)
            {
                throw new ArgumentException("集計配列のサイズがbinsに対して不足しています。");
            }

            if (hits.Length != radialAlphaThresholds.Length)
            {
                throw new ArgumentException("hitsの閾値次元がradialAlphaThresholdsと一致しません。", nameof(hits));
            }

            for (var i = 0; i < hits.Length; i++)
            {
                if (hits[i] is null || hits[i].Length < bins)
                {
                    throw new ArgumentException("hitsのbin次元がbinsに対して不足しています。", nameof(hits));
                }
            }

            var sb = new StringBuilder(capacity: 1024 * 1024);
            sb.Append("r_bin,px_from,px_to,total,mean_alpha");
            for (var i = 0; i < radialAlphaThresholds.Length; i++)
            {
                sb.Append(",p_ge_");
                sb.Append(radialAlphaThresholds[i]);
            }
            sb.AppendLine();

            for (var bin = 0; bin < bins; bin++)
            {
                var n = total[bin];
                if (n <= 0)
                {
                    continue;
                }

                var mean = (double)sumAlpha[bin] / n;

                sb.Append(bin);
                sb.Append(',');
                sb.Append((bin * binSize).ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(((bin + 1) * binSize).ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(n);
                sb.Append(',');
                sb.Append(mean.ToString("0.###", CultureInfo.InvariantCulture));

                for (var tIndex = 0; tIndex < radialAlphaThresholds.Length; tIndex++)
                {
                    var rate = (double)hits[tIndex][bin] / n;
                    sb.Append(',');
                    sb.Append(rate.ToString("0.######", CultureInfo.InvariantCulture));
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
