using SkiaTester.Helpers;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using System;
using System.Globalization;
using System.IO;
using System.Windows;
using static SkiaTester.SkiaHelpers;
using static SkiaTester.Constants;


namespace SkiaTester
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int CanvasSizePx = 512;
        private const int DefaultDiameter = 200;
        private const float DefaultPressure = 1f;
        private SKBitmap? _lastBitmap;
        private double[]? _lastAlpha01;
        private PaperNoise? _paperNoise;
        private string? _paperNoisePath;
        private NormalizedFalloffLut? _normalizedFalloff;
        private string? _normalizedFalloffPath;
        private bool isLoaded = false;

        private bool IsCheckedByName(string name)
        {
            if (FindName(name) is System.Windows.Controls.Primitives.ToggleButton t)
            {
                return t.IsChecked == true;
            }

            return false;
        }

        private static SKBitmap BuildGrayscaleFromAlpha01(double[] alpha01, int width, int height)
        {
            var dst = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var n = width * height;
            if (alpha01.Length < n) return dst;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var a = alpha01[y * width + x];
                    if (double.IsNaN(a) || double.IsInfinity(a)) a = 0;
                    a = Math.Clamp(a, 0.0, 1.0);

                    // 低アルファ域の差が見えるようにガンマ補正（sqrt）を掛ける
                    var vis = Math.Sqrt(a);
                    var v8 = (byte)Math.Clamp((int)Math.Round(vis * 255.0), 0, 255);
                    dst.SetPixel(x, y, new SKColor(v8, v8, v8, 255));
                }
            }

            return dst;
        }

        private static void ApplyInvert01IfNeeded(double[] v01)
        {
            for (var i = 0; i < v01.Length; i++)
            {
                v01[i] = 1.0 - Math.Clamp(v01[i], 0.0, 1.0);
            }
        }

        private static double[] BuildFalloffWeight01ForPreview(int canvasSizePx, int diameterPx, NormalizedFalloffLut? falloffLut)
        {
            var radiusPx = diameterPx * 0.5;
            var cx = (canvasSizePx - 1) * 0.5;
            var cy = (canvasSizePx - 1) * 0.5;
            var out01 = new double[canvasSizePx * canvasSizePx];

            // weightは 1..6 想定なので、0..1へ正規化して可視化する
            const double wMin = 1.0;
            const double wMax = 6.0;
            for (var y = 0; y < canvasSizePx; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < canvasSizePx; x++)
                {
                    var dx = x - cx;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > radiusPx) continue;

                    var f = 1.0;
                    var rNorm = dist * (200.0 / diameterPx);
                    if (falloffLut != null)
                    {
                        f = falloffLut.Eval(rNorm);
                    }

                    var denom = Math.Max(0.15, Math.Clamp(f, 0.0, 1.0));
                    var w = Math.Clamp(1.0 / denom, wMin, wMax);
                    var v01 = (w - wMin) / (wMax - wMin);
                    out01[y * canvasSizePx + x] = Math.Clamp(v01, 0.0, 1.0);
                }
            }

            return out01;
        }

        // outA_base/outA_masked はPencilDotRenderer側で計算して差分を切り分ける（UI側の再実装はズレやすいため）

        private static SKBitmap BuildGrayscaleFromValue01(double[] value01, int width, int height)
        {
            var dst = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var n = width * height;
            if (value01.Length < n) return dst;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var v = value01[y * width + x];
                    if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
                    v = Math.Clamp(v, 0.0, 1.0);
                    var v8 = (byte)Math.Clamp((int)Math.Round(v * 255.0), 0, 255);
                    dst.SetPixel(x, y, new SKColor(v8, v8, v8, 255));
                }
            }

            return dst;
        }

        private static double[] BuildPaperMask01ForPreview(
            int canvasSizePx,
            int diameterPx,
            PaperNoise noise,
            double paperNoiseScale,
            double paperNoiseOffsetX,
            double paperNoiseOffsetY,
            double paperNoiseLowFreqScale,
            double paperNoiseLowFreqMix,
            PencilDotRenderer.PaperMaskMode paperMaskMode,
            double paperMaskThreshold01,
            double paperMaskGain,
            PencilDotRenderer.PaperMaskFalloffMode paperMaskFalloffMode,
            NormalizedFalloffLut? falloffLut)
        {
            var radiusPx = diameterPx * 0.5;
            var cx = (canvasSizePx - 1) * 0.5;
            var cy = (canvasSizePx - 1) * 0.5;
            var out01 = new double[canvasSizePx * canvasSizePx];

            var mean = noise.Mean01;
            var std = noise.Stddev01;
            if (std <= 0) return out01;

            for (var y = 0; y < canvasSizePx; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < canvasSizePx; x++)
                {
                    var dx = x - cx;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > radiusPx) continue;

                    var nx = ((x + 0.5) + paperNoiseOffsetX) / paperNoiseScale;
                    var ny = ((y + 0.5) + paperNoiseOffsetY) / paperNoiseScale;
                    var n01 = noise.Sample01Mixed(nx, ny, paperNoiseLowFreqScale, paperNoiseLowFreqMix);

                    var f = 1.0;
                    if (paperMaskFalloffMode != PencilDotRenderer.PaperMaskFalloffMode.None)
                    {
                        var rNorm = dist * (200.0 / diameterPx);
                        if (falloffLut != null)
                        {
                            f = falloffLut.Eval(rNorm);
                        }
                    }

                    var falloffWeight = 1.0;
                    if (paperMaskFalloffMode == PencilDotRenderer.PaperMaskFalloffMode.StrongerAtEdge)
                    {
                        var denom = Math.Max(0.15, Math.Clamp(f, 0.0, 1.0));
                        falloffWeight = Math.Clamp(1.0 / denom, 1.0, 6.0);
                    }
                    var thresholdAdj = paperMaskThreshold01;
                    if (paperMaskFalloffMode == PencilDotRenderer.PaperMaskFalloffMode.ThresholdAtEdge)
                    {
                        var edge = 1.0 - Math.Clamp(f, 0.0, 1.0);
                        thresholdAdj = Math.Clamp(paperMaskThreshold01 + 0.35 * edge, 0.0, 1.0);
                    }

                    var z = (n01 - mean) / std;
                    z = Math.Clamp(z, -3.0, 3.0);
                    var t = (z + 3.0) / 6.0;
                    if (paperMaskGain > 0)
                    {
                        t = 0.5 + (t - 0.5) * paperMaskGain;
                    }
                    t = Math.Clamp(t, 0.0, 1.0);

                    var m = paperMaskMode switch
                    {
                        PencilDotRenderer.PaperMaskMode.MultiplyOutAlpha => t,
                        PencilDotRenderer.PaperMaskMode.SoftOutAlpha => Math.Clamp((t - thresholdAdj) * (paperMaskGain <= 0 ? 1.0 : paperMaskGain) * falloffWeight, 0.0, 1.0),
                        PencilDotRenderer.PaperMaskMode.ThresholdOutAlpha => t >= thresholdAdj ? 1.0 : 0.0,
                        _ => 1.0,
                    };

                    out01[y * canvasSizePx + x] = m;
                }
            }

            return out01;
        }

        private NormalizedFalloffLut GetNormalizedFalloffLut()
        {
            var relative = NormalizedFalloffPathTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(relative))
            {
                relative = Path.Combine("Sample", "Compair", "CSV", "normalized-falloff-S0200-P1-N1.csv");
            }

            var repoRoot = PathHelpers.TryFindRepositoryRoot(PathHelpers.GetAppBaseDirectory());
            var path = repoRoot == null ? relative : Path.Combine(repoRoot, relative);
            path = Path.GetFullPath(path);

            if (!string.Equals(_normalizedFalloffPath, path, StringComparison.OrdinalIgnoreCase) || _normalizedFalloff == null)
            {
                _normalizedFalloff = NormalizedFalloffLut.LoadFromCsv(path);
                _normalizedFalloffPath = path;
            }
            return _normalizedFalloff!;
        }

        private void BrowseNormalizedFalloffButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true
            };

            var result = dialog.ShowDialog(this);
            if (result != true) return;

            var repoRoot = PathHelpers.TryFindRepositoryRoot(PathHelpers.GetAppBaseDirectory());
            if (repoRoot != null)
            {
                try
                {
                    var rel = Path.GetRelativePath(repoRoot, dialog.FileName);
                    NormalizedFalloffPathTextBox.Text = rel;
                }
                catch
                {
                    NormalizedFalloffPathTextBox.Text = dialog.FileName;
                }
            }
            else
            {
                NormalizedFalloffPathTextBox.Text = dialog.FileName;
            }

            Rendering();
        }

        private void PaperNoiseDiagnosticsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var noisePath = PaperNoisePathTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(noisePath))
                {
                    System.Windows.MessageBox.Show(this, "PaperNoisePath が空です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var invalidMode = PaperNoiseInvalidNoneCheckBox.IsChecked == true
                    ? PaperNoise.InvalidPixelMode.None
                    : PaperNoise.InvalidPixelMode.Legacy;

                var channel = PaperNoiseUseAlphaCheckBox.IsChecked == true
                    ? PaperNoise.SampleChannel.Alpha
                    : PaperNoise.SampleChannel.RgbAverage;

                var repoRoot = PathHelpers.TryFindRepositoryRoot(PathHelpers.GetAppBaseDirectory());
                var fullNoisePath = repoRoot == null ? Path.GetFullPath(noisePath) : Path.GetFullPath(Path.Combine(repoRoot, noisePath));

                using var tmp = PaperNoise.LoadFromFile(fullNoisePath, invalidMode, channel);
                tmp.InvalidEdgeMode = PaperNoiseClampToValidCheckBox.IsChecked == true
                    ? PaperNoise.EdgeMode.ClampToValid
                    : PaperNoise.EdgeMode.TreatInvalidAsOne;

                var diag = tmp.GetPixelDiagnostics();
                System.Windows.MessageBox.Show(this, diag, "PaperNoise diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (ArgumentException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (InvalidOperationException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NoisePreview_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            var usePaperNoise = PaperNoiseCheckBox.IsChecked == true;
            if (!usePaperNoise)
            {
                using var p = new SKPaint { Color = SKColors.LightGray };
                canvas.DrawRect(0, 0, e.Info.Width, e.Info.Height, p);
                return;
            }

            PaperNoise? noise;
            try
            {
                noise = TryLoadPaperNoiseFromUi();
            }
            catch
            {
                noise = null;
            }

            if (noise == null)
            {
                using var p = new SKPaint { Color = SKColors.LightGray };
                canvas.DrawRect(0, 0, e.Info.Width, e.Info.Height, p);
                return;
            }

            if (!TryParsePaperNoiseScale(out var paperNoiseScale) || paperNoiseScale <= 0) paperNoiseScale = 2.0;
            if (!TryParsePaperNoiseOffset(out var paperNoiseOffsetX, out var paperNoiseOffsetY))
            {
                paperNoiseOffsetX = 0.0;
                paperNoiseOffsetY = 0.0;
            }
            if (!TryParsePaperNoiseGain(out var paperNoiseGain) || paperNoiseGain < 0) paperNoiseGain = 0.2;

            if (!TryParsePaperNoiseLowFreq(out var lowFreqScale, out var lowFreqMix))
            {
                lowFreqScale = 4.0;
                lowFreqMix = 0.0;
            }

            var zoom = (double)NoisePreviewZoomNumberBox.Value;
            if (zoom <= 0) zoom = 8.0;

            // 1タイルを拡大して表示
            var outW = e.Info.Width;
            var outH = e.Info.Height;
            using var bmp = new SKBitmap(outW, outH, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            for (var y = 0; y < outH; y++)
            {
                for (var x = 0; x < outW; x++)
                {
                    // 画面上の1pxが、ノイズ空間では 1/zoom px になる
                    var nx = ((x / zoom) + paperNoiseOffsetX) / paperNoiseScale;
                    var ny = ((y / zoom) + paperNoiseOffsetY) / paperNoiseScale;
                    var n01 = noise.Sample01Mixed(nx, ny, lowFreqScale, lowFreqMix);
                    // 表示はgainで見やすくする（正規化ではなく見た目用）
                    var v = Math.Clamp(0.5 + (n01 - noise.Mean01) * paperNoiseGain, 0.0, 1.0);
                    var g8 = (byte)Math.Clamp((int)Math.Round(v * 255.0), 0, 255);
                    bmp.SetPixel(x, y, new SKColor(g8, g8, g8, 255));
                }
            }

            canvas.DrawBitmap(bmp, 0, 0);
        }

        private void UpdateAutoCompareText()
        {
            try
            {
                if (!isLoaded) return;

                if (!TryParseDiameter(out var s) || !SkiaHelpers.ValidateSize(s))
                {
                    AutoCompareTextBlock.Text = "";
                    return;
                }

                if (!TryParsePressure(out var p) || !SkiaHelpers.ValidatePressure(p))
                {
                    AutoCompareTextBlock.Text = "";
                    return;
                }

                if (!TryParseStampCount(out var n) || n <= 0)
                {
                    AutoCompareTextBlock.Text = "";
                    return;
                }

                var usePreQuantize = UsePreQuantizeMetricsCheckBox.IsChecked == true;
                if (!usePreQuantize)
                {
                    AutoCompareTextBlock.Text = "";
                    return;
                }

                var usePaperNoise = PaperNoiseCheckBox.IsChecked == true;
                PaperNoise? noise = null;
                if (usePaperNoise)
                {
                    noise = TryLoadPaperNoiseFromUi();
                }

                if (!TryParsePaperNoiseStrength(out var paperNoiseStrength)) paperNoiseStrength = 0.35;
                if (!TryParsePaperNoiseScale(out var paperNoiseScale) || paperNoiseScale <= 0) paperNoiseScale = 2.0;
                if (!TryParsePaperNoiseOffset(out var paperNoiseOffsetX, out var paperNoiseOffsetY))
                {
                    paperNoiseOffsetX = 0.0;
                    paperNoiseOffsetY = 0.0;
                }
                if (!TryParsePaperNoiseGain(out var paperNoiseGain) || paperNoiseGain < 0) paperNoiseGain = 0.2;

                var applyMode = PaperNoiseApplyToCountCheckBox.IsChecked == true
                    ? PencilDotRenderer.PaperNoiseApplyMode.StampCount
                    : PencilDotRenderer.PaperNoiseApplyMode.Alpha;

                var disableKMeanNorm = IsCheckedByName("DisableKMeanNormalizationCheckBox");
                var applyStage = GetPaperNoiseApplyStageFromUi();
                if (!TryParseAlphaCutoff(out var alphaCutoff01) || alphaCutoff01 < 0) alphaCutoff01 = 0.0;
                var noiseDependentCutoff = IsCheckedByName("NoiseDependentCutoffCheckBox");
                var paperMaskMode = GetPaperMaskModeFromUi();
                if (!TryParsePaperMaskThreshold(out var paperMaskTh01)) paperMaskTh01 = 0.5;
                if (!TryParsePaperMaskGain(out var paperMaskGain)) paperMaskGain = 1.0;
                var paperMaskFalloffMode = GetPaperMaskFalloffModeFromUi();
                var baseShapeMode = GetBaseShapeModeFromUi();
                var paperOnlyFalloffMode = GetPaperOnlyFalloffModeFromUi();
                if (!TryParsePaperOnlyRadiusTh(out var paperOnlyRadiusThNorm)) paperOnlyRadiusThNorm = 1.0;
                var paperCapMode = GetPaperCapModeFromUi();
                if (!TryParsePaperCapGain(out var paperCapGain)) paperCapGain = 1.0;

                if (!TryParsePaperNoiseLowFreq(out var lowFreqScale, out var lowFreqMix))
                {
                    lowFreqScale = 4.0;
                    lowFreqMix = 0.0;
                }

                NormalizedFalloffLut? falloffLut = null;
                try
                {
                    falloffLut = GetNormalizedFalloffLut();
                }
                catch (IOException)
                {
                    falloffLut = null;
                }
                catch (UnauthorizedAccessException)
                {
                    falloffLut = null;
                }

                var alpha01 = PencilDotRenderer.RenderOutAlpha01(CanvasSizePx, s, p, n, noise, paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, paperNoiseGain, lowFreqScale, lowFreqMix, applyMode, falloffLut, disableKMeanNorm, applyStage, alphaCutoff01, noiseDependentCutoff, paperMaskMode, paperMaskTh01, paperMaskGain, paperMaskFalloffMode, baseShapeMode, PencilDotRenderer.PaperOnlyFalloffMode.None, 1.0, paperCapMode, paperCapGain, out var kStats);
                var (mean, stddev) = RadialFalloff.ComputeMeanAndStddevAlphaByRadius(alpha01, CanvasSizePx, CanvasSizePx);

                // UWP側の同条件CSVがある場合は比較
                var pText = p.ToString("0.####", CultureInfo.InvariantCulture);
                var uwpRelativePath = Path.Combine("Sample", "Compair", "CSV", "normalized-falloff-S0200-P1-N1.csv");
                var repoRoot = PathHelpers.TryFindRepositoryRoot(PathHelpers.GetAppBaseDirectory());
                var uwpPath = repoRoot == null ? uwpRelativePath : Path.Combine(repoRoot, uwpRelativePath);

                if (!File.Exists(uwpPath))
                {
                    AutoCompareTextBlock.Text = "";
                    return;
                }

                // 一時ファイルに書き出して比較器を再利用
                var tmpPath = Path.Combine(Path.GetTempPath(), $"SkiaTester-auto-normalized-falloff-S{s}-P{pText}-N{n}.csv");
                CsvWriter.WriteNormalizedFalloff(tmpPath, mean, stddev);
                var (meanMetrics, stddevMetrics) = RadialFalloffComparer.CompareCsvWithStddev(tmpPath, uwpPath);

                AutoCompareTextBlock.Text = $"- MAE(mean)={meanMetrics.mae:0.########}\n- RMSE(mean)={meanMetrics.rmse:0.########}\n- MAE(stddev)={stddevMetrics.mae:0.########}\n- RMSE(stddev)={stddevMetrics.rmse:0.########}\n"
                    + $"k(min,max,mean,std)={kStats.kMin:0.###},{kStats.kMax:0.###},{kStats.kMean:0.###},{kStats.kStddev:0.###}\nkMeanNorm={(disableKMeanNorm ? 1.0 : kStats.kMeanNorm):0.###}\ngain={paperNoiseGain:0.###}\nmode={applyMode.ToString().ToLowerInvariant()}\nstage={(applyStage == PencilDotRenderer.PaperNoiseApplyStage.PostComposite ? "post" : "pre")}\nbase={baseShapeMode.ToString().ToLowerInvariant()}\ncap={paperCapMode.ToString().ToLowerInvariant()}\ncapGain={paperCapGain:0.###}\ncutoff={alphaCutoff01:0.#####}\ncutoffMulK={(noiseDependentCutoff ? 1 : 0)}\nmask={paperMaskMode.ToString().ToLowerInvariant()}\nmaskTh={paperMaskTh01:0.###}\nmaskGain={paperMaskGain:0.###}\nkMeanNormDisabled={(disableKMeanNorm ? 1 : 0)}";
            }
            catch (ArgumentException)
            {
                // 自動更新は無音で落とす（UI操作を阻害しない）
                AutoCompareTextBlock.Text = "";
            }
            catch (IOException)
            {
                AutoCompareTextBlock.Text = "";
            }
            catch (UnauthorizedAccessException)
            {
                AutoCompareTextBlock.Text = "";
            }
            catch (InvalidOperationException)
            {
                AutoCompareTextBlock.Text = "";
            }
        }

        private void EstimatePaperNoiseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dotPath = DotMaterialPathTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(dotPath))
                {
                    System.Windows.MessageBox.Show(this, "DotPNG のパスが空です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                NormalizedFalloffLut lut;
                try
                {
                    lut = GetNormalizedFalloffLut();
                }
                catch (IOException ex)
                {
                    System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                catch (UnauthorizedAccessException ex)
                {
                    System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var repoRoot = PathHelpers.TryFindRepositoryRoot(PathHelpers.GetAppBaseDirectory());
                var fullDotPath = repoRoot == null ? Path.GetFullPath(dotPath) : Path.GetFullPath(Path.Combine(repoRoot, dotPath));

                using var estimated = PaperNoiseEstimator.EstimateFromDotAlphaPng(fullDotPath, lut);

                var outPath = repoRoot == null
                    ? Path.Combine("Sample", "paper-noise-estimated-from-dot.png")
                    : Path.Combine(repoRoot, "Sample", "paper-noise-estimated-from-dot.png");

                PaperNoiseEstimator.SavePng(estimated, outPath);
                PaperNoisePathTextBox.Text = outPath;

                System.Windows.Clipboard.SetText(outPath);
                System.Windows.MessageBox.Show(this, $"保存しました:\n{outPath}\n\n(PaperNoisePathに設定し、パスをクリップボードにコピーしました)", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Information);

                // 次のレンダリングで反映
                Rendering();
            }
            catch (ArgumentException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (InvalidOperationException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DiameterNumberBox.Value = DefaultDiameter;
            PressureNumberBox.Value = DefaultPressure;
            isLoaded = true;
        }

        private void RenderButton_Click(object sender, RoutedEventArgs e)
        {
            Rendering();
        }

        private PaperNoise? TryLoadPaperNoiseFromUi()
        {
            var usePaperNoise = PaperNoiseCheckBox.IsChecked == true;
            if (!usePaperNoise) return null;

            var path = PaperNoisePathTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(path)) return null;

            var invalidMode = PaperNoiseInvalidNoneCheckBox.IsChecked == true
                ? PaperNoise.InvalidPixelMode.None
                : PaperNoise.InvalidPixelMode.Legacy;

            var channel = PaperNoiseUseAlphaCheckBox.IsChecked == true
                ? PaperNoise.SampleChannel.Alpha
                : PaperNoise.SampleChannel.RgbAverage;

            var reloadKey = $"{path}|{invalidMode}|{channel}";
            if (!string.Equals(_paperNoisePath, reloadKey, StringComparison.OrdinalIgnoreCase) || _paperNoise == null)
            {
                var next = PaperNoise.LoadFromFile(path, invalidMode, channel);
                _paperNoise?.Dispose();
                _paperNoise = next;
                _paperNoisePath = reloadKey;
            }

            _paperNoise.InvalidEdgeMode = PaperNoiseClampToValidCheckBox.IsChecked == true
                ? PaperNoise.EdgeMode.ClampToValid
                : PaperNoise.EdgeMode.TreatInvalidAsOne;

            return _paperNoise;
        }

        private void ExportCenterAlphaCsvButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!TryParseDiameter(out var s) || !SkiaHelpers.ValidateSize(s))
                {
                    System.Windows.MessageBox.Show(this, "S(直径px) の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TryParsePressure(out var p) || !SkiaHelpers.ValidatePressure(p))
                {
                    System.Windows.MessageBox.Show(this, "P の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var usePaperNoise = PaperNoiseCheckBox.IsChecked == true;
                PaperNoise? noise = null;
                if (usePaperNoise)
                {
                    try
                    {
                        noise = TryLoadPaperNoiseFromUi();
                    }
                    catch (ArgumentException ex)
                    {
                        System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
                        noise = null;
                    }
                    catch (IOException ex)
                    {
                        System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
                        noise = null;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
                        noise = null;
                    }
                }

                if (!TryParsePaperNoiseStrength(out var paperNoiseStrength))
                {
                    paperNoiseStrength = 0.35;
                }

                if (!TryParsePaperNoiseScale(out var paperNoiseScale) || paperNoiseScale <= 0)
                {
                    paperNoiseScale = 2.0;
                }

                if (!TryParsePaperNoiseOffset(out var paperNoiseOffsetX, out var paperNoiseOffsetY))
                {
                    paperNoiseOffsetX = 0.0;
                    paperNoiseOffsetY = 0.0;
                }

                if (!TryParsePaperNoiseGain(out var paperNoiseGain) || paperNoiseGain < 0)
                {
                    paperNoiseGain = 0.2;
                }

                var applyMode = PaperNoiseApplyToCountCheckBox.IsChecked == true
                    ? PencilDotRenderer.PaperNoiseApplyMode.StampCount
                    : PencilDotRenderer.PaperNoiseApplyMode.Alpha;

                if (!TryParsePaperNoiseLowFreq(out var lowFreqScaleLocal, out var lowFreqMixLocal))
                {
                    lowFreqScaleLocal = 4.0;
                    lowFreqMixLocal = 0.0;
                }

                NormalizedFalloffLut? falloffLut = null;
                try
                {
                    falloffLut = GetNormalizedFalloffLut();
                }
                catch (IOException)
                {
                    falloffLut = null;
                }
                catch (UnauthorizedAccessException)
                {
                    falloffLut = null;
                }

                // UWPのデータセットに合わせたNサンプル（必要なら後でUI化）
                var nList = new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 25, 50, 75, 100 };
                var rows = new List<CenterAlphaSummaryCsvWriter.Row>(nList.Length);
                foreach (var n in nList)
                {
                    var disableKMeanNorm = IsCheckedByName("DisableKMeanNormalizationCheckBox");
                    var applyStage = GetPaperNoiseApplyStageFromUi();
                    if (!TryParseAlphaCutoff(out var alphaCutoff01) || alphaCutoff01 < 0) alphaCutoff01 = 0.0;
                    var noiseDependentCutoff = IsCheckedByName("NoiseDependentCutoffCheckBox");
                    var paperMaskMode = GetPaperMaskModeFromUi();
                    if (!TryParsePaperMaskThreshold(out var paperMaskTh01)) paperMaskTh01 = 0.5;
                    if (!TryParsePaperMaskGain(out var paperMaskGain)) paperMaskGain = 1.0;
                    var paperMaskFalloffMode = GetPaperMaskFalloffModeFromUi();
                    var baseShapeMode = GetBaseShapeModeFromUi();
            var paperOnlyFalloffMode = GetPaperOnlyFalloffModeFromUi();
            if (!TryParsePaperOnlyRadiusTh(out var paperOnlyRadiusThNorm)) paperOnlyRadiusThNorm = 1.0;
                    var paperCapMode = GetPaperCapModeFromUi();
                    if (!TryParsePaperCapGain(out var paperCapGain)) paperCapGain = 1.0;
                    var alpha01 = PencilDotRenderer.RenderOutAlpha01(CanvasSizePx, s, p, n, noise, paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, paperNoiseGain, lowFreqScaleLocal, lowFreqMixLocal, applyMode, falloffLut, disableKMeanNorm, applyStage, alphaCutoff01, noiseDependentCutoff, paperMaskMode, paperMaskTh01, paperMaskGain, paperMaskFalloffMode, baseShapeMode, PencilDotRenderer.PaperOnlyFalloffMode.None, 1.0, paperCapMode, paperCapGain, out _);
                    var center = CenterAlphaSummary.GetCenterAlpha01(alpha01, CanvasSizePx, CanvasSizePx);
                    var q = CenterAlphaSummary.QuantizeTo8Bit01(center);
                    rows.Add(new CenterAlphaSummaryCsvWriter.Row(s, p, n, q));
                }

                var repoRoot = PathHelpers.TryFindRepositoryRoot(PathHelpers.GetAppBaseDirectory());
                var outPath = repoRoot == null
                    ? Path.Combine("Sample", "Compair", "CSV", "N", "Skia-center-alpha-vs-N-vs-P.csv")
                    : Path.Combine(repoRoot, "Sample", "Compair", "CSV", "N", "Skia-center-alpha-vs-N-vs-P.csv");

                CenterAlphaSummaryCsvWriter.Write(outPath, rows);
                System.Windows.Clipboard.SetText(outPath);
                System.Windows.MessageBox.Show(this, $"保存しました:\n{outPath}\n\n(保存パスをクリップボードにコピーしました)", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (ArgumentException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Rendering()
        {

            if (!TryParseDiameter(out var s) || !SkiaHelpers.ValidateSize(s))
            {
                System.Windows.MessageBox.Show(this, "S(直径px) の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParsePressure(out var p) || !SkiaHelpers.ValidatePressure(p))
            {
                System.Windows.MessageBox.Show(this, "P の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseStampCount(out var n) || n <= 0)
            {
                System.Windows.MessageBox.Show(this, "N の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParsePaperNoiseStrength(out var paperNoiseStrength))
            {
                System.Windows.MessageBox.Show(this, "Strength の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParsePaperNoiseScale(out var paperNoiseScale) || paperNoiseScale <= 0)
            {
                System.Windows.MessageBox.Show(this, "NoiseScale の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParsePaperNoiseOffset(out var paperNoiseOffsetX, out var paperNoiseOffsetY))
            {
                System.Windows.MessageBox.Show(this, "NoiseOffset の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParsePaperNoiseGain(out var paperNoiseGain) || paperNoiseGain < 0)
            {
                System.Windows.MessageBox.Show(this, "NoiseGain の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var applyMode = PaperNoiseApplyToCountCheckBox.IsChecked == true
                ? PencilDotRenderer.PaperNoiseApplyMode.StampCount
                : PencilDotRenderer.PaperNoiseApplyMode.Alpha;

                    if (!TryParsePaperNoiseLowFreq(out var lowFreqScaleLocal, out var lowFreqMixLocal))
            {
                        lowFreqScaleLocal = 4.0;
                        lowFreqMixLocal = 0.0;
            }

            var pFloor = PencilPressureFloorTable.GetPFloor(s);
           FloorInfoTextBlock.Text = $"p_floor(S={s})={pFloor.ToString(CultureInfo.InvariantCulture)}  (P<=p_floor は無描画)";

            var usePaperNoise = PaperNoiseCheckBox.IsChecked == true;
            PaperNoise? noise = null;
            if (usePaperNoise)
            {
                try
                {
                    noise = TryLoadPaperNoiseFromUi();
                }
                catch (ArgumentException ex)
                {
                    System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
                    noise = null;
                }
                catch (IOException ex)
                {
                    System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
                    noise = null;
                }
                catch (UnauthorizedAccessException ex)
                {
                    System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
                    noise = null;
                }
            }

            _lastBitmap?.Dispose();
            NormalizedFalloffLut? falloffLut = null;
            try
            {
                falloffLut = GetNormalizedFalloffLut();
            }
            catch (IOException)
            {
                falloffLut = null;
            }
            catch (UnauthorizedAccessException)
            {
                falloffLut = null;
            }

            var disableKMeanNorm = IsCheckedByName("DisableKMeanNormalizationCheckBox");
            var applyStage = GetPaperNoiseApplyStageFromUi();
            if (!TryParseAlphaCutoff(out var alphaCutoff01) || alphaCutoff01 < 0) alphaCutoff01 = 0.0;
            var noiseDependentCutoff = IsCheckedByName("NoiseDependentCutoffCheckBox");
            var paperMaskMode = GetPaperMaskModeFromUi();
            if (!TryParsePaperMaskThreshold(out var paperMaskTh01)) paperMaskTh01 = 0.5;
            if (!TryParsePaperMaskGain(out var paperMaskGain)) paperMaskGain = 1.0;
            var paperMaskFalloffMode = GetPaperMaskFalloffModeFromUi();
            var baseShapeMode = GetBaseShapeModeFromUi();
            var paperOnlyFalloffMode = GetPaperOnlyFalloffModeFromUi();
            if (!TryParsePaperOnlyRadiusTh(out var paperOnlyRadiusThNorm)) paperOnlyRadiusThNorm = 1.0;
            var paperCapMode = GetPaperCapModeFromUi();
            if (!TryParsePaperCapGain(out var paperCapGain)) paperCapGain = 1.0;
            _lastBitmap = PencilDotRenderer.Render(CanvasSizePx, s, p, n, noise, paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, paperNoiseGain, lowFreqScaleLocal, lowFreqMixLocal, applyMode, falloffLut, disableKMeanNorm, applyStage, alphaCutoff01, noiseDependentCutoff, paperMaskMode, paperMaskTh01, paperMaskGain, paperMaskFalloffMode, baseShapeMode, paperOnlyFalloffMode, paperOnlyRadiusThNorm, paperCapMode, paperCapGain);
            _lastAlpha01 = PencilDotRenderer.RenderOutAlpha01(CanvasSizePx, s, p, n, noise, paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, paperNoiseGain, lowFreqScaleLocal, lowFreqMixLocal, applyMode, falloffLut, disableKMeanNorm, applyStage, alphaCutoff01, noiseDependentCutoff, paperMaskMode, paperMaskTh01, paperMaskGain, paperMaskFalloffMode, baseShapeMode, paperOnlyFalloffMode, paperOnlyRadiusThNorm, paperCapMode, paperCapGain, out _);

            var summary = AlphaSummary.FromBitmap(_lastBitmap);
            var meanText = noise == null ? "(none)" : noise.Mean01.ToString("0.######", CultureInfo.InvariantCulture);
            var statsText = noise == null
                ? string.Empty
                : $"\nnoise_channel={noise.Channel}\nnoise_invalidMode={noise.InvalidMode}" +
                  $"\nnoise_min={noise.Min01.ToString("0.######", CultureInfo.InvariantCulture)}\nnoise_max={noise.Max01.ToString("0.######", CultureInfo.InvariantCulture)}\nnoise_std={noise.Stddev01.ToString("0.######", CultureInfo.InvariantCulture)}\nnoise_valid={noise.ValidRatio.ToString("0.######", CultureInfo.InvariantCulture)}\nnoise_size={noise.Width}x{noise.Height}\nnoise_path={_paperNoisePath}";
            var warn = (noise != null && noise.Mean01 < 0.05) ? "  (noise_meanが小さく飽和しやすい)" : "";
            AlphaSummaryTextBlock.Text = $"max_alpha={summary.MaxAlpha.ToString("0.######", CultureInfo.InvariantCulture)}\nnonzero_count={summary.NonzeroCount}\nstrength={paperNoiseStrength.ToString("0.######", CultureInfo.InvariantCulture)}\nnoise_mean={meanText}{statsText}{warn}";
            UpdateAutoCompareText();
            Preview.InvalidateVisual();
            NoisePreview.InvalidateVisual();

        }

        private void PreviewOptions_Changed(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;
            Preview.InvalidateVisual();
            NoisePreview.InvalidateVisual();
        }

        private void ExportRadialCsvButton_Click(object sender, RoutedEventArgs e)
        {
            var bmp = _lastBitmap;
            if (bmp == null)
            {
                System.Windows.MessageBox.Show(this, "先にRenderを実行してください。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!TryParseDiameter(out var s) || !SkiaHelpers.ValidateSize(s))
            {
                System.Windows.MessageBox.Show(this, "S(直径px) の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParsePressure(out var p) || !SkiaHelpers.ValidatePressure(p))
            {
                System.Windows.MessageBox.Show(this, "P の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TryParseStampCount(out var n) || n <= 0)
            {
                System.Windows.MessageBox.Show(this, "N の入力が不正です。", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var dir = OutputDirectoryTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(dir))
            {
                dir = "Sample/Compair/CSV";
            }

            var fileName = BuildDefaultRadialFalloffFileName(s, p, n);
            if (UsePreQuantizeMetricsCheckBox.IsChecked == true)
            {
                fileName = fileName.Replace("Skia-radial-falloff-", "Skia-normalized-falloff-");
            }
            var path = Path.Combine(dir, fileName);
            RadialCsvPathTextBox.Text = path;

            try
            {
                var fullPath = Path.GetFullPath(path);
                var usePreQuantize = UsePreQuantizeMetricsCheckBox.IsChecked == true;
                (double[] mean, double[] stddev) stats;
                if (usePreQuantize)
                {
                    var usePaperNoise = PaperNoiseCheckBox.IsChecked == true;
                    PaperNoise? noise = null;
                    if (usePaperNoise)
                    {
                        try
                        {
                            noise = TryLoadPaperNoiseFromUi();
                        }
                        catch (IOException)
                        {
                            noise = null;
                        }
                        catch (UnauthorizedAccessException)
                        {
                            noise = null;
                        }
                        catch (ArgumentException)
                        {
                            noise = null;
                        }
                    }

                    if (!TryParsePaperNoiseStrength(out var paperNoiseStrength))
                    {
                        paperNoiseStrength = 0.35;
                    }

                    if (!TryParsePaperNoiseScale(out var paperNoiseScale) || paperNoiseScale <= 0)
                    {
                        paperNoiseScale = 2.0;
                    }

                    if (!TryParsePaperNoiseOffset(out var paperNoiseOffsetX, out var paperNoiseOffsetY))
                    {
                        paperNoiseOffsetX = 0.0;
                        paperNoiseOffsetY = 0.0;
                    }

                    if (!TryParsePaperNoiseGain(out var paperNoiseGain) || paperNoiseGain < 0)
                    {
                        paperNoiseGain = 0.2;
                    }

                    var applyMode = PaperNoiseApplyToCountCheckBox.IsChecked == true
                        ? PencilDotRenderer.PaperNoiseApplyMode.StampCount
                        : PencilDotRenderer.PaperNoiseApplyMode.Alpha;

                    var disableKMeanNorm = IsCheckedByName("DisableKMeanNormalizationCheckBox");
                    var applyStage = GetPaperNoiseApplyStageFromUi();
                    if (!TryParseAlphaCutoff(out var alphaCutoff01) || alphaCutoff01 < 0) alphaCutoff01 = 0.0;
                    var noiseDependentCutoff = IsCheckedByName("NoiseDependentCutoffCheckBox");
                    var paperMaskMode = GetPaperMaskModeFromUi();
                    if (!TryParsePaperMaskThreshold(out var paperMaskTh01)) paperMaskTh01 = 0.5;
                    if (!TryParsePaperMaskGain(out var paperMaskGain)) paperMaskGain = 1.0;
                    var paperMaskFalloffMode = GetPaperMaskFalloffModeFromUi();
                    var baseShapeMode = GetBaseShapeModeFromUi();
                    var paperCapMode = GetPaperCapModeFromUi();
                    if (!TryParsePaperCapGain(out var paperCapGain)) paperCapGain = 1.0;

                    if (!TryParsePaperNoiseLowFreq(out var lowFreqScaleLocal, out var lowFreqMixLocal))
                    {
                        lowFreqScaleLocal = 4.0;
                        lowFreqMixLocal = 0.0;
                    }

                    NormalizedFalloffLut? falloffLut = null;
                    try
                    {
                        falloffLut = GetNormalizedFalloffLut();
                    }
                    catch (IOException)
                    {
                        falloffLut = null;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        falloffLut = null;
                    }

                    var alpha01 = PencilDotRenderer.RenderOutAlpha01(CanvasSizePx, s, p, n, noise, paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, paperNoiseGain, lowFreqScaleLocal, lowFreqMixLocal, applyMode, falloffLut, disableKMeanNorm, applyStage, alphaCutoff01, noiseDependentCutoff, paperMaskMode, paperMaskTh01, paperMaskGain, paperMaskFalloffMode, baseShapeMode, PencilDotRenderer.PaperOnlyFalloffMode.None, 1.0, paperCapMode, paperCapGain, out var kStats);
                    stats = RadialFalloff.ComputeMeanAndStddevAlphaByRadius(alpha01, CanvasSizePx, CanvasSizePx);

                    // 量子化前集計の場合のみ、kの分布を後続メッセージで表示する
                    var modeText = applyMode == PencilDotRenderer.PaperNoiseApplyMode.StampCount ? "count" : "alpha";
                    var noiseText = noise == null
                        ? "noise=(none)"
                        : $"noise_mean={noise.Mean01:0.###} noise_std={noise.Stddev01:0.######}";
                    var kText = $"k(min,max,mean,std)={kStats.kMin:0.###},{kStats.kMax:0.###},{kStats.kMean:0.###},{kStats.kStddev:0.###}  kMeanNorm={kStats.kMeanNorm:0.###}  gain={paperNoiseGain:0.###}  mode={modeText}  stage={(applyStage == PencilDotRenderer.PaperNoiseApplyStage.PostComposite ? "post" : "pre")}  cutoff={alphaCutoff01:0.#####}  {noiseText}";
                    // 一時的にファイル名欄へ格納しておき、後のメッセージ表示で参照する
                    RadialCsvPathTextBox.Tag = kText;
                }
                else
                {
                    stats = RadialFalloff.ComputeMeanAndStddevAlphaByRadius(bmp);
                    RadialCsvPathTextBox.Tag = null;
                }

                var (mean, stddev) = stats;
                if (usePreQuantize)
                {
                    CsvWriter.WriteNormalizedFalloff(fullPath, mean, stddev);
                }
                else
                {
                    CsvWriter.WriteRadialFalloff(fullPath, mean, stddev);
                }
                RadialCsvPathTextBox.Text = fileName;

                // UWP側の同条件CSVがある場合は簡易比較して数値を表示する
                var pText = p.ToString("0.####", CultureInfo.InvariantCulture);
                var uwpRelativePath = usePreQuantize
                    ? Path.Combine("Sample", "Compair", "CSV", "normalized-falloff-S0200-P1-N1.csv")
                    : Path.Combine("Sample", "Compair", "CSV", $"radial-falloff-S{s}-P{pText}-N{n}.csv");

                var repoRoot = PathHelpers.TryFindRepositoryRoot(PathHelpers.GetAppBaseDirectory());
                var uwpPath = repoRoot == null ? uwpRelativePath : Path.Combine(repoRoot, uwpRelativePath);

                if (File.Exists(uwpPath))
                {
                    string resultText;
                    if (usePreQuantize)
                    {
                        try
                        {
                            var (meanMetrics, stddevMetrics) = RadialFalloffComparer.CompareCsvWithStddev(fullPath, uwpPath);
                            var kTextLocal = RadialCsvPathTextBox.Tag as string;
                            resultText = $"- MAE(mean)={meanMetrics.mae:0.########}\n- RMSE(mean)={meanMetrics.rmse:0.########}\n- MAE(stddev)={stddevMetrics.mae:0.########}\n- RMSE(stddev)={stddevMetrics.rmse:0.########}";
                            if (!string.IsNullOrWhiteSpace(kTextLocal))
                            {
                                resultText += $"\n- {kTextLocal}\n";
                            }
                        }
                        catch (InvalidOperationException)
                        {
                            var (mae, rmse) = RadialFalloffComparer.CompareCsv(fullPath, uwpPath);
                            resultText = $"- MAE={mae:0.########}\n- RMSE={rmse:0.########}\n- {RadialCsvPathTextBox.Tag}";
                        }
                    }
                    else
                    {
                        var (mae, rmse) = RadialFalloffComparer.CompareCsv(fullPath, uwpPath);
                        resultText = $"- MAE={mae:0.########}\n- RMSE={rmse:0.########}\n- {RadialCsvPathTextBox.Tag}";
                    }
                    bool setSuccess = true;
                    try
                    {
                        System.Windows.Clipboard.SetText(resultText);
                    }
                    catch
                    {
                        setSuccess = false;
                        
                        // クリップボード設定失敗は無視
                    }
                    var kText = RadialCsvPathTextBox.Tag as string;
                    var extra = string.IsNullOrWhiteSpace(kText) ? string.Empty : $"\n\n{kText}";
                    if(setSuccess)
                    {
                        extra += "\n(比較結果をクリップボードにコピーしました)";
                    }
                    else
                    {
                        extra += "\n(比較結果のクリップボードコピーに失敗しました)";
                    }
                    System.Windows.MessageBox.Show(this, $"保存しました:\n{fullPath}\n\nUWP比較:\n{uwpPath}\n{resultText}{extra}", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(this, $"保存しました:\n{fullPath}", "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (ArgumentException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (IOException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "SkiaTester", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string BuildDefaultRadialFalloffFileName(int diameterPx, double pressure, int stampCount)
        {
            // UWP側の命名に寄せる（Pはファイル名上は簡潔にする）。
            // 例: Sample/Compair/CSV/radial-falloff-S200-P0.0104-N1.skia.csv
            var pText = pressure.ToString("0.####", CultureInfo.InvariantCulture);
            return $"Skia-radial-falloff-S{diameterPx}-P{pText}-N{stampCount}.skia.csv";
        }

        private bool TryParseDiameter(out int diameterPx)
        {
            diameterPx = (int)DiameterNumberBox.Value;
            return true;
        }

        private bool TryParsePressure(out double pressure)
        {
            pressure = (double)PressureNumberBox.Value;
            return true;
        }

        private bool TryParseStampCount(out int stampCount)
        {
            stampCount = (int)StampCountNumberBox.Value;
            return true;
        }

        private bool TryParsePreviewScale(out double scale)
        {
            scale = (double)PreviewScaleNumberBox.Value;
            return true;
        }

        private bool TryParsePaperNoiseStrength(out double strength)
        {
            strength = (double)PaperNoiseStrengthNumberBox.Value;
            return true;
        }

        private bool TryParsePaperNoiseScale(out double scale)
        {
            scale = (double)PaperNoiseScaleNumberBox.Value;
            return true;
        }

        private bool TryParsePaperNoisePeriodPx(out double periodPx)
        {
            periodPx = (double)PaperNoisePeriodPxNumberBox.Value;
            return true;
        }

        private bool TryParsePaperNoiseOffset(out double offsetX, out double offsetY)
        {
            offsetX = (double)PaperNoiseOffsetXNumberBox.Value;
            offsetY = (double)PaperNoiseOffsetYNumberBox.Value;
            return true;
        }

        private bool TryParsePaperNoiseGain(out double gain)
        {
            gain = (double)PaperNoiseGainNumberBox.Value;
            return true;
        }

        private bool TryParsePaperNoiseLowFreq(out double scale, out double mix)
        {
            scale = (double)PaperNoiseLowFreqScaleNumberBox.Value;
            mix = (double)PaperNoiseLowFreqMixNumberBox.Value;
            return true;
        }

        private PencilDotRenderer.PaperNoiseApplyStage GetPaperNoiseApplyStageFromUi()
        {
            var idx = (FindName("PaperNoiseApplyStageComboBox") as System.Windows.Controls.Primitives.Selector)?.SelectedIndex ?? 0;
            return idx == 1 ? PencilDotRenderer.PaperNoiseApplyStage.PostComposite : PencilDotRenderer.PaperNoiseApplyStage.PreComposite;
        }

        private void PaperNoiseApplyStageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private bool TryParseAlphaCutoff(out double cutoff01)
        {
            cutoff01 = (FindName("AlphaCutoffNumberBox") as ModernWpf.Controls.NumberBox)?.Value ?? 0.0;
            return true;
        }

        private void AlphaCutoffNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private PencilDotRenderer.PaperMaskMode GetPaperMaskModeFromUi()
        {
            var idx = (FindName("PaperMaskModeComboBox") as System.Windows.Controls.Primitives.Selector)?.SelectedIndex ?? 0;
            return idx switch
            {
                1 => PencilDotRenderer.PaperMaskMode.MultiplyOutAlpha,
                2 => PencilDotRenderer.PaperMaskMode.SoftOutAlpha,
                3 => PencilDotRenderer.PaperMaskMode.ThresholdOutAlpha,
                _ => PencilDotRenderer.PaperMaskMode.None,
            };
        }

        private bool TryParsePaperMaskThreshold(out double threshold01)
        {
            threshold01 = (FindName("PaperMaskThresholdNumberBox") as ModernWpf.Controls.NumberBox)?.Value ?? 0.5;
            return true;
        }

        private bool TryParsePaperMaskGain(out double gain)
        {
            gain = (FindName("PaperMaskGainNumberBox") as ModernWpf.Controls.NumberBox)?.Value ?? 1.0;
            return true;
        }

        private PencilDotRenderer.PaperMaskFalloffMode GetPaperMaskFalloffModeFromUi()
        {
            var idx = (FindName("PaperMaskFalloffModeComboBox") as System.Windows.Controls.Primitives.Selector)?.SelectedIndex ?? 0;
            return idx switch
            {
                1 => PencilDotRenderer.PaperMaskFalloffMode.StrongerAtEdge,
                2 => PencilDotRenderer.PaperMaskFalloffMode.ThresholdAtEdge,
                _ => PencilDotRenderer.PaperMaskFalloffMode.None,
            };
        }

        private void PaperMaskFalloffModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private void PaperMaskModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private PencilDotRenderer.BaseShapeMode GetBaseShapeModeFromUi()
        {
            var idx = (FindName("BaseShapeModeComboBox") as System.Windows.Controls.Primitives.Selector)?.SelectedIndex ?? 0;
            return idx switch
            {
                1 => PencilDotRenderer.BaseShapeMode.PaperOnly,
                _ => PencilDotRenderer.BaseShapeMode.IdealCircle,
            };
        }

        private void BaseShapeModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private PencilDotRenderer.PaperOnlyFalloffMode GetPaperOnlyFalloffModeFromUi()
        {
            var idx = (FindName("PaperOnlyFalloffModeComboBox") as System.Windows.Controls.Primitives.Selector)?.SelectedIndex ?? 0;
            return idx switch
            {
                1 => PencilDotRenderer.PaperOnlyFalloffMode.RadiusThreshold,
                _ => PencilDotRenderer.PaperOnlyFalloffMode.None,
            };
        }

        private bool TryParsePaperOnlyRadiusTh(out double th)
        {
            th = (FindName("PaperOnlyRadiusThNumberBox") as ModernWpf.Controls.NumberBox)?.Value ?? 1.0;
            th = Math.Clamp(th, 0.0, 1.0);
            return true;
        }

        private void PaperOnlyFalloffModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private void PaperOnlyRadiusThNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private PencilDotRenderer.PaperCapMode GetPaperCapModeFromUi()
        {
            return IsCheckedByName("PaperCapCheckBox") ? PencilDotRenderer.PaperCapMode.CapOutAlpha : PencilDotRenderer.PaperCapMode.None;
        }

        private bool TryParsePaperCapGain(out double gain)
        {
            gain = (FindName("PaperCapGainNumberBox") as ModernWpf.Controls.NumberBox)?.Value ?? 1.0;
            return true;
        }

        private void PaperCapGainNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private int GetAlphaPreviewModeIndex()
        {
            return (FindName("AlphaPreviewModeComboBox") as System.Windows.Controls.Primitives.Selector)?.SelectedIndex ?? 0;
        }

        private bool UseFloatAlphaPreview() => GetAlphaPreviewModeIndex() == 1;
        private bool UsePaperMaskPreview() => GetAlphaPreviewModeIndex() == 2;
        private bool UseFalloffWeightPreview() => GetAlphaPreviewModeIndex() == 3;
        private bool UseMaskUsedPreview() => GetAlphaPreviewModeIndex() == 4;
        private bool UseOutABasePreview() => GetAlphaPreviewModeIndex() == 5;
        private bool UseOutAMaskedPreview() => GetAlphaPreviewModeIndex() == 6;
        private bool UseFalloffFPreview() => GetAlphaPreviewModeIndex() == 7;

        private void AlphaPreviewModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!isLoaded) return;
            Preview.InvalidateVisual();
        }

        private void PaperMaskNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private void ApplyPaperNoisePeriodButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;
            ApplyPaperNoisePeriodFromUi();
        }

        private void PresetNoisePeriod43_5Button_Click(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;
            PaperNoisePeriodPxNumberBox.Value = 43.5;
            ApplyPaperNoisePeriodFromUi();
        }

        private void PresetNoisePeriod348Button_Click(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;
            PaperNoisePeriodPxNumberBox.Value = 348;
            ApplyPaperNoisePeriodFromUi();
        }

        private void PaperNoisePeriodPxNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
        }

        private void ApplyPaperNoisePeriodFromUi()
        {
            PaperNoise? noise;
            try
            {
                noise = TryLoadPaperNoiseFromUi();
            }
            catch (ArgumentException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (InvalidOperationException)
            {
                return;
            }

            if (noise == null) return;
            if (!TryParsePaperNoisePeriodPx(out var periodPx) || periodPx <= 0) return;

            // 現在のノイズ画像（px）1周期が、キャンバス上でperiodPxになるように
            // nx=(x+offset)/scale を使っているため、キャンバス周期 = noiseWidth * scale
            // => scale = periodPx / noiseWidth
            var denom = (double)noise.Width;
            if (denom <= 0) return;

            var recommended = periodPx / denom;
            if (double.IsNaN(recommended) || double.IsInfinity(recommended) || recommended <= 0) return;

            // UIのMin/Maxにも合わせてクランプ
            if (recommended < 0.125) recommended = 0.125;
            if (recommended > 1024) recommended = 1024;

            PaperNoiseScaleNumberBox.Value = recommended;
            Rendering();
        }

        private void Preview_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;

            var useChecker = CheckerBackgroundCheckBox.IsChecked == true;
            if (useChecker)
            {
                DrawCheckerBackground(canvas, e.Info.Width, e.Info.Height);
            }
            else
            {
                canvas.Clear(SKColors.White);
            }

            var bmp = _lastBitmap;
            if (bmp == null) return;

            if (!TryParsePreviewScale(out var previewScale) || previewScale <= 0)
            {
                previewScale = 1.0;
            }

            var ignoreAlpha = IgnoreAlphaCheckBox.IsChecked == true;
            var quantizeA8 = IsCheckedByName("QuantizeAlpha8bitCheckBox");

            if (UseFloatAlphaPreview())
            {
                var alpha01 = _lastAlpha01;
                if (alpha01 == null || alpha01.Length != CanvasSizePx * CanvasSizePx) return;

                using var gs = BuildGrayscaleFromAlpha01(alpha01, CanvasSizePx, CanvasSizePx);
                var dst = BuildPreviewDstRect(e.Info.Width, e.Info.Height, previewScale);
                canvas.DrawBitmap(gs, dst);
                return;
            }

            if (UseOutABasePreview() || UseOutAMaskedPreview() || UseFalloffFPreview())
            {
                if (!TryParseDiameter(out var s) || !SkiaHelpers.ValidateSize(s)) return;
                if (!TryParsePressure(out var p) || !SkiaHelpers.ValidatePressure(p)) return;
                if (!TryParseStampCount(out var n) || n <= 0) return;

                var usePaperNoise = PaperNoiseCheckBox.IsChecked == true;
                PaperNoise? noise = null;
                if (usePaperNoise)
                {
                    try
                    {
                        noise = TryLoadPaperNoiseFromUi();
                    }
                    catch
                    {
                        noise = null;
                    }
                }

                if (!TryParsePaperNoiseStrength(out var paperNoiseStrength)) paperNoiseStrength = 0.35;
                if (!TryParsePaperNoiseScale(out var paperNoiseScale) || paperNoiseScale <= 0) paperNoiseScale = 2.0;
                if (!TryParsePaperNoiseOffset(out var paperNoiseOffsetX, out var paperNoiseOffsetY))
                {
                    paperNoiseOffsetX = 0.0;
                    paperNoiseOffsetY = 0.0;
                }
                if (!TryParsePaperNoiseGain(out var paperNoiseGain) || paperNoiseGain < 0) paperNoiseGain = 0.2;

                if (!TryParsePaperNoiseLowFreq(out var lowFreqScaleLocal, out var lowFreqMixLocal))
                {
                    lowFreqScaleLocal = 4.0;
                    lowFreqMixLocal = 0.0;
                }

                var applyMode = PaperNoiseApplyToCountCheckBox.IsChecked == true
                    ? PencilDotRenderer.PaperNoiseApplyMode.StampCount
                    : PencilDotRenderer.PaperNoiseApplyMode.Alpha;
                var disableKMeanNorm = IsCheckedByName("DisableKMeanNormalizationCheckBox");
                var applyStage = GetPaperNoiseApplyStageFromUi();
                if (!TryParseAlphaCutoff(out var alphaCutoff01) || alphaCutoff01 < 0) alphaCutoff01 = 0.0;
                var noiseDependentCutoff = IsCheckedByName("NoiseDependentCutoffCheckBox");
                var paperMaskMode = GetPaperMaskModeFromUi();
                if (!TryParsePaperMaskThreshold(out var paperMaskTh01)) paperMaskTh01 = 0.5;
                if (!TryParsePaperMaskGain(out var paperMaskGain)) paperMaskGain = 1.0;
                var paperMaskFalloffMode = GetPaperMaskFalloffModeFromUi();
                var baseShapeMode = GetBaseShapeModeFromUi();
                var paperOnlyFalloffMode = GetPaperOnlyFalloffModeFromUi();
                if (!TryParsePaperOnlyRadiusTh(out var paperOnlyRadiusThNorm)) paperOnlyRadiusThNorm = 1.0;

                NormalizedFalloffLut? falloffLut = null;
                try
                {
                    falloffLut = GetNormalizedFalloffLut();
                }
                catch (IOException)
                {
                    falloffLut = null;
                }
                catch (UnauthorizedAccessException)
                {
                    falloffLut = null;
                }

                var parts = PencilDotRenderer.RenderOutAlpha01Parts(
                    CanvasSizePx, s, p, n, noise,
                    paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY,
                    paperNoiseGain, lowFreqScaleLocal, lowFreqMixLocal,
                    applyMode, falloffLut, disableKMeanNorm, applyStage, alphaCutoff01, noiseDependentCutoff,
                    paperMaskMode, paperMaskTh01, paperMaskGain, paperMaskFalloffMode,
                    baseShapeMode,
                    paperOnlyFalloffMode,
                    paperOnlyRadiusThNorm);

                var src01 = UseOutABasePreview()
                    ? parts.OutABase01
                    : UseOutAMaskedPreview()
                        ? parts.OutAMasked01
                        : parts.FalloffF01;

                using var gs = BuildGrayscaleFromValue01(src01, CanvasSizePx, CanvasSizePx);
                var dst = BuildPreviewDstRect(e.Info.Width, e.Info.Height, previewScale);
                canvas.DrawBitmap(gs, dst);
                return;
            }

            if (UsePaperMaskPreview())
            {
                var usePaperNoise = PaperNoiseCheckBox.IsChecked == true;
                if (!usePaperNoise) return;

                PaperNoise? noise;
                try
                {
                    noise = TryLoadPaperNoiseFromUi();
                }
                catch
                {
                    return;
                }
                if (noise == null) return;

                if (!TryParseDiameter(out var s) || !SkiaHelpers.ValidateSize(s)) return;
                if (!TryParsePaperNoiseScale(out var paperNoiseScale) || paperNoiseScale <= 0) return;
                if (!TryParsePaperNoiseOffset(out var paperNoiseOffsetX, out var paperNoiseOffsetY)) return;
                if (!TryParsePaperNoiseLowFreq(out var lowFreqScaleLocal, out var lowFreqMixLocal))
                {
                    lowFreqScaleLocal = 4.0;
                    lowFreqMixLocal = 0.0;
                }

                var paperMaskMode = GetPaperMaskModeFromUi();
                if (!TryParsePaperMaskThreshold(out var paperMaskTh01)) paperMaskTh01 = 0.5;
                if (!TryParsePaperMaskGain(out var paperMaskGain)) paperMaskGain = 1.0;

                var paperMaskFalloffMode = GetPaperMaskFalloffModeFromUi();
                NormalizedFalloffLut? falloffLut = null;
                try
                {
                    falloffLut = GetNormalizedFalloffLut();
                }
                catch (IOException)
                {
                    falloffLut = null;
                }
                catch (UnauthorizedAccessException)
                {
                    falloffLut = null;
                }

                var mask01 = BuildPaperMask01ForPreview(CanvasSizePx, s, noise, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, lowFreqScaleLocal, lowFreqMixLocal, paperMaskMode, paperMaskTh01, paperMaskGain, paperMaskFalloffMode, falloffLut);
                if (InvertMaskPreviewCheckBox.IsChecked == true)
                {
                    ApplyInvert01IfNeeded(mask01);
                }
                using var gs = BuildGrayscaleFromValue01(mask01, CanvasSizePx, CanvasSizePx);
                var dst = BuildPreviewDstRect(e.Info.Width, e.Info.Height, previewScale);
                canvas.DrawBitmap(gs, dst);
                return;
            }

            if (UseFalloffWeightPreview())
            {
                if (!TryParseDiameter(out var s) || !SkiaHelpers.ValidateSize(s)) return;
                NormalizedFalloffLut? falloffLut = null;
                try
                {
                    falloffLut = GetNormalizedFalloffLut();
                }
                catch (IOException)
                {
                    falloffLut = null;
                }
                catch (UnauthorizedAccessException)
                {
                    falloffLut = null;
                }

                var w01 = BuildFalloffWeight01ForPreview(CanvasSizePx, s, falloffLut);
                using var gs = BuildGrayscaleFromValue01(w01, CanvasSizePx, CanvasSizePx);
                var dst = BuildPreviewDstRect(e.Info.Width, e.Info.Height, previewScale);
                canvas.DrawBitmap(gs, dst);
                return;
            }

            if (UseMaskUsedPreview())
            {
                if (!TryParseDiameter(out var s) || !SkiaHelpers.ValidateSize(s)) return;
                if (!TryParsePressure(out var p) || !SkiaHelpers.ValidatePressure(p)) return;
                if (!TryParseStampCount(out var n) || n <= 0) return;

                var usePaperNoise = PaperNoiseCheckBox.IsChecked == true;
                PaperNoise? noise = null;
                if (usePaperNoise)
                {
                    try
                    {
                        noise = TryLoadPaperNoiseFromUi();
                    }
                    catch
                    {
                        noise = null;
                    }
                }

                if (!TryParsePaperNoiseStrength(out var paperNoiseStrength)) paperNoiseStrength = 0.35;
                if (!TryParsePaperNoiseScale(out var paperNoiseScale) || paperNoiseScale <= 0) paperNoiseScale = 2.0;
                if (!TryParsePaperNoiseOffset(out var paperNoiseOffsetX, out var paperNoiseOffsetY))
                {
                    paperNoiseOffsetX = 0.0;
                    paperNoiseOffsetY = 0.0;
                }
                if (!TryParsePaperNoiseGain(out var paperNoiseGain) || paperNoiseGain < 0) paperNoiseGain = 0.2;

                if (!TryParsePaperNoiseLowFreq(out var lowFreqScaleLocal, out var lowFreqMixLocal))
                {
                    lowFreqScaleLocal = 4.0;
                    lowFreqMixLocal = 0.0;
                }

                var applyMode = PaperNoiseApplyToCountCheckBox.IsChecked == true
                    ? PencilDotRenderer.PaperNoiseApplyMode.StampCount
                    : PencilDotRenderer.PaperNoiseApplyMode.Alpha;
                var disableKMeanNorm = IsCheckedByName("DisableKMeanNormalizationCheckBox");
                var applyStage = GetPaperNoiseApplyStageFromUi();
                if (!TryParseAlphaCutoff(out var alphaCutoff01) || alphaCutoff01 < 0) alphaCutoff01 = 0.0;
                var noiseDependentCutoff = IsCheckedByName("NoiseDependentCutoffCheckBox");
                var paperMaskMode = GetPaperMaskModeFromUi();
                if (!TryParsePaperMaskThreshold(out var paperMaskTh01)) paperMaskTh01 = 0.5;
                if (!TryParsePaperMaskGain(out var paperMaskGain)) paperMaskGain = 1.0;
                var paperMaskFalloffMode = GetPaperMaskFalloffModeFromUi();
                var baseShapeMode = GetBaseShapeModeFromUi();
                var paperOnlyFalloffMode = GetPaperOnlyFalloffModeFromUi();
                if (!TryParsePaperOnlyRadiusTh(out var paperOnlyRadiusThNorm)) paperOnlyRadiusThNorm = 1.0;

                NormalizedFalloffLut? falloffLut = null;
                try
                {
                    falloffLut = GetNormalizedFalloffLut();
                }
                catch (IOException)
                {
                    falloffLut = null;
                }
                catch (UnauthorizedAccessException)
                {
                    falloffLut = null;
                }

                var parts = PencilDotRenderer.RenderOutAlpha01Parts(
                    CanvasSizePx, s, p, n, noise,
                    paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY,
                    paperNoiseGain, lowFreqScaleLocal, lowFreqMixLocal,
                    applyMode, falloffLut, disableKMeanNorm, applyStage, alphaCutoff01, noiseDependentCutoff,
                    paperMaskMode, paperMaskTh01, paperMaskGain, paperMaskFalloffMode,
                    baseShapeMode,
                    paperOnlyFalloffMode,
                    paperOnlyRadiusThNorm);

                var mask01 = parts.PaperMask01;
                if (InvertMaskPreviewCheckBox.IsChecked == true)
                {
                    ApplyInvert01IfNeeded(mask01);
                }
                using var gs = BuildGrayscaleFromValue01(mask01, CanvasSizePx, CanvasSizePx);
                var dst = BuildPreviewDstRect(e.Info.Width, e.Info.Height, previewScale);
                canvas.DrawBitmap(gs, dst);
                return;
            }

            if (!ignoreAlpha)
            {
                using var src = quantizeA8 ? QuantizeAlpha8bit(bmp) : null;
                var dst = BuildPreviewDstRect(e.Info.Width, e.Info.Height, previewScale);
                canvas.DrawBitmap(src ?? bmp, dst);
                return;
            }

            using (var opaque = MakeOpaque(bmp))
            {
                using var q = quantizeA8 ? QuantizeAlpha8bit(opaque) : null;
                var dst = BuildPreviewDstRect(e.Info.Width, e.Info.Height, previewScale);
                canvas.DrawBitmap(q ?? opaque, dst);
            }
        }

        private static SKBitmap QuantizeAlpha8bit(SKBitmap src)
        {
            var dst = new SKBitmap(src.Width, src.Height, src.ColorType, src.AlphaType);
            for (var y = 0; y < src.Height; y++)
            {
                for (var x = 0; x < src.Width; x++)
                {
                    var c = src.GetPixel(x, y);
                    var a01 = c.Alpha / 255.0;
                    var q01 = CenterAlphaSummary.QuantizeTo8Bit01(a01);
                    var a8 = (byte)Math.Clamp((int)Math.Round(q01 * 255.0), 0, 255);
                    dst.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, a8));
                }
            }
            return dst;
        }

        private static SKRect BuildPreviewDstRect(int viewW, int viewH, double previewScale)
        {
            if (previewScale <= 0) previewScale = 1.0;

            var w = (float)(viewW * previewScale);
            var h = (float)(viewH * previewScale);
            var cx = viewW * 0.5f;
            var cy = viewH * 0.5f;
            return new SKRect(cx - w * 0.5f, cy - h * 0.5f, cx + w * 0.5f, cy + h * 0.5f);
        }

        private static SKBitmap MakeOpaque(SKBitmap src)
        {
            var dst = new SKBitmap(src.Width, src.Height, src.ColorType, SKAlphaType.Opaque);
            for (var y = 0; y < src.Height; y++)
            {
                for (var x = 0; x < src.Width; x++)
                {
                    var c = src.GetPixel(x, y);
                    if (c.Alpha == 0)
                    {
                        dst.SetPixel(x, y, SKColors.White);
                    }
                    else
                    {
                        dst.SetPixel(x, y, new SKColor(c.Red, c.Green, c.Blue, 255));
                    }
                }
            }
            return dst;
        }

        private static void DrawCheckerBackground(SKCanvas canvas, int width, int height)
        {
            const int cell = 16;
            var c0 = new SKColor(240, 240, 240);
            var c1 = new SKColor(210, 210, 210);

            using (var paint = new SKPaint())
            {
                paint.IsAntialias = false;
                for (var y = 0; y < height; y += cell)
                {
                    for (var x = 0; x < width; x += cell)
                    {
                        var use0 = ((x / cell) + (y / cell)) % 2 == 0;
                        paint.Color = use0 ? c0 : c1;
                        canvas.DrawRect(x, y, cell, cell, paint);
                    }
                }
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _paperNoise?.Dispose();
            _paperNoise = null;
        }

        private void DiameterNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            var s = (int)DiameterNumberBox.Value;
            if (ValidateSize(s))
            {
                Rendering();
            }
        }

        private void PressureNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            var p = PressureNumberBox.Value;
            if (ValidatePressure(p))
            {
                Rendering();
            }
        }

        private void PaperNoiseStrengthNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private void PaperNoiseScaleNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private void PaperNoiseOffsetNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private void PaperNoiseGainNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private void PaperNoiseApplyModeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private void PaperNoiseLowFreqNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            Rendering();
        }

        private void PreviewScaleNumberBox_ValueChanged(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!isLoaded) return;
            Preview.InvalidateVisual();
        }

        private void BrowseOutputDirectoryButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            var result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.OK)
            {
                RadialCsvPathTextBox.Text = dialog.SelectedPath;

            }
        }
    }
}