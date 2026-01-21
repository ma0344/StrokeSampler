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


        internal static int GetNormalizedFalloffS0(MainPage mp)
        {
            if (int.TryParse(mp.NormalizedFalloffS0TextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s0))
            {
                return Math.Clamp(s0, 1, 200);
            }

            return 200;
        }

        internal static int GetExportWidth(MainPage mp)
        {
            if (int.TryParse(mp.ExportWidthTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var width))
            {
                return Math.Clamp(width, 256, 16384);
            }

            return 4096;
        }

        internal static int GetMaxOverwrite(MainPage mp)
        {
            if (int.TryParse(mp.MaxOverwriteTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxOverwrite))
            {
                return Math.Clamp(maxOverwrite, 1, 50);
            }

            return DefaultMaxOverwrite;
        }

        internal static float GetOverwritePressure(MainPage mp)
        {
            if (float.TryParse(mp.OverwritePressureTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pressure))
            {
                return Math.Clamp(pressure, 0.01f, 1.0f);
            }

            return DefaultOverwritePressure;
        }

        internal static int GetExportHeight(MainPage mp)
        {
            if (int.TryParse(mp.ExportHeightTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
            {
                return Math.Clamp(height, 256, 16384);
            }

            return 4096;
        }

        internal static int GetDotGridSpacing(MainPage mp)
        {
            if (int.TryParse(mp.DotGridSpacingTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spacing))
            {
                return Math.Clamp(spacing, 40, 800);
            }

            return DefaultDotGridSpacing;
        }

        internal static int GetRadialBinSize(MainPage mp)
        {
            if (int.TryParse(mp.RadialBinSizeTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bin))
            {
                return Math.Clamp(bin, 1, 32);
            }

            return 1;
        }

        internal static double? GetDot512SizeOrNull(MainPage mp)
        {
            var raw = mp.Dot512SizeTextBox.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
            {
                // 端切れを避けたいので、dotの"直径"相当はキャンバスより少し小さめを上限にする。
                return Math.Clamp(size, 1, Dot512Size - 2);
            }

            return null;
        }

        internal static float GetDot512Pressure(MainPage mp)
        {
            if (float.TryParse(mp.Dot512PressureTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pressure))
            {
                return Math.Clamp(pressure, 0.01f, 1.0f);
            }

            return 1.0f;
        }

        internal static int GetDot512Overwrite(MainPage mp)
        {
            if (int.TryParse(mp.Dot512OverwriteTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                return Math.Clamp(n, 1, 200);
            }

            return 1;
        }

        internal static int GetPaperNoiseCropDx(MainPage mp)
        {
            if (int.TryParse(mp.PaperNoiseCropDxTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dx))
            {
                return Math.Clamp(dx, -256, 256);
            }

            return 32;
        }

        internal static int GetPaperNoiseCropDy(MainPage mp)
        {
            if (int.TryParse(mp.PaperNoiseCropDyTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dy))
            {
                return Math.Clamp(dy, -256, 256);
            }

            return 0;
        }

        internal static double GetDot512SlideStep(MainPage mp)
        {
            if (double.TryParse(mp.Dot512SlideStepTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var step))
            {
                return Math.Clamp(step, -32.0, 32.0);
            }

            return 1.0;
        }

        internal static int GetDot512SlideFrames(MainPage mp)
        {
            if (int.TryParse(mp.Dot512SlideFramesTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames))
            {
                return Math.Clamp(frames, 1, 2000);
            }

            return 16;
        }

        internal static IReadOnlyList<double> GetRadialFalloffBatchSizes(MainPage mp)
        {
            if (mp.RadialFalloffBatchSizesTextBox is null)
            {
                return Array.Empty<double>();
            }

            var raw = mp.RadialFalloffBatchSizesTextBox.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<double>();
            }

            var set = new HashSet<double>();
            var list = new List<double>();

            var parts = raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                {
                    continue;
                }

                // 今回の前提：S上限200
                size = Math.Clamp(size, 1, 200);
                if (set.Add(size))
                {
                    list.Add(size);
                }
            }

            list.Sort();
            return list;
        }

        internal static IReadOnlyList<double> GetDot512BatchSizes(MainPage mp)
        {
            var raw = mp.Dot512BatchSizesTextBox.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<double>();
            }

            var set = new HashSet<double>();
            var list = new List<double>();

            var parts = raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                {
                    continue;
                }

                // Dot512の描画用サイズとして扱う（端切れ防止の上限は510）
                size = Math.Clamp(size, 1, Dot512Size - 2);
                if (set.Add(size))
                {
                    list.Add(size);
                }
            }

            list.Sort();
            return list;
        }

        internal static double GetDot512BatchJitter(MainPage mp )
        {
            if (double.TryParse(mp.Dot512BatchJitterTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var jitter))
            {
                return Math.Clamp(jitter, 0.0, 8.0);
            }

            return 0.5;
        }

        internal static int GetDot512BatchCount(MainPage mp )
        {
            if (int.TryParse(mp.Dot512BatchCountTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
            {
                return Math.Clamp(count, 1, 500);
            }

            return 30;
        }

        internal static string GetDot512BatchPrefixOrDefault(MainPage mp, string suffix)
        {
            var raw = mp.Dot512BatchPrefixTextBox.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                raw = $"dot512-{suffix}";
            }

            // ファイル名として危険な文字を避ける（最低限）
            foreach (var c in System.IO.Path.GetInvalidFileNameChars())
            {
                raw = raw.Replace(c, '_');
            }

            return raw;
        }

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

        internal static IReadOnlyList<float> GetRadialFalloffBatchPs(MainPage mp)
        {
            var raw = mp.RadialFalloffBatchPsTextBox.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<float>();
            }

            var set = new HashSet<float>();
            var list = new List<float>();

            var parts = raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                {
                    continue;
                }

                p = Math.Clamp(p, 0.01f, 1.0f);

                // floatの重複は誤差が出るので丸めた値を採用
                p = (float)Math.Round(p, 4);
                if (set.Add(p))
                {
                    list.Add(p);
                }
            }

            list.Sort();
            return list;
        }

        internal static IReadOnlyList<int> GetRadialFalloffBatchNs(MainPage mp)
        {
            var raw = mp.RadialFalloffBatchNsTextBox.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<int>();
            }

            var set = new HashSet<int>();
            var list = new List<int>();

            var parts = raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                {
                    continue;
                }

                // 実行時間の暴走を避けるため上限を設ける
                n = Math.Clamp(n, 1, 200);
                if (set.Add(n))
                {
                    list.Add(n);
                }
            }

            list.Sort();
            return list;
        }

        internal static IReadOnlyList<int> GetRadialSampleRs(MainPage mp)
        {
            var raw = mp.RadialSampleRsTextBox.Text;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<int>();
            }

            var set = new HashSet<int>();
            var list = new List<int>();

            var parts = raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
                {
                    continue;
                }

                // dot512なので最大でもだいたい360台、暴走防止で上限
                r = Math.Clamp(r, 0, 1024);

                if (set.Add(r))
                {
                    list.Add(r);
                }
            }

            list.Sort();
            return list;
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
        
        internal static async System.Threading.Tasks.Task ExportDot512Async(MainPage mp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)
        {
            var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);

            var dotSize = GetDot512SizeOrNull(mp);
            if (dotSize is double s)
            {
                attributes.Size = new Size(s, s);
            }

            var pressure = GetDot512Pressure(mp);
            var n = GetDot512Overwrite(mp);

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = suggestedFileName
            };
            picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            CachedFileManager.DeferUpdates(file);

            var cx = (Dot512Size - 1) / 2f;
            var cy = (Dot512Size - 1) / 2f;

            var device = CanvasDevice.GetSharedDevice();
            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            using (var target = new CanvasRenderTarget(device, Dot512Size, Dot512Size, Dot512Dpi))
            {
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                    for (var i = 0; i < n; i++)
                    {
                        var dot = CreatePencilDot(cx, cy, pressure, attributes);
                        ds.DrawInk(new[] { dot });
                    }

                    if (includeLabels)
                    {
                        DrawDot512Labels(ds, attributes, pressure, n);
                    }
                }

                await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            }

            await CachedFileManager.CompleteUpdatesAsync(file);
        }

        internal static async System.Threading.Tasks.Task ExportDot512BatchAsync(MainPage mp,bool isTransparentBackground, bool includeLabels, string defaultSuffix)
        {
            var count = GetDot512BatchCount(mp);
            var prefix = GetDot512BatchPrefixOrDefault(mp, defaultSuffix);
            var jitter = GetDot512BatchJitter(mp);

            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            folderPicker.FileTypeFilter.Add(".png");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
            var dotSize = GetDot512SizeOrNull(mp);
            if (dotSize is double s)
            {
                attributes.Size = new Size(s, s);
            }

            var pressure = GetDot512Pressure(mp);
            var n = GetDot512Overwrite(mp);

            var cx = (Dot512Size - 1) / 2f;
            var cy = (Dot512Size - 1) / 2f;

            var rng = new Random();

            var device = CanvasDevice.GetSharedDevice();

            for (var i = 1; i <= count; i++)
            {
                var dx = (float)((rng.NextDouble() * 2.0 - 1.0) * jitter);
                var dy = (float)((rng.NextDouble() * 2.0 - 1.0) * jitter);
                var x = cx + dx;
                var y = cy + dy;

                var fileName = $"{prefix}-P{pressure:0.###}-N{n}-i{i:0000}.png";
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, Dot512Size, Dot512Size, Dot512Dpi))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                        for (var k = 0; k < n; k++)
                        {
                            var dot = CreatePencilDot(x, y, pressure, attributes);
                            ds.DrawInk(new[] { dot });
                        }

                        if (includeLabels)
                        {
                            DrawDot512Labels(ds, attributes, pressure, n);
                        }
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }
            }
        }

        internal static async System.Threading.Tasks.Task ExportDot512BatchSizesAsync(MainPage mp,bool isTransparentBackground, bool includeLabels, string defaultSuffix)
        {
            var sizes = GetDot512BatchSizes(mp);
            if (sizes.Count == 0)
            {
                // サイズ指定が無い場合は従来の一括生成にフォールバック
                await ExportDot512BatchAsync(mp, isTransparentBackground, includeLabels, defaultSuffix);
                return;
            }

            var count = GetDot512BatchCount(mp);
            var prefix = GetDot512BatchPrefixOrDefault(mp, defaultSuffix);
            var jitter = GetDot512BatchJitter(mp);

            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            folderPicker.FileTypeFilter.Add(".png");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var pressure = GetDot512Pressure(mp);
            var n = GetDot512Overwrite(mp);

            var cx = (Dot512Size - 1) / 2f;
            var cy = (Dot512Size - 1) / 2f;

            var rng = new Random();
            var device = CanvasDevice.GetSharedDevice();

            foreach (var size in sizes)
            {
                var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
                attributes.Size = new Size(size, size);

                for (var i = 1; i <= count; i++)
                {
                    var dx = (float)((rng.NextDouble() * 2.0 - 1.0) * jitter);
                    var dy = (float)((rng.NextDouble() * 2.0 - 1.0) * jitter);
                    var x = cx + dx;
                    var y = cy + dy;

                    var fileName = $"{prefix}-S{size:0.##}-P{pressure:0.###}-N{n}-i{i:0000}.png";
                    var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                    using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                    using (var target = new CanvasRenderTarget(device, Dot512Size, Dot512Size, Dot512Dpi))
                    {
                        using (var ds = target.CreateDrawingSession())
                        {
                            ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                            for (var k = 0; k < n; k++)
                            {
                                var dot = CreatePencilDot(x, y, pressure, attributes);
                                ds.DrawInk(new[] { dot });
                            }

                            if (includeLabels)
                            {
                                DrawDot512Labels(ds, attributes, pressure, n);
                            }
                        }

                        await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                    }
                }
            }
        }

        internal static async System.Threading.Tasks.Task ExportDot512SlideAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string defaultSuffix)
        {
            var frames = GetDot512SlideFrames(mp);
            var step = GetDot512SlideStep(mp);
            var prefix = GetDot512BatchPrefixOrDefault(mp, $"slide-{defaultSuffix}");

            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            folderPicker.FileTypeFilter.Add(".png");

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
            var dotSize = GetDot512SizeOrNull(mp);
            if (dotSize is double s)
            {
                attributes.Size = new Size(s, s);
            }

            var pressure = GetDot512Pressure(mp);
            var n = GetDot512Overwrite(mp);

            var cx = (Dot512Size - 1) / 2f;
            var cy = (Dot512Size - 1) / 2f;

            var device = CanvasDevice.GetSharedDevice();

            for (var i = 0; i < frames; i++)
            {
                var x = cx + (float)(step * i);
                var y = cy;

                var fileName = $"{prefix}-P{pressure:0.###}-N{n}-step{step:0.###}-f{i:0000}.png";
                var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);

                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, Dot512Size, Dot512Size, Dot512Dpi))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                        for (var k = 0; k < n; k++)
                        {
                            var dot = CreatePencilDot(x, y, pressure, attributes);
                            ds.DrawInk(new[] { dot });
                        }

                        if (includeLabels)
                        {
                            DrawDot512Labels(ds, attributes, pressure, n);
                        }
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }
            }
        }

        internal static async System.Threading.Tasks.Task ExportS200LineAsync(MainPage mp, lastProperties _lp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = suggestedFileName
            };
            picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            CachedFileManager.DeferUpdates(file);

            var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
            attributes.Size = new Size(200, 200);
            _lp._lastGeneratedAttributes = attributes;
            _lp._lastOverwritePressure = null;
            _lp._lastMaxOverwrite = null;
            _lp._lastDotGridSpacing = null;
            _lp._lastWasDotGrid = false;

            var pressure = 1.0f;

            const int exportSize = 1024;
            const float x0 = 150f;
            const float x1 = 874f;

            var device = CanvasDevice.GetSharedDevice();
            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            using (var target = new CanvasRenderTarget(device, exportSize, exportSize, Dot512Dpi))
            {
                using (var ds = target.CreateDrawingSession())
                {
                    ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                    // 1024x1024内で横線を引く（中心付近）
                    var y = (exportSize - 1) / 2f;

                    var stroke = CreatePencilStroke(x0, x1, y, pressure, attributes);
                    ds.DrawInk(new[] { stroke });

                    if (includeLabels)
                    {
                        DrawS200LineLabels(mp,ds, attributes, pressure, exportSize, x0, x1);
                    }
                }

                await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
            }

            await CachedFileManager.CompleteUpdatesAsync(file);
        }

        internal static async System.Threading.Tasks.Task ExportPngAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)
        {
            var width = GetExportWidth(mp);
            var height = GetExportHeight(mp);

            var strokes = mp.InkCanvasControl.InkPresenter.StrokeContainer.GetStrokes();
            if (strokes.Count == 0)
            {
                return;
            }

            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = suggestedFileName
            };
            picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

            var file = await picker.PickSaveFileAsync();
            if (file is null)
            {
                return;
            }

            CachedFileManager.DeferUpdates(file);

            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            {
                var device = CanvasDevice.GetSharedDevice();

                using (var target = new CanvasRenderTarget(device, width, height, 96f))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                        ds.DrawInk(strokes);

                        if (includeLabels)
                        {
                            DrawPreviewLabels(mp, ds);
                        }
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }
            }

            await CachedFileManager.CompleteUpdatesAsync(file);
        }

        internal static void DrawPreviewLabels(MainPage mp, CanvasDrawingSession ds)
        {
            var format = new CanvasTextFormat
            {
                FontSize = 22,
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            ds.DrawText("Tool=Pencil", 16, 16, Colors.Black, format);

            if (mp._lastWasDotGrid && mp._lastMaxOverwrite is int dotMax && mp._lastDotGridSpacing is int dotSpacing)
            {
                ds.DrawText("Mode=DotGrid", 16, 44, Colors.Black, format);
                ds.DrawText($"Pressure={string.Join("/", DotGridPressurePreset)}", 16, 72, Colors.Black, format);
                ds.DrawText($"MaxOverwrite={dotMax}", 16, 100, Colors.Black, format);
                ds.DrawText($"DotSpacing={dotSpacing}", 16, 128, Colors.Black, format);
            }
            else if (mp._lastMaxOverwrite is int maxOverwrite && mp._lastOverwritePressure is float pressure)
            {
                ds.DrawText($"Mode=OverwriteSamples", 16, 44, Colors.Black, format);
                ds.DrawText($"Pressure={pressure:0.###}", 16, 72, Colors.Black, format);
                ds.DrawText($"MaxOverwrite={maxOverwrite}", 16, 100, Colors.Black, format);
            }
            else
            {
                ds.DrawText($"Mode=PressurePreset", 16, 44, Colors.Black, format);
                ds.DrawText($"Pressure={string.Join("/", PressurePreset)}", 16, 72, Colors.Black, format);
            }

            var w = mp._lastGeneratedAttributes?.Size.Width;
            if (w != null)
            {
                ds.DrawText($"StrokeWidth={w.Value:0.##}", 16, 156, Colors.Black, format);
            }

            var c = mp._lastGeneratedAttributes?.Color;
            if (c != null)
            {
                ds.DrawText($"Color=ARGB({c.Value.A},{c.Value.R},{c.Value.G},{c.Value.B})", 16, 184, Colors.Black, format);
            }

            ds.DrawText($"Export={GetExportWidth(mp)}x{GetExportHeight(mp)}", 16, 212, Colors.Black, format);

            if (mp._lastWasDotGrid && mp._lastMaxOverwrite is int maxOverwrite2 && mp._lastDotGridSpacing is int spacing)
            {
                // Column labels (pressure)
                for (var col = 0; col < DotGridPressurePreset.Length; col++)
                {
                    var x = DefaultDotGridStartX + (col * spacing);
                    ds.DrawText($"P={DotGridPressurePreset[col]:0.0}", x - 24, DefaultDotGridStartY - 48, Colors.Black, format);
                }

                // Row labels (N)
                for (var row = 1; row <= maxOverwrite2; row++)
                {
                    var y = DefaultDotGridStartY + ((row - 1) * spacing);
                    ds.DrawText($"N={row}", DefaultDotGridStartX - 120, y - 12, Colors.Black, format);
                }
            }
            else if (mp._lastMaxOverwrite is int maxOverwrite3)
            {
                for (var i = 1; i <= maxOverwrite3; i++)
                {
                    var y = DefaultStartY + ((i - 1) * DefaultSpacingY);
                    ds.DrawText($"N={i}", 16, y - 12, Colors.Black, format);
                }
            }
            else
            {
                for (var i = 0; i < PressurePreset.Length; i++)
                {
                    var y = DefaultStartY + (i * DefaultSpacingY);
                    ds.DrawText($"P={PressurePreset[i]:0.0}", 16, y - 12, Colors.Black, format);
                }
            }
        }

        internal static void DrawS200LineLabels(MainPage mp, CanvasDrawingSession ds, InkDrawingAttributes attributes, float pressure, int exportSize, float x0, float x1)
        {
            var format = new CanvasTextFormat
            {
                FontSize = 18,
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            ds.DrawText("Mode=Line", 16, 16, Colors.Black, format);
            ds.DrawText("S=200", 16, 40, Colors.Black, format);
            ds.DrawText($"Pressure={pressure:0.###}", 16, 64, Colors.Black, format);
            ds.DrawText($"Export={exportSize}x{exportSize}", 16, 88, Colors.Black, format);
            ds.DrawText($"X={x0:0.##}..{x1:0.##}", 16, 112, Colors.Black, format);
            ds.DrawText($"StrokeWidth={attributes.Size.Width:0.##}", 16, 136, Colors.Black, format);
            ds.DrawText($"Color=ARGB({attributes.Color.A},{attributes.Color.R},{attributes.Color.G},{attributes.Color.B})", 16, 160, Colors.Black, format);
        }

        internal static void DrawDot512Labels(CanvasDrawingSession ds, InkDrawingAttributes attributes, float pressure, int n)
        {
            var format = new CanvasTextFormat
            {
                FontSize = 18,
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            ds.DrawText("Mode=Dot512", 16, 16, Colors.Black, format);
            ds.DrawText($"Pressure={pressure:0.###}", 16, 40, Colors.Black, format);
            ds.DrawText($"N={n}", 16, 64, Colors.Black, format);
            ds.DrawText($"Export=512x512", 16, 88, Colors.Black, format);

            ds.DrawText($"StrokeWidth={attributes.Size.Width:0.##}", 16, 112, Colors.Black, format);
            ds.DrawText($"Color=ARGB({attributes.Color.A},{attributes.Color.R},{attributes.Color.G},{attributes.Color.B})", 16, 136, Colors.Black, format);
        }

        internal static async Task ExportNormalizedFalloffAsync(MainPage mp)
        {
            var s0 = GetNormalizedFalloffS0(mp);

            var folderPicker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            folderPicker.FileTypeFilter.Add(".csv");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var files = await folder.GetFilesAsync();
            var samples = new List<(double s, double p, int n, double[] fr)>();

            var skipped = 0;
            foreach (var f in files)
            {
                if (!f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!f.Name.StartsWith("radial-falloff-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseFalloffFilename(f.Name, out var s, out var p, out var n))
                {
                    skipped++;
                    continue;
                }

                var text = await FileIO.ReadTextAsync(f);
                if (!TryParseFalloffCsv(text, out var fr))
                {
                    skipped++;
                    continue;
                }

                // S上限200前提（念のため）
                if (s <= 0 || s > 200)
                {
                    skipped++;
                    continue;
                }

                samples.Add((s, p, n, fr));
            }

            if (samples.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "正規化mean/stddev",
                    Content = "対象CSVが見つかりませんでした（radial-falloff-S*-P*-N*.csv）。",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

            // P/Nが混在していると平均に意味が無いので、最多の(P,N)だけを採用する
            var groupCounts = new Dictionary<(double p, int n), int>();
            foreach (var s in samples)
            {
                var key = (s.p, s.n);
                groupCounts.TryGetValue(key, out var c);
                groupCounts[key] = c + 1;
            }

            (double p, int n) selected = default;
            var bestCount = -1;
            foreach (var kv in groupCounts)
            {
                if (kv.Value > bestCount)
                {
                    bestCount = kv.Value;
                    selected = kv.Key;
                }
            }

            var filtered = new List<(double s, double[] fr)>();
            foreach (var s in samples)
            {
                if (s.p == selected.p && s.n == selected.n)
                {
                    filtered.Add((s.s, s.fr));
                }
        }

            if (filtered.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "正規化mean/stddev",
                    Content = "集計対象が空です。",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

            // r_norm軸は整数pxとして 0..(S0/2) を採用（dotの有効範囲を想定）
            var rMax = Math.Max(1, s0 / 2);
            var sum = new double[rMax + 1];
            var sumSq = new double[rMax + 1];

            foreach (var (s, fr) in filtered)
            {
                var scale = (double)s0 / s; // r_norm = r * scale

                for (var rNorm = 0; rNorm <= rMax; rNorm++)
                {
                    var r = rNorm / scale; // 元CSV半径に戻す
                    var v = SampleLinear(fr, r);
                    sum[rNorm] += v;
                    sumSq[rNorm] += v * v;
                }
            }

            var mean = new double[rMax + 1];
            var stddev = new double[rMax + 1];
            for (var i = 0; i <= rMax; i++)
            {
                var m = sum[i] / filtered.Count;
                var v = sumSq[i] / filtered.Count - m * m;
                mean[i] = m;
                stddev[i] = Math.Sqrt(Math.Max(0.0, v));
            }

            var csv = BuildNormalizedFalloffCsv(mean, stddev, filtered.Count, s0, selected.p, selected.n);
            var outName = $"normalized-falloff-S0{s0}-P{selected.p:0.###}-N{selected.n}.csv";
            var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var done = new ContentDialog
            {
                Title = "正規化mean/stddev",
                Content = $"完了: {filtered.Count}件を集計しました。スキップ={skipped}件。\n出力={outName}",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }

    }
}
