using System.Globalization;
using System.IO;
using DotLab.Rendering;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using SkiaSharp.Views;
using DotLab.Analysis;
using Windows.UI.Input.Inking;
namespace DotLab {

    public partial class MainWindow
    {
        private DotLabNoise? _noise;
        private string? _loadedNoisePath;
        private bool mainWindowLoaded = false;

        private double[]? _lastOutA;
        private double[]? _lastV;
        private double[]? _lastB;
        private double[]? _lastD;
        private double[]? _lastR;
        private double[]? _lastH;
        private double[]? _lastWall;

        private DotLabInputs? _lastInputs;
        private InkPresenter? _presenter;
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void ExportInkPointsDumpStatsButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await InkPointsDumpAnalyzer.ExportInkPointsDumpStatsCsvAsync(this);
        }

        private async void ExportAlphaDiffButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await ImageAlphaDiff.ExportAlphaDiffAsync(this);
        }

        private async void ExportAlignedN1N2RoiAlphaDiffBatchButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await AlignedN12RoiAlphaDiffBatch.ExportAlignedN1N2RoiAlphaDiffBatchAsync(this);
        }

        private async void ExportAlphaBoundsButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await ImageAlphaBounds.ExportAlphaBoundsCsvAsync(this);
        }

        private async void ExportAlphaPresenceBatchButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await ImageAlphaPresenceBatch.ExportAlphaPresenceCsvBatchAsync(this);
        }

        private async void ExportAlphaHistogramButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await ImageAlphaHistogram.ExportAlphaHistogramCsvAsync(this);
        }

        private async void ExportAlphaHistogramBatchButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await ImageAlphaHistogram.ExportAlphaHistogramCsvBatchAsync(this);
        }

        private async void ExportAlphaWindowProfileButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await ImageAlphaWindowProfile.ExportAlphaWindowProfileCsvAsync(this);
        }

        private async void ExportAlphaWindowProfileBatchButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await ImageAlphaWindowProfile.ExportAlphaWindowProfileCsvBatchAsync(this);
        }

        private async void AnalyzeAlphaWindowProfileSummaryButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await AlphaWindowProfileSummaryAnalyzer.AnalyzeAsync(this);
        }

        private async void ExportS200MaskButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await S200RegionMaskExporter.ExportAsync(this);
        }

        private async void AnalyzeAlignedDiffSeriesButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await AlignedDiffSeriesAnalyzer.AnalyzeAsync(this);
        }

        private async void AnalyzeAlignedDiffSeriesMaskedButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await DotLab.Analysis.AlignedDiffSeriesMaskedAnalyzer.AnalyzeAsync(this);
        }

        private async void MatchLineN1VsDotN1Button_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var folderPicker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
            };
            folderPicker.FileTypeFilter.Add(".png");
            folderPicker.FileTypeFilter.Add(".csv");

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, hwnd);

            var folder = await folderPicker.PickSingleFolderAsync();
            if (folder is null) return;

            var csv = LineN1VsDotN1Matcher.BuildMatchCsv(folder.Path, out var lut);
            if (string.IsNullOrWhiteSpace(csv))
            {
                System.Windows.MessageBox.Show(this, "No matchable PNGs found in the selected folder.", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (!lut.Loaded)
            {
                var msg = $"α LUTが読み込めないため、LUT適用は無効になります。\n\nrequested={lut.RequestedPath}\nresolved={lut.ResolvedPath}\nerror={lut.Error}";
                System.Windows.MessageBox.Show(this, msg, "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }

            var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var file = await folder.CreateFileAsync($"lineN1-vs-dotN1-match-{ts}.csv", Windows.Storage.CreationCollisionOption.ReplaceExisting);
            await Windows.Storage.FileIO.WriteTextAsync(file, csv);

            System.Windows.MessageBox.Show(this, $"Done.\nfile={file.Path}", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }

        private async void RunLineN1VsDotOpacityBatchButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var lineFolderPath = LineN1FolderTextBox?.Text?.Trim() ?? "";
            var dotFolderPath = DotOpacityFolderTextBox?.Text?.Trim() ?? "";
            var outFolderPath = AlphaDiffBatchOutputFolderTextBox?.Text?.Trim() ?? "";

            var picker = new Windows.Storage.Pickers.FolderPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".csv");

            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            if (string.IsNullOrWhiteSpace(lineFolderPath) || !Directory.Exists(lineFolderPath))
            {
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null) return;
                lineFolderPath = folder.Path;
                if (LineN1FolderTextBox != null) LineN1FolderTextBox.Text = lineFolderPath;
            }

            if (string.IsNullOrWhiteSpace(dotFolderPath) || !Directory.Exists(dotFolderPath))
            {
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null) return;
                dotFolderPath = folder.Path;
                if (DotOpacityFolderTextBox != null) DotOpacityFolderTextBox.Text = dotFolderPath;
            }

            if (string.IsNullOrWhiteSpace(outFolderPath) || !Directory.Exists(outFolderPath))
            {
                var folder = await picker.PickSingleFolderAsync();
                if (folder is null) return;
                outFolderPath = folder.Path;
                if (AlphaDiffBatchOutputFolderTextBox != null) AlphaDiffBatchOutputFolderTextBox.Text = outFolderPath;
            }

            try
            {
                var useFullImage = LineN1VsDotBatchUseFullImageCheckBox?.IsChecked == true;
                var csv = LineN1VsDotN1BatchMatcher.BuildMatchCsv(lineFolderPath, dotFolderPath, useFullImage);
                if (string.IsNullOrWhiteSpace(csv))
                {
                    System.Windows.MessageBox.Show(this, "No matchable PNGs found in the selected folders.", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var summary = LineN1VsDotN1BatchMatcher.BuildSummaryCsv(lineFolderPath, dotFolderPath, useFullImage);

                var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                var outFile = Path.Combine(outFolderPath, $"lineN1-vs-dotN1-opacitysweep-match-{ts}.csv");
                await File.WriteAllTextAsync(outFile, csv);

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    var outSummary = Path.Combine(outFolderPath, $"lineN1-vs-dotN1-opacitysweep-summary-{ts}.csv");
                    await File.WriteAllTextAsync(outSummary, summary);
                }
                System.Windows.MessageBox.Show(this, $"Done.\nfile={outFile}", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (ArgumentException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void MainWindow_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            mainWindowLoaded = true;
        }

        private void RenderButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            try
            {
                Render();
            }
            catch (ArgumentException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            catch (InvalidOperationException ex)
            {
                System.Windows.MessageBox.Show(this, ex.Message, "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void Render()
        {
            var inputs = UiInputs.ReadFrom(this);
            _lastInputs = inputs;

            if (_noise == null || !string.Equals(_loadedNoisePath, inputs.NoisePath, StringComparison.OrdinalIgnoreCase))
            {
                _noise = DotLabNoise.LoadFromImageAlpha(Path.GetFullPath(inputs.NoisePath));
                _loadedNoisePath = inputs.NoisePath;
            }

            var falloff = inputs.FalloffMode == FalloffMode.IdealCircle
                ? Falloff.CreateIdealCircle(canvasSizePx: inputs.CanvasSizePx, diameterPx: inputs.DiameterPx)
                : inputs.FalloffMode == FalloffMode.Flat
                    ? Falloff.CreateFlat(canvasSizePx: inputs.CanvasSizePx)
                    : Falloff.CreateFromNormalizedFalloffCsv(
                        canvasSizePx: inputs.CanvasSizePx,
                        diameterPx: inputs.DiameterPx,
                        csvPath: Path.GetFullPath("Sample/normalized-falloff-S0200-P1-N1.csv"));

            var result = DotModel.RenderDot(
                canvasSizePx: inputs.CanvasSizePx,
                diameterPx: inputs.DiameterPx,
                pressure01: inputs.Pressure01,
                stampCount: inputs.StampCount,
                softnessK: inputs.SoftnessK,
                falloffF01: falloff,
                noise: _noise,
                noiseScale: inputs.NoiseScale,
                noiseOffsetX: inputs.NoiseOffsetX,
                noiseOffsetY: inputs.NoiseOffsetY);

            _lastOutA = result.OutA;
            _lastV = result.V;
            _lastB = result.B;
            _lastH = result.H;
            _lastWall = result.Wall;
            _lastD = null;
            _lastR = null;

            BuildDerivedArraysForDebug(inputs);

            Preview.InvalidateVisual();
        }

        private void Preview_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);

            var w = e.Info.Width;
            var h = e.Info.Height;

            var mode = (PreviewModeComboBox.SelectedIndex) switch
            {
                1 => PreviewMode.V,
                2 => PreviewMode.B,
                3 => PreviewMode.D,
                4 => PreviewMode.R,
                5 => PreviewMode.H,
                6 => PreviewMode.Wall,
                _ => PreviewMode.OutA,
            };

            var src = mode switch
            {
                PreviewMode.V => _lastV,
                PreviewMode.B => _lastB,
                PreviewMode.D => _lastD,
                PreviewMode.R => _lastR,
                PreviewMode.H => _lastH,
                PreviewMode.Wall => _lastWall,
                _ => _lastOutA,
            };
            if (src == null) return;

            var size = (int)Math.Sqrt(src.Length);
            if (size <= 0 || size * size != src.Length) return;

            using var bmp = DotBitmap.BuildGray8(src, size, size);
            var dst = new SKRect(0, 0, w, h);
            canvas.DrawBitmap(bmp, dst);

            using var paint = new SKPaint { Color = SKColors.DarkGray, IsAntialias = true };
            using var font = new SKFont { Size = 14 };
            canvas.DrawText(BuildOverlayText(mode), 8, 18, SKTextAlign.Left, font, paint);
            canvas.DrawText(BuildStatsText(src), 8, 36, SKTextAlign.Left, font, paint);
        }

        private string BuildOverlayText(PreviewMode mode)
        {
            var m = mode.ToString().ToLowerInvariant();
            var p = PressureNumberBox.Text?.Trim() ?? "";
            var s = DiameterNumberBox.Text?.Trim() ?? "";
            var n = StampCountNumberBox.Text?.Trim() ?? "";
            var k = SoftnessKNumberBox.Text?.Trim() ?? "";
            var ns = NoiseScaleNumberBox.Text?.Trim() ?? "";
            var ox = NoiseOffsetXNumberBox.Text?.Trim() ?? "";
            var oy = NoiseOffsetYNumberBox.Text?.Trim() ?? "";
            var fm = FalloffModeComboBox.SelectedIndex switch
            {
                1 => "flat",
                2 => "csv",
                _ => "ideal",
            };
            var lut = fm == "csv" ? "  lut=normalized-falloff-S0200-P1-N1.csv" : "";
            return $"mode={m}  S={s} P={p} N={n} k={k}  falloff={fm}{lut}  noiseScale={ns} off=({ox},{oy})";
        }

        private string BuildStatsText(double[] src)
        {
            if (_lastInputs is not DotLabInputs inputs) return "";

            var size = (int)Math.Sqrt(src.Length);
            if (size <= 0 || size * size != src.Length) return "";

            var radiusPx = inputs.DiameterPx * 0.5;
            var cx = (size - 1) * 0.5;
            var cy = (size - 1) * 0.5;

            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            var sum = 0.0;
            var count = 0;
            var nonZero = 0;

            for (var y = 0; y < size; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < size; x++)
                {
                    var dx = x - cx;
                    var dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist > radiusPx) continue;

                    var v = src[y * size + x];
                    if (v < min) min = v;
                    if (v > max) max = v;
                    sum += v;
                    count++;
                    if (v > 0) nonZero++;
                }
            }

            if (count == 0) return "(no samples)";

            var mean = sum / count;
            var nzPct = 100.0 * nonZero / count;

            var sampleX = (int)Math.Round(cx);
            var sampleY = (int)Math.Round(cy);
            var nx = ((sampleX + 0.5) + inputs.NoiseOffsetX) / inputs.NoiseScale;
            var ny = ((sampleY + 0.5) + inputs.NoiseOffsetY) / inputs.NoiseScale;

            return $"min={min:0.000} max={max:0.000} mean={mean:0.000} nz={nzPct:0.0}%  sample@({sampleX},{sampleY}) n=({nx:0.###},{ny:0.###})";
        }

        private enum PreviewMode
        {
            OutA,
            V,
            B,
            D,
            R,
            H,
            Wall,
        }

        private void BuildDerivedArraysForDebug(DotLabInputs inputs)
        {
            if (_lastB == null || _lastWall == null) return;
            if (_lastB.Length != _lastWall.Length) return;

            var d = new double[_lastB.Length];
            var r = new double[_lastB.Length];

            for (var i = 0; i < _lastB.Length; i++)
            {
                var dd = _lastB[i] - _lastWall[i];
                d[i] = dd;
                r[i] = dd / inputs.SoftnessK;
            }

            _lastD = d;
            _lastR = r;
        }


        private void Property_Changed(ModernWpf.Controls.NumberBox sender, ModernWpf.Controls.NumberBoxValueChangedEventArgs args)
        {
            if (!mainWindowLoaded) return;
            Render();
        }
    }

    internal static class UiInputs
    {
        public static DotLabInputs ReadFrom(MainWindow window)
        {
            var noisePath = (window.NoisePathTextBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(noisePath)) throw new ArgumentException("NoisePath が空です。");

            var canvasSizePx = ParseInt(window.CanvasSizeNumberBox.Text, "CanvasSize");
            if (canvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(canvasSizePx));

            var diameterPx = ParseInt(window.DiameterNumberBox.Text, "S(diameter)");
            if (diameterPx <= 0) throw new ArgumentOutOfRangeException(nameof(diameterPx));

            var pressure01 = ParseDouble(window.PressureNumberBox.Text, "P");
            if (pressure01 < 0 || pressure01 > 1) throw new ArgumentOutOfRangeException(nameof(pressure01));

            var stampCount = ParseInt(window.StampCountNumberBox.Text, "N");
            if (stampCount <= 0) throw new ArgumentOutOfRangeException(nameof(stampCount));

            var softnessK = ParseDouble(window.SoftnessKNumberBox.Text, "k");
            if (softnessK <= 0) throw new ArgumentOutOfRangeException(nameof(softnessK));

            var noiseScale = ParseDouble(window.NoiseScaleNumberBox.Text, "NoiseScale");
            if (noiseScale <= 0) throw new ArgumentOutOfRangeException(nameof(noiseScale));

            var noiseOffsetX = ParseDouble(window.NoiseOffsetXNumberBox.Text, "NoiseOffsetX");
            var noiseOffsetY = ParseDouble(window.NoiseOffsetYNumberBox.Text, "NoiseOffsetY");

            var falloffMode = window.FalloffModeComboBox.SelectedIndex switch
            {
                1 => FalloffMode.Flat,
                2 => FalloffMode.NormalizedCsvLut,
                _ => FalloffMode.IdealCircle,
            };

            return new DotLabInputs(
                noisePath,
                canvasSizePx,
                diameterPx,
                pressure01,
                stampCount,
                softnessK,
                noiseScale,
                noiseOffsetX,
                noiseOffsetY,
                falloffMode);
        }

        private static int ParseInt(string? text, string name)
        {
            if (!int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                throw new ArgumentException($"{name} の数値変換に失敗しました: '{text}'");
            }
            return v;
        }

        private static double ParseDouble(string? text, string name)
        {
            if (!double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                throw new ArgumentException($"{name} の数値変換に失敗しました: '{text}'");
            }
            return v;
        }
    }

    internal enum FalloffMode
    {
        IdealCircle,
        Flat,
        NormalizedCsvLut,
    }

    internal readonly record struct DotLabInputs(
        string NoisePath,
        int CanvasSizePx,
        int DiameterPx,
        double Pressure01,
        int StampCount,
        double SoftnessK,
        double NoiseScale,
        double NoiseOffsetX,
        double NoiseOffsetY,
        FalloffMode FalloffMode);
}