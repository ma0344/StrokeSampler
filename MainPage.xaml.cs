using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace StrokeSampler
{
    public sealed partial class MainPage : Page
    {
        private int _lineSyncGuard;

        public const double PencilStrokeWidthMin = 0.5;
        // 解析目的で大きいSizeも扱えるように上限を拡張する。
        // Dot512の端切れ防止は`GetDot512SizeOrNull`等で別途行う。
        public const double PencilStrokeWidthMax = 510.0;

        public static readonly float[] PressurePreset = {0.01f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f };

        public const float DefaultOverwritePressure = 0.5f;
        public const int DefaultMaxOverwrite = 10;

        public const float DefaultStartX = 160f;
        public const float DefaultEndX = 1800f;
        public const float DefaultStartY = 120f;
        public const float DefaultSpacingY = 110f;

        public InkDrawingAttributes _lastGeneratedAttributes;
        public float? _lastOverwritePressure;
        public int? _lastMaxOverwrite;
        public int? _lastDotGridSpacing;
        public bool _lastWasDotGrid;

        public static readonly float[] DotGridPressurePreset = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f };
        public const float DefaultDotGridStartX = 260f;
        public const float DefaultDotGridStartY = 260f;
        public const int DefaultDotGridSpacing = 120;

        public const int PaperNoiseCropSize = 24;
        public const int PaperNoiseCropHalf = PaperNoiseCropSize / 2;

        public const int Dot512Size = 512;
        public const float Dot512Dpi = 96f;


        public static readonly int[] RadialAlphaThresholds = StrokeHelpers.CreateRadialAlphaThresholds();

        public MainPage()
        {
            InitializeComponent();

            HookLineAutoCalcEvents();

            InkCanvasControl.InkPresenter.InputDeviceTypes = Windows.UI.Core.CoreInputDeviceTypes.Mouse
                                                            | Windows.UI.Core.CoreInputDeviceTypes.Pen
                                                            | Windows.UI.Core.CoreInputDeviceTypes.Touch;

            UpdateZoomFactorText();
        }

        private void HookLineAutoCalcEvents()
        {
            // UI補助機能（失敗してもアプリの本体機能には影響させない）
            try
            {
                if (LineTotalLengthTextBox != null)
                {
                    LineTotalLengthTextBox.TextChanged += LineTotalLengthTextBox_TextChanged;
                }
                if (LinePointStepTextBox != null)
                {
                    LinePointStepTextBox.TextChanged += LinePointStepTextBox_TextChanged;
                }
                if (LinePointCountTextBox != null)
                {
                    LinePointCountTextBox.TextChanged += LinePointCountTextBox_TextChanged;
                }

                UpdateLineTotalLengthFromStepAndPts();
            }
            catch
            {
                // ignore
            }
        }

        private void LineTotalLengthTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Interlocked.Exchange(ref _lineSyncGuard, 1) != 0) return;
            try
            {
                if (TryGetLinePts(out var pts) && TryGetLineTotalLength(out var l) && pts >= 2)
                {
                    var denom = pts - 1;
                    if (denom <= 0) return;
                    var step = l / denom;
                    if (LinePointStepTextBox != null)
                    {
                        LinePointStepTextBox.Text = step.ToString("0.############", CultureInfo.InvariantCulture);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _lineSyncGuard, 0);
            }
        }

        private void LinePointStepTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Interlocked.Exchange(ref _lineSyncGuard, 1) != 0) return;
            try
            {
                UpdateLineTotalLengthFromStepAndPts();
            }
            finally
            {
                Interlocked.Exchange(ref _lineSyncGuard, 0);
            }
        }

        private void LinePointCountTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (Interlocked.Exchange(ref _lineSyncGuard, 1) != 0) return;
            try
            {
                UpdateLineTotalLengthFromStepAndPts();
            }
            finally
            {
                Interlocked.Exchange(ref _lineSyncGuard, 0);
            }
        }

        private void UpdateLineTotalLengthFromStepAndPts()
        {
            if (LineTotalLengthTextBox == null) return;
            if (!TryGetLinePts(out var pts) || pts < 2) return;
            if (!TryGetLineStep(out var step)) return;

            var l = step * (pts - 1);
            LineTotalLengthTextBox.Text = l.ToString("0.############", CultureInfo.InvariantCulture);
        }

        private bool TryGetLinePts(out int pts)
        {
            pts = 0;
            var s = LinePointCountTextBox?.Text;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out pts))
            {
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out pts)) return false;
            }
            return true;
        }

        private bool TryGetLineStep(out double step)
        {
            step = 0;
            var s = LinePointStepTextBox?.Text;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out step))
            {
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out step)) return false;
            }
            return true;
        }

        private bool TryGetLineTotalLength(out double l)
        {
            l = 0;
            var s = LineTotalLengthTextBox?.Text;
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out l))
            {
                if (!double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out l)) return false;
            }
            return true;
        }

        private void InkScrollViewer_ViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            // ViewChanged中は継続的に値が変化するので、UIスレッド後段でもう一度読み直す
            UpdateZoomFactorText();
            _ = Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Low, UpdateZoomFactorText);
        }

        private void UpdateZoomFactorText()
        {
            if (InkScrollViewer == null || ZoomFactorTextBlock == null) return;
            var z = InkScrollViewer.ZoomFactor;
            var zx = z.ToString("0.00", CultureInfo.InvariantCulture);
            var zp = (z * 100.0).ToString("0", CultureInfo.InvariantCulture);
            var zRaw = z.ToString("0.######", CultureInfo.InvariantCulture);
            ZoomFactorTextBlock.Text = $"{zx}x ({zp}%)  raw={zRaw}";

            if (ZoomFactorInputTextBox != null && ZoomFactorInputTextBox.FocusState == Windows.UI.Xaml.FocusState.Unfocused)
            {
                // 操作中は上書きしない（数値入力を邪魔しない）
                ZoomFactorInputTextBox.Text = zRaw;
            }
        }

        private void ApplyZoomButton_Click(object sender, RoutedEventArgs e)
        {
            if (InkScrollViewer == null) return;

            var text = ZoomFactorInputTextBox?.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            double z;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            {
                // 日本ロケール向けにカンマも許容
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out z))
                {
                    return;
                }
            }

            if (z <= 0) return;

            // XAMLで設定しているMin/Maxに合わせる
            var min = (double)InkScrollViewer.MinZoomFactor;
            var max = (double)InkScrollViewer.MaxZoomFactor;
            if (z < min) z = min;
            if (z > max) z = max;

            // 現在の中心を維持したままズーム
            InkScrollViewer.ChangeView(null, null, (float)z, disableAnimation: true);
            UpdateZoomFactorText();
        }
        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateHelper.Generate(this);
        }

        private void GenerateOverwriteSamplesButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateHelper.GenerateOverwriteSamples(this);
        }

        private void GenerateDotGridButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateHelper.GenerateDotGrid(this);
        }

        private void GenerateDotGridFixedConditionButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateHelper.GenerateDotGridFixedCondition(this);
        }

        private void DrawS200StrokeButton_Click(object sender, RoutedEventArgs e)
        {
            // 200px幅のPencilストロークを生成して、紙目のサンプル範囲を広げる
            var attributes = StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(this);
            if (IgnorePressureCheckBox?.IsChecked == true) attributes.IgnorePressure = true;
            var strokeWidth = UIHelpers.GetDot512SizeOrNull(this) ?? 200.0;
            attributes.Size = new Size(strokeWidth, strokeWidth);

            // Start/End入力があればそれを使う。未指定は従来値。
            var startX = 260f;
            var y = 440f;
            if (TryParsePointText(StartPositionTextBox?.Text, out var sx, out var sy))
            {
                startX = sx;
                y = sy;
            }

            var endX = startX + 1000f;
            if (TryParsePointText(EndPositionTextBox?.Text, out var ex, out var ey))
            {
                endX = ex;
                // End側は自由入力だが、yが未指定の場合の補助として一致させる
                if (Math.Abs(ey - y) > 0.01f)
                {
                    // 横線なのでStart側yを優先
                }
            }
            float pressure = UIHelpers.GetDot512Pressure(this);

            InkStroke stroke;
            var modeIndex = S200StrokeModeComboBox?.SelectedIndex ?? 0;
            if (modeIndex == 1)
            {
                // step=4
                stroke = StrokeHelpers.CreatePencilStroke(startX, endX, y, pressure, attributes);
            }
            else
            {
                // 2 points
                stroke = StrokeHelpers.CreatePencilStroke2Points(startX, endX, y, pressure, attributes);
            }

            InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(stroke);
        }

        private void DrawDotButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryParsePointText(StartPositionTextBox?.Text, out var x, out var y))
            {
                x = 260;
                y = 440;
            }

            var attributes = StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(this);
            if (IgnorePressureCheckBox?.IsChecked == true) attributes.IgnorePressure = true;
            var size = UIHelpers.GetDot512SizeOrNull(this);
            if (size.HasValue)
            {
                attributes.Size = new Size(size.Value, size.Value);
            }

            var pressure = UIHelpers.GetDot512Pressure(this);
            var stroke = StrokeHelpers.CreatePencilDot(x, y, pressure, attributes);
            InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(stroke);
        }

        private void DrawStairLinesButton_Click(object sender, RoutedEventArgs e)
        {
            // Start/End入力を基準に、水平線をdyずつずらして複数本描画
            var startX = 260f;
            var y0 = 440f;
            if (TryParsePointText(StartPositionTextBox?.Text, out var sx, out var sy))
            {
                startX = sx;
                y0 = sy;
            }

            var endX = startX + 1000f;
            if (TryParsePointText(EndPositionTextBox?.Text, out var ex, out _))
            {
                endX = ex;
            }

            int count;
            if (!int.TryParse(StairCountTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
            {
                if (!int.TryParse(StairCountTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out count))
                {
                    count = 20;
                }
            }
            if (count < 1) count = 1;
            if (count > 500) count = 500;

            float dy;
            if (!float.TryParse(StairDyTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out dy))
            {
                if (!float.TryParse(StairDyTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out dy))
                {
                    dy = 2f;
                }
            }
            if (Math.Abs(dy) < 0.1f) dy = 0.1f;

            var attributes = StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(this);
            if (IgnorePressureCheckBox?.IsChecked == true) attributes.IgnorePressure = true;
            var strokeWidth = UIHelpers.GetDot512SizeOrNull(this) ?? 200.0;
            attributes.Size = new Size(strokeWidth, strokeWidth);
            var pressure = UIHelpers.GetDot512Pressure(this);

            var modeIndex = S200StrokeModeComboBox?.SelectedIndex ?? 0;
            for (var i = 0; i < count; i++)
            {
                var y = y0 + i * dy;
                InkStroke stroke = modeIndex == 1
                    ? StrokeHelpers.CreatePencilStroke(startX, endX, y, pressure, attributes)
                    : StrokeHelpers.CreatePencilStroke2Points(startX, endX, y, pressure, attributes);
                InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(stroke);
            }
        }

        private static bool TryParsePointText(string text, out float x, out float y)
        {
            x = 0;
            y = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split(',');
            if (parts.Length != 2) return false;

            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x))
            {
                if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out x)) return false;
            }

            if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            {
                if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out y)) return false;
            }

            return true;
        }

        private async void ExportAveragedTileButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportAveragedTileCoreAsync(transparentOutput: false);
        }

        private async void ExportAveragedTileTransparentButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportAveragedTileCoreAsync(transparentOutput: true);
        }

        private async System.Threading.Tasks.Task ExportAveragedTileCoreAsync(bool transparentOutput)
        {
            int tileSize;
            if (!int.TryParse(TileSizeTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out tileSize))
            {
                if (!int.TryParse(TileSizeTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out tileSize))
                {
                    return;
                }
            }

            int ox = 0;
            int oy = 0;
            if (!string.IsNullOrWhiteSpace(TileOffsetTextBox?.Text))
            {
                if (TryParsePointText(TileOffsetTextBox.Text, out var fx, out var fy))
                {
                    ox = (int)Math.Round(fx);
                    oy = (int)Math.Round(fy);
                }
            }

            if (tileSize <= 0) return;
            var autoOffset = AutoTileOffsetCheckBox?.IsChecked == true;

            var flatten = FlattenTileCheckBox?.IsChecked == true;
            int flattenRadius;
            if (!int.TryParse(FlattenRadiusTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out flattenRadius))
            {
                if (!int.TryParse(FlattenRadiusTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out flattenRadius))
                {
                    flattenRadius = 32;
                }
            }

            if (flattenRadius < 1) flattenRadius = 1;

            await ExportTileAveragedPaperNoise.ExportAsync(tileSize, ox, oy, transparentOutput, autoOffset, flatten, flattenRadius);
        }

        private void DrawS200StrokeVerticalButton_Click(object sender, RoutedEventArgs e)
        {
            var attributes = StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(this);
            if (IgnorePressureCheckBox?.IsChecked == true) attributes.IgnorePressure = true;
            var strokeWidth = UIHelpers.GetDot512SizeOrNull(this) ?? 200.0;
            attributes.Size = new Size(strokeWidth, strokeWidth);

            // Start/End入力があればそれを使う。未指定は従来値。
            var x = 360f;
            var startY = 260f;
            if (TryParsePointText(StartPositionTextBox?.Text, out var sx, out var sy))
            {
                x = sx;
                startY = sy;
            }

            var endY = startY + 1000f;
            if (TryParsePointText(EndPositionTextBox?.Text, out var ex, out var ey))
            {
                // 縦線なのでxはStart側を優先し、End側はYのみ採用
                endY = ey;
                if (Math.Abs(ex - x) > 0.01f)
                {
                    // 縦線なのでStart側xを優先
                }
            }
            float pressure = UIHelpers.GetDot512Pressure(this);

            InkStroke stroke;
            var modeIndex = S200StrokeModeComboBox?.SelectedIndex ?? 0;
            if (modeIndex == 1)
            {
                stroke = StrokeHelpers.CreatePencilStrokeVertical(x, startY, endY, pressure, attributes);
            }
            else
            {
                stroke = StrokeHelpers.CreatePencilStrokeVertical2Points(x, startY, endY, pressure, attributes);
            }

            InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(stroke);
        }

        private async void EstimateTilePeriodButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                    ViewMode = PickerViewMode.Thumbnail
                };
                picker.FileTypeFilter.Add(".png");
                picker.FileTypeFilter.Add(".jpg");
                picker.FileTypeFilter.Add(".jpeg");

                var file = await picker.PickSingleFileAsync();
                if (file == null) return;

                byte[] pixels;
                int w;
                int h;
                using (var stream = await file.OpenReadAsync())
                {
                    var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
                    var pixelProvider = await decoder.GetPixelDataAsync();
                    pixels = pixelProvider.DetachPixelData();
                    w = (int)decoder.PixelWidth;
                    h = (int)decoder.PixelHeight;
                }
                if (w <= 0 || h <= 0)
                {
                    TilePeriodTextBlock.Text = "(invalid)";
                    return;
                }

                var stride = w * 4;
                var axisIndex = TilePeriodAxisComboBox?.SelectedIndex ?? 0;
                if (axisIndex == 1)
                {
                    // Y方向: 縦線の描画列（目安: x=360）付近を優先。範囲外なら中央。
                    var x = w / 2;
                    if (w > 400) x = 360;
                    var col = new double[h];
                    for (var y = 0; y < h; y++)
                    {
                        var i = y * stride + x * 4;
                        if ((uint)(i + 3) >= (uint)pixels.Length)
                        {
                            col[y] = 0;
                            continue;
                        }
                        var b = pixels[i + 0];
                        var g = pixels[i + 1];
                        var r = pixels[i + 2];
                        col[y] = (r + g + b) / (3.0 * 255.0);
                    }

                    var minLag = Math.Max(8, h / 256);
                    var maxLag = Math.Min(h / 2, 4096);
                    var result = TilePeriodEstimator.EstimatePeriodByAutocorrelation(col, minLag, maxLag);
                    if (result == null)
                    {
                        TilePeriodTextBlock.Text = "(n/a)";
                        return;
                    }
                    TilePeriodTextBlock.Text = $"{result.PeriodPx}px (score={result.Score:0.###})";
                    return;
                }

                // X方向: 横線の描画行（目安: y=440）付近を優先。範囲外なら中央。
                var yRow = h / 2;
                if (h > 480) yRow = 440;
                var row = new double[w];
                var baseIdx = yRow * stride;
                for (var x = 0; x < w; x++)
                {
                    var i = baseIdx + x * 4;
                    if ((uint)(i + 3) >= (uint)pixels.Length)
                    {
                        row[x] = 0;
                        continue;
                    }
                    var b = pixels[i + 0];
                    var g = pixels[i + 1];
                    var r = pixels[i + 2];
                    row[x] = (r + g + b) / (3.0 * 255.0);
                }

                var minLagX = Math.Max(8, w / 256);
                var maxLagX = Math.Min(w / 2, 4096);
                var resultX = TilePeriodEstimator.EstimatePeriodByAutocorrelation(row, minLagX, maxLagX);
                if (resultX == null)
                {
                    TilePeriodTextBlock.Text = "(n/a)";
                    return;
                }

                TilePeriodTextBlock.Text = $"{resultX.PeriodPx}px (score={resultX.Score:0.###})";
            }
            catch (ArgumentException ex)
            {
                TilePeriodTextBlock.Text = ex.GetType().Name;
            }
            catch (InvalidOperationException ex)
            {
                TilePeriodTextBlock.Text = ex.GetType().Name;
            }
        }
        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            InkCanvasControl.InkPresenter.StrokeContainer.Clear();

            _lastGeneratedAttributes = null;
            _lastOverwritePressure = null;
            _lastMaxOverwrite = null;
            _lastDotGridSpacing = null;
            _lastWasDotGrid = false;
        }

        private async void ExportRadialSamplesSummaryButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportRadialSamplesSummary.ExportAsync(this);
         }

        private async void ExportS200AlignedDotIndexRepeatButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale))
            {
                if (!int.TryParse(ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out scale)) return;
            }

            // P は Dot512 Pressure を使う
            var pressure = 0.5f;
            if (float.TryParse(Dot512PressureNumberBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            {
                pressure = p;
            }
            else if (float.TryParse(Dot512PressureNumberBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out p))
            {
                pressure = p;
            }

            if (!TryParsePointText(StartPositionTextBox?.Text, out var sxF, out var syF)) return;
            if (!TryParsePointText(EndPositionTextBox?.Text, out var exF, out var eyF)) return;

            var sx = (double)sxF;
            var sy = (double)syF;
            var ex = (double)exF;
            var ey = (double)eyF;

            var lDip = Math.Sqrt(((ex - sx) * (ex - sx)) + ((ey - sy) * (ey - sy)));
            if (double.TryParse(LineTotalLengthTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var lTmp) && lTmp > 0)
            {
                lDip = lTmp;
            }
            else if (double.TryParse(LineTotalLengthTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out lTmp) && lTmp > 0)
            {
                lDip = lTmp;
            }

            if (!int.TryParse(ExportWidthTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outW))
            {
                if (!int.TryParse(ExportWidthTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out outW)) return;
            }
            if (!int.TryParse(ExportHeightTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outH))
            {
                if (!int.TryParse(ExportHeightTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out outH)) return;
            }

            var periodStepDip = 1.75;
            if (double.TryParse(S200AlignedPeriodDipTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pd) && pd > 0)
            {
                periodStepDip = pd;
            }
            else if (double.TryParse(S200AlignedPeriodDipTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out pd) && pd > 0)
            {
                periodStepDip = pd;
            }

            var dotCount = 12;
            if (int.TryParse(S200AlignedDotCountTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dc) && dc > 0)
            {
                dotCount = dc;
            }
            else if (int.TryParse(S200AlignedDotCountTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out dc) && dc > 0)
            {
                dotCount = dc;
            }
            dotCount = Math.Clamp(dotCount, 1, 200);

            var repeat = 50;
            if (int.TryParse(S200AlignedRepeatTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rep) && rep > 0)
            {
                repeat = rep;
            }
            else if (int.TryParse(S200AlignedRepeatTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out rep) && rep > 0)
            {
                repeat = rep;
            }
            repeat = Math.Clamp(repeat, 1, 10000);

            var folderPicker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary };
            folderPicker.FileTypeFilter.Add(".png");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null) return;

            await ExportS200Service.ExportAlignedDotIndexSeriesRepeatedAsync(
                mp: this,
                folder: folder,
                isTransparentBackground: true,
                pressure: pressure,
                exportScale: scale,
                dotCount: dotCount,
                repeat: repeat,
                periodStepDip: periodStepDip,
                startXDip: sx,
                startYDip: sy,
                lDip: lDip,
                outWidthDip: outW,
                outHeightDip: outH,
                roiLeftDip: 0,
                roiTopDip: 0);
        }

        // XAMLのイベントハンドラが参照しているため、欠落するとビルドが失敗する。
        private async void ComparePaperNoiseModelsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "紙目モデル比較",
                Content = "このビルドでは紙目モデル比較の処理が無効です。必要なら機能を復元してください。",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }

        private async void ExportRadialFalloffBatchSizesNsButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialFalloffBatchSizesNsAsync(this);
        }

        private async void ExportRadialFalloffBatchPsSizesNsButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialFalloffBatchPsSizesNsAsync(this);
        }

        private async void ExportCenterAlphaSummaryButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportCenterAlphaSummary.ExportAsync(this);
        }

        private async void ExportRadialFalloffBatchButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialFalloffBatchAsync(this);
        }

        private async void ExportRadialFalloffHiResFromPngButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialFalloffCsvFromHiResPngAsync(this);
        }

        private async void ExportMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportPngService.ExportAsync(mp: this,isTransparentBackground: true,includeLabels: false,suggestedFileName: "pencil-material");
        }

        private async void ExportPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportPngService.ExportAsync(mp: this,isTransparentBackground: false,includeLabels: true,suggestedFileName: "pencil-preview");
        }

        private async void ExportS200AlignedDotIndexButton_Click(object sender, RoutedEventArgs e)
        {
            // S=200 の「n番目の更新点（スタンプ）が同一点に来る」連番PNGを出力
            // 既存の運用パラメータに合わせたデフォルト。
            if (!int.TryParse(ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale))
            {
                if (!int.TryParse(ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out scale))
                {
                    return;
                }
            }

            // AlignedDotsのPは Dot512 Pressure 入力に合わせる（OverwritePressureは別用途）
            var pressure = 0.5f;
            if (float.TryParse(Dot512PressureNumberBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            {
                pressure = p;
            }
            else if (float.TryParse(Dot512PressureNumberBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out p))
            {
                pressure = p;
            }

            if (!TryParsePointText(StartPositionTextBox?.Text, out var sxF, out var syF)) return;
            if (!TryParsePointText(EndPositionTextBox?.Text, out var exF, out var eyF)) return;

            var sx = (double)sxF;
            var sy = (double)syF;
            var ex = (double)exF;
            var ey = (double)eyF;

            var lDip = Math.Sqrt(((ex - sx) * (ex - sx)) + ((ey - sy) * (ey - sy)));
            if (double.TryParse(LineTotalLengthTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var lTmp) && lTmp > 0)
            {
                lDip = lTmp;
            }
            else if (double.TryParse(LineTotalLengthTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out lTmp) && lTmp > 0)
            {
                lDip = lTmp;
            }

            if (!int.TryParse(ExportWidthTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outW))
            {
                if (!int.TryParse(ExportWidthTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out outW)) return;
            }
            if (!int.TryParse(ExportHeightTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outH))
            {
                if (!int.TryParse(ExportHeightTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out outH)) return;
            }

            var periodStepDip = 1.75;
            if (double.TryParse(S200AlignedPeriodDipTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pd) && pd > 0)
            {
                periodStepDip = pd;
            }
            else if (double.TryParse(S200AlignedPeriodDipTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out pd) && pd > 0)
            {
                periodStepDip = pd;
            }

            var dotCount = 12;
            if (int.TryParse(S200AlignedDotCountTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dc) && dc > 0)
            {
                dotCount = dc;
            }
            else if (int.TryParse(S200AlignedDotCountTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out dc) && dc > 0)
            {
                dotCount = dc;
            }

            dotCount = Math.Clamp(dotCount, 1, 200);

            await ExportS200Service.ExportAlignedDotIndexSeriesAsync(
                mp: this,
                isTransparentBackground: true,
                pressure: pressure,
                exportScale: scale,
                dotCount: dotCount,
                periodStepDip: periodStepDip,
                startXDip: sx,
                startYDip: sy,
                lDip: lDip,
                outWidthDip: outW,
                outHeightDip: outH,
                roiLeftDip: 0,
                roiTopDip: 0);
        }

        private async void ExportS200AlignedDotIndexBatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var scale))
            {
                if (!int.TryParse(ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out scale)) return;
            }

            if (!TryParsePointText(StartPositionTextBox?.Text, out var sxF, out var syF)) return;
            if (!TryParsePointText(EndPositionTextBox?.Text, out var exF, out var eyF)) return;

            var sx = (double)sxF;
            var sy = (double)syF;
            var ex = (double)exF;
            var ey = (double)eyF;

            var lDip = Math.Sqrt(((ex - sx) * (ex - sx)) + ((ey - sy) * (ey - sy)));
            if (double.TryParse(LineTotalLengthTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var lTmp) && lTmp > 0)
            {
                lDip = lTmp;
            }
            else if (double.TryParse(LineTotalLengthTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out lTmp) && lTmp > 0)
            {
                lDip = lTmp;
            }

            if (!int.TryParse(ExportWidthTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outW))
            {
                if (!int.TryParse(ExportWidthTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out outW)) return;
            }
            if (!int.TryParse(ExportHeightTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outH))
            {
                if (!int.TryParse(ExportHeightTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out outH)) return;
            }

            var periodStepDip = 1.75;
            if (double.TryParse(S200AlignedPeriodDipTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var pd) && pd > 0)
            {
                periodStepDip = pd;
            }
            else if (double.TryParse(S200AlignedPeriodDipTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out pd) && pd > 0)
            {
                periodStepDip = pd;
            }

            var dotCount = 12;
            if (int.TryParse(S200AlignedDotCountTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dc) && dc > 0)
            {
                dotCount = dc;
            }
            else if (int.TryParse(S200AlignedDotCountTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out dc) && dc > 0)
            {
                dotCount = dc;
            }
            dotCount = Math.Clamp(dotCount, 1, 200);

            // 現在のUI入力値から圧力を取得（Dot512 Pressure）
            var basePressure = 0.5f;
            if (float.TryParse(Dot512PressureNumberBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            {
                basePressure = p;
            }
            else if (float.TryParse(Dot512PressureNumberBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out p))
            {
                basePressure = p;
            }

            // basePressure は未使用（UI互換のため保持）
            _ = basePressure;

            var folderPicker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary };
            folderPicker.FileTypeFilter.Add(".png");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null) return;

            var pStart = 0.01;
            var pEnd = 1.0;
            var pStep = 0.05;

            if (!double.TryParse(S200AlignedBatchPStartTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out pStart))
            {
                _ = double.TryParse(S200AlignedBatchPStartTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out pStart);
            }
            if (!double.TryParse(S200AlignedBatchPEndTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out pEnd))
            {
                _ = double.TryParse(S200AlignedBatchPEndTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out pEnd);
            }
            if (!double.TryParse(S200AlignedBatchPStepTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out pStep))
            {
                _ = double.TryParse(S200AlignedBatchPStepTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out pStep);
            }

            if (pStep <= 0) return;
            if (pStart < 0) pStart = 0;
            if (pEnd < pStart)
            {
                var tmp = pStart;
                pStart = pEnd;
                pEnd = tmp;
            }

            // ガード: 誤入力で無限/大量生成しない
            var maxCount = 2000;
            var count = (int)Math.Floor(((pEnd - pStart) / pStep) + 1.0 + 1e-9);
            if (count < 1) count = 1;
            if (count > maxCount) count = maxCount;

            for (var idx = 0; idx < count; idx++)
            {
                var pVal = pStart + (pStep * idx);
                if (pVal > pEnd + 1e-12) break;
                var pressure = (float)Math.Clamp(pVal, 0.0, 1.0);

                await ExportS200Service.ExportAlignedDotIndexSeriesAsync(
                    mp: this,
                    folder: folder,
                    isTransparentBackground: true,
                    pressure: pressure,
                    exportScale: scale,
                    dotCount: dotCount,
                    periodStepDip: periodStepDip,
                    startXDip: sx,
                    startYDip: sy,
                    lDip: lDip,
                    outWidthDip: outW,
                    outHeightDip: outH,
                    roiLeftDip: 0,
                    roiTopDip: 0);
            }
        }

        private async void ExportHighResPngButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportHighResPngCoreAsync(transparentBackground: false, cropToBounds: false);
        }

        private async void ExportHighResPngTransparentButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportHighResPngCoreAsync(transparentBackground: true, cropToBounds: false);
        }

        private async void ExportHighResPngCroppedButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportHighResPngCoreAsync(transparentBackground: false, cropToBounds: true);
        }

        private async void ExportHighResPngCroppedTransparentButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportHighResPngCoreAsync(transparentBackground: true, cropToBounds: true);
        }

        private async void ExportHiResLastStrokeCroppedTransparentButton_Click(object sender, RoutedEventArgs e)
        {
            await TestMethods.ExportHiResLastStrokeAsync(this, transparentBackground: true, cropToBounds: true);
        }

        private async void ExportHiResPreSaveAlphaStatsCanvasButton_Click(object sender, RoutedEventArgs e)
        {
            await TestMethods.ExportHiResPreSaveAlphaStatsAsync(this, useLastStrokeOnly: false, transparentBackground: true, cropToBounds: true);
        }

        private async void ExportHiResPreSaveAlphaStatsLastStrokeButton_Click(object sender, RoutedEventArgs e)
        {
            await TestMethods.ExportHiResPreSaveAlphaStatsAsync(this, useLastStrokeOnly: true, transparentBackground: true, cropToBounds: true);
        }

        private async void ExportHiResSimulatedCompositeButton_Click(object sender, RoutedEventArgs e)
        {
            await TestMethods.ExportHiResSimulatedCompositeAsync(this, transparentBackground: true, cropToBounds: true);
        }

        private void DrawLineStrokeFixedButton_Click(object sender, RoutedEventArgs e)
        {
            var startText = (FindName("StartPositionTextBox") as TextBox)?.Text;
            var endText = (FindName("EndPositionTextBox") as TextBox)?.Text;
            var pointCountText = (FindName("LinePointCountTextBox") as TextBox)?.Text;
            var pointStepText = (FindName("LinePointStepTextBox") as TextBox)?.Text;

            var ignorePressure = IgnorePressureCheckBox?.IsChecked == true;
            TestMethods.DrawLineStrokeFixed(this, startText, endText, pointCountText, pointStepText, ignorePressure);
        }

        private void DrawHoldStrokeFixedButton_Click(object sender, RoutedEventArgs e)
        {
            var startText = (FindName("StartPositionTextBox") as TextBox)?.Text;
            var pointCountText = (FindName("LinePointCountTextBox") as TextBox)?.Text;
            var ignorePressure = IgnorePressureCheckBox?.IsChecked == true;
            TestMethods.DrawHoldStrokeFixed(this, startText, pointCountText, ignorePressure);
        }

        private async System.Threading.Tasks.Task ExportHighResPngCoreAsync(bool transparentBackground, bool cropToBounds)
        {
            int scale;
            if (!int.TryParse(ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out scale))
            {
                if (!int.TryParse(ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out scale))
                {
                    return;
                }
            }

            double dpiD;
            if (!double.TryParse(ExportDpiTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out dpiD))
            {
                if (!double.TryParse(ExportDpiTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out dpiD))
                {
                    return;
                }
            }

            if (scale <= 0) return;
            if (dpiD <= 0) return;

            var s = UIHelpers.GetDot512SizeOrNull(this);
            var p = (double)UIHelpers.GetDot512Pressure(this);
            var n = UIHelpers.GetDot512Overwrite(this);
            var ctx = new ExportHighResInk.ExportContext(s, p, n, exportScale: scale);
            await ExportHighResInk.ExportAsync(this, scale, (float)dpiD, transparentBackground, cropToBounds, ctx);
        }
        private async void ExportDot512MaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512Async(this,isTransparentBackground: true, includeLabels: false, suggestedFileName: "dot512-material");
        }

        private async void ExportDot512PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512Async(mp: this,isTransparentBackground: false, includeLabels: true, suggestedFileName: "dot512-preview");
        }

        private async void ExportDot512BatchMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512BatchAsync(mp:this,isTransparentBackground: true, includeLabels: false, defaultSuffix: "material");
        }

        private async void ExportDot512BatchPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512BatchAsync(mp:this,isTransparentBackground: false, includeLabels: true, defaultSuffix: "preview");
        }

        private async void ExportDot512BatchMaterialSizesButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512BatchSizesAsync(mp:this,isTransparentBackground: true, includeLabels: false, defaultSuffix: "material");
        }

        private async void ExportDot512BatchMaterialSizesPsNsButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512BatchSizesPsNsAsync(mp: this, isTransparentBackground: true, includeLabels: false, defaultSuffix: "material");
        }

        private async void ExportDot512BatchPreviewSizesButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512BatchSizesAsync(mp:this,isTransparentBackground: false, includeLabels: true, defaultSuffix: "preview");
        }

        private async void ExportDot512SlideMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512SlideAsync(mp:this,isTransparentBackground: true, includeLabels: false, defaultSuffix: "material");
        }

        private async void ExportDot512SlidePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512SlideAsync(mp:this,isTransparentBackground: false, includeLabels: true, defaultSuffix: "preview");
        }

        private async void ExportEstimatedPaperNoiseButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportEstimatedPaperNoise.ExportAsync(this);
         }

        private async void ExportS200LineMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportS200Service.ExportAsync(mp: this, isTransparentBackground: true, includeLabels: false, suggestedFileName: "pencil-material-line-s200");
        }

        private async void ExportS200LinePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportS200Service.ExportAsync(mp: this, isTransparentBackground: false, includeLabels: true, suggestedFileName: "pencil-preview-line-s200");
        }

        private async void ExportPaperNoiseCrop24Button_Click(object sender, RoutedEventArgs e)
        {
            await ExportPaperNoiseCrop24.ExportAsync(this);
         }

        private async void ExportRadialFalloffCsvButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialFalloffCsvAsync(this);
        }

        private async void ExportRadialAlphaCsvButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialAlphaCsvAsync(this);
         }

        private async void ExportNormalizedFalloffButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportNormalizedFalloffService.ExportAsync(this);
        }

        private async void CompareDot512WithSkiaButton_Click(object sender, RoutedEventArgs e)
        {
            await CompareDot512WithSkia.CompareDot512WithSkiaAsync(this);
        }

        private async void ExportDot512PreSaveAlphaSummaryButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512PreSaveAlphaSummaryCsvAsync(this);
        }

        private async void ExportDot512PreSaveAlphaFloorBySizeButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512PreSaveAlphaFloorBySizeCsvAsync(this);
        }

    }
}
