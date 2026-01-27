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
        private PaperNoise? _paperNoise;
        private string? _paperNoisePath;
        private NormalizedFalloffLut? _normalizedFalloff;
        private string? _normalizedFalloffPath;
        private bool isLoaded = false;

        private NormalizedFalloffLut GetNormalizedFalloffLut()
        {
            var relative = Path.Combine("Sample", "Compair", "CSV", "normalized-falloff-S0200-P1-N1.csv");
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
                    var noisePath = PaperNoisePathTextBox.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(noisePath))
                    {
                        if (!string.Equals(_paperNoisePath, noisePath, StringComparison.OrdinalIgnoreCase) || _paperNoise == null)
                        {
                            _paperNoise?.Dispose();
                            _paperNoise = PaperNoise.LoadFromFile(noisePath);
                            _paperNoisePath = noisePath;
                        }
                        _paperNoise.InvalidEdgeMode = PaperNoiseClampToValidCheckBox.IsChecked == true
                            ? PaperNoise.EdgeMode.ClampToValid
                            : PaperNoise.EdgeMode.TreatInvalidAsOne;
                        noise = _paperNoise;
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

                var applyMode = PaperNoiseApplyToCountCheckBox.IsChecked == true
                    ? PencilDotRenderer.PaperNoiseApplyMode.StampCount
                    : PencilDotRenderer.PaperNoiseApplyMode.Alpha;

                if (!TryParsePaperNoiseLowFreq(out var lowFreqScale, out var lowFreqMix))
                {
                    lowFreqScale = 4.0;
                    lowFreqMix = 0.0;
                }

                NormalizedFalloffLut? falloffLut;
                try
                {
                    falloffLut = GetNormalizedFalloffLut();
                }
                catch
                {
                    falloffLut = null;
                }

                var alpha01 = PencilDotRenderer.RenderOutAlpha01(CanvasSizePx, s, p, n, noise, paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, paperNoiseGain, lowFreqScale, lowFreqMix, applyMode, falloffLut, out var kStats);
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
                    + $"k(min,max,mean,std)={kStats.kMin:0.###},{kStats.kMax:0.###},{kStats.kMean:0.###},{kStats.kStddev:0.###}  kMeanNorm={kStats.kMeanNorm:0.###}  gain={paperNoiseGain:0.###}";
            }
            catch
            {
                // 自動更新は無音で落とす（UI操作を阻害しない）
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
                    var path = PaperNoisePathTextBox.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        if (!string.Equals(_paperNoisePath, path, StringComparison.OrdinalIgnoreCase) || _paperNoise == null)
                        {
                            _paperNoise?.Dispose();
                            _paperNoise = PaperNoise.LoadFromFile(path);
                            _paperNoisePath = path;
                        }
                        _paperNoise.InvalidEdgeMode = PaperNoiseClampToValidCheckBox.IsChecked == true
                            ? PaperNoise.EdgeMode.ClampToValid
                            : PaperNoise.EdgeMode.TreatInvalidAsOne;
                        noise = _paperNoise;
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
                    var alpha01 = PencilDotRenderer.RenderOutAlpha01(CanvasSizePx, s, p, n, noise, paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, paperNoiseGain, lowFreqScaleLocal, lowFreqMixLocal, applyMode, falloffLut, out _);
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
                var path = PaperNoisePathTextBox.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    try
                    {
                        // パスが変わった場合だけ読み直す
                        if (!string.Equals(_paperNoisePath, path, StringComparison.OrdinalIgnoreCase) || _paperNoise == null)
                        {
                            _paperNoise?.Dispose();
                            _paperNoise = PaperNoise.LoadFromFile(path);
                            _paperNoisePath = path;
                        }
                        noise = _paperNoise;
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

            _lastBitmap = PencilDotRenderer.Render(CanvasSizePx, s, p, n, noise, paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, paperNoiseGain, lowFreqScaleLocal, lowFreqMixLocal, applyMode, falloffLut);

            var summary = AlphaSummary.FromBitmap(_lastBitmap);
            var meanText = noise == null ? "(none)" : noise.Mean01.ToString("0.######", CultureInfo.InvariantCulture);
            var statsText = noise == null
                ? string.Empty
                : $"\nnoise_min={noise.Min01.ToString("0.######", CultureInfo.InvariantCulture)}\nnoise_max={noise.Max01.ToString("0.######", CultureInfo.InvariantCulture)}\nnoise_std={noise.Stddev01.ToString("0.######", CultureInfo.InvariantCulture)}\nnoise_valid={noise.ValidRatio.ToString("0.######", CultureInfo.InvariantCulture)}\nnoise_size={noise.Width}x{noise.Height}\nnoise_path={_paperNoisePath}";
            var warn = (noise != null && noise.Mean01 < 0.05) ? "  (noise_meanが小さく飽和しやすい)" : "";
            AlphaSummaryTextBlock.Text = $"max_alpha={summary.MaxAlpha.ToString("0.######", CultureInfo.InvariantCulture)}\nnonzero_count={summary.NonzeroCount}\nstrength={paperNoiseStrength.ToString("0.######", CultureInfo.InvariantCulture)}\nnoise_mean={meanText}{statsText}{warn}";
            UpdateAutoCompareText();
            Preview.InvalidateVisual();

        }

        private void PreviewOptions_Changed(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;
            Preview.InvalidateVisual();
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
                        var noisePath = PaperNoisePathTextBox.Text?.Trim();
                        if (!string.IsNullOrWhiteSpace(noisePath))
                        {
                            // Render側と同じインスタンスを使う（未ロードならロード）
                            if (!string.Equals(_paperNoisePath, noisePath, StringComparison.OrdinalIgnoreCase) || _paperNoise == null)
                            {
                                _paperNoise?.Dispose();
                                _paperNoise = PaperNoise.LoadFromFile(noisePath);
                                _paperNoisePath = noisePath;
                            }
                            _paperNoise.InvalidEdgeMode = PaperNoiseClampToValidCheckBox.IsChecked == true
                                ? PaperNoise.EdgeMode.ClampToValid
                                : PaperNoise.EdgeMode.TreatInvalidAsOne;
                            noise = _paperNoise;
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

                    var alpha01 = PencilDotRenderer.RenderOutAlpha01(CanvasSizePx, s, p, n, noise, paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, paperNoiseGain, lowFreqScaleLocal, lowFreqMixLocal, applyMode, falloffLut, out var kStats);
                    stats = RadialFalloff.ComputeMeanAndStddevAlphaByRadius(alpha01, CanvasSizePx, CanvasSizePx);

                    // 量子化前集計の場合のみ、kの分布を後続メッセージで表示する
                    var modeText = applyMode == PencilDotRenderer.PaperNoiseApplyMode.StampCount ? "count" : "alpha";
                    var noiseText = noise == null
                        ? "noise=(none)"
                        : $"noise_mean={noise.Mean01:0.###} noise_std={noise.Stddev01:0.######}";
                    var kText = $"k(min,max,mean,std)={kStats.kMin:0.###},{kStats.kMax:0.###},{kStats.kMean:0.###},{kStats.kStddev:0.###}  kMeanNorm={kStats.kMeanNorm:0.###}  gain={paperNoiseGain:0.###}  mode={modeText}  {noiseText}";
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

            var ignoreAlpha = IgnoreAlphaCheckBox.IsChecked == true;
            if (!ignoreAlpha)
            {
                canvas.DrawBitmap(bmp, new SKRect(0, 0, e.Info.Width, e.Info.Height));
                return;
            }

            using (var opaque = MakeOpaque(bmp))
            {
                canvas.DrawBitmap(opaque, new SKRect(0, 0, e.Info.Width, e.Info.Height));
            }
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