using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;

using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
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


        public static readonly int[] RadialAlphaThresholds = Helpers.CreateRadialAlphaThresholds();

        public MainPage()
        {
            InitializeComponent();

            InkCanvasControl.InkPresenter.InputDeviceTypes = Windows.UI.Core.CoreInputDeviceTypes.Mouse
                                                            | Windows.UI.Core.CoreInputDeviceTypes.Pen
                                                            | Windows.UI.Core.CoreInputDeviceTypes.Touch;
        }
        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            InkCanvasControl.InkPresenter.StrokeContainer.Clear();

            var attributes = Helpers.CreatePencilAttributesFromToolbarBestEffort(this);
            _lastGeneratedAttributes = attributes;
            _lastOverwritePressure = null;
            _lastMaxOverwrite = null;
            _lastDotGridSpacing = null;
            _lastWasDotGrid = false;

            foreach (var stroke in PencilPressurePresetGenerator.Generate(
                attributes,
                PressurePreset,
                DefaultStartX,
                DefaultEndX,
                DefaultStartY,
                DefaultSpacingY,
                Helpers.CreatePencilStroke))
            {
                InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(stroke);
            }
        }

        private void GenerateOverwriteSamplesButton_Click(object sender, RoutedEventArgs e)
        {
            InkCanvasControl.InkPresenter.StrokeContainer.Clear();

            var attributes = Helpers.CreatePencilAttributesFromToolbarBestEffort(this);
            _lastGeneratedAttributes = attributes;

            var pressure = Helpers.GetOverwritePressure(this);
            var maxOverwrite = Helpers.GetMaxOverwrite(this);
            _lastOverwritePressure = pressure;
            _lastMaxOverwrite = maxOverwrite;

            _lastDotGridSpacing = null;
            _lastWasDotGrid = false;

            foreach (var stroke in PencilOverwriteSampleGenerator.Generate(
                attributes,
                pressure,
                maxOverwrite,
                DefaultStartX,
                DefaultEndX,
                DefaultStartY,
                DefaultSpacingY,
                Helpers.CreatePencilStroke))
            {
                InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(stroke);
            }
        }

        private void GenerateDotGridButton_Click(object sender, RoutedEventArgs e)
        {
            InkCanvasControl.InkPresenter.StrokeContainer.Clear();

            var attributes = Helpers.CreatePencilAttributesFromToolbarBestEffort(this);


            _lastGeneratedAttributes = attributes;

            var maxOverwrite = Helpers.GetMaxOverwrite(this);
            var spacing = Helpers.GetDotGridSpacing(this);

            _lastOverwritePressure = null;
            _lastMaxOverwrite = maxOverwrite;
            _lastDotGridSpacing = spacing;
            _lastWasDotGrid = true;

            foreach (var dot in PencilDotGridGenerator.Generate(
                attributes,
                DotGridPressurePreset,
                maxOverwrite,
                spacing,
                DefaultDotGridStartX,
                DefaultDotGridStartY,
                Helpers.CreatePencilDot))
            {
                InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(dot);
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
            var rs = Helpers.GetRadialSampleRs(this);
            if (rs.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "半径別サマリCSV",
                    Content = "半径一覧が空です。例: 0,1,2,5,10,20,50,100",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

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
            var rows = new List<(double s, double p, int n, double[] a)>();
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

                if (!Helpers.TryParseFalloffFilename(f.Name, out var s, out var p, out var n))
                {
                    skipped++;
                    continue;
                }

                var text = await FileIO.ReadTextAsync(f);
                if (!Helpers.TryReadAlphaSamplesFromFalloffCsv(text, rs, out var a))
                {
                    skipped++;
                    continue;
                }

                rows.Add((s, p, n, a));
            }

            if (rows.Count == 0)
            {
                var dlg0 = new ContentDialog
                {
                    Title = "半径別サマリCSV",
                    Content = "対象CSVが見つかりませんでした（radial-falloff-S*-P*-N*.csv）。",
                    CloseButtonText = "OK"
                };
                await dlg0.ShowAsync();
                return;
            }

            rows.Sort((x, y) =>
            {
                var c = x.s.CompareTo(y.s);
                if (c != 0) return c;
                c = x.p.CompareTo(y.p);
                if (c != 0) return c;
                return x.n.CompareTo(y.n);
            });

            var sb = new StringBuilder(capacity: Math.Max(1024, rows.Count * (30 + rs.Count * 12)));
            sb.Append("S,P,N");
            for (var i = 0; i < rs.Count; i++)
            {
                sb.Append(",a_r");
                sb.Append(rs[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.AppendLine();

            foreach (var row in rows)
            {
                sb.Append(row.s.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(row.p.ToString("0.####", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(row.n.ToString(CultureInfo.InvariantCulture));

                for (var i = 0; i < rs.Count; i++)
                {
                    sb.Append(',');
                    sb.Append(row.a[i].ToString("0.########", CultureInfo.InvariantCulture));
                }

                sb.AppendLine();
            }

            var outName = "alpha-samples-vs-N-vs-P.csv";
            var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var done = new ContentDialog
            {
                Title = "半径別サマリCSV",
                Content = $"完了: {rows.Count}行を書き出しました。スキップ={skipped}件。\n出力={outName}",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
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
            var ps = Helpers.GetRadialFalloffBatchPs(this);
            var sizes = Helpers.GetRadialFalloffBatchSizes(this);
            var ns = Helpers.GetRadialFalloffBatchNs(this);

            if (ps.Count == 0 || sizes.Count == 0 || ns.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "距離減衰CSV一括(P×S×N)",
                    Content = "P一覧 / Sizes / N一覧 のいずれかが空です。例: P=0.05,0.1,...  Sizes=5,12,...  N=1,2,...",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

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

            var device = CanvasDevice.GetSharedDevice();

            var cx = (Dot512Size - 1) / 2f;
            var cy = (Dot512Size - 1) / 2f;

            var total = ps.Count * sizes.Count * ns.Count;
            var doneCount = 0;

            foreach (var p in ps)
            {
                foreach (var size in sizes)
                {
                    var attributes = Helpers.CreatePencilAttributesFromToolbarBestEffort(this);
                    attributes.Size = new Size(size, size);

                    foreach (var n in ns)
                    {
                        var pngName = $"dot512-material-S{size:0.##}-P{p:0.####}-N{n}.png";
                        var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

                        using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
                        using (var target = new CanvasRenderTarget(device, Dot512Size, Dot512Size, Dot512Dpi))
                        {
                            using (var ds = target.CreateDrawingSession())
                            {
                                ds.Clear(Color.FromArgb(0, 0, 0, 0));

                                for (var i = 0; i < n; i++)
                                {
                                    var dot = Helpers.CreatePencilDot(cx, cy, p, attributes);
                                    ds.DrawInk(new[] { dot });
                                }
                            }

                            await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                        }

                        byte[] dotBytes;
                        using (var s = await pngFile.OpenAsync(FileAccessMode.Read))
                        using (var bmp = await CanvasBitmap.LoadAsync(device, s))
                        {
                            dotBytes = bmp.GetPixelBytes();
                        }

                        var fr = Helpers.ComputeRadialMeanAlphaD(dotBytes, Dot512Size, Dot512Size);
                        var csv = Helpers.BuildRadialFalloffCsv(fr);
                        var csvName = $"radial-falloff-S{size:0.##}-P{p:0.####}-N{n}.csv";
                        var csvFile = await folder.CreateFileAsync(csvName, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(csvFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);

                        doneCount++;
                    }
                }
            }

            var done = new ContentDialog
            {
                Title = "距離減衰CSV一括(P×S×N)",
                Content = $"完了: {doneCount}/{total} 個出力しました。",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }

        private async void ExportRadialFalloffBatchPsSizesNsButton_Click(object sender, RoutedEventArgs e)
        {
            var ps = Helpers.GetRadialFalloffBatchPs(this);
            var sizes = Helpers.GetRadialFalloffBatchSizes(this);
            var ns = Helpers.GetRadialFalloffBatchNs(this);

            if (ps.Count == 0 || sizes.Count == 0 || ns.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "距離減衰CSV一括(P×S×N)",
                    Content = "P一覧 / Sizes / N一覧 のいずれかが空です。例: P=0.05,0.1,...  Sizes=5,12,...  N=1,2,...",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

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

            var device = CanvasDevice.GetSharedDevice();

            var cx = (Dot512Size - 1) / 2f;
            var cy = (Dot512Size - 1) / 2f;

            var total = ps.Count * sizes.Count * ns.Count;
            var doneCount = 0;

            foreach (var p in ps)
            {
                foreach (var size in sizes)
                {
                    var attributes = Helpers.CreatePencilAttributesFromToolbarBestEffort(this);
                    attributes.Size = new Size(size, size);

                    foreach (var n in ns)
                    {
                        var pngName = $"dot512-material-S{size:0.##}-P{p:0.####}-N{n}.png";
                        var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

                        using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
                        using (var target = new CanvasRenderTarget(device, Dot512Size, Dot512Size, Dot512Dpi))
                        {
                            using (var ds = target.CreateDrawingSession())
                            {
                                ds.Clear(Color.FromArgb(0, 0, 0, 0));

                                for (var i = 0; i < n; i++)
                                {
                                    var dot = Helpers.CreatePencilDot(cx, cy, p, attributes);
                                    ds.DrawInk(new[] { dot });
                                }
                            }

                            await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                        }

                        byte[] dotBytes;
                        using (var s = await pngFile.OpenAsync(FileAccessMode.Read))
                        using (var bmp = await CanvasBitmap.LoadAsync(device, s))
                        {
                            dotBytes = bmp.GetPixelBytes();
                        }

                        var fr = Helpers.ComputeRadialMeanAlphaD(dotBytes, Dot512Size, Dot512Size);
                        var csv = Helpers.BuildRadialFalloffCsv(fr);

                        var csvName = $"radial-falloff-S{size:0.##}-P{p:0.####}-N{n}.csv";
                        var csvFile = await folder.CreateFileAsync(csvName, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(csvFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);

                        doneCount++;
                    }
                }
            }

            var done = new ContentDialog
            {
                Title = "距離減衰CSV一括(P×S×N)",
                Content = $"完了: {doneCount}/{total} 個出力しました。",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }

        private async void ExportCenterAlphaSummaryButton_Click(object sender, RoutedEventArgs e)
        {
            // 中心αサマリCSVの収集・生成処理は `CenterAlphaSummaryCsvBuilder` に共通化している。
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

            var result = await CenterAlphaSummaryCsvBuilder.BuildFromFolderAsync(
                folder,
                isTargetCsvFile: name =>
                    name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                    && name.StartsWith("radial-falloff-", StringComparison.OrdinalIgnoreCase),
                tryParseKey: name =>
                {
                    if (!Helpers.TryParseFalloffFilename(name, out var s, out var p, out var n))
                    {
                        return (false, default, default, default);
                    }

                    return (true, s, p, n);
                },
                tryReadCenterAlpha: text =>
                {
                    if (!Helpers.TryReadCenterAlphaFromFalloffCsv(text, out var centerAlpha))
                    {
                        return (false, default);
                    }

                    return (true, centerAlpha);
                });

            if (result.Rows == 0)
            {
                var dlg0 = new ContentDialog
                {
                    Title = "中心αサマリCSV",
                    Content = "対象CSVが見つかりませんでした（radial-falloff-S*-P*-N*.csv）。",
                    CloseButtonText = "OK"
                };
                await dlg0.ShowAsync();
                return;
            }

            var outName = "center-alpha-vs-N-vs-P.csv";
            await CenterAlphaSummaryCsvBuilder.SaveAsUtf8Async(folder, outName, result.CsvText);

            var dlg = new ContentDialog
            {
                Title = "中心αサマリCSV",
                Content = $"完了: {result.Rows}行を書き出しました。スキップ={result.Skipped}件。\n出力={outName}",
                CloseButtonText = "OK"
            };
            await dlg.ShowAsync();
        }

        private async void ExportRadialFalloffBatchButton_Click(object sender, RoutedEventArgs e)
        {
            var sizes = Helpers.GetRadialFalloffBatchSizes(this);
            if (sizes.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "距離減衰CSV一括",
                    Content = "Sizes が空です。例: 50,100,150,200",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

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

            var pressure = Helpers.GetDot512Pressure(this);
            var n = Helpers.GetDot512Overwrite(this);

            var device = CanvasDevice.GetSharedDevice();

            foreach (var size in sizes)
            {
                var attributes = Helpers.CreatePencilAttributesFromToolbarBestEffort(this);
                attributes.Size = new Size(size, size);

                var cx = (Dot512Size - 1) / 2f;
                var cy = (Dot512Size - 1) / 2f;

                // dot512-material 相当（透過/ラベル無し）を生成して保存
                var pngName = $"dot512-material-S{size:0.##}-P{pressure:0.###}-N{n}.png";
                var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

                using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, Dot512Size, Dot512Size, Dot512Dpi))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(Color.FromArgb(0, 0, 0, 0));

                        for (var i = 0; i < n; i++)
                        {
                            var dot = Helpers.CreatePencilDot(cx, cy, pressure, attributes);
                            ds.DrawInk(new[] { dot });
                        }
                    }

                    await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                }

                // 距離減衰CSVを生成して保存
                byte[] dotBytes;
                using (var s = await pngFile.OpenAsync(FileAccessMode.Read))
                using (var bmp = await CanvasBitmap.LoadAsync(device, s))
                {
                    dotBytes = bmp.GetPixelBytes();
                }

                var fr = Helpers.ComputeRadialMeanAlphaD(dotBytes, Dot512Size, Dot512Size);
                var csv = Helpers.BuildRadialFalloffCsv(fr);

                var csvName = $"radial-falloff-S{size:0.##}-P{pressure:0.###}-N{n}.csv";
                var csvFile = await folder.CreateFileAsync(csvName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(csvFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }

            var done = new ContentDialog
            {
                Title = "距離減衰CSV一括",
                Content = $"完了: {sizes.Count} サイズ出力しました。",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }

        private async void ExportMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportPngAsync(
                mp: this,
                isTransparentBackground: true,
                includeLabels: false,
                suggestedFileName: "pencil-material");
        }

        private async void ExportPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportPngAsync(
                mp: this,
                isTransparentBackground: false,
                includeLabels: true,
                suggestedFileName: "pencil-preview");
        }

        private async void ExportRadialAlphaCsvButton_Click(object sender, RoutedEventArgs e)
        {
            var sourcePicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            sourcePicker.FileTypeFilter.Add(".png");

            var sourceFile = await sourcePicker.PickSingleFileAsync();
            if (sourceFile is null)
            {
                return;
            }

            var binSize = Helpers.GetRadialBinSize(this);
 
            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"radial-alpha-{sourceFile.DisplayName}"
            };
            savePicker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });

            var saveFile = await savePicker.PickSaveFileAsync();
            if (saveFile is null)
            {
                return;
            }

            var device = CanvasDevice.GetSharedDevice();

            using (var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read))
            using (var bitmap = await CanvasBitmap.LoadAsync(device, sourceStream))
            {
                var bytes = bitmap.GetPixelBytes();
                var width = (int)bitmap.SizeInPixels.Width;
                var height = (int)bitmap.SizeInPixels.Height;

                var analysis = RadialAlphaBinAnalyzer.Analyze(
                    bytes,
                    width,
                    height,
                    binSize,
                    RadialAlphaThresholds);

                var csv = RadialAlphaCsvBuilder.Build(
                    analysis.Bins,
                    binSize,
                    RadialAlphaThresholds,
                    analysis.Total,
                    analysis.SumAlpha,
                    analysis.Hits);

                await FileIO.WriteTextAsync(saveFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }
        }

        private async void ExportDot512MaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportDot512Async(this,isTransparentBackground: true, includeLabels: false, suggestedFileName: "dot512-material");
        }

        private async void ExportDot512PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportDot512Async(mp: this,isTransparentBackground: false, includeLabels: true, suggestedFileName: "dot512-preview");
        }

        private async void ExportDot512BatchMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportDot512BatchAsync(mp:this,isTransparentBackground: true, includeLabels: false, defaultSuffix: "material");
        }

        private async void ExportDot512BatchPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportDot512BatchAsync(mp:this,isTransparentBackground: false, includeLabels: true, defaultSuffix: "preview");
        }

        private async void ExportDot512BatchMaterialSizesButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportDot512BatchSizesAsync(mp:this,isTransparentBackground: true, includeLabels: false, defaultSuffix: "material");
        }

        private async void ExportDot512BatchPreviewSizesButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportDot512BatchSizesAsync(mp:this,isTransparentBackground: false, includeLabels: true, defaultSuffix: "preview");
        }

        private async void ExportDot512SlideMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportDot512SlideAsync(mp:this,isTransparentBackground: true, includeLabels: false, defaultSuffix: "material");
        }

        private async void ExportDot512SlidePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportDot512SlideAsync(mp:this,isTransparentBackground: false, includeLabels: true, defaultSuffix: "preview");
        }

        private async void ExportEstimatedPaperNoiseButton_Click(object sender, RoutedEventArgs e)
        {
            var sourcePicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            sourcePicker.FileTypeFilter.Add(".png");

            var sourceFile = await sourcePicker.PickSingleFileAsync();
            if (sourceFile is null)
            {
                return;
            }

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"paper-noise-estimated-{sourceFile.DisplayName}"
            };
            savePicker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

            var saveFile = await savePicker.PickSaveFileAsync();
            if (saveFile is null)
            {
                return;
            }

            var device = CanvasDevice.GetSharedDevice();

            using (var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read))
            using (var bitmap = await CanvasBitmap.LoadAsync(device, sourceStream))
            {
                var w = (int)bitmap.SizeInPixels.Width;
                var h = (int)bitmap.SizeInPixels.Height;
                var bytes = bitmap.GetPixelBytes();

                var cx = (w - 1) / 2.0;
                var cy = (h - 1) / 2.0;

                var maxR = Math.Sqrt(cx * cx + cy * cy);
                var bins = (int)Math.Floor(maxR) + 1;

                // F(r): 半径方向の平均アルファ（0..1）を推定する
                var sumAlpha = new double[bins];
                var count = new int[bins];

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
                        var a = bytes[idx + 3] / 255.0;
                        sumAlpha[bin] += a;
                        count[bin]++;
                    }
                }

                var fr = new double[bins];
                for (var i = 0; i < bins; i++)
                {
                    var m = count[i] > 0 ? (sumAlpha[i] / count[i]) : 0.0;
                    fr[i] = m;
                }

                // 紙目推定: noise = alpha / F(r)
                // 中心付近と外縁は不安定になりやすいので除外して正規化する
                const int rMin = 2;
                var rMax = Math.Max(rMin + 1, bins - 2);
                const double eps = 1e-6;

                var noise = new double[w * h];
                double minN = double.PositiveInfinity;
                double maxN = double.NegativeInfinity;

                var outBytes = new byte[w * h * 4];
 
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        var n = noise[y * w + x];
 
                        var t = (n - minN) / (maxN - minN);
                        t = Math.Clamp(t, 0.0, 1.0);
                        var g = (byte)Math.Round(t * 255.0);
 
                        var outIdx = (y * w + x) * 4;
                        outBytes[outIdx + 0] = g; // B
                        outBytes[outIdx + 1] = g; // G
                        outBytes[outIdx + 2] = g; // R
                        outBytes[outIdx + 3] = 255;
 
                        // 中心からの距離を推定する（r=r_maxの近傍は不安定なので無視）
                        /*
                        var dx = x - cx;
                        var dy = y - cy;
                        var r = Math.Sqrt((dx * dx) + (dy * dy));
                        if (r >= rMin && r < rMax)
                        {
                            var idx = (y * w + x) * 4;
                            var a = bytes[idx + 3] / 255.0;
                            sumAlpha[bin] += a;
                            count[bin]++;
                        }
                        */
                    }
                }

                CachedFileManager.DeferUpdates(saveFile);
                using (var outStream = await saveFile.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, w, h, Dot512Dpi))
                {
                    target.SetPixelBytes(outBytes);
                    await target.SaveAsync(outStream, CanvasBitmapFileFormat.Png);
                }
                await CachedFileManager.CompleteUpdatesAsync(saveFile);
            }
        }

        private async void ExportS200LineMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            Helpers.lastProperties _lp = new Helpers.lastProperties()
            {
                _lastGeneratedAttributes = this._lastGeneratedAttributes,
                _lastOverwritePressure = this._lastOverwritePressure,
                _lastMaxOverwrite = this._lastMaxOverwrite,
                _lastDotGridSpacing = this._lastDotGridSpacing,
                _lastWasDotGrid = this._lastWasDotGrid
            };
            await Helpers.ExportS200LineAsync(mp: this,_lp: _lp, isTransparentBackground: true,
                includeLabels: false,
                suggestedFileName: "pencil-material-line-s200");
        }

        private async void ExportS200LinePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            Helpers.lastProperties _lp = new Helpers.lastProperties()
            {
                _lastGeneratedAttributes = this._lastGeneratedAttributes,
                _lastOverwritePressure = this._lastOverwritePressure,
                _lastMaxOverwrite = this._lastMaxOverwrite,
                _lastDotGridSpacing = this._lastDotGridSpacing,
                _lastWasDotGrid = this._lastWasDotGrid
            };

            await Helpers.ExportS200LineAsync(mp: this,_lp: _lp, isTransparentBackground: false,
                includeLabels: true,
                suggestedFileName: "pencil-preview-line-s200");
        }

        private async void ExportPaperNoiseCrop24Button_Click(object sender, RoutedEventArgs e)
        {
            var sourcePicker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            sourcePicker.FileTypeFilter.Add(".png");

            var sourceFile = await sourcePicker.PickSingleFileAsync();
            if (sourceFile is null)
            {
                return;
            }

            var dx = Helpers.GetPaperNoiseCropDx(this);
            var dy = Helpers.GetPaperNoiseCropDy(this);

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"crop24-{sourceFile.DisplayName}-dx{dx}-dy{dy}"
            };
            savePicker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

            var saveFile = await savePicker.PickSaveFileAsync();
            if (saveFile is null)
            {
                return;
            }

            var device = CanvasDevice.GetSharedDevice();
            byte[] cropped;

            using (var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read))
            using (var src = await CanvasBitmap.LoadAsync(device, sourceStream))
            {
                var w = (int)src.SizeInPixels.Width;
                var h = (int)src.SizeInPixels.Height;
                var bytes = src.GetPixelBytes();

                var cx = (w - 1) / 2;
                var cy = (h - 1) / 2;

                var cropCx = cx + dx;
                var cropCy = cy + dy;

                var x0 = cropCx - PaperNoiseCropHalf;
                var y0 = cropCy - PaperNoiseCropHalf;
                cropped = Helpers.CropRgba(bytes, w, h, x0, y0, PaperNoiseCropSize, PaperNoiseCropSize);
            }

            CachedFileManager.DeferUpdates(saveFile);
            using (var outStream = await saveFile.OpenAsync(FileAccessMode.ReadWrite))
            using (var target = new CanvasRenderTarget(device, PaperNoiseCropSize, PaperNoiseCropSize, Dot512Dpi))
            {
                target.SetPixelBytes(cropped);
                await target.SaveAsync(outStream, CanvasBitmapFileFormat.Png);
            }
            await CachedFileManager.CompleteUpdatesAsync(saveFile);
        }

        private async void ExportRadialFalloffCsvButton_Click(object sender, RoutedEventArgs e)
        {
            var sourcePicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            sourcePicker.FileTypeFilter.Add(".png");

            var sourceFile = await sourcePicker.PickSingleFileAsync();
            if (sourceFile is null)
            {
                return;
            }

            var binSize = Helpers.GetRadialBinSize(this);
 
            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"radial-alpha-{sourceFile.DisplayName}"
            };
            savePicker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });

            var saveFile = await savePicker.PickSaveFileAsync();
            if (saveFile is null)
            {
                return;
            }

            var device = CanvasDevice.GetSharedDevice();

            using (var sourceStream = await sourceFile.OpenAsync(FileAccessMode.Read))
            using (var bitmap = await CanvasBitmap.LoadAsync(device, sourceStream))
            {
                var bytes = bitmap.GetPixelBytes();
                var width = (int)bitmap.SizeInPixels.Width;
                var height = (int)bitmap.SizeInPixels.Height;

                var analysis = RadialAlphaBinAnalyzer.Analyze(
                    bytes,
                    width,
                    height,
                    binSize,
                    RadialAlphaThresholds);

                var csv = RadialAlphaCsvBuilder.Build(
                    analysis.Bins,
                    binSize,
                    RadialAlphaThresholds,
                    analysis.Total,
                    analysis.SumAlpha,
                    analysis.Hits);

                await FileIO.WriteTextAsync(saveFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }
        }

        private async void ExportNormalizedFalloffButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.ExportNormalizedFalloffAsync(this);
        }
    }
}
