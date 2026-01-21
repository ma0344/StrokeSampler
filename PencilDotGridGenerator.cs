using System;
using System.Collections.Generic;
using Windows.UI.Input.Inking;

namespace StrokeSampler
{
    internal static class PencilDotGridGenerator
    {
        public static IEnumerable<InkStroke> Generate(
            InkDrawingAttributes attributes,
            IReadOnlyList<float> pressurePreset,
            int maxOverwrite,
            int spacing,
            float startX,
            float startY,
            Func<float, float, float, InkDrawingAttributes, InkStroke> createDot)
        {
            if (attributes is null || createDot is null || pressurePreset is null)
            {
                yield break;
            }

            if (maxOverwrite <= 0 || spacing <= 0 || pressurePreset.Count == 0)
            {
                yield break;
            }

            // Columns = pressure, Rows = overwrite count
            for (var row = 1; row <= maxOverwrite; row++)
            {
                var y = startY + ((row - 1) * spacing);

                for (var col = 0; col < pressurePreset.Count; col++)
                {
                    var pressure = pressurePreset[col];
                    var x = startX + (col * spacing);

                    for (var n = 0; n < row; n++)
                    {
                        yield return createDot(x, y, pressure, attributes);
                    }
                }
            }
        }
    }
}
