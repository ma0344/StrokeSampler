using System.Collections.Generic;
using Windows.UI.Input.Inking;

namespace StrokeSampler
{
    internal static class PencilOverwriteSampleGenerator
    {
        public static IEnumerable<InkStroke> Generate(
            InkDrawingAttributes attributes,
            float pressure,
            int maxOverwrite,
            float startX,
            float endX,
            float startY,
            float spacingY,
            System.Func<float, float, float, float, InkDrawingAttributes, InkStroke> createStroke)
        {
            if (attributes is null || createStroke is null)
            {
                yield break;
            }

            if (maxOverwrite <= 0)
            {
                yield break;
            }

            for (var overwriteCount = 1; overwriteCount <= maxOverwrite; overwriteCount++)
            {
                var y = startY + ((overwriteCount - 1) * spacingY);

                for (var i = 0; i < overwriteCount; i++)
                {
                    yield return createStroke(startX, endX, y, pressure, attributes);
                }
            }
        }
    }
}
