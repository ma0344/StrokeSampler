using System;
using System.Collections.Generic;
using Windows.UI.Input.Inking;

namespace StrokeSampler
{
    internal static class PencilPressurePresetGenerator
    {
        public static IEnumerable<InkStroke> Generate(
            InkDrawingAttributes attributes,
            IReadOnlyList<float> pressurePreset,
            float startX,
            float endX,
            float startY,
            float spacingY,
            Func<float, float, float, float, InkDrawingAttributes, InkStroke> createStroke)
        {
            if (attributes is null || createStroke is null || pressurePreset is null)
            {
                yield break;
            }

            if (pressurePreset.Count == 0)
            {
                yield break;
            }

            for (var i = 0; i < pressurePreset.Count; i++)
            {
                var pressure = pressurePreset[i];
                var y = startY + (i * spacingY);

                yield return createStroke(startX, endX, y, pressure, attributes);
            }
        }
    }
}
