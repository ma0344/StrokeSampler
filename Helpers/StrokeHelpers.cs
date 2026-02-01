using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace StrokeSampler
{
    internal class StrokeHelpers
    {

        private const double PencilStrokeWidthMin = MainPage.PencilStrokeWidthMin;
        private const double PencilStrokeWidthMax = MainPage.PencilStrokeWidthMax;



        internal static int[] CreateRadialAlphaThresholds()
        {
            var list = new List<int>(27) { 1 };
            for (var t = 10; t <= 250; t += 10)
            {
                list.Add(t);
            }
            list.Add(255);
            return list.ToArray();
        }

        internal static InkStroke CreatePencilDot(float centerX, float centerY, float pressure, InkDrawingAttributes attributes)
        {
            var strokeBuilder = new InkStrokeBuilder();

            var points = new List<InkPoint>
            {
                new InkPoint(new Point(centerX, centerY), pressure),
                new InkPoint(new Point(centerX + 0.5f, centerY), pressure)
            };

            var stroke = strokeBuilder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity, null, null);
            stroke.DrawingAttributes = attributes;
            return stroke;
        }

        internal static InkStroke CreatePencilStrokeVertical(float x, float startY, float endY, float pressure, InkDrawingAttributes attributes)
        {
            var strokeBuilder = new InkStrokeBuilder();

            var points = new List<InkPoint>();
            const float stepY = 4f;

            for (var y = startY; y <= endY; y += stepY)
            {
                points.Add(new InkPoint(new Point(x, y), pressure));
            }

            var stroke = strokeBuilder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity, null, null);
            stroke.DrawingAttributes = attributes;
            return stroke;
        }

        internal static InkStroke CreatePencilStrokeVertical2Points(float x, float startY, float endY, float pressure, InkDrawingAttributes attributes)
        {
            var strokeBuilder = new InkStrokeBuilder();

            var points = new List<InkPoint>
            {
                new InkPoint(new Point(x, startY), pressure),
                new InkPoint(new Point(x, endY), pressure)
            };

            var stroke = strokeBuilder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity, null, null);
            stroke.DrawingAttributes = attributes;
            return stroke;
        }

        internal static InkStroke CreatePencilStroke2Points(float startX, float endX, float y, float pressure, InkDrawingAttributes attributes)
        {
            var strokeBuilder = new InkStrokeBuilder();

            var points = new List<InkPoint>
            {
                new InkPoint(new Point(startX, y), pressure),
                new InkPoint(new Point(endX, y), pressure)
            };

            var stroke = strokeBuilder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity, null, null);
            stroke.DrawingAttributes = attributes;
            return stroke;
        }

        internal static InkStroke CreatePencilStroke(float startX, float endX, float y, float pressure, InkDrawingAttributes attributes)
        {
            var strokeBuilder = new InkStrokeBuilder();

            var points = new List<InkPoint>();
            const float stepX = 4f;

            for (var x = startX; x <= endX; x += stepX)
            {
                points.Add(new InkPoint(new Point(x, y), pressure));
            }

            var stroke = strokeBuilder.CreateStrokeFromInkPoints(points, Matrix3x2.Identity, null, null);
            stroke.DrawingAttributes = attributes;
            return stroke;
        }

        internal static InkDrawingAttributes CreatePencilAttributesFromToolbarBestEffort(MainPage mp)
        {
            var attributes = InkDrawingAttributes.CreateForPencil();
            attributes.Color = Colors.DarkSlateGray;
            attributes.Size = new Size(4, 4);


            object toolButton = null;
            try
            {
                toolButton = mp.InkToolbar?.GetToolButton(InkToolbarTool.Pencil);
            }
            catch
            {
                toolButton = null;
            }

            if (toolButton is InkToolbarPencilButton pencilButton)
            {
                var w = Math.Clamp(pencilButton.SelectedStrokeWidth, PencilStrokeWidthMin, PencilStrokeWidthMax);
                attributes.Size = new Size(w, w);

                var brush = pencilButton.SelectedBrush;
                if (brush is SolidColorBrush solidColorBrush)
                {
                    attributes.Color = solidColorBrush.Color;
                }

                return attributes;
            }

            if (toolButton != null)
            {
                if (UIHelpers.TryGetSelectedStrokeWidth(toolButton, out var strokeWidth))
                {
                    var w = Math.Clamp(strokeWidth, PencilStrokeWidthMin, PencilStrokeWidthMax);
                    attributes.Size = new Size(w, w);
                }

                if (UIHelpers.TryGetSelectedBrushColor(toolButton, out var color))
                {
                    attributes.Color = color;
                }
            }

            return attributes;
        }

        internal static bool TryGetStrokesBoundingRect(IReadOnlyList<InkStroke> strokes, out Rect bounds)
        {
            bounds = default;
            if (strokes == null || strokes.Count == 0) return false;

            var first = true;
            double x0 = 0, y0 = 0, x1 = 0, y1 = 0;

            foreach (var s in strokes)
            {
                if (s == null) continue;
                var r = s.BoundingRect;
                if (r.Width <= 0 || r.Height <= 0) continue;

                if (first)
                {
                    x0 = r.X;
                    y0 = r.Y;
                    x1 = r.X + r.Width;
                    y1 = r.Y + r.Height;
                    first = false;
                }
                else
                {
                    x0 = Math.Min(x0, r.X);
                    y0 = Math.Min(y0, r.Y);
                    x1 = Math.Max(x1, r.X + r.Width);
                    y1 = Math.Max(y1, r.Y + r.Height);
                }
            }

            if (first) return false;
            bounds = new Rect(x0, y0, x1 - x0, y1 - y0);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        internal static double[] ComputeRadialMeanAlphaD(byte[] rgba, int w, int h)
        {
            var cx = (w - 1) / 2.0;
            var cy = (h - 1) / 2.0;

            var maxR = Math.Sqrt(cx * cx + cy * cy);
            var bins = (int)Math.Floor(maxR) + 1;

            var sum = new double[bins];
            var cnt = new int[bins];

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var dx = x - cx;
                    var dy = y - cy;
                    var r = Math.Sqrt((dx * dx) + (dy * dy));
                    var bin = (int)Math.Floor(r);
                    if ((uint)bin >= (uint)bins)
                    {
                        continue;
                    }

                    var idx = (y * w + x) * 4;
                    var a = rgba[idx + 3] / 255.0;
                    sum[bin] += a;
                    cnt[bin]++;
                }
            }

            var fr = new double[bins];
            for (var i = 0; i < bins; i++)
            {
                fr[i] = cnt[i] > 0 ? (sum[i] / cnt[i]) : 0.0;
            }

            return fr;
        }

        internal static double[] ResampleRadialByExportScale(double[] frPx, int exportScale)
        {
            if (frPx is null) throw new ArgumentNullException(nameof(frPx));
            if (exportScale <= 0) throw new ArgumentOutOfRangeException(nameof(exportScale));
            if (exportScale == 1) return frPx;
            if (frPx.Length == 0) return frPx;

            // r_dip に対して、元の配列(=px半径)を r_px=r_dip*scale でサンプルする。
            var outLen = (int)Math.Floor((frPx.Length - 1) / (double)exportScale) + 1;
            if (outLen <= 0) outLen = 1;
            var frDip = new double[outLen];
            for (var r = 0; r < outLen; r++)
            {
                var x = r * (double)exportScale;
                frDip[r] = SampleLinear(frPx, x);
            }
            return frDip;
        }

        internal static string BuildNormalizedFalloffCsv(double[] mean, double[] stddev, int count, int s0, double p, int n)
        {
            var sb = new StringBuilder(capacity: Math.Max(1024, mean.Length * 40));
            sb.AppendLine($"# normalized-falloff S0={s0} P={p.ToString(CultureInfo.InvariantCulture)} N={n} count={count}");
            sb.AppendLine("r_norm,mean_alpha,stddev_alpha,count");

            for (var r = 0; r < mean.Length; r++)
            {
                sb.Append(r.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(mean[r].ToString("0.########", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(stddev[r].ToString("0.########", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(count.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
            }

            return sb.ToString();
        }

        internal static string BuildRadialFalloffCsv(double[] fr, double? s, double? p, int? n, int? exportScale)
        {
            var sb = new StringBuilder(capacity: Math.Max(1024, fr.Length * 24));
            if (s != null || p != null || n != null || exportScale != null)
            {
                var parts = new List<string>(4);
                if (s != null) parts.Add($"S={s.Value.ToString(CultureInfo.InvariantCulture)}");
                if (p != null) parts.Add($"P={p.Value.ToString(CultureInfo.InvariantCulture)}");
                if (n != null) parts.Add($"N={n.Value.ToString(CultureInfo.InvariantCulture)}");
                if (exportScale != null) parts.Add($"scale={exportScale.Value.ToString(CultureInfo.InvariantCulture)}");
                sb.AppendLine("# " + string.Join(" ", parts));
            }

            sb.AppendLine("r,mean_alpha");
            for (var r = 0; r < fr.Length; r++)
            {
                sb.Append(r.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(fr[r].ToString("0.########", CultureInfo.InvariantCulture));
                sb.AppendLine();
            }
            return sb.ToString();
        }

        internal static string BuildRadialFalloffCsv(double[] fr)
        {
            var sb = new StringBuilder(capacity: Math.Max(1024, fr.Length * 24));
            sb.AppendLine("r,mean_alpha");

            for (var r = 0; r < fr.Length; r++)
            {
                sb.Append(r.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(fr[r].ToString("0.########", CultureInfo.InvariantCulture));
                sb.AppendLine();
            }

            return sb.ToString();
        }
        
        internal static (double mae, double rmse) CompareAlphaOnly(byte[] aRgba, byte[] bRgba)
        {
            var n = aRgba.Length / 4;
            if (n <= 0 || bRgba.Length != aRgba.Length)
            {
                return (double.NaN, double.NaN);
            }

            double sumAbs = 0;
            double sumSq = 0;

            for (var i = 0; i < n; i++)
            {
                var a = aRgba[i * 4 + 3] / 255.0;
                var b = bRgba[i * 4 + 3] / 255.0;
                var d = a - b;
                sumAbs += Math.Abs(d);
                sumSq += d * d;
            }

            var mae = sumAbs / n;
            var rmse = Math.Sqrt(sumSq / n);
            return (mae, rmse);
        }

        internal static double SampleLinear(double[] y, double x)
        {
            if (y is null || y.Length == 0)
            {
                return 0.0;
            }

            if (x <= 0)
            {
                return y[0];
            }

            var max = y.Length - 1;
            if (x >= max)
            {
                return y[max];
            }

            var x0 = (int)Math.Floor(x);
            var t = x - x0;
            var a = y[x0];
            var b = y[x0 + 1];
            return a + (b - a) * t;
        }
        
    }
}
