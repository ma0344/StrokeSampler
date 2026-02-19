using System;
using System.Globalization;

namespace InkDrawGen.Helpers
{
    internal static class FileNameBuilder
    {
        internal static string BuildStrokeSamplerLike(
            string prefix,
            int outWpx,
            int outHpx,
            float dpi,
            double s,
            double p,
            int n,
            string runTag,
            string extraSuffix,
            int scale,
            bool transparent,
            double? opacity = null)
        {
            var pTag = p.ToString("0.########", CultureInfo.InvariantCulture);
            var dpiTag = dpi.ToString("0.##", CultureInfo.InvariantCulture);
            var sTag = ((int)Math.Round(s)).ToString(CultureInfo.InvariantCulture);
            var opPart = opacity.HasValue ? "-Op" + opacity.Value.ToString("0.#####", CultureInfo.InvariantCulture) : "";

            var runTagPart = string.IsNullOrWhiteSpace(runTag) ? "" : "-" + runTag;
            var extra = string.IsNullOrWhiteSpace(extraSuffix) ? "" : "-" + extraSuffix;

            var name = string.Format(
                "{0}-{1}x{2}-dpi{3}-S{4}-P{5}-alignedN{6}{7}{8}{9}-scale{10}{11}",
                prefix,
                outWpx,
                outHpx,
                dpiTag,
                sTag,
                pTag,
                n,
                runTagPart,
                extra,
                opPart,
                scale,
                transparent ? "-transparent" : "");

            return name + ".png";
        }
    }
}
