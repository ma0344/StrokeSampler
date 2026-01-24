using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StrokeSampler
{
    internal class ParseFalloffFilenameService
    {
        internal static bool TryParseFalloffFilename(string fileName, out double s, out double p, out int n)
        {
            // 例: radial-falloff-S50-P1-N1.csv
            s = default;
            p = default;
            n = default;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var name = fileName;
            if (name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }

            var parts = name.Split('-');
            double? sOpt = null;
            double? pOpt = null;
            int? nOpt = null;

            foreach (var part in parts)
            {
                if (part.Length >= 2 && (part[0] == 'S' || part[0] == 's'))
                {
                    if (double.TryParse(part.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var sv))
                    {
                        sOpt = sv;
                    }
                }
                else if (part.Length >= 2 && (part[0] == 'P' || part[0] == 'p'))
                {
                    if (double.TryParse(part.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var pv))
                    {
                        pOpt = pv;
                    }
                }
                else if (part.Length >= 2 && (part[0] == 'N' || part[0] == 'n'))
                {
                    if (int.TryParse(part.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nv))
                    {
                        nOpt = nv;
                    }
                }
            }

            if (sOpt is null || pOpt is null || nOpt is null)
            {
                return false;
            }

            s = sOpt.Value;
            p = pOpt.Value;
            n = nOpt.Value;
            return true;
        }

    }
}
