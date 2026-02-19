using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI.Input.Inking;
using Windows.UI.Input.Inking.Core;

namespace InkDrawGen.Helpers
{
    internal static class InkStrokeBuildService
    {
        internal static InkStroke BuildSDotStroke(Point center, double sDip, float pressure, float opacity = 1.0f)
        {
            var builder = new InkStrokeBuilder();
            var attributes = InkDrawingAttributes.CreateForPencil();
            attributes.Color = Windows.UI.Colors.Black;
            attributes.Size = new Size(sDip, sDip);
            attributes.PencilProperties.Opacity = Math.Clamp(opacity, 0.01f, 5.0f);

            var pts = new List<InkPoint>(2)
            {
                new InkPoint(center, pressure),
                new InkPoint(new Point(center.X + 0.5, center.Y), pressure),
            };

            var stroke = builder.CreateStrokeFromInkPoints(pts, System.Numerics.Matrix3x2.Identity, null, null);
            stroke.DrawingAttributes = attributes;
            return stroke;
        }

        internal static InkStroke BuildSLineStroke2Points(Point start, Point end, double sDip, float pressure, float opacity = 1.0f)
        {
            var builder = new InkStrokeBuilder();
            var attributes = InkDrawingAttributes.CreateForPencil();
            attributes.Color = Windows.UI.Colors.Black;
            attributes.Size = new Size(sDip, sDip);
            attributes.PencilProperties.Opacity = Math.Clamp(opacity, 0.01f, 5.0f);

            var pts = new List<InkPoint>(2)
            {
                new InkPoint(start, pressure),
                new InkPoint(end, pressure),
            };

            var stroke = builder.CreateStrokeFromInkPoints(pts, System.Numerics.Matrix3x2.Identity, null, null);
            stroke.DrawingAttributes = attributes;
            return stroke;
        }
    }
}
