using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SkiaTester.Constants;

namespace SkiaTester
{
    internal class SkiaHelpers
    {
        internal static bool ValidatePressure(double p)
        {
            return !(double.IsNaN(p) || double.IsInfinity(p) || p < MinPressure || p > MaxPressure);
        }
        internal static bool ValidateSize(int s)
        {
            return s >= MinSizePx && s <= MaxSizePx;
        }
    }
}
