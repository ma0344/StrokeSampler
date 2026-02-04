using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Windows.UI;

namespace StrokeSampler
{
    internal static class UIHelpers
    {

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


        internal static IReadOnlyList<float> GetDot512BatchPs(MainPage mp)
        {
            if (mp.Dot512BatchPsTextBox is null)
            {
                return Array.Empty<float>();
            }

            var raw = mp.Dot512BatchPsTextBox.Text;
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

        internal static IReadOnlyList<int> GetDot512BatchNs(MainPage mp)
        {
            if (mp.Dot512BatchNsTextBox is null)
            {
                return Array.Empty<int>();
            }

            var raw = mp.Dot512BatchNsTextBox.Text;
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
                return Math.Clamp(width, 16, 16384);
            }

            return 4096;
        }

        internal static int GetExportHeight(MainPage mp)
        {
            if (int.TryParse(mp.ExportHeightTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
            {
                return Math.Clamp(height, 16, 16384);
            }

            return 4096;
        }

        internal static int GetMaxOverwrite(MainPage mp)
        {
            if (int.TryParse(mp.MaxOverwriteTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxOverwrite))
            {
                return Math.Clamp(maxOverwrite, 1, 50);
            }

            return MainPage.DefaultMaxOverwrite;
        }

        internal static float GetOverwritePressure(MainPage mp)
        {
            if (float.TryParse(mp.OverwritePressureNumberBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pressure))
            {
                return Math.Clamp(pressure, 0.01f, 1.0f);
            }

            return MainPage.DefaultOverwritePressure;
        }

        internal static int GetDotGridSpacing(MainPage mp)
        {
            if (int.TryParse(mp.DotGridSpacingTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var spacing))
            {
                return Math.Clamp(spacing, 40, 800);
            }

            return MainPage.DefaultDotGridSpacing;
        }

        internal static int GetRadialBinSize(MainPage mp)
        {
            if (int.TryParse(mp.RadialBinSizeTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bin))
            {
                return Math.Clamp(bin, 1, 32);
            }

            return 1;
        }

        internal static int ParseLinePointCount(string? text)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                return Math.Clamp(n, 2, 10000);
            }
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out n))
            {
                return Math.Clamp(n, 2, 10000);
            }
            return 2;
        }

        internal static float ParseLinePointStep(string? text)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var step))
            {
                return Math.Clamp(step, 0.1f, 2000f);
            }
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out step))
            {
                return Math.Clamp(step, 0.1f, 2000f);
            }
            return 4f;
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
                return Math.Clamp(size, 1, MainPage.Dot512Size - 2);
            }

            return null;
        }

        internal static float GetDot512Pressure(MainPage mp)
        {
            if (float.TryParse(mp.Dot512PressureNumberBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pressure))
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
                size = Math.Clamp(size, 1, MainPage.Dot512Size - 2);
                if (set.Add(size))
                {
                    list.Add(size);
                }
            }

            list.Sort();
            return list;
        }

        internal static double GetDot512BatchJitter(MainPage mp)
        {
            if (double.TryParse(mp.Dot512BatchJitterTextBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var jitter))
            {
                return Math.Clamp(jitter, 0.0, 8.0);
            }

            return 0.5;
        }

        internal static int GetDot512BatchCount(MainPage mp)
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

    }
}