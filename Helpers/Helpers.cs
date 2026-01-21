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
    internal class Helpers
    {

        private const double PencilStrokeWidthMin = MainPage.PencilStrokeWidthMin;
        private const double PencilStrokeWidthMax = MainPage.PencilStrokeWidthMax;
        private static readonly float[] PressurePreset = MainPage.PressurePreset;
        private static readonly float[] DotGridPressurePreset = MainPage.DotGridPressurePreset;

        private const float DefaultStartX = MainPage.DefaultStartX;
        private const float DefaultEndX = MainPage.DefaultEndX;
        private const float DefaultStartY = MainPage.DefaultStartY;
        private const float DefaultSpacingY = MainPage.DefaultSpacingY;
        private const int DefaultMaxOverwrite = MainPage.DefaultMaxOverwrite;
        private const float DefaultOverwritePressure = MainPage.DefaultOverwritePressure;
        private const int Dot512Size = MainPage.Dot512Size;
        private const float Dot512Dpi = MainPage.Dot512Dpi;
        private const float DefaultDotGridStartX = MainPage.DefaultDotGridStartX;
        private const float DefaultDotGridStartY = MainPage.DefaultDotGridStartY;
        private const int DefaultDotGridSpacing = MainPage.DefaultDotGridSpacing;

        private const int PaperNoiseCropSize = MainPage.PaperNoiseCropSize;
        private const int PaperNoiseCropHalf = MainPage.PaperNoiseCropHalf;
        private static readonly int[] RadialAlphaThresholds = MainPage.RadialAlphaThresholds;


        public class lastProperties
        {
            public InkDrawingAttributes _lastGeneratedAttributes;
            public float? _lastOverwritePressure;
            public int? _lastMaxOverwrite;
            public int? _lastDotGridSpacing;
            public bool _lastWasDotGrid;
        };



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

        internal static bool TryReadAlphaSamplesFromFalloffCsv(string text, IReadOnlyList<int> rs, out double[] samples)
        {
            samples = Array.Empty<double>();
            if (rs is null || rs.Count == 0)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                return false;
            }

            var map = new Dictionary<int, double>(capacity: Math.Min(lines.Length, rs.Count));
            for (var i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length < 2)
                {
                    continue;
                }

                if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
                {
                    continue;
                }
                if (!double.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var a))
                {
                    continue;
                }

                // 必要なrだけ保持
                if (!map.ContainsKey(r))
                {
                    map[r] = a;
                }
            }

            var tmp = new double[rs.Count];
            for (var i = 0; i < rs.Count; i++)
            {
                if (!map.TryGetValue(rs[i], out var v))
                {
                    return false;
                }
                tmp[i] = v;
            }

            samples = tmp;
            return true;
        }

        internal static bool TryReadCenterAlphaFromFalloffCsv(string text, out double centerAlpha)
        {
            // 期待形式:
            // r,mean_alpha
            // 0,0.123...
            centerAlpha = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                return false;
            }

            // 2行目がr=0である前提（本ツールの出力は必ず0から開始）
            var cols = lines[1].Split(',');
            if (cols.Length < 2)
            {
                return false;
            }

            if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) || r != 0)
            {
                return false;
            }

            if (!double.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out centerAlpha))
            {
                return false;
            }

            return true;
        }

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

        internal static bool TryParseFalloffCsv(string text, out double[] fr)
        {
            // r,mean_alpha
            fr = Array.Empty<double>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var lines = text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2)
            {
                return false;
            }

            var list = new List<double>(lines.Length);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var cols = line.Split(',');
                if (cols.Length < 2)
                {
                    continue;
                }

                if (!int.TryParse(cols[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                if (!double.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                {
                    continue;
                }

                list.Add(v);
            }

            if (list.Count == 0)
            {
                return false;
            }

            fr = list.ToArray();
            return true;
        }

        internal static bool TryGetSelectedBrushColor(object toolButton, out Color color)
        {
            color = default;

            var type = toolButton.GetType();
            var prop = type.GetRuntimeProperty("SelectedBrush");
            if (prop?.GetMethod is null)
            {
                return false;
            }

            var brush = prop.GetValue(toolButton);
            if (brush is null)
            {
                return false;
            }

            var brushType = brush.GetType();
            var colorProp = brushType.GetRuntimeProperty("Color");
            if (colorProp?.GetMethod is null)
            {
                return false;
            }

            var value = colorProp.GetValue(brush);
            if (value is Color c)
            {
                color = c;
                return true;
            }

            return false;
        }

        internal static bool TryGetSelectedStrokeWidth(object toolButton, out double strokeWidth)
        {
            strokeWidth = default;

            var type = toolButton.GetType();
            var prop = type.GetRuntimeProperty("SelectedStrokeWidth");
            if (prop?.GetMethod is null)
            {
                return false;
            }

            var value = prop.GetValue(toolButton);
            if (value is double d)
            {
                strokeWidth = d * 2;
                return true;
            }

            if (value is float f)
            {
                strokeWidth = f * 2;
                return true;
            }

            return false;
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
                if (TryGetSelectedStrokeWidth(toolButton, out var strokeWidth))
                {
                    var w = Math.Clamp(strokeWidth, PencilStrokeWidthMin, PencilStrokeWidthMax);
                    attributes.Size = new Size(w, w);
                }

                if (TryGetSelectedBrushColor(toolButton, out var color))
                {
                    attributes.Color = color;
                }
            }

            return attributes;
        }

        internal static byte[] CropRgba(byte[] srcRgba, int srcW, int srcH, int x0, int y0, int cropW, int cropH)
        {
            var dst = new byte[cropW * cropH * 4];

            for (var y = 0; y < cropH; y++)
            {
                var sy = y0 + y;
                for (var x = 0; x < cropW; x++)
                {
                    var sx = x0 + x;

                    var dstIdx = (y * cropW + x) * 4;

                    // 範囲外は透明で埋める（安全側）
                    if ((uint)sx >= (uint)srcW || (uint)sy >= (uint)srcH)
                    {
                        dst[dstIdx + 0] = 0;
                        dst[dstIdx + 1] = 0;
                        dst[dstIdx + 2] = 0;
                        dst[dstIdx + 3] = 0;
                        continue;
                    }

                    var srcIdx = (sy * srcW + sx) * 4;
                    dst[dstIdx + 0] = srcRgba[srcIdx + 0];
                    dst[dstIdx + 1] = srcRgba[srcIdx + 1];
                    dst[dstIdx + 2] = srcRgba[srcIdx + 2];
                    dst[dstIdx + 3] = srcRgba[srcIdx + 3];
                }
            }

            return dst;
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
