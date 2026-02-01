using System;
using System.Collections.Generic;
using Windows.UI.Input.Inking;

namespace StrokeSampler
{
    internal static class PencilDotGridGenerator
    {
        public static IEnumerable<InkStroke> GenerateFixedCondition(
            InkDrawingAttributes attributes,
            float pressure,
            int overwrite,
            int spacing,
            float startX,
            float startY,
            int columns,
            int rows,
            Func<float, float, float, InkDrawingAttributes, InkStroke> createDot)
        {
            if (attributes is null || createDot is null)
            {
                yield break;
            }

            if (overwrite <= 0 || spacing <= 0 || columns <= 0 || rows <= 0)
            {
                yield break;
            }

            // Every cell uses the same pressure and the same overwrite count (N).
            // This is intended to compare positional differences (paper noise / sampling).
            for (var row = 0; row < rows; row++)
            {
                var y = startY + (row * spacing);
                for (var col = 0; col < columns; col++)
                {
                    var x = startX + (col * spacing);
                    for (var n = 0; n < overwrite; n++)
                    {
                        yield return createDot(x, y, pressure, attributes);
                    }
                }
            }
        }

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
