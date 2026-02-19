using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace InkDrawGen.Helpers
{
    internal enum JobType
    {
        Dot,
        Line,
    }

    internal struct OpacityRangeSpec
    {
        internal double Start;
        internal double End;
        internal double Step;

        internal IEnumerable<double> Expand()
        {
            if (Step == 0 || Start == End)
            {
                yield return Normalize(Start);
                yield break;
            }

            if (double.IsNaN(Step) || double.IsInfinity(Step) || Step == 0) yield break;

            // Opacityは0.01刻み等の小数スイープが多いため、端数誤差がファイル名に出ないよう丸める。
            const int MaxIter = 1_000_000;

            if (Start < End)
            {
                if (Step < 0) yield break;
                for (var i = 0; i < MaxIter; i++)
                {
                    var v = Start + (Step * i);
                    if (v > End + 1e-12) yield break;
                    yield return Normalize(v);
                }
            }
            else
            {
                if (Step > 0) yield break;
                for (var i = 0; i < MaxIter; i++)
                {
                    var v = Start + (Step * i);
                    if (v < End - 1e-12) yield break;
                    yield return Normalize(v);
                }
            }
        }

        private static double Normalize(double v)
        {
            // Opは0.0001刻みスイープを想定するため、小数第4位で丸めて安定化する。
            v = Math.Round(v, 5, MidpointRounding.AwayFromZero);
            return Math.Clamp(v, 0.01, 5.0);
        }
    }

    internal struct RangeSpec
    {
        internal double Start;
        internal double End;
        internal double Step;

        internal IEnumerable<double> Expand()
        {
            if (Step == 0 || Start == End)
            {
                yield return Start;
                yield break;
            }

            if (double.IsNaN(Step) || double.IsInfinity(Step) || Step == 0) yield break;

            // 浮動小数の加算誤差（例: 0.01刻みが0.15000001になる）を避けるため、
            // v += Step ではなくカウンタiで Start + Step*i を計算する。
            const int MaxIter = 1_000_000;

            if (Start < End)
            {
                if (Step < 0) yield break;
                for (var i = 0; i < MaxIter; i++)
                {
                    var v = Start + (Step * i);
                    if (v > End + 1e-12) yield break;
                    yield return v;
                }
            }
            else
            {
                if (Step > 0) yield break;
                for (var i = 0; i < MaxIter; i++)
                {
                    var v = Start + (Step * i);
                    if (v < End - 1e-12) yield break;
                    yield return v;
                }
            }
        }
    }

    internal struct IntRangeSpec
    {
        internal int Start;
        internal int End;
        internal int Step;

        internal IEnumerable<int> Expand()
        {
            if (Step == 0 || Start == End)
            {
                yield return Start;
                yield break;
            }

            if (Start < End)
            {
                if (Step < 0) yield break;
                for (var v = Start; v <= End; v += Step) yield return v;
            }
            else
            {
                if (Step > 0) yield break;
                for (var v = Start; v >= End; v += Step) yield return v;
            }
        }
    }

    internal sealed class InkDrawGenUiState
    {
        internal string OutputFolder;
        internal JobType JobType;
        internal string RunTag;
        internal RangeSpec S;
        internal RangeSpec P;
        internal OpacityRangeSpec Opacity;
        internal IntRangeSpec N;
        internal int Repeat;
        internal int Scale;
        internal double Dpi;
        internal bool Transparent;
        internal Point Start;
        internal Point Step;
        internal RangeSpec DotStepX;
        internal bool DotStepTwoPoints;
        internal bool DotStepFixedCount;
        internal int DotStepCount;
        internal IntRangeSpec DotStepCountRange;
        internal int RepeatCount;
        internal Point End;
        internal RangeSpec EndXSweep;
        internal Rect Roi;
        internal int OutWidthPx;
        internal int OutHeightPx;

        internal InkDrawGenUiState()
        {
            OutputFolder = "";
            RunTag = "";
            Repeat = 1;
            Step = new Point(0, 0);
            DotStepX = new RangeSpec { Start = 4, End = 4, Step = 0 };
            DotStepTwoPoints = false;
            DotStepFixedCount = false;
            DotStepCount = 2;
            DotStepCountRange = new IntRangeSpec { Start = 2, End = 2, Step = 0 };
            RepeatCount = 0;
            Opacity = new OpacityRangeSpec { Start = 1, End = 1, Step = 0 };
            EndXSweep = new RangeSpec { Start = 118, End = 280, Step = 18 };
        }
    }
}
