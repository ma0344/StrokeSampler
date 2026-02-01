using System;

namespace StrokeSampler
{
    internal static class TilePeriodEstimator
    {
        internal sealed class Result
        {
            public Result(int periodPx, double score)
            {
                PeriodPx = periodPx;
                Score = score;
            }

            public int PeriodPx { get; }
            public double Score { get; }
        }

        // 1D自己相関で周期候補を推定する。
        // - values: グレースケール(0..1)の列
        // - minLag/maxLag: 探索範囲（ピクセル）
        internal static Result EstimatePeriodByAutocorrelation(double[] values, int minLag, int maxLag)
        {
            if (values == null || values.Length < 8) return null;
            if (minLag < 1) minLag = 1;
            if (maxLag <= minLag) return null;
            if (maxLag >= values.Length - 1) maxLag = values.Length - 2;

            // 平均を引く
            double mean = 0;
            for (var i = 0; i < values.Length; i++) mean += values[i];
            mean /= values.Length;

            // 分散（正規化用）
            double var0 = 0;
            for (var i = 0; i < values.Length; i++)
            {
                var d = values[i] - mean;
                var0 += d * d;
            }
            if (var0 <= 0) return null;

            var bestLag = -1;
            var bestScore = double.NegativeInfinity;

            for (var lag = minLag; lag <= maxLag; lag++)
            {
                double acc = 0;
                var n = values.Length - lag;
                for (var i = 0; i < n; i++)
                {
                    acc += (values[i] - mean) * (values[i + lag] - mean);
                }

                // 正規化相関係数っぽくする
                var score = acc / var0;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLag = lag;
                }
            }

            if (bestLag <= 0) return null;
            return new Result(bestLag, bestScore);
        }
    }
}
