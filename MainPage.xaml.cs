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
using StrokeSampler.Helpers;
using System.IO;
using System.Threading.Tasks;
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
            //if (count > 500) count = 500;

            float dy;
            if (!float.TryParse(StairDyTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out dy))
            {
                if (!float.TryParse(StairDyTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out dy))
                {
                    dy = 2f;
                }
            }
            if (Math.Abs(dy) < 0.001f) dy = 0.001f;

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

        private async void RunAlignedN1To12PresetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var baseSettings = ReadS200AlignedBatchSettingsFromUi();
                if (baseSettings is null) return;

                var startUI = StartPositionTextBox?.Text ?? "";
                var endUI = EndPositionTextBox?.Text ?? "";

                // pressure は aligned 系の既存運用に合わせて Dot512 Pressure を使用
                var pressure = UIHelpers.GetDot512Pressure(this);

                var chooseDialog = new ContentDialog
                {
                    Title = "AlignedN 1..12 preset",
                    Content = "Choose output folder for alignedN=1..12 series.",
                    PrimaryButtonText = "Choose folder",
                    SecondaryButtonText = "Use LocalFolder",
                    CloseButtonText = "Cancel"
                };
                var choice = await chooseDialog.ShowAsync();
                if (choice == ContentDialogResult.None) return;

                StorageFolder folder;
                if (choice == ContentDialogResult.Secondary)
                {
                    folder = await GetAlignedJobsDefaultOutputFolderAsync();
                }
                else
                {
                    var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
                    folderPicker.FileTypeFilter.Add(".png");
                    folder = await folderPicker.PickSingleFolderAsync();
                    if (folder is null) return;
                }

                var runTag = baseSettings.RunTag;
                if (string.IsNullOrWhiteSpace(runTag))
                {
                    runTag = "alignedN1to12-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                }

                var suggested = BuildAlignedJobsSuggestedFolderName(
                    purpose: "AlignedPreset_N1-12",
                    baseSettings: baseSettings,
                    startUI: startUI,
                    endUI: endUI,
                    runTag: runTag,
                    pStart: pressure,
                    pEnd: pressure,
                    pStep: null);
                folder = await folder.CreateFolderAsync(suggested, CreationCollisionOption.OpenIfExists);

                // ExportAlignedDotIndexSeriesAsync は 1..dotCount の連番を一括生成する。
                // ここでは dotCount=12 を1回だけ呼んで、alignedN1..12 を一発で出す。
                await ExportS200Service.ExportAlignedDotIndexSeriesAsync(
                    mp: this,
                    folder: folder,
                    isTransparentBackground: S200AlignedTransparentOutputCheckBox?.IsChecked == true,
                    pressure: pressure,
                    exportScale: baseSettings.ExportScale,
                    dotCount: 12,
                    periodStepDip: baseSettings.PeriodStepDip,
                    startXDip: baseSettings.StartXDip,
                    startYDip: baseSettings.StartYDip,
                    lDip: baseSettings.LineLengthDip,
                    outWidthDip: baseSettings.OutWidthDip,
                    outHeightDip: baseSettings.OutHeightDip,
                    roiLeftDip: 0,
                    roiTopDip: 0,
                    runTag: runTag);

                _ = new ContentDialog
                {
                    Title = "AlignedN 1..12 preset",
                    Content = $"Done.\nfolder={folder.Path}",
                    CloseButtonText = "OK"
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                _ = new ContentDialog
                {
                    Title = "Error",
                    Content = ex.ToString(),
                    CloseButtonText = "OK"
                }.ShowAsync();
            }
        }

        private static async Task<StorageFolder> GetAlignedJobsDefaultOutputFolderAsync()
        {
            var root = ApplicationData.Current.LocalFolder;
            var runs = await root.CreateFolderAsync("AlignedJobsRuns", CreationCollisionOption.OpenIfExists);
            return runs;
        }

        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "run";
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }

        private static string BuildAlignedJobsSuggestedFolderName(
            string purpose,
            S200AlignedBatchSettings baseSettings,
            string startUI,
            string endUI,
            string runTag,
            double? pStart,
            double? pEnd,
            double? pStep)
        {
            string pPart;
            if (pStart.HasValue && pEnd.HasValue && pStep.HasValue)
            {
                pPart = $"P{pStart.Value:0.########}-P{pEnd.Value:0.########}_step{pStep.Value:0.########}";
            }
            else if (pStart.HasValue)
            {
                pPart = $"P{pStart.Value:0.########}";
            }
            else
            {
                pPart = "P";
            }

            var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var name = $"{purpose}_{pPart}_scale{baseSettings.ExportScale}_W{baseSettings.OutWidthDip}H{baseSettings.OutHeightDip}_start{startUI}_end{endUI}_{runTag}_{ts}";
            return SanitizeFolderName(name);
        }

        private async void RunS200AlignedJobsCsvButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // sanity: click is wired
                Debug.WriteLine("RunS200AlignedJobsCsvButton_Click: start");

                var picker = new FileOpenPicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    ViewMode = PickerViewMode.List
                };
                picker.FileTypeFilter.Add(".csv");

                var csvFile = await picker.PickSingleFileAsync();
                if (csvFile is null) return;

                // UWPでは csvFile.Path が空/アクセス不可になることがあるため、StorageFileから直接読む
                var csvPath = csvFile.Path;
                if (string.IsNullOrWhiteSpace(csvPath))
                {
                    Debug.WriteLine("RunS200AlignedJobsCsvButton_Click: csvFile.Path is empty (UWP)");
                }

                var baseSettings = ReadS200AlignedBatchSettingsFromUi();
                if (baseSettings is null) return;

                var startUI = StartPositionTextBox?.Text ?? "";
                var endUI = EndPositionTextBox?.Text ?? "";

                var useDefaultFolder = false;
                var chooseDialog = new ContentDialog
                {
                    Title = "AlignedJobs",
                    Content = "Choose output folder. If FolderPicker is problematic in your environment, you can use the app LocalFolder instead.",
                    PrimaryButtonText = "Choose folder",
                    SecondaryButtonText = "Use LocalFolder",
                    CloseButtonText = "Cancel"
                };
                var choice = await chooseDialog.ShowAsync();
                if (choice == ContentDialogResult.None) return;
                useDefaultFolder = choice == ContentDialogResult.Secondary;

                StorageFolder folder;
                if (useDefaultFolder)
                {
                    folder = await GetAlignedJobsDefaultOutputFolderAsync();
                }
                else
                {
                    var folderPicker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary };
                    folderPicker.FileTypeFilter.Add(".png");
                    folder = await folderPicker.PickSingleFolderAsync();
                    if (folder is null) return;
                }
                var jobs = await AlignedJobsCsv.ReadAsync(csvFile);
                Debug.WriteLine($"RunS200AlignedJobsCsvButton_Click: jobs={jobs.Count}");

                // Use first job to annotate P range if available
                double? pStartMeta = null;
                double? pEndMeta = null;
                double? pStepMeta = null;
                if (jobs.Count > 0)
                {
                    pStartMeta = jobs[0].PressureStart;
                    pEndMeta = jobs[0].PressureEnd;
                    pStepMeta = jobs[0].PressureStep;
                }

                var effectiveRunTag = baseSettings.RunTag;
                if (jobs.Count > 0 && !string.IsNullOrWhiteSpace(jobs[0].RunTag))
                {
                    effectiveRunTag = jobs[0].RunTag!;
                }
                var suggested = BuildAlignedJobsSuggestedFolderName(
                    purpose: "AlignedJobs",
                    baseSettings: baseSettings,
                    startUI: startUI,
                    endUI: endUI,
                    runTag: effectiveRunTag,
                    pStart: pStartMeta,
                    pEnd: pEndMeta,
                    pStep: pStepMeta);
                folder = await folder.CreateFolderAsync(suggested, CreationCollisionOption.OpenIfExists);
                foreach (var job in jobs)
                {
                    // start/endはUIの書式（"x,y"）をそのまま使う前提
                    var sx = baseSettings.StartXDip;
                    var sy = baseSettings.StartYDip;
                    var lDip = baseSettings.LineLengthDip;
                    if (!string.IsNullOrWhiteSpace(job.StartXY) && TryParsePointText(job.StartXY, out var x, out var y))
                    {
                        sx = x;
                        sy = y;
                    }
                    if (job.LineLengthDip.HasValue && job.LineLengthDip.Value > 0)
                    {
                        lDip = job.LineLengthDip.Value;
                    }
                    else if (!string.IsNullOrWhiteSpace(job.EndXY)
                        && TryParsePointText(job.EndXY, out var ex, out var ey))
                    {
                        var dx = ex - sx;
                        var dy = ey - sy;
                        lDip = Math.Sqrt((dx * dx) + (dy * dy));
                    }

                    var mode = (job.AlignedMode ?? "").Trim();
                    if (string.Equals(mode, "dot-index-single", StringComparison.OrdinalIgnoreCase))
                    {
                        var outWDip = job.OutWidthDip ?? baseSettings.OutWidthDip;
                        var outHDip = job.OutHeightDip ?? baseSettings.OutHeightDip;
                        var scale = job.ExportScale ?? baseSettings.ExportScale;
                        var periodStepDip = job.PeriodStepDip ?? baseSettings.PeriodStepDip;
                        var trials = job.Trials ?? baseSettings.Trials;
                        var runTag = job.RunTag ?? baseSettings.RunTag;
                        var isTransparent = job.Transparent ?? (S200AlignedTransparentOutputCheckBox?.IsChecked == true);
                        var nSingle = job.SingleN ?? job.DotCount ?? (baseSettings.DotCount > 0 ? baseSettings.DotCount : 1);
                        nSingle = Math.Clamp(nSingle, 1, 200);

                        var pStart = job.PressureStart ?? baseSettings.BatchPStart;
                        var pEnd = job.PressureEnd ?? baseSettings.BatchPEnd;
                        var pStep = job.PressureStep ?? baseSettings.BatchPStep;
                        if (pStep <= 0) pStep = 0.01;
                        if (pEnd < pStart) { var tmp = pStart; pStart = pEnd; pEnd = tmp; }

                        for (var t = 1; t <= trials; t++)
                        {
                            var tagT = new S200AlignedBatchSettings(
                                outWidthDip: outWDip,
                                outHeightDip: outHDip,
                                exportScale: scale,
                                periodStepDip: periodStepDip,
                                dotCount: 1,
                                trials: trials,
                                runTag: runTag,
                                batchPStart: pStart,
                                batchPEnd: pEnd,
                                batchPStep: pStep,
                                startXDip: sx,
                                startYDip: sy,
                                lineLengthDip: lDip,
                                isTransparentBackground: isTransparent);

                            var effectiveTag = tagT.BuildEffectiveRunTag(t);
                            for (var p = pStart; p <= pEnd + 1e-12; p += pStep)
                            {
                                await ExportS200Service.ExportAlignedDotIndexSingleAsync(
                                    mp: this,
                                    folder: folder,
                                    isTransparentBackground: isTransparent,
                                    pressure: (float)p,
                                    exportScale: scale,
                                    n: nSingle,
                                    periodStepDip: periodStepDip,
                                    startXDip: sx,
                                    startYDip: sy,
                                    lDip: lDip,
                                    outWidthDip: outWDip,
                                    outHeightDip: outHDip,
                                    runTag: effectiveTag);
                            }
                        }
                    }
                    else
                    {
                        var settings = new S200AlignedBatchSettings(
                            outWidthDip: job.OutWidthDip ?? baseSettings.OutWidthDip,
                            outHeightDip: job.OutHeightDip ?? baseSettings.OutHeightDip,
                            exportScale: job.ExportScale ?? baseSettings.ExportScale,
                            periodStepDip: job.PeriodStepDip ?? baseSettings.PeriodStepDip,
                            dotCount: job.DotCount
                                ?? (baseSettings.DotCount > 0
                                    ? baseSettings.DotCount
                                    : (job.OutWidthDip ?? baseSettings.OutWidthDip)),
                            trials: job.Trials ?? baseSettings.Trials,
                            runTag: job.RunTag ?? baseSettings.RunTag,
                            batchPStart: job.PressureStart ?? baseSettings.BatchPStart,
                            batchPEnd: job.PressureEnd ?? baseSettings.BatchPEnd,
                            batchPStep: job.PressureStep ?? baseSettings.BatchPStep,
                            startXDip: sx,
                            startYDip: sy,
                            lineLengthDip: lDip,
                            isTransparentBackground: job.Transparent ?? (S200AlignedTransparentOutputCheckBox?.IsChecked == true));

                        await RunS200AlignedBatchAsync(folder, settings);
                    }
                }

                _ = new ContentDialog
                {
                    Title = "AlignedJobs",
                    Content = $"Done. jobs={jobs.Count}\n" +
                              $"start={startUI}\n" +
                              $"end={endUI}\n" +
                              $"outW/outH={baseSettings.OutWidthDip}x{baseSettings.OutHeightDip} (DIP)\n" +
                              $"scale={baseSettings.ExportScale}\n" +
                              $"period_step_dip={baseSettings.PeriodStepDip:0.########}\n" +
                              $"folder={folder.Path}",
                    CloseButtonText = "OK"
                }.ShowAsync();
            }
            catch (Exception ex)
            {
                _ = new ContentDialog
                {
                    Title = "Error",
                    Content = ex.ToString(),
                    CloseButtonText = "OK"
                }.ShowAsync();
            }
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
            if (int.TryParse(S200AlignedDotCountTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dc)
                || int.TryParse(S200AlignedDotCountTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out dc))
            {
                // Allow 0 as "unspecified" to trigger downstream fallbacks.
                dotCount = Math.Clamp(dc, 0, 200);
            }
            else
            {
                dotCount = Math.Clamp(dotCount, 1, 200);
            }

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
                isTransparentBackground: S200AlignedTransparentOutputCheckBox?.IsChecked == true,
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

        private async void ExportDotStepSweepPseudoLineButton_Click(object sender, RoutedEventArgs e)
        {
            await TestMethods.ExportPseudoLineDotStepSweepAsync(this);
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

            var runTag = (FindName("S200AlignedRunTagTextBox") as TextBox)?.Text;
            if (string.IsNullOrWhiteSpace(runTag))
            {
                runTag = "run" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            }

            var trials = 1;
            var trialsText = (FindName("S200AlignedTrialsTextBox") as TextBox)?.Text;
            if (!int.TryParse(trialsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out trials))
            {
                _ = int.TryParse(trialsText, NumberStyles.Integer, CultureInfo.CurrentCulture, out trials);
            }
            trials = Math.Clamp(trials, 1, 1000);

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
                roiTopDip: 0,
                runTag: runTag);
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

            var settingsSnapshot = ReadS200AlignedBatchSettingsFromUi();
            if (settingsSnapshot is null) return;

            var folderPicker = new Windows.Storage.Pickers.FolderPicker { SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary };
            folderPicker.FileTypeFilter.Add(".png");
            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null) return;

            await RunS200AlignedBatchAsync(folder, settingsSnapshot);
        }

        private async Task RunS200AlignedBatchAsync(StorageFolder folder, S200AlignedBatchSettings settings)
        {
            var decimals = settings.DecimalsFromStep();
            var stepI = (decimal)settings.BatchPStep;
            var startI = (decimal)settings.BatchPStart;
            var endI = (decimal)settings.BatchPEnd;

            // ガード: 誤入力で無限/大量生成しない
            var maxCount = 20000;
            var count = (int)Math.Floor(((double)(endI - startI) / (double)stepI) + 1.0 + 1e-9);
            if (count < 1) count = 1;
            if (count > maxCount) count = maxCount;

            for (var idx = 0; idx < count; idx++)
            {
                var pVal = startI + (stepI * idx);
                if (pVal > endI + 0.0000000001m) break;

                // ファイル名や描画系での表記揺れ・境界ずれを避けるため、stepの小数桁に揃えて丸める
                var pRounded = Math.Round((double)pVal, decimals, MidpointRounding.AwayFromZero);
                var pressure = (float)Math.Clamp(pRounded, 0.0, 1.0);

                for (var t = 1; t <= settings.Trials; t++)
                {
                    var tag = settings.BuildEffectiveRunTag(settings.Trials <= 1 ? null : t);
                    await ExportS200Service.ExportAlignedDotIndexSeriesAsync(
                        mp: this,
                        folder: folder,
                        isTransparentBackground: settings.IsTransparentBackground,
                        pressure: pressure,
                        exportScale: settings.ExportScale,
                        dotCount: settings.DotCount,
                        periodStepDip: settings.PeriodStepDip,
                        startXDip: settings.StartXDip,
                        startYDip: settings.StartYDip,
                        lDip: settings.LineLengthDip,
                        outWidthDip: settings.OutWidthDip,
                        outHeightDip: settings.OutHeightDip,
                        roiLeftDip: 0,
                        roiTopDip: 0,
                        runTag: tag);
                }
            }
        }

        private S200AlignedBatchSettings? ReadS200AlignedBatchSettingsFromUi()
        {
            if (!int.TryParse(ExportWidthTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outW))
            {
                if (!int.TryParse(ExportWidthTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out outW)) return null;
            }
            if (!int.TryParse(ExportHeightTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outH))
            {
                if (!int.TryParse(ExportHeightTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out outH)) return null;
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

            var scale = 10;
            if (int.TryParse(ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sc) && sc > 0)
            {
                scale = sc;
            }
            else if (int.TryParse(ExportScaleTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out sc) && sc > 0)
            {
                scale = sc;
            }
            scale = Math.Clamp(scale, 1, 100);

            var runTag = (FindName("S200AlignedRunTagTextBox") as TextBox)?.Text ?? "";

            var trials = 1;
            var trialsText = (FindName("S200AlignedTrialsTextBox") as TextBox)?.Text;
            if (!int.TryParse(trialsText, NumberStyles.Integer, CultureInfo.InvariantCulture, out trials))
            {
                _ = int.TryParse(trialsText, NumberStyles.Integer, CultureInfo.CurrentCulture, out trials);
            }
            trials = Math.Clamp(trials, 1, 1000);

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

            if (pStep <= 0) return null;
            if (pStart < 0) pStart = 0;
            if (pEnd < pStart)
            {
                var tmp = pStart;
                pStart = pEnd;
                pEnd = tmp;
            }

            if (!TryParsePointText(StartPositionTextBox?.Text, out var sxF, out var syF)) return null;
            if (!TryParsePointText(EndPositionTextBox?.Text, out var exF, out var eyF)) return null;

            var sx = (double)sxF;
            var sy = (double)syF;
            var ex = (double)exF;
            var ey = (double)eyF;

            var lDip = Math.Sqrt(((ex - sx) * (ex - sx)) + ((ey - sy) * (ey - sy)));
            if (double.TryParse(LineTotalLengthTextBox?.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var ld) && ld > 0)
            {
                lDip = ld;
            }
            else if (double.TryParse(LineTotalLengthTextBox?.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out ld) && ld > 0)
            {
                lDip = ld;
            }

            return new S200AlignedBatchSettings(
                outWidthDip: outW,
                outHeightDip: outH,
                exportScale: scale,
                periodStepDip: periodStepDip,
                dotCount: dotCount,
                trials: trials,
                runTag: runTag,
                batchPStart: pStart,
                batchPEnd: pEnd,
                batchPStep: pStep,
                startXDip: sx,
                startYDip: sy,
                lineLengthDip: lDip,
                isTransparentBackground: true,
                isEraser: EraserRadioButton.IsChecked ?? false,
                isPencil: PencilRadioButton.IsChecked ?? false,
                isBlack: BlackRadioButton.IsChecked ?? false,
                isWhite: WhiteRadioButton.IsChecked ?? false,
                isTransparent: TransparentRadioButton.IsChecked ?? false,
                isRed: RedRadioButton.IsChecked ?? false,
                isGreen: GreenRadioButton.IsChecked ?? false,
                isBlue: BlueRadioButton.IsChecked ?? false
                );
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
            var stairDyText = (FindName("StairDyTextBox") as TextBox)?.Text;
            var stairCountText = (FindName("StairCountTextBox") as TextBox)?.Text;

            var ignorePressure = IgnorePressureCheckBox?.IsChecked == true;
            TestMethods.DrawLineStrokeFixedMulti(this, startText, endText, pointCountText, pointStepText, UIHelpers.GetLineFixedStepY(stairDyText), UIHelpers.GetLineFixedRepeatCount(stairCountText), ignorePressure);
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
