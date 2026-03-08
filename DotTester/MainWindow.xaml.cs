using Microsoft.Win32;
using ModernWpf.Controls;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using DotTester.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DotTester
{
    public partial class MainWindow : Window
    {
        private SKBitmap? _expected;
        private SKBitmap? _rendered;

        private DotReproRenderer.Options? _lastRenderedOptions;

        private readonly DispatcherTimer _autoRenderTimer;
        private bool _isAutoRendering;

        private PaperNoiseTile? _paperNoise;
        private string? _paperNoisePath;

        private NormalizedFalloffLut? _falloffLut;
        private string? _falloffCsvPath;
        private int? _falloffLoadedScale;

        private CancellationTokenSource? _sweepCts;
        private bool _isSweeping;

        public MainWindow()
        {
            InitializeComponent();
            _autoRenderTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(150),
            };
            _autoRenderTimer.Tick += AutoRenderTimer_Tick;
            ApplyAutoUiState();
            SetDefaultPaths();
            WireAutoRenderHandlers();
            UpdateStatus("Ready.");
        }

        private void WireAutoRenderHandlers()
        {
            // 値を変えたら即反映したいので、主要な入力をまとめて監視する。
            // 重い処理なので、実行自体はデバウンス（ScheduleAutoRender）でまとめる。

            // NumberBox
            DiameterNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            RenderScaleNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PressureNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            CanvasPadNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            RadiusPadNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            CanvasSizeNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseStrengthNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseGainNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseZClampNegAbsNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseZClampPosAbsNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseKClampMinNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseKClampMaxNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseTileScaleNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseScaleNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseOffsetXNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseOffsetYNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            AlphaCutoffByteNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperMaskThresholdNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperMaskGainNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            FalloffScaleNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            FalloffRNormScaleNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            FalloffGammaNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseEdgeBoostNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            PaperNoiseEdgeBoostGammaNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            WallKNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            WallBaseScaleNumberBox.ValueChanged += AnyNumberBoxValueChanged;
            WallThresholdBiasNumberBox.ValueChanged += AnyNumberBoxValueChanged;

            // ComboBox
            PaperNoiseKModeComboBox.SelectionChanged += AnySelectionChanged;
            PaperNoiseSamplingComboBox.SelectionChanged += AnySelectionChanged;
            PaperNoiseApplyModeComboBox.SelectionChanged += AnySelectionChanged;
            OutAlphaModelComboBox.SelectionChanged += AnySelectionChanged;
            PaperMaskModeComboBox.SelectionChanged += AnySelectionChanged;
            PaperMaskFalloffModeComboBox.SelectionChanged += AnySelectionChanged;

            // CheckBox
            AutoCanvasSizeCheckBox.Checked += AnyRoutedChanged;
            AutoCanvasSizeCheckBox.Unchecked += AnyRoutedChanged;
            AutoPaperNoiseScaleCheckBox.Checked += AnyRoutedChanged;
            AutoPaperNoiseScaleCheckBox.Unchecked += AnyRoutedChanged;
            UsePaperNoiseCheckBox.Checked += AnyRoutedChanged;
            UsePaperNoiseCheckBox.Unchecked += AnyRoutedChanged;
            DisableKMeanNormalizationCheckBox.Checked += AnyRoutedChanged;
            DisableKMeanNormalizationCheckBox.Unchecked += AnyRoutedChanged;
            EnablePaperNoiseZClampCheckBox.Checked += AnyRoutedChanged;
            EnablePaperNoiseZClampCheckBox.Unchecked += AnyRoutedChanged;
            NoiseDependentCutoffCheckBox.Checked += AnyRoutedChanged;
            NoiseDependentCutoffCheckBox.Unchecked += AnyRoutedChanged;
            EnablePaperNoiseEdgeBoostCheckBox.Checked += AnyRoutedChanged;
            EnablePaperNoiseEdgeBoostCheckBox.Unchecked += AnyRoutedChanged;

            // TextBox（パス等）
            ExpectedPathTextBox.TextChanged += AnyTextChanged;
            PaperNoisePathTextBox.TextChanged += AnyTextChanged;
            FalloffCsvPathTextBox.TextChanged += AnyTextChanged;
        }

        private void AnyNumberBoxValueChanged(object sender, NumberBoxValueChangedEventArgs e)
        {
            ScheduleAutoRender();
        }

        private void AnySelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ScheduleAutoRender();
        }

        private void AnyRoutedChanged(object sender, RoutedEventArgs e)
        {
            ScheduleAutoRender();
        }

        private void AnyTextChanged(object sender, TextChangedEventArgs e)
        {
            ScheduleAutoRender();
        }

        private void AutoRenderTimer_Tick(object? sender, EventArgs e)
        {
            _autoRenderTimer.Stop();
            TriggerRender(showErrors: false);
        }

        private void ScheduleAutoRender()
        {
            if (!IsLoaded) return;
            if (_isAutoRendering) return;

            _autoRenderTimer.Stop();
            _autoRenderTimer.Start();
        }

        private void TriggerRender(bool showErrors)
        {
            if (_isAutoRendering) return;
            _isAutoRendering = true;
            try
            {
                var sw = Stopwatch.StartNew();
                RenderN1();
                sw.Stop();
                UpdateStatus(StatusTextBlock.Text + $"  time={sw.ElapsedMilliseconds}ms");
            }
            catch (ArgumentException ex)
            {
                if (showErrors)
                {
                    MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    UpdateStatus(ex.Message);
                }
            }

            catch (InvalidOperationException ex)
            {
                if (showErrors)
                {
                    MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    UpdateStatus(ex.Message);
                }
            }
            catch (IOException ex)
            {
                if (showErrors)
                {
                    MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    UpdateStatus(ex.Message);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                if (showErrors)
                {
                    MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    UpdateStatus(ex.Message);
                }
            }
            finally
            {
                _isAutoRendering = false;
            }
        }

        private sealed record RoiImageStats(
            long Count,
            int Min,
            int Max,
            double Mean,
            double Stddev,
            int UniqueCount);

        private sealed record RoiCompareStats(
            long Count,
            double Mae,
            double Rmse,
            int MaxAbsError);

        private static RoiImageStats ComputeRoiImageStats(SKColor[] pixels, int w, int x, int y, int roiW, int roiH, int stride, TileExtremaMatchEvaluator.ExpectedAlphaMode mode)
        {
            if (stride <= 0) stride = 1;

            long count = 0;
            long sum = 0;
            long sumSq = 0;
            var min = 255;
            var max = 0;
            var seen = new bool[256];

            for (var yy = y; yy < y + roiH; yy += stride)
            {
                var rowBase = yy * w;
                for (var xx = x; xx < x + roiW; xx += stride)
                {
                    var v = TileExtremaMatchEvaluator.GetExpectedAlphaByte(pixels[rowBase + xx], mode);
                    count++;
                    sum += v;
                    sumSq += (long)v * v;
                    if (v < min) min = v;
                    if (v > max) max = v;
                    seen[v] = true;
                }
            }

            if (count <= 0)
            {
                return new RoiImageStats(0, 0, 0, 0, 0, 0);
            }

            var mean = sum / (double)count;
            var var0 = (sumSq / (double)count) - (mean * mean);
            if (var0 < 0) var0 = 0;
            var stddev = Math.Sqrt(var0);

            var unique = 0;
            for (var i = 0; i < seen.Length; i++)
            {
                if (seen[i]) unique++;
            }

            return new RoiImageStats(count, min, max, mean, stddev, unique);
        }

        private static RoiCompareStats ComputeRoiCompareStats(SKColor[] expectedPixels, SKColor[] renderedPixels, int w, int x, int y, int roiW, int roiH, int stride, TileExtremaMatchEvaluator.ExpectedAlphaMode mode)
        {
            if (stride <= 0) stride = 1;

            long count = 0;
            long sumAbs = 0;
            long sumSq = 0;
            var maxAbs = 0;

            for (var yy = y; yy < y + roiH; yy += stride)
            {
                var rowBase = yy * w;
                for (var xx = x; xx < x + roiW; xx += stride)
                {
                    var expA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(expectedPixels[rowBase + xx], mode);
                    var renA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(renderedPixels[rowBase + xx], mode);
                    var d = renA - expA;
                    var abs = Math.Abs(d);
                    sumAbs += abs;
                    sumSq += (long)d * d;
                    if (abs > maxAbs) maxAbs = abs;
                    count++;
                }
            }

            if (count <= 0)
            {
                return new RoiCompareStats(0, double.PositiveInfinity, double.PositiveInfinity, 0);
            }

            var mae = sumAbs / (double)count;
            var rmse = Math.Sqrt(sumSq / (double)count);
            return new RoiCompareStats(count, mae, rmse, maxAbs);
        }

        private void EvaluateRoiButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateRoiEvalText(showErrors: true);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateRoiEvalText(bool showErrors)
        {
            if (RoiEvalEnabledCheckBox.IsChecked != true)
            {
                RoiEvalTextBlock.Text = string.Empty;
                return;
            }

            if (_expected == null)
            {
                if (showErrors) throw new InvalidOperationException("Expected PNGが未ロードです。");
                RoiEvalTextBlock.Text = "Expected: (not loaded)";
                return;
            }
            if (_rendered == null)
            {
                if (showErrors) throw new InvalidOperationException("Renderedが未生成です。先にRenderを実行してください。");
                RoiEvalTextBlock.Text = "Rendered: (not generated)";
                return;
            }

            if (_expected.Width != _rendered.Width || _expected.Height != _rendered.Height)
            {
                throw new InvalidOperationException($"Expected/Renderedのサイズが一致しません。 expected={_expected.Width}x{_expected.Height} rendered={_rendered.Width}x{_rendered.Height}");
            }

            if (SweepUseRoiCheckBox.IsChecked != true)
            {
                RoiEvalTextBlock.Text = "ROI: (OFF)";
                return;
            }

            var x = (int)Math.Round(SweepRoiXNumberBox.Value, MidpointRounding.AwayFromZero);
            var y = (int)Math.Round(SweepRoiYNumberBox.Value, MidpointRounding.AwayFromZero);
            var roiW = (int)Math.Round(SweepRoiWNumberBox.Value, MidpointRounding.AwayFromZero);
            var roiH = (int)Math.Round(SweepRoiHNumberBox.Value, MidpointRounding.AwayFromZero);
            var stride = (int)Math.Round(SweepRoiStrideNumberBox.Value, MidpointRounding.AwayFromZero);
            if (stride <= 0) stride = 1;

            if (x < 0 || y < 0 || roiW <= 0 || roiH <= 0)
            {
                throw new ArgumentException("ROI(xywh) の入力が不正です。", nameof(x));
            }

            var w = _expected.Width;
            var h = _expected.Height;
            if (x + roiW > w || y + roiH > h)
            {
                throw new ArgumentException($"ROIが画像外です。 roi=({x},{y},{roiW},{roiH}) img={w}x{h}", nameof(x));
            }

            var expectedModeTag = (ExpectedAlphaModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
            var expectedMode = expectedModeTag switch
            {
                "Alpha" => TileExtremaMatchEvaluator.ExpectedAlphaMode.UseAlpha,
                "White" => TileExtremaMatchEvaluator.ExpectedAlphaMode.WhiteBackground255MinusLuma,
                _ => TileExtremaMatchEvaluator.ExpectedAlphaMode.Auto,
            };

            var expPixels = _expected.Pixels;
            var renPixels = _rendered.Pixels;

            var expStats = ComputeRoiImageStats(expPixels, w, x, y, roiW, roiH, stride, expectedMode);
            var renStats = ComputeRoiImageStats(renPixels, w, x, y, roiW, roiH, stride, expectedMode);
            var cmp = ComputeRoiCompareStats(expPixels, renPixels, w, x, y, roiW, roiH, stride, expectedMode);

            var sb = new StringBuilder(1024);
            sb.AppendLine($"ROI=({x},{y},{roiW},{roiH}) stride={stride}  samples={cmp.Count}");
            sb.AppendLine($"mode={expectedMode}");
            sb.AppendLine($"Expected: min={expStats.Min} max={expStats.Max} mean={expStats.Mean:0.###} std={expStats.Stddev:0.###} unique={expStats.UniqueCount}");
            sb.AppendLine($"Rendered: min={renStats.Min} max={renStats.Max} mean={renStats.Mean:0.###} std={renStats.Stddev:0.###} unique={renStats.UniqueCount}");
            sb.AppendLine($"Compare: MAE={cmp.Mae:0.###}  RMSE={cmp.Rmse:0.###}  MaxAbs={cmp.MaxAbsError}");

            RoiEvalTextBlock.Text = sb.ToString().TrimEnd();
        }

        private void SaveRenderedPngButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_rendered == null)
                {
                    MessageBox.Show(this, "先に Render を実行してください。", "DotTester", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var defaultName = BuildDefaultRenderedPngFileName();
                var save = new SaveFileDialog
                {
                    Filter = "PNG (*.png)|*.png",
                    FileName = defaultName,
                    Title = "Rendered（再現）PNGの保存先を選択"
                };
                if (save.ShowDialog(this) != true) return;

                SaveSkBitmapAsPng(_rendered, save.FileName);
                UpdateStatus($"Saved: {save.FileName}");
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string BuildDefaultRenderedPngFileName()
        {
            var sPx = (int)Math.Round(DiameterNumberBox.Value, MidpointRounding.AwayFromZero);
            var scale = (int)Math.Round(RenderScaleNumberBox.Value, MidpointRounding.AwayFromZero);
            var p = PressureNumberBox.Value;

            var expectedTag = "";
            var expectedPath = ExpectedPathTextBox.Text;
            if (!string.IsNullOrWhiteSpace(expectedPath))
            {
                try
                {
                    expectedTag = Path.GetFileNameWithoutExtension(expectedPath);
                }
                catch
                {
                    expectedTag = "";
                }
            }

            var pText = p.ToString("0.####", CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(expectedTag))
            {
                return $"rendered-{expectedTag}-S{sPx}-scale{scale}-P{pText}.png";
            }

            return $"rendered-S{sPx}-scale{scale}-P{pText}.png";
        }

        private static void SaveSkBitmapAsPng(SKBitmap bitmap, string filePath)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath が空です。", nameof(filePath));

            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, quality: 100);
            if (data == null)
            {
                throw new InvalidOperationException("PNGのエンコードに失敗しました。");
            }

            using var fs = File.Open(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            data.SaveTo(fs);
        }

        private void SetDefaultPaths()
        {
            // 既定: リポジトリ内のサンプルLUT（S200/P1/N1）
            var repoRoot = TryFindRepositoryRoot(AppContext.BaseDirectory);
            if (repoRoot == null)
            {
                return;
            }

            var falloff = Path.Combine(repoRoot, "DotLab", "Sample", "normalized-falloff-S0200-P1-N1.csv");
            if (File.Exists(falloff))
            {
                FalloffCsvPathTextBox.Text = falloff;
            }
        }

        private void ApplyAutoUiState()
        {
            if (AutoPaperNoiseScaleCheckBox != null && PaperNoiseScaleNumberBox != null)
            {
                PaperNoiseScaleNumberBox.IsEnabled = AutoPaperNoiseScaleCheckBox.IsChecked != true;
            }
            if (AutoCanvasSizeCheckBox != null && CanvasSizeNumberBox != null)
            {
                CanvasSizeNumberBox.IsEnabled = AutoCanvasSizeCheckBox.IsChecked != true;
            }

            if (EnablePaperNoiseZClampCheckBox != null)
            {
                var enabled = EnablePaperNoiseZClampCheckBox.IsChecked == true;
                if (PaperNoiseZClampNegAbsNumberBox != null) PaperNoiseZClampNegAbsNumberBox.IsEnabled = enabled;
                if (PaperNoiseZClampPosAbsNumberBox != null) PaperNoiseZClampPosAbsNumberBox.IsEnabled = enabled;
            }

            if (EnablePaperNoiseEdgeBoostCheckBox != null)
            {
                var enabled = EnablePaperNoiseEdgeBoostCheckBox.IsChecked == true;
                if (PaperNoiseEdgeBoostNumberBox != null) PaperNoiseEdgeBoostNumberBox.IsEnabled = enabled;
                if (PaperNoiseEdgeBoostGammaNumberBox != null) PaperNoiseEdgeBoostGammaNumberBox.IsEnabled = enabled;
            }
        }

        private void PaperNoiseZClampUiChanged(object sender, RoutedEventArgs e)
        {
            ApplyAutoUiState();
            ScheduleAutoRender();
        }

        private void PaperNoiseEdgeBoostUiChanged(object sender, RoutedEventArgs e)
        {
            ApplyAutoUiState();
            ScheduleAutoRender();
        }

        private void BrowseFalloffCsvButton_Click(object sender, RoutedEventArgs e)
        {
            var open = new OpenFileDialog
            {
                Filter = "CSV (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = false,
                Title = "normalized-falloff CSVを選択"
            };
            if (open.ShowDialog(this) != true) return;

            FalloffCsvPathTextBox.Text = open.FileName;
            InvalidateFalloffCache();
        }

        private void BrowseExpectedButton_Click(object sender, RoutedEventArgs e)
        {
            var open = new OpenFileDialog
            {
                Filter = "PNG (*.png)|*.png",
                Multiselect = false,
                Title = "Expected（観測）PNGを選択"
            };
            if (open.ShowDialog(this) != true) return;

            ExpectedPathTextBox.Text = open.FileName;
            LoadExpected(open.FileName);
            ApplyAutoUiState();
        }

        private void BrowsePaperNoiseButton_Click(object sender, RoutedEventArgs e)
        {
            var open = new OpenFileDialog
            {
                Filter = "PNG (*.png)|*.png",
                Multiselect = false,
                Title = "PaperNoise（タイル）PNGを選択"
            };
            if (open.ShowDialog(this) != true) return;

            PaperNoisePathTextBox.Text = open.FileName;
            InvalidatePaperNoiseCache();
            ApplyAutoUiState();
        }

        private void RenderButton_Click(object sender, RoutedEventArgs e)
        {
            TriggerRender(showErrors: true);
        }

        private void SweepCancelButton_Click(object sender, RoutedEventArgs e)
        {
            _sweepCts?.Cancel();
        }

        private async void SweepSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isSweeping) return;

            try
            {
                if (_expected == null)
                {
                    throw new InvalidOperationException("Expected PNGが未ロードです。");
                }

                // 探索はexpectedの点サンプルと比較するので、expectedサイズはキャンバスと一致している必要がある。
                var renderScale = (int)Math.Round(RenderScaleNumberBox.Value, MidpointRounding.AwayFromZero);
                if (renderScale <= 0)
                {
                    throw new ArgumentException("RenderScale の入力が不正です。", nameof(renderScale));
                }

                var sPx = (int)Math.Round(DiameterNumberBox.Value, MidpointRounding.AwayFromZero);
                if (sPx <= 0)
                {
                    throw new ArgumentException("S(直径px) の入力が不正です。", nameof(sPx));
                }
                var diameterPx = checked(sPx * renderScale);

                int canvasSizePx;
                if (AutoCanvasSizeCheckBox.IsChecked == true)
                {
                    var padPx = (int)Math.Round(CanvasPadNumberBox.Value, MidpointRounding.AwayFromZero);
                    if (padPx < 0)
                    {
                        throw new ArgumentException("Pad(px) の入力が不正です。", nameof(padPx));
                    }
                    canvasSizePx = checked(diameterPx + padPx);
                }
                else
                {
                    canvasSizePx = (int)Math.Round(CanvasSizeNumberBox.Value, MidpointRounding.AwayFromZero);
                }
                if (canvasSizePx <= 0)
                {
                    throw new ArgumentException("Canvas(px) の入力が不正です。", nameof(canvasSizePx));
                }

                if (_expected.Width != canvasSizePx || _expected.Height != canvasSizePx)
                {
                    throw new InvalidOperationException($"Expectedのサイズがキャンバスと一致しません。 expected={_expected.Width}x{_expected.Height} canvas={canvasSizePx}x{canvasSizePx}");
                }

                if (_paperNoise == null)
                {
                    _paperNoise = GetOrLoadPaperNoise();
                }

                // RenderN1()と同じロジックでnoiseScaleを確定する
                var paperNoiseScale = PaperNoiseScaleNumberBox.Value;
                if (AutoPaperNoiseScaleCheckBox.IsChecked == true)
                {
                    var tileScale = (int)Math.Round(PaperNoiseTileScaleNumberBox.Value, MidpointRounding.AwayFromZero);
                    if (tileScale <= 0)
                    {
                        throw new ArgumentException("TileScale の入力が不正です。", nameof(tileScale));
                    }
                    paperNoiseScale = (double)renderScale / tileScale;
                }
                if (paperNoiseScale <= 0 || !double.IsFinite(paperNoiseScale))
                {
                    throw new ArgumentException("noiseScale の入力が不正です。", nameof(paperNoiseScale));
                }

                // Sweep設定
                var sweepOffset = SweepOffsetCheckBox.IsChecked == true;
                var sweepStrength = SweepStrengthCheckBox.IsChecked == true;
                var sweepGain = SweepGainCheckBox.IsChecked == true;
                var sweepFalloffScale = SweepFalloffScaleCheckBox.IsChecked == true;
                var sweepRNormScale = SweepRNormScaleCheckBox.IsChecked == true;
                var sweepFalloffGamma = SweepFalloffGammaCheckBox.IsChecked == true;
                var sweepWallK = SweepWallKCheckBox.IsChecked == true;
                var sweepWallBaseScale = SweepWallBaseScaleCheckBox.IsChecked == true;
                var sweepWallThresholdBias = SweepWallThresholdBiasCheckBox.IsChecked == true;
                var apply = ApplyBestToUiCheckBox.IsChecked == true;

                var offsetXMin = (int)Math.Round(SweepOffsetXMinNumberBox.Value, MidpointRounding.AwayFromZero);
                var offsetXMax = (int)Math.Round(SweepOffsetXMaxNumberBox.Value, MidpointRounding.AwayFromZero);
                var offsetYMin = (int)Math.Round(SweepOffsetYMinNumberBox.Value, MidpointRounding.AwayFromZero);
                var offsetYMax = (int)Math.Round(SweepOffsetYMaxNumberBox.Value, MidpointRounding.AwayFromZero);
                if (offsetXMin > offsetXMax) (offsetXMin, offsetXMax) = (offsetXMax, offsetXMin);
                if (offsetYMin > offsetYMax) (offsetYMin, offsetYMax) = (offsetYMax, offsetYMin);

                var strengthMin = SweepStrengthMinNumberBox.Value;
                var strengthMax = SweepStrengthMaxNumberBox.Value;
                var strengthStep = SweepStrengthStepNumberBox.Value;
                if (strengthMin > strengthMax) (strengthMin, strengthMax) = (strengthMax, strengthMin);
                if (!double.IsFinite(strengthStep) || strengthStep <= 0) strengthStep = 0.01;

                var gainMin = SweepGainMinNumberBox.Value;
                var gainMax = SweepGainMaxNumberBox.Value;
                var gainStep = SweepGainStepNumberBox.Value;
                if (gainMin > gainMax) (gainMin, gainMax) = (gainMax, gainMin);
                if (!double.IsFinite(gainStep) || gainStep <= 0) gainStep = 0.1;

                var kMeanStride = (int)Math.Round(SweepKMeanStrideNumberBox.Value, MidpointRounding.AwayFromZero);
                if (kMeanStride <= 0) kMeanStride = 1;

                var usePaper = UsePaperNoiseCheckBox.IsChecked == true;
                if (!usePaper)
                {
                    throw new InvalidOperationException("探索はPaperNoise前提です（PaperNoiseをONにしてください）。");
                }

                var pressure = PressureNumberBox.Value;
                if (pressure <= 0 || pressure > 1.0)
                {
                    throw new ArgumentException("P の入力が不正です。", nameof(pressure));
                }

                var falloffLut = GetOrLoadFalloffLut(renderScale);

                var radiusPadPx = RadiusPadNumberBox.Value;
                if (radiusPadPx < 0 || !double.IsFinite(radiusPadPx))
                {
                    throw new ArgumentException("EdgePad(px) の入力が不正です。", nameof(radiusPadPx));
                }

                var falloffScale = FalloffScaleNumberBox.Value;
                if (falloffScale < 0 || !double.IsFinite(falloffScale))
                {
                    throw new ArgumentException("FalloffScale の入力が不正です。", nameof(falloffScale));
                }

                var falloffRNormScale = FalloffRNormScaleNumberBox.Value;
                if (falloffRNormScale <= 0 || !double.IsFinite(falloffRNormScale))
                {
                    throw new ArgumentException("FalloffRNormScale の入力が不正です。", nameof(falloffRNormScale));
                }

                var falloffGamma = FalloffGammaNumberBox.Value;
                if (falloffGamma <= 0 || !double.IsFinite(falloffGamma))
                {
                    throw new ArgumentException("FalloffGamma の入力が不正です。", nameof(falloffGamma));
                }

                var paperNoiseStrength0 = PaperNoiseStrengthNumberBox.Value;
                if (paperNoiseStrength0 < 0 || paperNoiseStrength0 > 1.0)
                {
                    throw new ArgumentException("Strength の入力が不正です。", nameof(paperNoiseStrength0));
                }

                var paperNoiseGain0 = PaperNoiseGainNumberBox.Value;
                if (paperNoiseGain0 < 0 || !double.IsFinite(paperNoiseGain0))
                {
                    throw new ArgumentException("Gain の入力が不正です。", nameof(paperNoiseGain0));
                }

                var enableEdgeBoost = EnablePaperNoiseEdgeBoostCheckBox.IsChecked == true;
                var edgeBoost = 0.0;
                var edgeBoostGamma = 1.0;
                if (enableEdgeBoost)
                {
                    edgeBoost = PaperNoiseEdgeBoostNumberBox.Value;
                    if (!double.IsFinite(edgeBoost) || edgeBoost < 0)
                    {
                        throw new ArgumentException("EdgeBoost boost の入力が不正です。", nameof(edgeBoost));
                    }
                    edgeBoostGamma = PaperNoiseEdgeBoostGammaNumberBox.Value;
                    if (!double.IsFinite(edgeBoostGamma) || edgeBoostGamma <= 0)
                    {
                        throw new ArgumentException("EdgeBoost gamma の入力が不正です。", nameof(edgeBoostGamma));
                    }
                }

                var alphaCutoffByte = (int)Math.Round(AlphaCutoffByteNumberBox.Value, MidpointRounding.AwayFromZero);
                if (alphaCutoffByte < 0 || alphaCutoffByte > 255)
                {
                    throw new ArgumentException("Cutoff(alpha) の入力が不正です。", nameof(alphaCutoffByte));
                }
                var alphaCutoff01 = alphaCutoffByte / 255.0;
                var noiseDependentCutoff = NoiseDependentCutoffCheckBox.IsChecked == true;

                var kClampMin = PaperNoiseKClampMinNumberBox.Value;
                var kClampMax = PaperNoiseKClampMaxNumberBox.Value;
                if (!double.IsFinite(kClampMin) || !double.IsFinite(kClampMax) || kClampMin <= 0 || kClampMax <= 0 || kClampMax < kClampMin)
                {
                    throw new ArgumentException("kClamp の入力が不正です。", nameof(kClampMin));
                }

                var enableZClamp = EnablePaperNoiseZClampCheckBox.IsChecked == true;
                var zClampNegAbs = 0.0;
                var zClampPosAbs = 0.0;
                if (enableZClamp)
                {
                    zClampNegAbs = PaperNoiseZClampNegAbsNumberBox.Value;
                    zClampPosAbs = PaperNoiseZClampPosAbsNumberBox.Value;
                    if (zClampNegAbs <= 0 || !double.IsFinite(zClampNegAbs))
                    {
                        throw new ArgumentException("zClamp- の入力が不正です。", nameof(zClampNegAbs));
                    }
                    if (zClampPosAbs <= 0 || !double.IsFinite(zClampPosAbs))
                    {
                        throw new ArgumentException("zClamp+ の入力が不正です。", nameof(zClampPosAbs));
                    }
                }

                var kModeTag = (PaperNoiseKModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
                var kMode = kModeTag switch
                {
                    "Direct01" => DotReproRenderer.KDefinition.Direct01,
                    "BlendToMean1" => DotReproRenderer.KDefinition.BlendToMean1,
                    "ZNormalized" => DotReproRenderer.KDefinition.ZNormalized,
                    _ => DotReproRenderer.KDefinition.RatioToMean,
                };

                var samplingTag = (PaperNoiseSamplingComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
                var samplingMode = samplingTag switch
                {
                    "Nearest" => PaperNoiseTile.SamplingMode.Nearest,
                    "Bicubic" => PaperNoiseTile.SamplingMode.Bicubic,
                    _ => PaperNoiseTile.SamplingMode.Bilinear,
                };

                var applyTag = (PaperNoiseApplyModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
                var applyMode = applyTag switch
                {
                    "Add" => DotReproRenderer.PaperNoiseApplyMode.AddAlpha,
                    _ => DotReproRenderer.PaperNoiseApplyMode.MultiplyAlpha,
                };

                var outAlphaModelTag = (OutAlphaModelComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
                var outAlphaModel = outAlphaModelTag switch
                {
                    "WallThrough" => DotReproRenderer.OutAlphaModel.WallThrough,
                    _ => DotReproRenderer.OutAlphaModel.MultiplyK,
                };

                var wallK = WallKNumberBox.Value;
                if (!double.IsFinite(wallK) || wallK <= 0)
                {
                    throw new ArgumentException("WallK の入力が不正です。", nameof(wallK));
                }

                var wallBaseScale = WallBaseScaleNumberBox.Value;
                if (!double.IsFinite(wallBaseScale) || wallBaseScale <= 0)
                {
                    throw new ArgumentException("Wall base× の入力が不正です。", nameof(wallBaseScale));
                }

                var wallThresholdBias = WallThresholdBiasNumberBox.Value;
                if (!double.IsFinite(wallThresholdBias))
                {
                    throw new ArgumentException("Wall bias の入力が不正です。", nameof(wallThresholdBias));
                }

                var wallKMin = SweepWallKMinNumberBox.Value;
                var wallKMax = SweepWallKMaxNumberBox.Value;
                var wallKStep = SweepWallKStepNumberBox.Value;
                if (wallKMin > wallKMax) (wallKMin, wallKMax) = (wallKMax, wallKMin);
                if (!double.IsFinite(wallKStep) || wallKStep <= 0) wallKStep = 0.01;

                var wallBaseScaleMin = SweepWallBaseScaleMinNumberBox.Value;
                var wallBaseScaleMax = SweepWallBaseScaleMaxNumberBox.Value;
                var wallBaseScaleStep = SweepWallBaseScaleStepNumberBox.Value;
                if (wallBaseScaleMin > wallBaseScaleMax) (wallBaseScaleMin, wallBaseScaleMax) = (wallBaseScaleMax, wallBaseScaleMin);
                if (!double.IsFinite(wallBaseScaleStep) || wallBaseScaleStep <= 0) wallBaseScaleStep = 0.05;

                var wallBiasMin = SweepWallThresholdBiasMinNumberBox.Value;
                var wallBiasMax = SweepWallThresholdBiasMaxNumberBox.Value;
                var wallBiasStep = SweepWallThresholdBiasStepNumberBox.Value;
                if (wallBiasMin > wallBiasMax) (wallBiasMin, wallBiasMax) = (wallBiasMax, wallBiasMin);
                if (!double.IsFinite(wallBiasStep) || wallBiasStep <= 0) wallBiasStep = 0.01;

                var paperMaskModeTag = (PaperMaskModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
                var paperMaskMode = paperMaskModeTag switch
                {
                    "MultiplyOutAlpha" => DotReproRenderer.PaperMaskMode.MultiplyOutAlpha,
                    "SoftOutAlpha" => DotReproRenderer.PaperMaskMode.SoftOutAlpha,
                    "ThresholdOutAlpha" => DotReproRenderer.PaperMaskMode.ThresholdOutAlpha,
                    _ => DotReproRenderer.PaperMaskMode.None,
                };

                var paperMaskThreshold01 = PaperMaskThresholdNumberBox.Value;
                if (!double.IsFinite(paperMaskThreshold01) || paperMaskThreshold01 < 0 || paperMaskThreshold01 > 1.0)
                {
                    throw new ArgumentException("PaperMask th の入力が不正です。", nameof(paperMaskThreshold01));
                }

                var paperMaskGain = PaperMaskGainNumberBox.Value;
                if (!double.IsFinite(paperMaskGain) || paperMaskGain < 0)
                {
                    throw new ArgumentException("PaperMask gain の入力が不正です。", nameof(paperMaskGain));
                }

                var paperMaskFalloffModeTag = (PaperMaskFalloffModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
                var paperMaskFalloffMode = paperMaskFalloffModeTag switch
                {
                    "StrongerAtEdge" => DotReproRenderer.PaperMaskFalloffMode.StrongerAtEdge,
                    "ThresholdAtEdge" => DotReproRenderer.PaperMaskFalloffMode.ThresholdAtEdge,
                    _ => DotReproRenderer.PaperMaskFalloffMode.None,
                };

                var brightX = (int)Math.Round(TileBrightXNumberBox.Value, MidpointRounding.AwayFromZero);
                var brightY = (int)Math.Round(TileBrightYNumberBox.Value, MidpointRounding.AwayFromZero);
                var darkX = (int)Math.Round(TileDarkXNumberBox.Value, MidpointRounding.AwayFromZero);
                var darkY = (int)Math.Round(TileDarkYNumberBox.Value, MidpointRounding.AwayFromZero);

                var expectedModeTag = (ExpectedAlphaModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
                var expectedMode = expectedModeTag switch
                {
                    "Alpha" => TileExtremaMatchEvaluator.ExpectedAlphaMode.UseAlpha,
                    "White" => TileExtremaMatchEvaluator.ExpectedAlphaMode.WhiteBackground255MinusLuma,
                    _ => TileExtremaMatchEvaluator.ExpectedAlphaMode.Auto,
                };

                var baseOffsetX = (int)Math.Round(PaperNoiseOffsetXNumberBox.Value, MidpointRounding.AwayFromZero);
                var baseOffsetY = (int)Math.Round(PaperNoiseOffsetYNumberBox.Value, MidpointRounding.AwayFromZero);

                var offsetXs = sweepOffset
                    ? Enumerable.Range(offsetXMin, offsetXMax - offsetXMin + 1).ToArray()
                    : new[] { baseOffsetX };
                var offsetYs = sweepOffset
                    ? Enumerable.Range(offsetYMin, offsetYMax - offsetYMin + 1).ToArray()
                    : new[] { baseOffsetY };

                var strengths = sweepStrength
                    ? BuildLinearCandidates(strengthMin, strengthMax, strengthStep).ToArray()
                    : new[] { paperNoiseStrength0 };
                var gains = sweepGain
                    ? BuildLinearCandidates(gainMin, gainMax, gainStep).ToArray()
                    : new[] { paperNoiseGain0 };

                var falloffScaleMin = SweepFalloffScaleMinNumberBox.Value;
                var falloffScaleMax = SweepFalloffScaleMaxNumberBox.Value;
                var falloffScaleStep = SweepFalloffScaleStepNumberBox.Value;
                if (falloffScaleMin > falloffScaleMax) (falloffScaleMin, falloffScaleMax) = (falloffScaleMax, falloffScaleMin);
                if (!double.IsFinite(falloffScaleStep) || falloffScaleStep <= 0) falloffScaleStep = 0.05;

                var rNormScaleMin = SweepRNormScaleMinNumberBox.Value;
                var rNormScaleMax = SweepRNormScaleMaxNumberBox.Value;
                var rNormScaleStep = SweepRNormScaleStepNumberBox.Value;
                if (rNormScaleMin > rNormScaleMax) (rNormScaleMin, rNormScaleMax) = (rNormScaleMax, rNormScaleMin);
                if (!double.IsFinite(rNormScaleStep) || rNormScaleStep <= 0) rNormScaleStep = 0.02;

                var falloffGammaMin = SweepFalloffGammaMinNumberBox.Value;
                var falloffGammaMax = SweepFalloffGammaMaxNumberBox.Value;
                var falloffGammaStep = SweepFalloffGammaStepNumberBox.Value;
                if (falloffGammaMin > falloffGammaMax) (falloffGammaMin, falloffGammaMax) = (falloffGammaMax, falloffGammaMin);
                if (!double.IsFinite(falloffGammaStep) || falloffGammaStep <= 0) falloffGammaStep = 0.05;

                var falloffScales = sweepFalloffScale
                    ? BuildLinearCandidates(falloffScaleMin, falloffScaleMax, falloffScaleStep).ToArray()
                    : new[] { falloffScale };
                var rNormScales = sweepRNormScale
                    ? BuildLinearCandidates(rNormScaleMin, rNormScaleMax, rNormScaleStep).ToArray()
                    : new[] { falloffRNormScale };
                var falloffGammas = sweepFalloffGamma
                    ? BuildLinearCandidates(falloffGammaMin, falloffGammaMax, falloffGammaStep).ToArray()
                    : new[] { falloffGamma };

                var wallKs = sweepWallK
                    ? BuildLinearCandidates(wallKMin, wallKMax, wallKStep).ToArray()
                    : new[] { wallK };

                var wallBaseScales = sweepWallBaseScale
                    ? BuildLinearCandidates(wallBaseScaleMin, wallBaseScaleMax, wallBaseScaleStep).ToArray()
                    : new[] { wallBaseScale };

                var wallBiases = sweepWallThresholdBias
                    ? BuildLinearCandidates(wallBiasMin, wallBiasMax, wallBiasStep).ToArray()
                    : new[] { wallThresholdBias };

                var parallelDegree = (int)Math.Round(SweepParallelDegreeNumberBox.Value, MidpointRounding.AwayFromZero);
                if (parallelDegree <= 0) parallelDegree = 1;

                var maxIters = (long)Math.Round(SweepMaxItersNumberBox.Value, MidpointRounding.AwayFromZero);
                if (maxIters <= 0) maxIters = 2_000_000;

                _isSweeping = true;
                _sweepCts?.Dispose();
                _sweepCts = new CancellationTokenSource();
                var ct = _sweepCts.Token;

                UpdateStatus("Sweep preparing...");

                var expectedPixels = _expected.Pixels;
                var noiseTile = _paperNoise;

                var baseOpt = new DotReproRenderer.Options(
                    CanvasSizePx: canvasSizePx,
                    DiameterPx: diameterPx,
                    Pressure: pressure,
                    FalloffLut: falloffLut,
                    FalloffScale: falloffScale,
                    FalloffRNormScale: falloffRNormScale,
                    FalloffGamma: falloffGamma,
                    RadiusPadPx: radiusPadPx,
                    NoiseTile: noiseTile,
                    UsePaperNoise: true,
                    NoiseSamplingMode: samplingMode,
                    PaperNoiseScale: paperNoiseScale,
                    PaperNoiseOffsetX: baseOffsetX,
                    PaperNoiseOffsetY: baseOffsetY,
                    PaperNoiseStrength: paperNoiseStrength0,
                    PaperNoiseGain: paperNoiseGain0,
                    KMode: kMode,
                    PaperNoiseApplyMode: applyMode,
                    OutAlphaModel: outAlphaModel,
                    WallK: wallK,
                WallBaseScale: wallBaseScale,
                WallThresholdBias: wallThresholdBias,
                    KClampMin: kClampMin,
                    KClampMax: kClampMax,
                    AlphaCutoff01: alphaCutoff01,
                    NoiseDependentCutoff: noiseDependentCutoff,
                    DisableKMeanNormalization: DisableKMeanNormalizationCheckBox.IsChecked == true,
                    EnablePaperNoiseZClamp: enableZClamp,
                    PaperNoiseZClampNegAbs: zClampNegAbs,
                    PaperNoiseZClampPosAbs: zClampPosAbs,
                    EnablePaperNoiseEdgeBoost: enableEdgeBoost,
                    PaperNoiseEdgeBoost: edgeBoost,
                    PaperNoiseEdgeBoostGamma: edgeBoostGamma,
                    PaperMaskMode: paperMaskMode,
                    PaperMaskThreshold01: paperMaskThreshold01,
                    PaperMaskGain: paperMaskGain,
                    PaperMaskFalloffMode: paperMaskFalloffMode);

                var staged = SweepStagedCheckBox.IsChecked == true;
                var coarseOffsetStep = (int)Math.Round(SweepCoarseOffsetStepNumberBox.Value, MidpointRounding.AwayFromZero);
                if (coarseOffsetStep <= 0) coarseOffsetStep = 1;

                var coarseStepMultiplier = (int)Math.Round(SweepCoarseStepMultiplierNumberBox.Value, MidpointRounding.AwayFromZero);
                if (coarseStepMultiplier <= 0) coarseStepMultiplier = 1;

                var topK = (int)Math.Round(SweepRefineTopKNumberBox.Value, MidpointRounding.AwayFromZero);
                if (topK <= 0) topK = 1;

                var refineOffsetRadius = (int)Math.Round(SweepRefineOffsetRadiusNumberBox.Value, MidpointRounding.AwayFromZero);
                if (refineOffsetRadius < 0) refineOffsetRadius = 0;

                var refineStepsRadius = (int)Math.Round(SweepRefineStepsRadiusNumberBox.Value, MidpointRounding.AwayFromZero);
                if (refineStepsRadius < 0) refineStepsRadius = 0;

                var swAll = Stopwatch.StartNew();
                var swProgress = Stopwatch.StartNew();

                var useRoi = SweepUseRoiCheckBox.IsChecked == true;
                var roiX = (int)Math.Round(SweepRoiXNumberBox.Value, MidpointRounding.AwayFromZero);
                var roiY = (int)Math.Round(SweepRoiYNumberBox.Value, MidpointRounding.AwayFromZero);
                var roiW = (int)Math.Round(SweepRoiWNumberBox.Value, MidpointRounding.AwayFromZero);
                var roiH = (int)Math.Round(SweepRoiHNumberBox.Value, MidpointRounding.AwayFromZero);
                var roiStride = (int)Math.Round(SweepRoiStrideNumberBox.Value, MidpointRounding.AwayFromZero);
                if (roiStride <= 0) roiStride = 1;

                var detailMaxRows = (int)Math.Round(SweepDetailMaxRowsNumberBox.Value, MidpointRounding.AwayFromZero);
                if (detailMaxRows <= 0) detailMaxRows = 200_000;

                if (useRoi)
                {
                    if (roiX < 0 || roiY < 0 || roiW <= 0 || roiH <= 0)
                    {
                        throw new ArgumentException("ROI(xywh) の入力が不正です。", nameof(roiX));
                    }

                    if (roiX + roiW > canvasSizePx || roiY + roiH > canvasSizePx)
                    {
                        throw new ArgumentException($"ROIがキャンバス外です。 roi=({roiX},{roiY},{roiW},{roiH}) canvas={canvasSizePx}x{canvasSizePx}", nameof(roiX));
                    }
                }

                // プログレス更新（ステージ込み）
                var progress = new Progress<(string stage, long done, long total, SweepEval best)>(p =>
                {
                    SweepProgressBar.Minimum = 0;
                    SweepProgressBar.Maximum = Math.Max(1, p.total);
                    SweepProgressBar.Value = Math.Min(p.done, p.total);

                    static string FormatEta(TimeSpan t)
                    {
                        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
                        // 99:59:59 まではHH:mm:ss、それ以上はd.HH:mm:ss
                        if (t.TotalDays >= 1)
                        {
                            return $"{(int)t.TotalDays}.{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";
                        }
                        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
                    }

                    var etaText = "";
                    if (p.done > 0)
                    {
                        var elapsed = swProgress.Elapsed;
                        var elapsedSec = elapsed.TotalSeconds;
                        if (elapsedSec >= 0.2)
                        {
                            var rate = p.done / elapsedSec;
                            if (rate > 0)
                            {
                                var remaining = Math.Max(0, p.total - p.done);
                                var eta = TimeSpan.FromSeconds(remaining / rate);
                                etaText = $"  ETA={FormatEta(eta)}";
                            }
                        }
                    }

                    var best = p.best;
                    SweepProgressTextBlock.Text = $"{p.stage}  {p.done} / {p.total}  bestMAE={best.Mae:0.###}  off=({best.Candidate.OffsetX},{best.Candidate.OffsetY})" +
                        $"  strength={best.Candidate.Strength:0.####} gain={best.Candidate.Gain:0.####}" +
                        $"  falloff={best.Candidate.FalloffScale:0.###}/{best.Candidate.RNormScale:0.###}/{best.Candidate.Gamma:0.###}" +
                        $"  wall={best.Candidate.WallK:0.####}/{best.Candidate.WallBaseScale:0.####}/{best.Candidate.WallThresholdBias:0.####}" +
                        etaText;
                });

                var ctx = new SweepEvalContext(
                    CanvasSizePx: canvasSizePx,
                    DiameterPx: diameterPx,
                    RadiusPadPx: radiusPadPx,
                    NoiseTile: noiseTile,
                    NoiseScale: paperNoiseScale,
                    BrightTileX: brightX,
                    BrightTileY: brightY,
                    DarkTileX: darkX,
                    DarkTileY: darkY,
                    UseRoi: useRoi,
                    RoiX: roiX,
                    RoiY: roiY,
                    RoiW: roiW,
                    RoiH: roiH,
                    RoiStride: roiStride,
                    DetailMaxRows: detailMaxRows,
                    ExpectedMode: expectedMode,
                    ExpectedPixels: expectedPixels,
                    KMeanStride: kMeanStride,
                    ParallelDegree: parallelDegree);

                SweepEval bestAll;
                List<SweepEval> topSeeds;
                var overallTop10 = new List<SweepEval>(capacity: 10);

                if (!staged)
                {
                    var totalIters = ComputeTotalIters(
                        offsetXs.Length,
                        offsetYs.Length,
                        strengths.Length,
                        gains.Length,
                        falloffScales.Length,
                        rNormScales.Length,
                        falloffGammas.Length,
                        wallKs.Length,
                        wallBaseScales.Length,
                        wallBiases.Length);
                    if (totalIters <= 0 || totalIters > maxIters)
                    {
                        throw new InvalidOperationException($"探索件数が多すぎます: {totalIters} 件（上限 {maxIters}）。範囲/stepを絞ってください。");
                    }

                    UpdateStatus($"Sweep start... iters={totalIters} parallel={parallelDegree}");

                    SweepProgressBar.Value = 0;
                    SweepProgressTextBlock.Text = $"0 / {totalIters}";

                    var stage1 = await Task.Run(() => RunSweepStage(
                        ctx,
                        baseOpt,
                        offsetXs,
                        offsetYs,
                        strengths,
                        gains,
                        falloffScales,
                        rNormScales,
                        falloffGammas,
                        wallKs,
                        wallBaseScales,
                        wallBiases,
                        topK: 10,
                        stageName: "Sweep",
                        progressBase: 0,
                        progressTotal: totalIters,
                        progress,
                        ct), ct);

                    bestAll = stage1.Best;
                    topSeeds = new List<SweepEval> { stage1.Best };
                    overallTop10 = stage1.TopK;
                }
                else
                {
                    // --- Coarse stage ---
                    var offsetXsCoarse = sweepOffset ? BuildIntCandidates(offsetXMin, offsetXMax, coarseOffsetStep) : new[] { baseOffsetX };
                    var offsetYsCoarse = sweepOffset ? BuildIntCandidates(offsetYMin, offsetYMax, coarseOffsetStep) : new[] { baseOffsetY };

                    var strengthsCoarse = sweepStrength ? BuildLinearCandidates(strengthMin, strengthMax, strengthStep * coarseStepMultiplier).ToArray() : new[] { paperNoiseStrength0 };
                    var gainsCoarse = sweepGain ? BuildLinearCandidates(gainMin, gainMax, gainStep * coarseStepMultiplier).ToArray() : new[] { paperNoiseGain0 };
                    var falloffScalesCoarse = sweepFalloffScale ? BuildLinearCandidates(falloffScaleMin, falloffScaleMax, falloffScaleStep * coarseStepMultiplier).ToArray() : new[] { falloffScale };
                    var rNormScalesCoarse = sweepRNormScale ? BuildLinearCandidates(rNormScaleMin, rNormScaleMax, rNormScaleStep * coarseStepMultiplier).ToArray() : new[] { falloffRNormScale };
                    var gammasCoarse = sweepFalloffGamma ? BuildLinearCandidates(falloffGammaMin, falloffGammaMax, falloffGammaStep * coarseStepMultiplier).ToArray() : new[] { falloffGamma };

                    var wallKsCoarse = sweepWallK ? BuildLinearCandidates(wallKMin, wallKMax, wallKStep * coarseStepMultiplier).ToArray() : new[] { wallK };
                    var wallBaseScalesCoarse = sweepWallBaseScale ? BuildLinearCandidates(wallBaseScaleMin, wallBaseScaleMax, wallBaseScaleStep * coarseStepMultiplier).ToArray() : new[] { wallBaseScale };
                    var wallBiasesCoarse = sweepWallThresholdBias ? BuildLinearCandidates(wallBiasMin, wallBiasMax, wallBiasStep * coarseStepMultiplier).ToArray() : new[] { wallThresholdBias };

                    var coarseIters = ComputeTotalIters(
                        offsetXsCoarse.Length,
                        offsetYsCoarse.Length,
                        strengthsCoarse.Length,
                        gainsCoarse.Length,
                        falloffScalesCoarse.Length,
                        rNormScalesCoarse.Length,
                        gammasCoarse.Length,
                        wallKsCoarse.Length,
                        wallBaseScalesCoarse.Length,
                        wallBiasesCoarse.Length);
                    if (coarseIters <= 0 || coarseIters > maxIters)
                    {
                        throw new InvalidOperationException($"粗探索の件数が多すぎます: {coarseIters} 件（上限 {maxIters}）。粗探索の刻みを増やすか範囲を絞ってください。");
                    }

                    UpdateStatus($"Sweep staged start... coarseIters={coarseIters} topK={topK} parallel={parallelDegree}");

                    SweepProgressBar.Value = 0;
                    SweepProgressTextBlock.Text = $"0 / {coarseIters}";

                    var coarse = await Task.Run(() => RunSweepStage(
                        ctx,
                        baseOpt,
                        offsetXsCoarse,
                        offsetYsCoarse,
                        strengthsCoarse,
                        gainsCoarse,
                        falloffScalesCoarse,
                        rNormScalesCoarse,
                        gammasCoarse,
                        wallKsCoarse,
                        wallBaseScalesCoarse,
                        wallBiasesCoarse,
                        topK,
                        stageName: "Coarse",
                        progressBase: 0,
                        progressTotal: coarseIters,
                        progress,
                        ct), ct);

                    bestAll = coarse.Best;
                    topSeeds = coarse.TopK;
                    foreach (var item in coarse.TopK)
                    {
                        AddTopK(overallTop10, item, 10);
                    }

                    // --- Refine stage ---
                    // 近傍探索の範囲は「細stepの±N step」方式に寄せる。
                    var refineSeeds = topSeeds.OrderBy(v => v.Mae).Take(topK).ToArray();
                    if (refineSeeds.Length == 0)
                    {
                        throw new InvalidOperationException("粗探索の結果が0件です（評価点が0件の可能性があります）。");
                    }

                    // 上限内に収まるseed数まで実行（端で範囲が狭まるケースもあるので、順に加算して切る）
                    var refineRuns = new List<RefineRun>(refineSeeds.Length);
                    long refineItersSum = 0;
                    foreach (var seed in refineSeeds)
                    {
                        var rx = sweepOffset ? BuildIntCandidates(Math.Max(offsetXMin, seed.Candidate.OffsetX - refineOffsetRadius), Math.Min(offsetXMax, seed.Candidate.OffsetX + refineOffsetRadius), step: 1) : new[] { baseOffsetX };
                        var ry = sweepOffset ? BuildIntCandidates(Math.Max(offsetYMin, seed.Candidate.OffsetY - refineOffsetRadius), Math.Min(offsetYMax, seed.Candidate.OffsetY + refineOffsetRadius), step: 1) : new[] { baseOffsetY };

                        var rs = sweepStrength ? BuildLinearCandidates(
                            Math.Max(strengthMin, seed.Candidate.Strength - (refineStepsRadius * strengthStep)),
                            Math.Min(strengthMax, seed.Candidate.Strength + (refineStepsRadius * strengthStep)),
                            strengthStep).ToArray() : new[] { paperNoiseStrength0 };

                        var rg = sweepGain ? BuildLinearCandidates(
                            Math.Max(gainMin, seed.Candidate.Gain - (refineStepsRadius * gainStep)),
                            Math.Min(gainMax, seed.Candidate.Gain + (refineStepsRadius * gainStep)),
                            gainStep).ToArray() : new[] { paperNoiseGain0 };

                        var rfs = sweepFalloffScale ? BuildLinearCandidates(
                            Math.Max(falloffScaleMin, seed.Candidate.FalloffScale - (refineStepsRadius * falloffScaleStep)),
                            Math.Min(falloffScaleMax, seed.Candidate.FalloffScale + (refineStepsRadius * falloffScaleStep)),
                            falloffScaleStep).ToArray() : new[] { falloffScale };

                        var rrn = sweepRNormScale ? BuildLinearCandidates(
                            Math.Max(rNormScaleMin, seed.Candidate.RNormScale - (refineStepsRadius * rNormScaleStep)),
                            Math.Min(rNormScaleMax, seed.Candidate.RNormScale + (refineStepsRadius * rNormScaleStep)),
                            rNormScaleStep).ToArray() : new[] { falloffRNormScale };

                        var rga = sweepFalloffGamma ? BuildLinearCandidates(
                            Math.Max(falloffGammaMin, seed.Candidate.Gamma - (refineStepsRadius * falloffGammaStep)),
                            Math.Min(falloffGammaMax, seed.Candidate.Gamma + (refineStepsRadius * falloffGammaStep)),
                            falloffGammaStep).ToArray() : new[] { falloffGamma };

                        var rwk = sweepWallK ? BuildLinearCandidates(
                            Math.Max(wallKMin, seed.Candidate.WallK - (refineStepsRadius * wallKStep)),
                            Math.Min(wallKMax, seed.Candidate.WallK + (refineStepsRadius * wallKStep)),
                            wallKStep).ToArray() : new[] { wallK };

                        var rwb = sweepWallBaseScale ? BuildLinearCandidates(
                            Math.Max(wallBaseScaleMin, seed.Candidate.WallBaseScale - (refineStepsRadius * wallBaseScaleStep)),
                            Math.Min(wallBaseScaleMax, seed.Candidate.WallBaseScale + (refineStepsRadius * wallBaseScaleStep)),
                            wallBaseScaleStep).ToArray() : new[] { wallBaseScale };

                        var rbi = sweepWallThresholdBias ? BuildLinearCandidates(
                            Math.Max(wallBiasMin, seed.Candidate.WallThresholdBias - (refineStepsRadius * wallBiasStep)),
                            Math.Min(wallBiasMax, seed.Candidate.WallThresholdBias + (refineStepsRadius * wallBiasStep)),
                            wallBiasStep).ToArray() : new[] { wallThresholdBias };

                        var iters = ComputeTotalIters(rx.Length, ry.Length, rs.Length, rg.Length, rfs.Length, rrn.Length, rga.Length, rwk.Length, rwb.Length, rbi.Length);
                        if (iters <= 0) continue;

                        if ((coarseIters + refineItersSum + iters) > maxIters)
                        {
                            break;
                        }

                        refineItersSum += iters;
                        refineRuns.Add(new RefineRun(seed, rx, ry, rs, rg, rfs, rrn, rga, rwk, rwb, rbi, iters));
                    }

                    if (refineRuns.Count == 0)
                    {
                        // coarseのみで完了
                        SweepProgressBar.Value = coarseIters;
                    }
                    else
                    {
                        var total = coarseIters + refineItersSum;
                        var baseDone = coarseIters;

                        foreach (var run in refineRuns)
                        {
                            var refined = await Task.Run(() => RunSweepStage(
                                ctx,
                                baseOpt,
                                run.OffsetXs,
                                run.OffsetYs,
                                run.Strengths,
                                run.Gains,
                                run.FalloffScales,
                                run.RNormScales,
                                run.Gammas,
                                run.WallKs,
                                run.WallBaseScales,
                                run.WallBiases,
                                topK: 10,
                                stageName: "Refine",
                                progressBase: baseDone,
                                progressTotal: total,
                                progress,
                                ct), ct);

                            baseDone += run.TotalIters;
                            if (refined.Best.Mae < bestAll.Mae)
                            {
                                bestAll = refined.Best;
                            }

                            foreach (var item in refined.TopK)
                            {
                                AddTopK(overallTop10, item, 10);
                            }
                        }
                    }
                }

                swAll.Stop();

                var msg = $"Sweep done. bestMAE={bestAll.Mae:0.###}  bestOffset=({bestAll.Candidate.OffsetX},{bestAll.Candidate.OffsetY})" +
                          $"  strength={bestAll.Candidate.Strength:0.####} gain={bestAll.Candidate.Gain:0.####}" +
                          $"  falloff={bestAll.Candidate.FalloffScale:0.###}/{bestAll.Candidate.RNormScale:0.###}/{bestAll.Candidate.Gamma:0.###}" +
                          $"  wall={bestAll.Candidate.WallK:0.####}/{bestAll.Candidate.WallBaseScale:0.####}/{bestAll.Candidate.WallThresholdBias:0.####}" +
                          $"  points(bright={bestAll.BrightCount} dark={bestAll.DarkCount})" +
                          $"  time={swAll.Elapsed.TotalSeconds:0.0}s";

                UpdateStatus(msg);

                // Top10をCSVへ出力（設定値とスコア詳細）
                try
                {
                    ExportSweepTop10Csv(overallTop10, ctx, baseOpt, ExpectedPathTextBox.Text);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Sweep結果自体は確定済みなので、CSV出力失敗は警告扱い
                    UpdateStatus(StatusTextBlock.Text + $"  (CSV export failed: {ex.Message})");
                }

                if (apply)
                {
                    PaperNoiseOffsetXNumberBox.Value = bestAll.Candidate.OffsetX;
                    PaperNoiseOffsetYNumberBox.Value = bestAll.Candidate.OffsetY;
                    if (sweepStrength) PaperNoiseStrengthNumberBox.Value = bestAll.Candidate.Strength;
                    if (sweepGain) PaperNoiseGainNumberBox.Value = bestAll.Candidate.Gain;

                    if (sweepWallK) WallKNumberBox.Value = bestAll.Candidate.WallK;
                    if (sweepWallBaseScale) WallBaseScaleNumberBox.Value = bestAll.Candidate.WallBaseScale;
                    if (sweepWallThresholdBias) WallThresholdBiasNumberBox.Value = bestAll.Candidate.WallThresholdBias;

                    if (sweepFalloffScale) FalloffScaleNumberBox.Value = bestAll.Candidate.FalloffScale;
                    if (sweepRNormScale) FalloffRNormScaleNumberBox.Value = bestAll.Candidate.RNormScale;
                    if (sweepFalloffGamma) FalloffGammaNumberBox.Value = bestAll.Candidate.Gamma;
                }

                // 最後に1回だけフルレンダ（視覚確認とRendered更新用）
                if (apply)
                {
                    TriggerRender(showErrors: true);
                }

            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Sweep canceled.");
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isSweeping = false;
                _sweepCts?.Dispose();
                _sweepCts = null;
            }
        }

        private static int[] BuildIntCandidates(int min, int max, int step)
        {
            if (step <= 0) step = 1;
            if (min > max) (min, max) = (max, min);

            var n = ((max - min) / step) + 1;
            if (n <= 0) n = 1;

            var list = new List<int>(capacity: n + 1);
            for (var v = min; v <= max; v += step)
            {
                list.Add(v);
            }

            if (list.Count == 0 || list[^1] != max)
            {
                list.Add(max);
            }

            return list.ToArray();
        }

        private static long ComputeTotalIters(params int[] lens)
        {
            long total = 1;
            foreach (var len in lens)
            {
                if (len <= 0) return 0;
                total = checked(total * len);
            }
            return total;
        }

        private readonly record struct SweepCandidate(
            int OffsetX,
            int OffsetY,
            double Strength,
            double Gain,
            double FalloffScale,
            double RNormScale,
            double Gamma,
            double WallK,
            double WallBaseScale,
            double WallThresholdBias);

        private sealed record SweepEval(SweepCandidate Candidate, double Mae, int BrightCount, int DarkCount)
        {
            public static SweepEval CreateWorst(SweepCandidate candidate)
            {
                return new SweepEval(candidate, double.PositiveInfinity, 0, 0);
            }
        }

        private sealed record SweepStageResult(SweepEval Best, List<SweepEval> TopK);

        private sealed record RefineRun(
            SweepEval Seed,
            int[] OffsetXs,
            int[] OffsetYs,
            double[] Strengths,
            double[] Gains,
            double[] FalloffScales,
            double[] RNormScales,
            double[] Gammas,
            double[] WallKs,
            double[] WallBaseScales,
            double[] WallBiases,
            long TotalIters);

        private sealed record SweepEvalContext(
            int CanvasSizePx,
            int DiameterPx,
            double RadiusPadPx,
            PaperNoiseTile NoiseTile,
            double NoiseScale,
            int BrightTileX,
            int BrightTileY,
            int DarkTileX,
            int DarkTileY,
            bool UseRoi,
            int RoiX,
            int RoiY,
            int RoiW,
            int RoiH,
            int RoiStride,
            int DetailMaxRows,
            TileExtremaMatchEvaluator.ExpectedAlphaMode ExpectedMode,
            SKColor[] ExpectedPixels,
            int KMeanStride,
            int ParallelDegree);

        private static SweepStageResult RunSweepStage(
            SweepEvalContext ctx,
            DotReproRenderer.Options baseOpt,
            int[] offsetXs,
            int[] offsetYs,
            double[] strengths,
            double[] gains,
            double[] falloffScales,
            double[] rNormScales,
            double[] gammas,
            double[] wallKs,
            double[] wallBaseScales,
            double[] wallBiases,
            int topK,
            string stageName,
            long progressBase,
            long progressTotal,
            IProgress<(string stage, long done, long total, SweepEval best)> progress,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(ctx);
            ArgumentNullException.ThrowIfNull(baseOpt);
            ArgumentNullException.ThrowIfNull(offsetXs);
            ArgumentNullException.ThrowIfNull(offsetYs);
            ArgumentNullException.ThrowIfNull(strengths);
            ArgumentNullException.ThrowIfNull(gains);
            ArgumentNullException.ThrowIfNull(falloffScales);
            ArgumentNullException.ThrowIfNull(rNormScales);
            ArgumentNullException.ThrowIfNull(gammas);
            ArgumentNullException.ThrowIfNull(wallKs);
            ArgumentNullException.ThrowIfNull(wallBaseScales);
            ArgumentNullException.ThrowIfNull(wallBiases);

            if (topK <= 0) topK = 1;

            var totalIters = ComputeTotalIters(
                offsetXs.Length,
                offsetYs.Length,
                strengths.Length,
                gains.Length,
                falloffScales.Length,
                rNormScales.Length,
                gammas.Length,
                wallKs.Length,
                wallBaseScales.Length,
                wallBiases.Length);
            if (totalIters <= 0)
            {
                var worst = SweepEval.CreateWorst(new SweepCandidate(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
                return new SweepStageResult(worst, new List<SweepEval>());
            }

            // bestMaeの共有（早期打ち切り用）
            var bestMaeBits = BitConverter.DoubleToInt64Bits(double.PositiveInfinity);
            var bestGate = new object();
            var bestEval = SweepEval.CreateWorst(new SweepCandidate(
                offsetXs[0],
                offsetYs[0],
                strengths[0],
                gains[0],
                falloffScales[0],
                rNormScales[0],
                gammas[0],
                wallKs[0],
                wallBaseScales[0],
                wallBiases[0]));

            var globalTopK = new List<SweepEval>(capacity: topK);
            long done = 0;

            var reportEvery = Math.Max(500L, totalIters / 500);
            var lastReport = 0L;

            void ReportProgressIfNeeded(long doneNow)
            {
                if ((doneNow - lastReport) < reportEvery) return;
                lock (bestGate)
                {
                    if ((doneNow - lastReport) < reportEvery) return;
                    lastReport = doneNow;
                    progress.Report((stageName, progressBase + doneNow, progressTotal, bestEval));
                }
            }

            var useRoi = ctx.UseRoi;

            var roiStride = Math.Max(1, ctx.RoiStride);
            var roiTotalPts = 0L;
            if (useRoi)
            {
                var roiWCount = (ctx.RoiW + roiStride - 1) / roiStride;
                var roiHCount = (ctx.RoiH + roiStride - 1) / roiStride;
                roiTotalPts = (long)roiWCount * roiHCount;
            }

            // 一番長い軸で並列分割して偏りを抑える
            var splitAxis = SelectSplitAxis(
                offsetXs.Length,
                offsetYs.Length,
                strengths.Length,
                gains.Length,
                falloffScales.Length,
                rNormScales.Length,
                gammas.Length,
                wallKs.Length,
                wallBaseScales.Length,
                wallBiases.Length);
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, ctx.ParallelDegree),
                CancellationToken = ct,
            };

            Parallel.ForEach(splitAxis, options, sliceIndex =>
            {
                var localTop = new List<SweepEval>(capacity: topK);
                var localBest = SweepEval.CreateWorst(bestEval.Candidate);

                var oxs = splitAxis.Axis == SplitAxisKind.OffsetX ? new[] { offsetXs[sliceIndex] } : offsetXs;
                var oys = splitAxis.Axis == SplitAxisKind.OffsetY ? new[] { offsetYs[sliceIndex] } : offsetYs;
                var ss = splitAxis.Axis == SplitAxisKind.Strength ? new[] { strengths[sliceIndex] } : strengths;
                var gs = splitAxis.Axis == SplitAxisKind.Gain ? new[] { gains[sliceIndex] } : gains;
                var fss = splitAxis.Axis == SplitAxisKind.FalloffScale ? new[] { falloffScales[sliceIndex] } : falloffScales;
                var rns = splitAxis.Axis == SplitAxisKind.RNormScale ? new[] { rNormScales[sliceIndex] } : rNormScales;
                var gams = splitAxis.Axis == SplitAxisKind.Gamma ? new[] { gammas[sliceIndex] } : gammas;
                var wks = splitAxis.Axis == SplitAxisKind.WallK ? new[] { wallKs[sliceIndex] } : wallKs;
                var wbs = splitAxis.Axis == SplitAxisKind.WallBaseScale ? new[] { wallBaseScales[sliceIndex] } : wallBaseScales;
                var biases = splitAxis.Axis == SplitAxisKind.WallThresholdBias ? new[] { wallBiases[sliceIndex] } : wallBiases;

                void Consider(SweepEval e)
                {
                    if (e.Mae < localBest.Mae)
                    {
                        localBest = e;
                    }
                    AddTopK(localTop, e, topK);
                }

                // ループ順は現状のまま。sliceIndexで該当軸を固定し、それ以外を全探索。
                foreach (var fs in fss)
                {
                    foreach (var rn in rns)
                    {
                        foreach (var gamma in gams)
                        {
                            foreach (var wk in wks)
                            {
                                foreach (var wb in wbs)
                                {
                                    foreach (var bias in biases)
                                    {
                            foreach (var strength in ss)
                            {
                                foreach (var gain in gs)
                                {
                                    foreach (var ox in oxs)
                                    {
                                        foreach (var oy in oys)
                                        {
                                            options.CancellationToken.ThrowIfCancellationRequested();
                                            var doneNow = Interlocked.Increment(ref done);

                                            // 候補opt
                                            var opt = baseOpt with
                                            {
                                                FalloffScale = fs,
                                                FalloffRNormScale = rn,
                                                FalloffGamma = gamma,
                                                PaperNoiseOffsetX = ox,
                                                PaperNoiseOffsetY = oy,
                                                PaperNoiseStrength = strength,
                                                PaperNoiseGain = gain,
                                                WallK = wk,
                                                WallBaseScale = wb,
                                                WallThresholdBias = bias,
                                            };

                                            // 評価点
                                            TileExtremaMatchEvaluator.PointLists? pts = null;
                                            long totalPts;
                                            if (useRoi)
                                            {
                                                totalPts = roiTotalPts;
                                            }
                                            else
                                            {
                                                pts = TileExtremaMatchEvaluator.EnumeratePoints(new TileExtremaMatchEvaluator.EnumerateInputs(
                                                    CanvasW: ctx.CanvasSizePx,
                                                    CanvasH: ctx.CanvasSizePx,
                                                    DiameterPx: ctx.DiameterPx,
                                                    RadiusPadPx: ctx.RadiusPadPx,
                                                    NoiseTileW: ctx.NoiseTile.Width,
                                                    NoiseTileH: ctx.NoiseTile.Height,
                                                    NoiseScale: ctx.NoiseScale,
                                                    NoiseOffsetX: ox,
                                                    NoiseOffsetY: oy,
                                                    BrightTileX: ctx.BrightTileX,
                                                    BrightTileY: ctx.BrightTileY,
                                                    DarkTileX: ctx.DarkTileX,
                                                    DarkTileY: ctx.DarkTileY));

                                                totalPts = pts.TotalCount;
                                            }

                                            if (totalPts <= 0)
                                            {
                                                ReportProgressIfNeeded(doneNow);
                                                continue;
                                            }

                                            // point evaluator
                                            var eval = DotReproRenderer.CreatePointEvaluator(opt, ctx.KMeanStride);

                                            var bestMae = BitConverter.Int64BitsToDouble(Interlocked.Read(ref bestMaeBits));
                                            long sumAbs = 0;
                                            var idxLimit = bestMae < double.PositiveInfinity ? (long)Math.Floor(bestMae * totalPts) : long.MaxValue;
                                            if (idxLimit < 0) idxLimit = 0;

                                            if (useRoi)
                                            {
                                                for (var y = ctx.RoiY; y < ctx.RoiY + ctx.RoiH; y += roiStride)
                                                {
                                                    for (var x = ctx.RoiX; x < ctx.RoiX + ctx.RoiW; x += roiStride)
                                                    {
                                                        var idx = (y * ctx.CanvasSizePx) + x;
                                                        var expA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(ctx.ExpectedPixels[idx], ctx.ExpectedMode);
                                                        var renA = eval.EvalAlphaByte(x, y);
                                                        sumAbs += Math.Abs(renA - expA);
                                                        if (sumAbs > idxLimit) goto NextCandidate;
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                foreach (var (x, y) in pts!.BrightPoints)
                                                {
                                                    var idx = (y * ctx.CanvasSizePx) + x;
                                                    var expA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(ctx.ExpectedPixels[idx], ctx.ExpectedMode);
                                                    var renA = eval.EvalAlphaByte(x, y);
                                                    sumAbs += Math.Abs(renA - expA);
                                                    if (sumAbs > idxLimit) goto NextCandidate;
                                                }

                                                foreach (var (x, y) in pts!.DarkPoints)
                                                {
                                                    var idx = (y * ctx.CanvasSizePx) + x;
                                                    var expA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(ctx.ExpectedPixels[idx], ctx.ExpectedMode);
                                                    var renA = eval.EvalAlphaByte(x, y);
                                                    sumAbs += Math.Abs(renA - expA);
                                                    if (sumAbs > idxLimit) goto NextCandidate;
                                                }
                                            }

                                            var mae = sumAbs / (double)totalPts;
                                            var e = new SweepEval(
                                                new SweepCandidate(ox, oy, strength, gain, fs, rn, gamma, wk, wb, bias),
                                                mae,
                                                BrightCount: useRoi ? (int)Math.Min(int.MaxValue, totalPts) : pts!.BrightPoints.Count,
                                                DarkCount: useRoi ? 0 : pts!.DarkPoints.Count);

                                            Consider(e);

                                            // global best更新
                                            if (mae < bestMae)
                                            {
                                                // bestMaeBitsを先に更新し、ロック下でbestEvalを更新
                                                Interlocked.Exchange(ref bestMaeBits, BitConverter.DoubleToInt64Bits(mae));
                                                lock (bestGate)
                                                {
                                                    if (mae < bestEval.Mae)
                                                    {
                                                        bestEval = e;
                                                    }
                                                }
                                            }

                                        NextCandidate:
                                            ReportProgressIfNeeded(doneNow);
                                        }
                                    }
                                }
                            }
                                    }
                                }
                            }
                        }
                    }
                }

                if (localTop.Count == 0) return;

                lock (bestGate)
                {
                    // merge topK
                    foreach (var e in localTop)
                    {
                        AddTopK(globalTopK, e, topK);
                    }

                    if (localBest.Mae < bestEval.Mae)
                    {
                        bestEval = localBest;
                        Interlocked.Exchange(ref bestMaeBits, BitConverter.DoubleToInt64Bits(bestEval.Mae));
                    }
                }
            });

            progress.Report((stageName, progressBase + done, progressTotal, bestEval));

            // TopKは昇順で返す
            globalTopK.Sort(static (a, b) => a.Mae.CompareTo(b.Mae));
            if (globalTopK.Count > topK)
            {
                globalTopK.RemoveRange(topK, globalTopK.Count - topK);
            }

            return new SweepStageResult(bestEval, globalTopK);
        }

        private enum SplitAxisKind
        {
            OffsetX,
            OffsetY,
            Strength,
            Gain,
            FalloffScale,
            RNormScale,
            Gamma,
            WallK,
            WallBaseScale,
            WallThresholdBias,
        }

        private sealed record SplitAxis(SplitAxisKind Axis, int Length) : IEnumerable<int>
        {
            public IEnumerator<int> GetEnumerator()
            {
                for (var i = 0; i < Length; i++) yield return i;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private static SplitAxis SelectSplitAxis(int lenOx, int lenOy, int lenS, int lenG, int lenFs, int lenRn, int lenGamma, int lenWallK, int lenWallBase, int lenWallBias)
        {
            // 最も長い軸を選ぶ（同率ならoffset優先）
            var best = (Axis: SplitAxisKind.OffsetX, Len: lenOx);
            void Consider(SplitAxisKind axis, int len)
            {
                if (len > best.Len)
                {
                    best = (axis, len);
                }
            }

            Consider(SplitAxisKind.OffsetY, lenOy);
            Consider(SplitAxisKind.Strength, lenS);
            Consider(SplitAxisKind.Gain, lenG);
            Consider(SplitAxisKind.FalloffScale, lenFs);
            Consider(SplitAxisKind.RNormScale, lenRn);
            Consider(SplitAxisKind.Gamma, lenGamma);
            Consider(SplitAxisKind.WallK, lenWallK);
            Consider(SplitAxisKind.WallBaseScale, lenWallBase);
            Consider(SplitAxisKind.WallThresholdBias, lenWallBias);

            return new SplitAxis(best.Axis, Math.Max(1, best.Len));
        }

        private static void AddTopK(List<SweepEval> list, SweepEval e, int k)
        {
            if (k <= 0) return;

            // 常にMAE昇順で保持する（kが小さい前提でO(k)のinsertにする）
            static int CompareMae(SweepEval a, SweepEval b) => a.Mae.CompareTo(b.Mae);

            // 追加して良いかの粗判定（満杯ならworstより良い必要がある）
            if (list.Count >= k && e.Mae >= list[^1].Mae)
            {
                return;
            }

            var idx = list.BinarySearch(e, Comparer<SweepEval>.Create(CompareMae));
            if (idx < 0) idx = ~idx;
            list.Insert(idx, e);
            if (list.Count > k)
            {
                list.RemoveAt(k);
            }
        }

        private static void ExportSweepTop10Csv(List<SweepEval> overallTop10, SweepEvalContext ctx, DotReproRenderer.Options baseOpt, string? expectedPngPath)
        {
            if (overallTop10 == null || overallTop10.Count == 0)
            {
                return;
            }

            // 最終的なTop10（MAE昇順）に整形
            var top = overallTop10
                .OrderBy(v => v.Mae)
                .Take(10)
                .ToArray();

            var dir = ".";
            if (!string.IsNullOrWhiteSpace(expectedPngPath))
            {
                try
                {
                    dir = Path.GetDirectoryName(expectedPngPath) ?? ".";
                }
                catch
                {
                    dir = ".";
                }
            }

            // Expected PNGと同じフォルダにSweepResultを作り、その中へ出力する
            dir = Path.Combine(dir, "SweepResult");
            Directory.CreateDirectory(dir);

            var expectedTag = "expected";
            if (!string.IsNullOrWhiteSpace(expectedPngPath))
            {
                try
                {
                    expectedTag = Path.GetFileNameWithoutExtension(expectedPngPath);
                }
                catch
                {
                    expectedTag = "expected";
                }
            }

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var summaryPath = Path.Combine(dir, $"sweep-top10-summary-{expectedTag}-{stamp}.csv");
            var detailPath = Path.Combine(dir, $"sweep-top10-detail-{expectedTag}-{stamp}.csv");

            var inv = CultureInfo.InvariantCulture;

            var sb = new StringBuilder(capacity: 4096);
            sb.AppendLine("rank,evalMode,roiX,roiY,roiW,roiH,roiStride" +
                          ",mae,rmse,maxAbsError,meanExpectedA,meanRenderedA,meanExpectedA_bright,meanRenderedA_bright,meanExpectedA_dark,meanRenderedA_dark,brightCount,darkCount" +
                          ",offsetX,offsetY,strength,gain,falloffScale,falloffRNormScale,falloffGamma,wallK,wallBaseScale,wallThresholdBias");

            for (var i = 0; i < top.Length; i++)
            {
                var c = top[i].Candidate;
                var d = ComputeCandidateScoreDetails(ctx, baseOpt, c);
                sb.AppendLine(string.Join(",",
                    (i + 1).ToString(inv),
                    (ctx.UseRoi ? "roi" : "tile"),
                    (ctx.UseRoi ? ctx.RoiX.ToString(inv) : ""),
                    (ctx.UseRoi ? ctx.RoiY.ToString(inv) : ""),
                    (ctx.UseRoi ? ctx.RoiW.ToString(inv) : ""),
                    (ctx.UseRoi ? ctx.RoiH.ToString(inv) : ""),
                    (ctx.UseRoi ? ctx.RoiStride.ToString(inv) : ""),
                    d.Mae.ToString("0.########", inv),
                    d.Rmse.ToString("0.########", inv),
                    d.MaxAbsError.ToString(inv),
                    d.MeanExpectedA.ToString("0.########", inv),
                    d.MeanRenderedA.ToString("0.########", inv),
                    d.MeanExpectedA_Bright.ToString("0.########", inv),
                    d.MeanRenderedA_Bright.ToString("0.########", inv),
                    d.MeanExpectedA_Dark.ToString("0.########", inv),
                    d.MeanRenderedA_Dark.ToString("0.########", inv),
                    d.BrightCount.ToString(inv),
                    d.DarkCount.ToString(inv),
                    c.OffsetX.ToString(inv),
                    c.OffsetY.ToString(inv),
                    c.Strength.ToString("0.########", inv),
                    c.Gain.ToString("0.########", inv),
                    c.FalloffScale.ToString("0.########", inv),
                    c.RNormScale.ToString("0.########", inv),
                    c.Gamma.ToString("0.########", inv),
                    c.WallK.ToString("0.########", inv),
                    c.WallBaseScale.ToString("0.########", inv),
                    c.WallThresholdBias.ToString("0.########", inv)));
            }

            File.WriteAllText(summaryPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            ExportSweepTop10PointsCsv(detailPath, top, ctx, baseOpt);
        }

        private static void ExportSweepTop10PointsCsv(string pointsPath, SweepEval[] top, SweepEvalContext ctx, DotReproRenderer.Options baseOpt)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pointsPath);
            ArgumentNullException.ThrowIfNull(top);

            var inv = CultureInfo.InvariantCulture;

            var sb = new StringBuilder(capacity: 32 * 1024);
            sb.AppendLine("rank,evalMode,group,ptIndex,x,y,expectedA,renderedA,diff,absDiff,mae" +
                          ",offsetX,offsetY,strength,gain,falloffScale,falloffRNormScale,falloffGamma,wallK,wallBaseScale,wallThresholdBias");

            var maxRows = Math.Max(1, ctx.DetailMaxRows);
            var rowsWritten = 0;

            for (var i = 0; i < top.Length; i++)
            {
                var rank = i + 1;
                var cand = top[i].Candidate;

                var opt = baseOpt with
                {
                    FalloffScale = cand.FalloffScale,
                    FalloffRNormScale = cand.RNormScale,
                    FalloffGamma = cand.Gamma,
                    PaperNoiseOffsetX = cand.OffsetX,
                    PaperNoiseOffsetY = cand.OffsetY,
                    PaperNoiseStrength = cand.Strength,
                    PaperNoiseGain = cand.Gain,
                    WallK = cand.WallK,
                    WallBaseScale = cand.WallBaseScale,
                    WallThresholdBias = cand.WallThresholdBias,
                };

                var eval = DotReproRenderer.CreatePointEvaluator(opt, ctx.KMeanStride);

                if (ctx.UseRoi)
                {
                    var stride = Math.Max(1, ctx.RoiStride);
                    var ptIndex = 0;
                    for (var y = ctx.RoiY; y < ctx.RoiY + ctx.RoiH; y += stride)
                    {
                        for (var x = ctx.RoiX; x < ctx.RoiX + ctx.RoiW; x += stride)
                        {
                            var idx = (y * ctx.CanvasSizePx) + x;
                            var expA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(ctx.ExpectedPixels[idx], ctx.ExpectedMode);
                            var renA = eval.EvalAlphaByte(x, y);
                            var diff = renA - expA;
                            sb.AppendLine(string.Join(",",
                                rank.ToString(inv),
                                "roi",
                                "roi",
                                ptIndex.ToString(inv),
                                x.ToString(inv),
                                y.ToString(inv),
                                expA.ToString(inv),
                                renA.ToString(inv),
                                diff.ToString(inv),
                                Math.Abs(diff).ToString(inv),
                                top[i].Mae.ToString("0.########", inv),
                                cand.OffsetX.ToString(inv),
                                cand.OffsetY.ToString(inv),
                                cand.Strength.ToString("0.########", inv),
                                cand.Gain.ToString("0.########", inv),
                                cand.FalloffScale.ToString("0.########", inv),
                                cand.RNormScale.ToString("0.########", inv),
                                cand.Gamma.ToString("0.########", inv),
                                cand.WallK.ToString("0.########", inv),
                                cand.WallBaseScale.ToString("0.########", inv),
                                cand.WallThresholdBias.ToString("0.########", inv)));
                            ptIndex++;
                            rowsWritten++;
                            if (rowsWritten >= maxRows) goto Done;
                        }
                    }
                }
                else
                {
                    var pts = TileExtremaMatchEvaluator.EnumeratePoints(new TileExtremaMatchEvaluator.EnumerateInputs(
                        CanvasW: ctx.CanvasSizePx,
                        CanvasH: ctx.CanvasSizePx,
                        DiameterPx: ctx.DiameterPx,
                        RadiusPadPx: ctx.RadiusPadPx,
                        NoiseTileW: ctx.NoiseTile.Width,
                        NoiseTileH: ctx.NoiseTile.Height,
                        NoiseScale: ctx.NoiseScale,
                        NoiseOffsetX: cand.OffsetX,
                        NoiseOffsetY: cand.OffsetY,
                        BrightTileX: ctx.BrightTileX,
                        BrightTileY: ctx.BrightTileY,
                        DarkTileX: ctx.DarkTileX,
                        DarkTileY: ctx.DarkTileY));

                    var totalPts = pts.TotalCount;
                    if (totalPts <= 0)
                    {
                        continue;
                    }

                    var ptIndex = 0;
                    foreach (var (x, y) in pts.BrightPoints)
                    {
                        var idx = (y * ctx.CanvasSizePx) + x;
                        var expA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(ctx.ExpectedPixels[idx], ctx.ExpectedMode);
                        var renA = eval.EvalAlphaByte(x, y);
                        var diff = renA - expA;
                        sb.AppendLine(string.Join(",",
                            rank.ToString(inv),
                            "tile",
                            "bright",
                            ptIndex.ToString(inv),
                            x.ToString(inv),
                            y.ToString(inv),
                            expA.ToString(inv),
                            renA.ToString(inv),
                            diff.ToString(inv),
                            Math.Abs(diff).ToString(inv),
                            top[i].Mae.ToString("0.########", inv),
                            cand.OffsetX.ToString(inv),
                            cand.OffsetY.ToString(inv),
                            cand.Strength.ToString("0.########", inv),
                            cand.Gain.ToString("0.########", inv),
                            cand.FalloffScale.ToString("0.########", inv),
                            cand.RNormScale.ToString("0.########", inv),
                            cand.Gamma.ToString("0.########", inv),
                            cand.WallK.ToString("0.########", inv),
                            cand.WallBaseScale.ToString("0.########", inv),
                            cand.WallThresholdBias.ToString("0.########", inv)));
                        ptIndex++;
                        rowsWritten++;
                        if (rowsWritten >= maxRows) goto Done;
                    }

                    foreach (var (x, y) in pts.DarkPoints)
                    {
                        var idx = (y * ctx.CanvasSizePx) + x;
                        var expA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(ctx.ExpectedPixels[idx], ctx.ExpectedMode);
                        var renA = eval.EvalAlphaByte(x, y);
                        var diff = renA - expA;
                        sb.AppendLine(string.Join(",",
                            rank.ToString(inv),
                            "tile",
                            "dark",
                            ptIndex.ToString(inv),
                            x.ToString(inv),
                            y.ToString(inv),
                            expA.ToString(inv),
                            renA.ToString(inv),
                            diff.ToString(inv),
                            Math.Abs(diff).ToString(inv),
                            top[i].Mae.ToString("0.########", inv),
                            cand.OffsetX.ToString(inv),
                            cand.OffsetY.ToString(inv),
                            cand.Strength.ToString("0.########", inv),
                            cand.Gain.ToString("0.########", inv),
                            cand.FalloffScale.ToString("0.########", inv),
                            cand.RNormScale.ToString("0.########", inv),
                            cand.Gamma.ToString("0.########", inv),
                            cand.WallK.ToString("0.########", inv),
                            cand.WallBaseScale.ToString("0.########", inv),
                            cand.WallThresholdBias.ToString("0.########", inv)));
                        ptIndex++;
                        rowsWritten++;
                        if (rowsWritten >= maxRows) goto Done;
                    }
                }
            }

        Done:

            File.WriteAllText(pointsPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        private sealed record CandidateScoreDetails(
            double Mae,
            double Rmse,
            int MaxAbsError,
            double MeanExpectedA,
            double MeanRenderedA,
            double MeanExpectedA_Bright,
            double MeanRenderedA_Bright,
            double MeanExpectedA_Dark,
            double MeanRenderedA_Dark,
            int BrightCount,
            int DarkCount);

        private static CandidateScoreDetails ComputeCandidateScoreDetails(SweepEvalContext ctx, DotReproRenderer.Options baseOpt, SweepCandidate c)
        {
            // 候補opt
            var opt = baseOpt with
            {
                FalloffScale = c.FalloffScale,
                FalloffRNormScale = c.RNormScale,
                FalloffGamma = c.Gamma,
                PaperNoiseOffsetX = c.OffsetX,
                PaperNoiseOffsetY = c.OffsetY,
                PaperNoiseStrength = c.Strength,
                PaperNoiseGain = c.Gain,
                WallK = c.WallK,
                WallBaseScale = c.WallBaseScale,
                WallThresholdBias = c.WallThresholdBias,
            };

            if (ctx.UseRoi)
            {
                var stride = Math.Max(1, ctx.RoiStride);
                var wCount = (ctx.RoiW + stride - 1) / stride;
                var hCount = (ctx.RoiH + stride - 1) / stride;
                var roiTotalPts = (long)wCount * hCount;
                if (roiTotalPts <= 0)
                {
                    return new CandidateScoreDetails(
                        Mae: double.PositiveInfinity,
                        Rmse: double.PositiveInfinity,
                        MaxAbsError: 0,
                        MeanExpectedA: 0,
                        MeanRenderedA: 0,
                        MeanExpectedA_Bright: 0,
                        MeanRenderedA_Bright: 0,
                        MeanExpectedA_Dark: 0,
                        MeanRenderedA_Dark: 0,
                        BrightCount: 0,
                        DarkCount: 0);
                }

                var roiEval = DotReproRenderer.CreatePointEvaluator(opt, ctx.KMeanStride);

                double roiSumAbs = 0;
                double roiSumSq = 0;
                var roiMaxAbs = 0;

                double roiSumExp = 0;
                double roiSumRen = 0;

                for (var y = ctx.RoiY; y < ctx.RoiY + ctx.RoiH; y += stride)
                {
                    for (var x = ctx.RoiX; x < ctx.RoiX + ctx.RoiW; x += stride)
                    {
                        var idx = (y * ctx.CanvasSizePx) + x;
                        var expA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(ctx.ExpectedPixels[idx], ctx.ExpectedMode);
                        var renA = roiEval.EvalAlphaByte(x, y);
                        var d = renA - expA;
                        var abs = Math.Abs(d);

                        roiSumAbs += abs;
                        roiSumSq += d * d;
                        if (abs > roiMaxAbs) roiMaxAbs = abs;

                        roiSumExp += expA;
                        roiSumRen += renA;
                    }
                }

                var roiMae = roiSumAbs / roiTotalPts;
                var roiRmse = Math.Sqrt(roiSumSq / roiTotalPts);
                var roiMeanExp = roiSumExp / roiTotalPts;
                var roiMeanRen = roiSumRen / roiTotalPts;

                var roiCount = (int)Math.Min(int.MaxValue, roiTotalPts);
                return new CandidateScoreDetails(
                    Mae: roiMae,
                    Rmse: roiRmse,
                    MaxAbsError: roiMaxAbs,
                    MeanExpectedA: roiMeanExp,
                    MeanRenderedA: roiMeanRen,
                    MeanExpectedA_Bright: roiMeanExp,
                    MeanRenderedA_Bright: roiMeanRen,
                    MeanExpectedA_Dark: 0.0,
                    MeanRenderedA_Dark: 0.0,
                    BrightCount: roiCount,
                    DarkCount: 0);
            }

            var pts = TileExtremaMatchEvaluator.EnumeratePoints(new TileExtremaMatchEvaluator.EnumerateInputs(
                CanvasW: ctx.CanvasSizePx,
                CanvasH: ctx.CanvasSizePx,
                DiameterPx: ctx.DiameterPx,
                RadiusPadPx: ctx.RadiusPadPx,
                NoiseTileW: ctx.NoiseTile.Width,
                NoiseTileH: ctx.NoiseTile.Height,
                NoiseScale: ctx.NoiseScale,
                NoiseOffsetX: c.OffsetX,
                NoiseOffsetY: c.OffsetY,
                BrightTileX: ctx.BrightTileX,
                BrightTileY: ctx.BrightTileY,
                DarkTileX: ctx.DarkTileX,
                DarkTileY: ctx.DarkTileY));

            var brightCount = pts.BrightPoints.Count;
            var darkCount = pts.DarkPoints.Count;
            var totalPts = brightCount + darkCount;
            if (totalPts <= 0)
            {
                return new CandidateScoreDetails(
                    Mae: double.PositiveInfinity,
                    Rmse: double.PositiveInfinity,
                    MaxAbsError: 0,
                    MeanExpectedA: 0,
                    MeanRenderedA: 0,
                    MeanExpectedA_Bright: 0,
                    MeanRenderedA_Bright: 0,
                    MeanExpectedA_Dark: 0,
                    MeanRenderedA_Dark: 0,
                    BrightCount: 0,
                    DarkCount: 0);
            }

            var eval = DotReproRenderer.CreatePointEvaluator(opt, ctx.KMeanStride);

            double sumAbs = 0;
            double sumSq = 0;
            var maxAbs = 0;

            double sumExp = 0;
            double sumRen = 0;

            double sumExpBright = 0;
            double sumRenBright = 0;

            double sumExpDark = 0;
            double sumRenDark = 0;

            foreach (var (x, y) in pts.BrightPoints)
            {
                var idx = (y * ctx.CanvasSizePx) + x;
                var expA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(ctx.ExpectedPixels[idx], ctx.ExpectedMode);
                var renA = eval.EvalAlphaByte(x, y);
                var d = renA - expA;
                var abs = Math.Abs(d);
                sumAbs += abs;
                sumSq += d * d;
                if (abs > maxAbs) maxAbs = abs;
                sumExp += expA;
                sumRen += renA;
                sumExpBright += expA;
                sumRenBright += renA;
            }

            foreach (var (x, y) in pts.DarkPoints)
            {
                var idx = (y * ctx.CanvasSizePx) + x;
                var expA = TileExtremaMatchEvaluator.GetExpectedAlphaByte(ctx.ExpectedPixels[idx], ctx.ExpectedMode);
                var renA = eval.EvalAlphaByte(x, y);
                var d = renA - expA;
                var abs = Math.Abs(d);
                sumAbs += abs;
                sumSq += d * d;
                if (abs > maxAbs) maxAbs = abs;
                sumExp += expA;
                sumRen += renA;
                sumExpDark += expA;
                sumRenDark += renA;
            }

            var mae = sumAbs / totalPts;
            var rmse = Math.Sqrt(sumSq / totalPts);
            var meanExp = sumExp / totalPts;
            var meanRen = sumRen / totalPts;

            var meanExpBright = brightCount > 0 ? (sumExpBright / brightCount) : 0.0;
            var meanRenBright = brightCount > 0 ? (sumRenBright / brightCount) : 0.0;

            var meanExpDark = darkCount > 0 ? (sumExpDark / darkCount) : 0.0;
            var meanRenDark = darkCount > 0 ? (sumRenDark / darkCount) : 0.0;

            return new CandidateScoreDetails(
                Mae: mae,
                Rmse: rmse,
                MaxAbsError: maxAbs,
                MeanExpectedA: meanExp,
                MeanRenderedA: meanRen,
                MeanExpectedA_Bright: meanExpBright,
                MeanRenderedA_Bright: meanRenBright,
                MeanExpectedA_Dark: meanExpDark,
                MeanRenderedA_Dark: meanRenDark,
                BrightCount: brightCount,
                DarkCount: darkCount);
        }

        private static IEnumerable<double> BuildLinearCandidates(double min, double max, double step)
        {
            if (!double.IsFinite(min) || !double.IsFinite(max) || !double.IsFinite(step) || step <= 0)
            {
                yield break;
            }

            // 浮動小数の誤差を考慮して、回数上限を持つ。
            const int maxCount = 10_000;
            var v = min;
            for (var i = 0; i < maxCount && v <= max + (step * 0.5); i++)
            {
                yield return Math.Clamp(v, min, max);
                v += step;
            }
        }

        private void EvaluateTileExtremaButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_expected == null)
                {
                    throw new InvalidOperationException("Expected PNGが未ロードです。");
                }
                if (_rendered == null)
                {
                    throw new InvalidOperationException("Renderedが未生成です。先にRenderを実行してください。");
                }

                // 評価は画像の同一座標比較なので、サイズ不一致はここでは不可とする。
                if (_expected.Width != _rendered.Width || _expected.Height != _rendered.Height)
                {
                    throw new InvalidOperationException($"Expected/Renderedのサイズが一致しません。 expected={_expected.Width}x{_expected.Height} rendered={_rendered.Width}x{_rendered.Height}");
                }

                if (_paperNoise == null)
                {
                    _paperNoise = GetOrLoadPaperNoise();
                }

                var sPx = (int)Math.Round(DiameterNumberBox.Value, MidpointRounding.AwayFromZero);
                if (sPx <= 0)
                {
                    throw new ArgumentException("S(直径px) の入力が不正です。", nameof(sPx));
                }
                var renderScale = (int)Math.Round(RenderScaleNumberBox.Value, MidpointRounding.AwayFromZero);
                if (renderScale <= 0)
                {
                    throw new ArgumentException("RenderScale の入力が不正です。", nameof(renderScale));
                }
                var diameterPx = checked(sPx * renderScale);

                var radiusPadPx = RadiusPadNumberBox.Value;
                if (radiusPadPx < 0 || !double.IsFinite(radiusPadPx))
                {
                    throw new ArgumentException("EdgePad(px) の入力が不正です。", nameof(radiusPadPx));
                }

                // RenderN1()と同じロジックでnoiseScaleを確定する
                var paperNoiseScale = PaperNoiseScaleNumberBox.Value;
                if (AutoPaperNoiseScaleCheckBox.IsChecked == true)
                {
                    var tileScale = (int)Math.Round(PaperNoiseTileScaleNumberBox.Value, MidpointRounding.AwayFromZero);
                    if (tileScale <= 0)
                    {
                        throw new ArgumentException("TileScale の入力が不正です。", nameof(tileScale));
                    }
                    paperNoiseScale = (double)renderScale / tileScale;
                }
                if (paperNoiseScale <= 0 || !double.IsFinite(paperNoiseScale))
                {
                    throw new ArgumentException("noiseScale の入力が不正です。", nameof(paperNoiseScale));
                }

                var offsetX = PaperNoiseOffsetXNumberBox.Value;
                var offsetY = PaperNoiseOffsetYNumberBox.Value;

                var brightX = (int)Math.Round(TileBrightXNumberBox.Value, MidpointRounding.AwayFromZero);
                var brightY = (int)Math.Round(TileBrightYNumberBox.Value, MidpointRounding.AwayFromZero);
                var darkX = (int)Math.Round(TileDarkXNumberBox.Value, MidpointRounding.AwayFromZero);
                var darkY = (int)Math.Round(TileDarkYNumberBox.Value, MidpointRounding.AwayFromZero);

                var expectedModeTag = (ExpectedAlphaModeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
                var expectedMode = expectedModeTag switch
                {
                    "Alpha" => TileExtremaMatchEvaluator.ExpectedAlphaMode.UseAlpha,
                    "White" => TileExtremaMatchEvaluator.ExpectedAlphaMode.WhiteBackground255MinusLuma,
                    _ => TileExtremaMatchEvaluator.ExpectedAlphaMode.Auto,
                };

                var result = TileExtremaMatchEvaluator.Evaluate(new TileExtremaMatchEvaluator.Inputs(
                    Expected: _expected,
                    Rendered: _rendered,
                    NoiseTile: _paperNoise,
                    NoiseScale: paperNoiseScale,
                    NoiseOffsetX: offsetX,
                    NoiseOffsetY: offsetY,
                    DiameterPx: diameterPx,
                    RadiusPadPx: radiusPadPx,
                    BrightTileX: brightX,
                    BrightTileY: brightY,
                    DarkTileX: darkX,
                    DarkTileY: darkY,
                    ExpectedMode: expectedMode));

                MessageBox.Show(this, result.ToReportText(), "DotTester - Tile extrema", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadExpected(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            _expected?.Dispose();
            _expected = SKBitmap.Decode(path);
            if (_expected == null)
            {
                throw new InvalidOperationException("Expected PNGの読み込みに失敗しました。");
            }

            ExpectedPreview.InvalidateVisual();

            try
            {
                UpdateRoiEvalText(showErrors: false);
            }
            catch (ArgumentException ex)
            {
                RoiEvalTextBlock.Text = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                RoiEvalTextBlock.Text = ex.Message;
            }
        }

        private void RenderN1()
        {
            var sPx = (int)Math.Round(DiameterNumberBox.Value, MidpointRounding.AwayFromZero);
            if (sPx <= 0)
            {
                throw new ArgumentException("S(直径px) の入力が不正です。", nameof(sPx));
            }

            var renderScale = (int)Math.Round(RenderScaleNumberBox.Value, MidpointRounding.AwayFromZero);
            if (renderScale <= 0)
            {
                throw new ArgumentException("RenderScale の入力が不正です。", nameof(renderScale));
            }

            var diameterPx = checked(sPx * renderScale);

            var padPx = (int)Math.Round(CanvasPadNumberBox.Value, MidpointRounding.AwayFromZero);
            if (padPx < 0)
            {
                throw new ArgumentException("Pad(px) の入力が不正です。", nameof(padPx));
            }

            var radiusPadPx = RadiusPadNumberBox.Value;
            if (radiusPadPx < 0)
            {
                throw new ArgumentException("EdgePad(px) の入力が不正です。", nameof(radiusPadPx));
            }

            int canvasSizePx;
            if (AutoCanvasSizeCheckBox.IsChecked == true)
            {
                canvasSizePx = checked(diameterPx + padPx);
                CanvasSizeNumberBox.Value = canvasSizePx;
            }
            else
            {
                canvasSizePx = (int)Math.Round(CanvasSizeNumberBox.Value, MidpointRounding.AwayFromZero);
            }
            if (canvasSizePx <= 0)
            {
                throw new ArgumentException("Canvas(px) の入力が不正です。", nameof(canvasSizePx));
            }

            var pressure = PressureNumberBox.Value;
            if (pressure <= 0 || pressure > 1.0)
            {
                throw new ArgumentException("P の入力が不正です。", nameof(pressure));
            }

            var usePaper = UsePaperNoiseCheckBox.IsChecked == true;
            PaperNoiseTile? noise = null;
            if (usePaper)
            {
                noise = GetOrLoadPaperNoise();
            }

            var noiseText = string.Empty;
            if (usePaper && noise != null)
            {
                noiseText = $"  noise(mean={noise.Mean01:0.######} std={noise.Stddev01:0.######} min={noise.Min01:0.######} max={noise.Max01:0.######})";
            }

            var falloffLut = GetOrLoadFalloffLut(renderScale);

            var paperNoiseStrength = PaperNoiseStrengthNumberBox.Value;
            if (paperNoiseStrength < 0 || paperNoiseStrength > 1.0)
            {
                throw new ArgumentException("Strength の入力が不正です。", nameof(paperNoiseStrength));
            }

            var enableEdgeBoost = EnablePaperNoiseEdgeBoostCheckBox.IsChecked == true;
            var edgeBoost = 0.0;
            var edgeBoostGamma = 1.0;
            if (enableEdgeBoost)
            {
                edgeBoost = PaperNoiseEdgeBoostNumberBox.Value;
                if (!double.IsFinite(edgeBoost) || edgeBoost < 0)
                {
                    throw new ArgumentException("EdgeBoost boost の入力が不正です。", nameof(edgeBoost));
                }

                edgeBoostGamma = PaperNoiseEdgeBoostGammaNumberBox.Value;
                if (!double.IsFinite(edgeBoostGamma) || edgeBoostGamma <= 0)
                {
                    throw new ArgumentException("EdgeBoost gamma の入力が不正です。", nameof(edgeBoostGamma));
                }
            }

            var alphaCutoffByte = (int)Math.Round(AlphaCutoffByteNumberBox.Value, MidpointRounding.AwayFromZero);
            if (alphaCutoffByte < 0 || alphaCutoffByte > 255)
            {
                throw new ArgumentException("Cutoff(alpha) の入力が不正です。", nameof(alphaCutoffByte));
            }
            var alphaCutoff01 = alphaCutoffByte / 255.0;
            var noiseDependentCutoff = NoiseDependentCutoffCheckBox.IsChecked == true;

            var falloffScale = FalloffScaleNumberBox.Value;
            if (falloffScale < 0 || !double.IsFinite(falloffScale))
            {
                throw new ArgumentException("FalloffScale の入力が不正です。", nameof(falloffScale));
            }

            var falloffRNormScale = FalloffRNormScaleNumberBox.Value;
            if (falloffRNormScale <= 0 || !double.IsFinite(falloffRNormScale))
            {
                throw new ArgumentException("FalloffRNormScale の入力が不正です。", nameof(falloffRNormScale));
            }

            var falloffGamma = FalloffGammaNumberBox.Value;
            if (falloffGamma <= 0 || !double.IsFinite(falloffGamma))
            {
                throw new ArgumentException("FalloffGamma の入力が不正です。", nameof(falloffGamma));
            }

            var paperNoiseGain = PaperNoiseGainNumberBox.Value;
            if (paperNoiseGain < 0 || !double.IsFinite(paperNoiseGain))
            {
                throw new ArgumentException("Gain の入力が不正です。", nameof(paperNoiseGain));
            }

            var enableZClamp = EnablePaperNoiseZClampCheckBox.IsChecked == true;
            var zClampNegAbs = 0.0;
            var zClampPosAbs = 0.0;
            if (enableZClamp)
            {
                zClampNegAbs = PaperNoiseZClampNegAbsNumberBox.Value;
                zClampPosAbs = PaperNoiseZClampPosAbsNumberBox.Value;
                if (zClampNegAbs <= 0 || !double.IsFinite(zClampNegAbs))
                {
                    throw new ArgumentException("zClamp- の入力が不正です。", nameof(zClampNegAbs));
                }
                if (zClampPosAbs <= 0 || !double.IsFinite(zClampPosAbs))
                {
                    throw new ArgumentException("zClamp+ の入力が不正です。", nameof(zClampPosAbs));
                }
            }

            var kClampMin = PaperNoiseKClampMinNumberBox.Value;
            var kClampMax = PaperNoiseKClampMaxNumberBox.Value;
            if (!double.IsFinite(kClampMin) || !double.IsFinite(kClampMax) || kClampMin <= 0 || kClampMax <= 0 || kClampMax < kClampMin)
            {
                throw new ArgumentException("kClamp の入力が不正です。", nameof(kClampMin));
            }

            var paperNoiseScale = PaperNoiseScaleNumberBox.Value;
            if (AutoPaperNoiseScaleCheckBox.IsChecked == true)
            {
                var tileScale = (int)Math.Round(PaperNoiseTileScaleNumberBox.Value, MidpointRounding.AwayFromZero);
                if (tileScale <= 0)
                {
                    throw new ArgumentException("TileScale の入力が不正です。", nameof(tileScale));
                }
                paperNoiseScale = (double)renderScale / tileScale;
                PaperNoiseScaleNumberBox.Value = paperNoiseScale;
            }

            if (paperNoiseScale <= 0)
            {
                throw new ArgumentException("noiseScale の入力が不正です。", nameof(paperNoiseScale));
            }

            var offsetX = PaperNoiseOffsetXNumberBox.Value;
            var offsetY = PaperNoiseOffsetYNumberBox.Value;

            var kModeTag = (PaperNoiseKModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
            var kMode = kModeTag switch
            {
                "Direct01" => DotReproRenderer.KDefinition.Direct01,
                "BlendToMean1" => DotReproRenderer.KDefinition.BlendToMean1,
                "ZNormalized" => DotReproRenderer.KDefinition.ZNormalized,
                _ => DotReproRenderer.KDefinition.RatioToMean,
            };

            var samplingTag = (PaperNoiseSamplingComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
            var samplingMode = samplingTag switch
            {
                "Nearest" => PaperNoiseTile.SamplingMode.Nearest,
                "Bicubic" => PaperNoiseTile.SamplingMode.Bicubic,
                _ => PaperNoiseTile.SamplingMode.Bilinear,
            };

            var applyTag = (PaperNoiseApplyModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
            var applyMode = applyTag switch
            {
                "Add" => DotReproRenderer.PaperNoiseApplyMode.AddAlpha,
                _ => DotReproRenderer.PaperNoiseApplyMode.MultiplyAlpha,
            };

            var outAlphaModelTag = (OutAlphaModelComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
            var outAlphaModel = outAlphaModelTag switch
            {
                "WallThrough" => DotReproRenderer.OutAlphaModel.WallThrough,
                _ => DotReproRenderer.OutAlphaModel.MultiplyK,
            };

            var wallK = WallKNumberBox.Value;
            if (!double.IsFinite(wallK) || wallK <= 0)
            {
                throw new ArgumentException("WallK の入力が不正です。", nameof(wallK));
            }

                var wallBaseScale = WallBaseScaleNumberBox.Value;
                if (!double.IsFinite(wallBaseScale) || wallBaseScale <= 0)
                {
                    throw new ArgumentException("Wall base× の入力が不正です。", nameof(wallBaseScale));
                }

                var wallThresholdBias = WallThresholdBiasNumberBox.Value;
                if (!double.IsFinite(wallThresholdBias))
                {
                    throw new ArgumentException("Wall bias の入力が不正です。", nameof(wallThresholdBias));
                }

            var paperMaskModeTag = (PaperMaskModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
            var paperMaskMode = paperMaskModeTag switch
            {
                "MultiplyOutAlpha" => DotReproRenderer.PaperMaskMode.MultiplyOutAlpha,
                "SoftOutAlpha" => DotReproRenderer.PaperMaskMode.SoftOutAlpha,
                "ThresholdOutAlpha" => DotReproRenderer.PaperMaskMode.ThresholdOutAlpha,
                _ => DotReproRenderer.PaperMaskMode.None,
            };

            var paperMaskThreshold01 = PaperMaskThresholdNumberBox.Value;
            if (!double.IsFinite(paperMaskThreshold01) || paperMaskThreshold01 < 0 || paperMaskThreshold01 > 1.0)
            {
                throw new ArgumentException("PaperMask th の入力が不正です。", nameof(paperMaskThreshold01));
            }

            var paperMaskGain = PaperMaskGainNumberBox.Value;
            if (!double.IsFinite(paperMaskGain) || paperMaskGain < 0)
            {
                throw new ArgumentException("PaperMask gain の入力が不正です。", nameof(paperMaskGain));
            }

            var paperMaskFalloffModeTag = (PaperMaskFalloffModeComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
            var paperMaskFalloffMode = paperMaskFalloffModeTag switch
            {
                "StrongerAtEdge" => DotReproRenderer.PaperMaskFalloffMode.StrongerAtEdge,
                "ThresholdAtEdge" => DotReproRenderer.PaperMaskFalloffMode.ThresholdAtEdge,
                _ => DotReproRenderer.PaperMaskFalloffMode.None,
            };

            // 今はDot単発（N=1）に固定
            var opt = new DotReproRenderer.Options(
                CanvasSizePx: canvasSizePx,
                DiameterPx: diameterPx,
                Pressure: pressure,
                FalloffLut: falloffLut,
                FalloffScale: falloffScale,
                FalloffRNormScale: falloffRNormScale,
                FalloffGamma: falloffGamma,
                RadiusPadPx: radiusPadPx,
                NoiseTile: noise,
                UsePaperNoise: usePaper,
                NoiseSamplingMode: samplingMode,
                PaperNoiseScale: paperNoiseScale,
                PaperNoiseOffsetX: offsetX,
                PaperNoiseOffsetY: offsetY,
                PaperNoiseStrength: paperNoiseStrength,
                PaperNoiseGain: paperNoiseGain,
                KMode: kMode,
                PaperNoiseApplyMode: applyMode,
                OutAlphaModel: outAlphaModel,
                WallK: wallK,
                WallBaseScale: wallBaseScale,
                WallThresholdBias: wallThresholdBias,
                KClampMin: kClampMin,
                KClampMax: kClampMax,
                AlphaCutoff01: alphaCutoff01,
                NoiseDependentCutoff: noiseDependentCutoff,
                DisableKMeanNormalization: DisableKMeanNormalizationCheckBox.IsChecked == true,
                EnablePaperNoiseZClamp: enableZClamp,
                PaperNoiseZClampNegAbs: zClampNegAbs,
                PaperNoiseZClampPosAbs: zClampPosAbs,
                EnablePaperNoiseEdgeBoost: enableEdgeBoost,
                PaperNoiseEdgeBoost: edgeBoost,
                PaperNoiseEdgeBoostGamma: edgeBoostGamma,
                PaperMaskMode: paperMaskMode,
                PaperMaskThreshold01: paperMaskThreshold01,
                PaperMaskGain: paperMaskGain,
                PaperMaskFalloffMode: paperMaskFalloffMode);

            _rendered?.Dispose();
            _lastRenderedOptions = opt;
            _rendered = DotReproRenderer.Render(opt);

            RenderedPreview.InvalidateVisual();

            UpdateStatus($"Rendered. S={sPx} scale={renderScale} diameterPx={diameterPx} canvas={canvasSizePx} P={pressure.ToString("0.####", CultureInfo.InvariantCulture)} noiseScale={paperNoiseScale.ToString("0.####", CultureInfo.InvariantCulture)} off=({offsetX.ToString("0.####", CultureInfo.InvariantCulture)},{offsetY.ToString("0.####", CultureInfo.InvariantCulture)}){noiseText}");

            try
            {
                UpdateRoiEvalText(showErrors: false);
            }
            catch (ArgumentException ex)
            {
                RoiEvalTextBlock.Text = ex.Message;
            }
            catch (InvalidOperationException ex)
            {
                RoiEvalTextBlock.Text = ex.Message;
            }
        }

        private void ExportEdgeScatterCsvButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_expected == null)
                {
                    throw new InvalidOperationException("Expected PNGが未ロードです。");
                }

                // 最新のUI値でRenderしてからOptionsを確定させる（kMean等の前提を揃える）
                RenderN1();
                if (_lastRenderedOptions == null)
                {
                    throw new InvalidOperationException("描画設定の取得に失敗しました。");
                }

                var expectedPath = ExpectedPathTextBox.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(expectedPath))
                {
                    throw new InvalidOperationException("Expected PNGのパスが空です。");
                }

                var expectedDir = Path.GetDirectoryName(expectedPath);
                if (string.IsNullOrWhiteSpace(expectedDir))
                {
                    throw new InvalidOperationException("Expected PNGのフォルダが特定できません。");
                }

                var aMin = (int)Math.Round(EdgeScatterAlphaMinByteNumberBox.Value, MidpointRounding.AwayFromZero);
                var aMax = (int)Math.Round(EdgeScatterAlphaMaxByteNumberBox.Value, MidpointRounding.AwayFromZero);
                var stride = (int)Math.Round(EdgeScatterStrideNumberBox.Value, MidpointRounding.AwayFromZero);
                if (stride <= 0) stride = 1;

                var kMeanStride = (int)Math.Round(SweepKMeanStrideNumberBox.Value, MidpointRounding.AwayFromZero);
                if (kMeanStride <= 0) kMeanStride = 1;

                var outDir = Path.Combine(expectedDir, "SweepResult");
                Directory.CreateDirectory(outDir);

                var expectedTag = Path.GetFileNameWithoutExtension(expectedPath);
                var csvPath = Path.Combine(outDir, $"edge-scatter-{expectedTag}-a{Math.Min(aMin, aMax):D3}-{Math.Max(aMin, aMax):D3}-stride{stride}.csv");

                var msg = EdgeScatterExportService.ExportCsv(
                    expected: _expected,
                    opt: _lastRenderedOptions,
                    kMeanStridePx: kMeanStride,
                    settings: new EdgeScatterExportService.Settings
                    {
                        AlphaMinByte = aMin,
                        AlphaMaxByte = aMax,
                        StridePx = stride,
                        MaxRows = 200_000,
                    },
                    outCsvPath: csvPath);

                UpdateStatus(msg);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, ex.Message, "DotTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // PaperMask は旧実装互換のためUIは残すが、再構築レンダラでは現時点で未適用。

        private void InvalidatePaperNoiseCache()
        {
            _paperNoise?.Dispose();
            _paperNoise = null;
            _paperNoisePath = null;
        }

        private void InvalidateFalloffCache()
        {
            _falloffLut = null;
            _falloffCsvPath = null;
            _falloffLoadedScale = null;
        }

        private PaperNoiseTile GetOrLoadPaperNoise()
        {
            var path = PaperNoisePathTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("PaperNoiseのパスが空です。");
            }
            if (_paperNoise != null && string.Equals(_paperNoisePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return _paperNoise;
            }

            InvalidatePaperNoiseCache();
            _paperNoise = PaperNoiseTile.LoadFromFile(path);
            _paperNoisePath = path;
            return _paperNoise;
        }

        private NormalizedFalloffLut? GetOrLoadFalloffLut(int renderScale)
        {
            var path = FalloffCsvPathTextBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (_falloffLut != null
                && string.Equals(_falloffCsvPath, path, StringComparison.OrdinalIgnoreCase)
                && _falloffLoadedScale == renderScale)
            {
                return _falloffLut;
            }

            _falloffLut = LoadFalloffLut(path, renderScale);
            _falloffCsvPath = path;
            _falloffLoadedScale = renderScale;
            return _falloffLut;
        }

        private static NormalizedFalloffLut LoadFalloffLut(string path, int renderScale)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Falloff CSV が見つかりません。", path);
            }

            // 1) normalized-falloff: r_norm,mean_alpha,...
            // 2) kernel-profile: r_bin,px_from,px_to,total,mean_alpha,mean_alpha_byte
            using var sr = new StreamReader(path);
            string? header = null;
            while (!sr.EndOfStream)
            {
                var line = (sr.ReadLine() ?? string.Empty).Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith('#')) continue;
                header = line;
                break;
            }

            if (header == null)
            {
                throw new InvalidOperationException("Falloff CSV のヘッダが読み取れません。");
            }

            if (header.StartsWith("r_norm", StringComparison.OrdinalIgnoreCase))
            {
                return NormalizedFalloffLut.LoadFromCsv(path);
            }

            if (header.StartsWith("r_bin", StringComparison.OrdinalIgnoreCase))
            {
                return LoadKernelProfileAsFalloffLut(path);
            }

            if (header.StartsWith("dx_px", StringComparison.OrdinalIgnoreCase))
            {
                return LoadKernelSweepAsFalloffLut(path, renderScale);
            }

            // フォーマット不一致でも、既存のパーサで救える可能性があるので最後に試す
            return NormalizedFalloffLut.LoadFromCsv(path);
        }

        private static NormalizedFalloffLut LoadKernelSweepAsFalloffLut(string path, int renderScale)
        {
            // kernel-sweep.csv は dx_px（出力px）ごとの観測点1ピクセルα(0..1)を持つ。
            // DotTester側ではr_norm=0..100を想定しているので、dxTarget=r_norm*scale を基本にして抽出する。
            // NOTE: kernel-sweep生成時のscaleとDotTesterのScaleが同じ前提（違う場合は一致しない）。
            if (renderScale <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(renderScale));
            }

            var lines = File.ReadAllLines(path);
            var maxDx = 0;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith('#')) continue;
                if (line.StartsWith("dx_px", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(',');
                if (parts.Length < 6) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dxPx)) continue;
                if (dxPx < 0) dxPx = -dxPx;
                if (dxPx > maxDx) maxDx = dxPx;
            }

            if (maxDx <= 0)
            {
                throw new InvalidOperationException("kernel-sweep.csv のサイズ推定に失敗しました。");
            }

            var byDx = new double[maxDx + 1];
            Array.Fill(byDx, double.NaN);

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith('#')) continue;
                if (line.StartsWith("dx_px", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(',');
                if (parts.Length < 6) continue;

                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dxPx)) continue;
                if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var a01)) continue;

                dxPx = Math.Abs(dxPx);
                if (dxPx > maxDx) continue;

                byDx[dxPx] = Math.Clamp(a01, 0.0, 1.0);
            }

            // r_norm=0..100 の配列へ抽出（dxTargetが欠損している場合は近傍を拾う）
            var mean = new double[101];
            for (var rNorm = 0; rNorm <= 100; rNorm++)
            {
                var dxTarget = rNorm * renderScale;
                if (dxTarget < 0) dxTarget = 0;
                if (dxTarget > maxDx) dxTarget = maxDx;

                var v = byDx[dxTarget];
                if (!double.IsFinite(v))
                {
                    // 近傍探索（通常はdxStep=1で存在するはずなので、実質ほぼ通らない）
                    var found = false;
                    for (var d = 1; d <= maxDx; d++)
                    {
                        var lo = dxTarget - d;
                        if (lo >= 0)
                        {
                            var vLo = byDx[lo];
                            if (double.IsFinite(vLo))
                            {
                                v = vLo;
                                found = true;
                                break;
                            }
                        }

                        var hi = dxTarget + d;
                        if (hi <= maxDx)
                        {
                            var vHi = byDx[hi];
                            if (double.IsFinite(vHi))
                            {
                                v = vHi;
                                found = true;
                                break;
                            }
                        }
                    }

                    if (!found)
                    {
                        v = 0.0;
                    }
                }

                mean[rNorm] = Math.Clamp(v, 0.0, 1.0);
            }

            return NormalizedFalloffLut.CreateFromMeanArray(mean);
        }

        private static NormalizedFalloffLut LoadKernelProfileAsFalloffLut(string path)
        {
            // kernel-profile.csv は半径ビン（px_from..px_to）ごとの mean_alpha(0..1) を持つ。
            // `PencilDotRenderer` 側は r_norm=0..100 を想定しているため、ここで r_norm スケールへ正規化する。
            var lines = File.ReadAllLines(path);
            var maxTo = 0;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith('#')) continue;
                if (line.StartsWith("r_bin", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(',');
                if (parts.Length < 6) continue;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pxTo)) continue;
                if (pxTo > maxTo) maxTo = pxTo;
            }

            if (maxTo <= 0)
            {
                throw new InvalidOperationException("kernel-profile.csv のサイズ推定に失敗しました。");
            }

            var meanByPx = new double[maxTo];

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith('#')) continue;
                if (line.StartsWith("r_bin", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(',');
                if (parts.Length < 6) continue;

                // r_bin,px_from,px_to,total,mean_alpha,mean_alpha_byte
                if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pxFrom)) continue;
                if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pxTo)) continue;
                if (!double.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out var meanAlpha01)) continue;

                if (pxFrom < 0) pxFrom = 0;
                if (pxTo > meanByPx.Length) pxTo = meanByPx.Length;
                if (pxTo <= pxFrom) continue;

                meanAlpha01 = Math.Clamp(meanAlpha01, 0.0, 1.0);
                for (var r = pxFrom; r < pxTo; r++)
                {
                    meanByPx[r] = meanAlpha01;
                }
            }

            // 有効半径（px）を推定: 最後に非ゼロのインデックス
            var rLast = meanByPx.Length - 1;
            while (rLast > 0 && meanByPx[rLast] <= 0)
            {
                rLast--;
            }

            if (rLast <= 0)
            {
                throw new InvalidOperationException("kernel-profile.csv の有効半径推定に失敗しました（非ゼロが見つかりません）。");
            }

            // r_norm=0..100 の配列へリサンプル（線形補間）
            var meanNorm = new double[101];
            for (var rNorm = 0; rNorm <= 100; rNorm++)
            {
                var rPx = (rNorm / 100.0) * rLast;

                var i0 = (int)Math.Floor(rPx);
                var i1 = i0 + 1;
                if (i0 < 0) i0 = 0;
                if (i1 >= meanByPx.Length) i1 = meanByPx.Length - 1;

                var t = rPx - i0;
                var v = (1.0 - t) * meanByPx[i0] + t * meanByPx[i1];
                meanNorm[rNorm] = Math.Clamp(v, 0.0, 1.0);
            }

            return NormalizedFalloffLut.CreateFromMeanArray(meanNorm);
        }

        private void ExpectedPreview_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            var bmp = _expected;
            if (bmp == null)
            {
                DrawPlaceholder(canvas, e.Info.Width, e.Info.Height, "No expected");
                return;
            }

            DrawBitmapFit(canvas, bmp, e.Info.Width, e.Info.Height);
        }

        private void RenderedPreview_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            var bmp = _rendered;
            if (bmp == null)
            {
                DrawPlaceholder(canvas, e.Info.Width, e.Info.Height, "No rendered");
                return;
            }

            DrawBitmapFit(canvas, bmp, e.Info.Width, e.Info.Height);
        }

        private void DrawBitmapFit(SKCanvas canvas, SKBitmap bmp, int viewW, int viewH)
        {
            var dst = SKRect.Create(0, 0, viewW, viewH);

            using var paint = new SKPaint
            {
                FilterQuality = SKFilterQuality.Medium,
                IsAntialias = false,
            };

            if (NearestPreviewCheckBox?.IsChecked == true)
            {
                paint.FilterQuality = SKFilterQuality.None;
            }

            if (AlphaViewCheckBox?.IsChecked == true)
            {
                // 入力αをそのまま明度にして、常に不透明で表示する（微小αを目視しやすくする）。
                paint.ColorFilter = SKColorFilter.CreateColorMatrix(new float[]
                {
                    0, 0, 0, 1, 0,
                    0, 0, 0, 1, 0,
                    0, 0, 0, 1, 0,
                    0, 0, 0, 0, 255,
                });
            }

            canvas.DrawBitmap(bmp, dst, paint);
        }

        private static void DrawPlaceholder(SKCanvas canvas, int w, int h, string text)
        {
            using var p = new SKPaint { Color = new SKColor(240, 240, 240), IsAntialias = true };
            canvas.DrawRect(0, 0, w, h, p);

            using var tp = new SKPaint { Color = SKColors.Gray, IsAntialias = true };
            using var font = new SKFont { Size = 16 };
            canvas.DrawText(text, 12, 24, SKTextAlign.Left, font, tp);
        }

        private void UpdateStatus(string text)
        {
            StatusTextBlock.Text = text;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _expected?.Dispose();
            _rendered?.Dispose();
            _paperNoise?.Dispose();
        }

        private static string? TryFindRepositoryRoot(string startDirectory)
        {
            if (string.IsNullOrWhiteSpace(startDirectory))
            {
                return null;
            }

            var dir = new DirectoryInfo(startDirectory);
            while (dir != null)
            {
                var gitDirPath = Path.Combine(dir.FullName, ".git");
                if (Directory.Exists(gitDirPath) || File.Exists(gitDirPath))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            return null;
        }
    }
}