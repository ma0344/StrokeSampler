using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace InkDrawGen.Helpers
{
    internal static class KernelSweepExportService
    {
        private static readonly double[] LocalSlopeVectorAnchors = new[] { 0.10, 0.25, 0.40, 0.55, 0.70, 0.85 };

        private const int ObservationSelectionWindowPx = 9;

        internal static async Task ExportKernelSweepCsvAsync(MainPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            var state = InkDrawGenUiReader.Read(page);
            var scale = Math.Max(1, state.Scale);
            var size = Math.Max(1.0, state.S.Start);
            var requestedParallelism = ResolveMaxParallelism(state.KernelMaxParallelism);
            const int effectiveParallelism = 1;
            var angleStepDeg = state.KernelAngleStepDeg;
            if (double.IsNaN(angleStepDeg) || double.IsInfinity(angleStepDeg) || angleStepDeg <= 0)
            {
                angleStepDeg = 60.0;
            }

            // 観測点（出力px座標）
            var obsPxX = ReadIntFromTextBox(page, "KernelObsPxXTextBox", 0);
            var obsPxY = ReadIntFromTextBox(page, "KernelObsPxYTextBox", 100);

            var sampleCanvasPx = ReadIntFromTextBox(page, "KernelSampleCanvasPxTextBox", 9);
            if (sampleCanvasPx <= 0) sampleCanvasPx = 1;
            if ((sampleCanvasPx % 2) == 0) sampleCanvasPx += 1;

            // S/P/Op は既存UIの start を使用（最短）
            var sDip = state.S.Start;
            var pressure = (float)Math.Clamp(state.P.Start, 0.0, 1.0);
            var opacity = (float)Math.Clamp(state.Opacity.Start, 0.01, 5.0);

            var dpi = (float)Math.Max(1.0, state.Dpi);

            // 出力先フォルダ
            var folder = await PickOutputFolderBestEffortAsync(page, state.OutputFolder);
            if (folder is null)
            {
                return;
            }

            // 観測点（DIP）
            var obsDip = new Point(obsPxX / (double)scale, obsPxY / (double)scale);

            // サンプルキャンバス中心のローカルpx座標
            var local = sampleCanvasPx / 2;

            // ROI（DIP）: 観測点がサンプルキャンバス中心に来るようにする
            var roiDip = new Rect(
                x: obsDip.X - (local / (double)scale),
                y: obsDip.Y - (local / (double)scale),
                width: sampleCanvasPx / (double)scale,
                height: sampleCanvasPx / (double)scale);

            // 透明背景でαを取得する（白背景だとαが潰れるため）
            var transparent = true;

            var effectiveRadiusPx = 0.5 * size * scale;
            var maxRadiusPx = Math.Max(0, (int)Math.Ceiling(effectiveRadiusPx));
            var stableRadiusPx = Math.Max(2, (int)Math.Floor(scale * 0.05));
            var anglesDeg = BuildAngleList(angleStepDeg);

            var profiles = await BuildAngleProfilesAsync(
                transparent,
                sampleCanvasPx,
                local,
                roiDip,
                obsDip,
                scale,
                sDip,
                pressure,
                opacity,
                maxRadiusPx,
                stableRadiusPx,
                anglesDeg,
                dpi,
                effectiveParallelism);

            var rows = BuildKernelRows(profiles, maxRadiusPx, scale);
            var sb = BuildKernelCsv(
                rows,
                size,
                scale,
                pressure,
                opacity,
                obsPxX,
                obsPxY,
                sampleCanvasPx,
                angleStepDeg,
                anglesDeg.Count,
                new[]
                {
                    $"# requested_max_parallelism={requestedParallelism}",
                    $"# effective_parallelism={effectiveParallelism}",
                    "# parallel_mode=async-serial-win2d"
                });

            var fileName = BuildFileName(sDip, pressure, opacity, scale, obsPxX, obsPxY, sampleCanvasPx, angleStepDeg, maxRadiusPx);
            var outFile = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            await ShowDoneDialogAsync(page, outFile.Path, sDip, pressure, scale, obsPxX, obsPxY, sampleCanvasPx, angleStepDeg, anglesDeg.Count, maxRadiusPx, requestedParallelism, effectiveParallelism);
        }

        internal static async Task ExportKernelSweepCsvFromPaperTileAsync(MainPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            var state = InkDrawGenUiReader.Read(page);
            var scale = Math.Max(1, state.Scale);
            var size = Math.Max(1.0, state.S.Start);
            var requestedParallelism = ResolveMaxParallelism(state.KernelMaxParallelism);
            const int effectiveParallelism = 1;
            var angleStepDeg = state.KernelAngleStepDeg;
            if (double.IsNaN(angleStepDeg) || double.IsInfinity(angleStepDeg) || angleStepDeg <= 0)
            {
                angleStepDeg = 60.0;
            }

            var seedObsPxX = ReadIntFromTextBox(page, "KernelObsPxXTextBox", 0);
            var seedObsPxY = ReadIntFromTextBox(page, "KernelObsPxYTextBox", 100);

            var paperTileFile = await PickPngFileAsync();
            if (paperTileFile is null)
            {
                return;
            }

            var paperTile = await LoadAlphaImageAsync(paperTileFile);
            var paperPoint = FindMedianLikePoint(paperTile.Bgra, paperTile.Width, paperTile.Height, seedObsPxX, seedObsPxY, ObservationSelectionWindowPx, excludeZero: false);

            var sampleCanvasPx = ReadIntFromTextBox(page, "KernelSampleCanvasPxTextBox", 9);
            if (sampleCanvasPx <= 0) sampleCanvasPx = 1;
            if ((sampleCanvasPx % 2) == 0) sampleCanvasPx += 1;

            var sDip = state.S.Start;
            var opacity = (float)Math.Clamp(state.Opacity.Start, 0.01, 5.0);
            var dpi = (float)Math.Max(1.0, state.Dpi);
            var pressures = new List<float>();
            foreach (var p in state.P.Expand())
            {
                pressures.Add((float)Math.Clamp(p, 0.0, 1.0));
            }
            if (pressures.Count == 0)
            {
                pressures.Add((float)Math.Clamp(state.P.Start, 0.0, 1.0));
            }

            var folder = await PickOutputFolderBestEffortAsync(page, state.OutputFolder);
            if (folder is null)
            {
                return;
            }

            var transparent = true;
            var effectiveRadiusPx = 0.5 * size * scale;
            var maxRadiusPx = Math.Max(0, (int)Math.Ceiling(effectiveRadiusPx));
            var stableRadiusPx = Math.Max(2, (int)Math.Floor(scale * 0.05));
            var anglesDeg = BuildAngleList(angleStepDeg);
            var summaries = new List<string>(pressures.Count);

            foreach (var pressure in pressures)
            {
                var actualPoint = FindRenderedMedianLikePoint(state, sDip, pressure, opacity, paperPoint.X, paperPoint.Y, ObservationSelectionWindowPx);
                var obsPxX = actualPoint.X;
                var obsPxY = actualPoint.Y;
                var obsDip = new Point(obsPxX / (double)scale, obsPxY / (double)scale);
                var local = sampleCanvasPx / 2;
                var roiDip = new Rect(
                    x: obsDip.X - (local / (double)scale),
                    y: obsDip.Y - (local / (double)scale),
                    width: sampleCanvasPx / (double)scale,
                    height: sampleCanvasPx / (double)scale);

                var profiles = await BuildAngleProfilesAsync(
                    transparent,
                    sampleCanvasPx,
                    local,
                    roiDip,
                    obsDip,
                    scale,
                    sDip,
                    pressure,
                    opacity,
                    maxRadiusPx,
                    stableRadiusPx,
                    anglesDeg,
                    dpi,
                    effectiveParallelism);
                var rows = BuildKernelRows(profiles, maxRadiusPx, scale);
                var metadata = new List<string>
                {
                    $"# paper_tile={paperTileFile.Name}",
                    $"# obs_seed_px={seedObsPxX},{seedObsPxY}",
                    $"# obs_paper_tile_px={paperPoint.X},{paperPoint.Y}",
                    $"# obs_actual_px={actualPoint.X},{actualPoint.Y}",
                    $"# obs_window_px={ObservationSelectionWindowPx}",
                    $"# p_sweep_count={pressures.Count}",
                    $"# requested_max_parallelism={requestedParallelism}",
                    $"# effective_parallelism={effectiveParallelism}",
                    "# parallel_mode=async-serial-win2d",
                };

                var sb = BuildKernelCsv(
                    rows,
                    size,
                    scale,
                    pressure,
                    opacity,
                    obsPxX,
                    obsPxY,
                    sampleCanvasPx,
                    angleStepDeg,
                    anglesDeg.Count,
                    metadata);

                var fileName = BuildFileName(sDip, pressure, opacity, scale, obsPxX, obsPxY, sampleCanvasPx, angleStepDeg, maxRadiusPx, $"papertile-seed{seedObsPxX}_{seedObsPxY}");
                var outFile = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
                await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
                summaries.Add($"P={pressure.ToString("0.####", CultureInfo.InvariantCulture)} actualObs=({actualPoint.X},{actualPoint.Y}) file={outFile.Name}");
            }

            await new ContentDialog
            {
                Title = "カーネル断面CSV(紙目Tile)",
                Content = $"完了: CSVを書き出しました。\n\npaperTile={paperTileFile.Name}\nseedObs=({seedObsPxX},{seedObsPxY}) paperObs=({paperPoint.X},{paperPoint.Y})\ncount={summaries.Count} scale={scale} angleStep={angleStepDeg.ToString("0.####", CultureInfo.InvariantCulture)} canvas={sampleCanvasPx}px requestedParallel={requestedParallelism} effectiveParallel={effectiveParallelism}\n\n{string.Join("\n", summaries)}",
                CloseButtonText = "OK"
            }.ShowAsync();
        }

        private static Task<AngleProfile[]> BuildAngleProfilesAsync(
            bool transparent,
            int sampleCanvasPx,
            int local,
            Rect roiDip,
            Point obsDip,
            int scale,
            double sDip,
            float pressure,
            float opacity,
            int maxRadiusPx,
            int stableRadiusPx,
            IReadOnlyList<double> anglesDeg,
            float dpi,
            int maxParallelism)
        {
            return Task.Run(() =>
            {
                var results = new AngleProfile[anglesDeg.Count];
                var device = CanvasDevice.GetSharedDevice();
                using (var target = new CanvasRenderTarget(device, sampleCanvasPx, sampleCanvasPx, dpi))
                {
                    for (var index = 0; index < anglesDeg.Count; index++)
                    {
                        results[index] = SampleAngleProfile(
                            target,
                            transparent,
                            sampleCanvasPx,
                            local,
                            roiDip,
                            obsDip,
                            scale,
                            sDip,
                            pressure,
                            opacity,
                            maxRadiusPx,
                            stableRadiusPx,
                            anglesDeg[index]);
                    }
                }

                return results;
            });
        }

        internal static async Task ExportKernelDebugPngAsync(MainPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            var state = InkDrawGenUiReader.Read(page);
            var scale = Math.Max(1, state.Scale);
            var sDip = state.S.Start;
            var pressure = (float)Math.Clamp(state.P.Start, 0.0, 1.0);
            var opacity = (float)Math.Clamp(state.Opacity.Start, 0.01, 5.0);
            var dpi = (float)Math.Max(1.0, state.Dpi);
            var debugRadiusPx = Math.Max(0, state.KernelDebugRadiusPx);
            var debugAngleDeg = state.KernelDebugAngleDeg;
            if (double.IsNaN(debugAngleDeg) || double.IsInfinity(debugAngleDeg))
            {
                debugAngleDeg = 0.0;
            }

            var obsPxX = ReadIntFromTextBox(page, "KernelObsPxXTextBox", 0);
            var obsPxY = ReadIntFromTextBox(page, "KernelObsPxYTextBox", 100);
            var sampleCanvasPx = ReadIntFromTextBox(page, "KernelSampleCanvasPxTextBox", 9);
            if (sampleCanvasPx <= 0) sampleCanvasPx = 1;
            if ((sampleCanvasPx % 2) == 0) sampleCanvasPx += 1;

            var folder = await PickOutputFolderBestEffortAsync(page, state.OutputFolder);
            if (folder is null)
            {
                return;
            }

            var obsDip = new Point(obsPxX / (double)scale, obsPxY / (double)scale);
            var local = sampleCanvasPx / 2;
            var roiDip = new Rect(
                x: obsDip.X - (local / (double)scale),
                y: obsDip.Y - (local / (double)scale),
                width: sampleCanvasPx / (double)scale,
                height: sampleCanvasPx / (double)scale);

            var bytes = RenderKernelSampleBytes(
                sampleCanvasPx,
                local,
                roiDip,
                obsDip,
                scale,
                sDip,
                pressure,
                opacity,
                debugRadiusPx,
                debugAngleDeg,
                dpi,
                transparent: true);

            var centerIndex = ((local * sampleCanvasPx) + local) * 4;
            var centerAlpha = centerIndex >= 0 && (centerIndex + 3) < bytes.Length ? bytes[centerIndex + 3] : (byte)0;

            var bitmap = new WriteableBitmap(sampleCanvasPx, sampleCanvasPx);
            using (var stream = bitmap.PixelBuffer.AsStream())
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            bitmap.Invalidate();

            var outName = BuildDebugFileName(sDip, pressure, opacity, scale, obsPxX, obsPxY, sampleCanvasPx, debugRadiusPx, debugAngleDeg, centerAlpha);
            var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
            await PngExportService.SaveAsync(bitmap, outFile);

            var dlg = new ContentDialog
            {
                Title = "kernel debug PNG",
                Content = $"完了: PNGを書き出しました。\n\nfile={outFile.Path}\nobs=({obsPxX},{obsPxY}) r={debugRadiusPx} angle={debugAngleDeg.ToString("0.####", CultureInfo.InvariantCulture)} canvas={sampleCanvasPx}px centerAlpha={centerAlpha}",
                CloseButtonText = "OK"
            };
            await dlg.ShowAsync();
        }

        internal static async Task ExportKernelSweepStairAnalysisAsync(MainPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            var state = InkDrawGenUiReader.Read(page);
            var file = await PickCsvFileAsync();
            if (file is null)
            {
                return;
            }

            var folder = await PickOutputFolderBestEffortAsync(page, state.OutputFolder);
            if (folder is null)
            {
                return;
            }

            var source = await LoadWideKernelAnalysisSourceAsync(file);
            var detailRows = new List<KernelStairDetailRow>(512);
            var summaryRows = new List<KernelStairSummaryRow>(source.Pressures.Count);

            foreach (var pressure in source.Pressures)
            {
                var plateaus = BuildKernelPlateaus(source.Rows, pressure.HeaderSuffix, pressure.PressureValue);
                if (plateaus.Count == 0)
                {
                    summaryRows.Add(new KernelStairSummaryRow(
                        pressure.HeaderSuffix,
                        pressure.PressureValue,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        0,
                        null,
                        null));
                    continue;
                }

                for (var i = 0; i < plateaus.Count; i++)
                {
                    var current = plateaus[i];
                    decimal? riser = null;
                    if ((i + 1) < plateaus.Count)
                    {
                        riser = Decimal.Round(current.LevelEff01 - plateaus[i + 1].LevelEff01, 10, MidpointRounding.AwayFromZero);
                    }

                    int? deltaTread = null;
                    double? treadRatio = null;
                    double? log2TreadRatio = null;
                    if (i > 0)
                    {
                        var prev = plateaus[i - 1];
                        deltaTread = current.TreadPx - prev.TreadPx;
                        if (prev.TreadPx > 0)
                        {
                            treadRatio = current.TreadPx / (double)prev.TreadPx;
                            if (treadRatio > 0)
                            {
                                log2TreadRatio = Math.Log(treadRatio.Value, 2.0);
                            }
                        }
                    }

                    detailRows.Add(new KernelStairDetailRow(
                        pressure.HeaderSuffix,
                        pressure.PressureValue,
                        current.PlateauIndex,
                        current.StartRadiusPx,
                        current.EndRadiusPx,
                        current.StartRadiusNorm,
                        current.EndRadiusNorm,
                        current.TreadPx,
                        current.LevelEff01,
                        riser,
                        deltaTread,
                        treadRatio,
                        log2TreadRatio));
                }

                var treadValues = new List<double>(plateaus.Count);
                var riserValues = new List<double>(Math.Max(0, plateaus.Count - 1));
                for (var i = 0; i < plateaus.Count; i++)
                {
                    treadValues.Add(plateaus[i].TreadPx);
                    if ((i + 1) < plateaus.Count)
                    {
                        riserValues.Add((double)(plateaus[i].LevelEff01 - plateaus[i + 1].LevelEff01));
                    }
                }

                var last = plateaus[plateaus.Count - 1];
                summaryRows.Add(new KernelStairSummaryRow(
                    pressure.HeaderSuffix,
                    pressure.PressureValue,
                    plateaus.Count,
                    Math.Max(0, plateaus.Count - 1),
                    ComputeMean(treadValues),
                    ComputeMedian(treadValues),
                    ComputeMax(treadValues),
                    riserValues.Count > 0 ? ComputeMean(riserValues) : 0.0,
                    riserValues.Count > 0 ? ComputeStddev(riserValues, ComputeMean(riserValues)) : 0.0,
                    last.EndRadiusPx,
                    last.EndRadiusNorm,
                    plateaus[0].LevelEff01));
            }

            var baseName = RemoveExtensionSafe(file.Name);
            var detailFile = await folder.CreateFileAsync($"{baseName}-stair-detail.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(detailFile, BuildKernelStairDetailCsv(file.Name, detailRows), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var summaryFile = await folder.CreateFileAsync($"{baseName}-stair-summary.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(summaryFile, BuildKernelStairSummaryCsv(file.Name, summaryRows), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            await new ContentDialog
            {
                Title = "カーネル断面CSV(階段解析)",
                Content = $"完了: 階段解析CSVを書き出しました。\n\ninput={file.Name}\ndetail={detailFile.Path}\nsummary={summaryFile.Path}\np_count={summaryRows.Count}",
                CloseButtonText = "OK"
            }.ShowAsync();
        }

        internal static async Task ExportKernelSweepPredictionComparisonAsync(MainPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            var state = InkDrawGenUiReader.Read(page);
            var file = await PickCsvFileAsync();
            if (file is null)
            {
                return;
            }

            var folder = await PickOutputFolderBestEffortAsync(page, state.OutputFolder);
            if (folder is null)
            {
                return;
            }

            List<KernelObservedMetrics> observedRows;
            var inputFileName = file.Name;
            var compareBaseName = RemovePredictionInputSuffix(RemoveExtensionSafe(file.Name));

            if (file.Name.EndsWith("-stair-detail.csv", StringComparison.OrdinalIgnoreCase))
            {
                var summaryName = BuildSiblingSummaryFileName(file.Name);
                var summaryFile = await TryResolveSiblingCsvAsync(file, summaryName);
                if (summaryFile == null)
                {
                    throw new InvalidOperationException($"対応する階段summary CSVが見つかりません: {summaryName}");
                }

                var detailRows = await LoadKernelStairDetailRowsAsync(file);
                var summaryRows = await LoadKernelStairSummaryRowsAsync(summaryFile);
                observedRows = BuildKernelObservedMetricsFromStairRows(detailRows, summaryRows);
                inputFileName = $"{file.Name};{summaryFile.Name}";
            }
            else
            {
                var source = await LoadWideKernelAnalysisSourceAsync(file);
                var maxObservedRadiusPx = source.Rows.Count > 0 ? source.Rows[source.Rows.Count - 1].RadiusPx : 0;
                var radiusNormScale = ResolveRadiusNormScale(source.Rows);
                observedRows = new List<KernelObservedMetrics>(source.Pressures.Count);

                foreach (var pressure in source.Pressures)
                {
                    var plateaus = BuildKernelPlateaus(source.Rows, pressure.HeaderSuffix, pressure.PressureValue);
                    if (plateaus.Count == 0)
                    {
                        continue;
                    }

                    observedRows.Add(BuildKernelObservedMetrics(
                        pressure.HeaderSuffix,
                        pressure.PressureValue,
                        plateaus,
                        maxObservedRadiusPx,
                        radiusNormScale,
                        null));
                }
            }

            var comparisonRows = BuildKernelPredictionComparisonRows(observedRows);
            var compareFile = await folder.CreateFileAsync($"{compareBaseName}-stair-prediction-compare.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(compareFile, BuildKernelPredictionComparisonCsv(inputFileName, comparisonRows), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            await new ContentDialog
            {
                Title = "カーネル断面CSV(予測比較)",
                Content = $"完了: 予測比較CSVを書き出しました。\n\ninput={file.Name}\ncompare={compareFile.Path}\np_count={comparisonRows.Count}",
                CloseButtonText = "OK"
            }.ShowAsync();
        }

        internal static async Task ExportKernelSweepWideCsvAsync(MainPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            var state = InkDrawGenUiReader.Read(page);
            var files = await PickCsvFilesAsync();
            if (files == null || files.Count == 0)
            {
                return;
            }

            var folder = await PickOutputFolderBestEffortAsync(page, state.OutputFolder);
            if (folder is null)
            {
                return;
            }

            var sources = new List<WideKernelSource>(files.Count);
            foreach (var file in files)
            {
                sources.Add(await LoadWideKernelSourceAsync(file));
            }

            sources.Sort((a, b) =>
            {
                var cmp = a.PressureSortKey.CompareTo(b.PressureSortKey);
                if (cmp != 0) return cmp;
                return string.Compare(a.PressureHeaderSuffix, b.PressureHeaderSuffix, StringComparison.OrdinalIgnoreCase);
            });

            var metricNames = new List<string>();
            if (sources.Count > 0)
            {
                for (var i = 0; i < sources[0].MetricNames.Count; i++)
                {
                    metricNames.Add(sources[0].MetricNames[i]);
                }
            }

            var rowsByRadius = new SortedDictionary<int, WideKernelRadiusRow>();
            foreach (var source in sources)
            {
                foreach (var row in source.Rows)
                {
                    if (!rowsByRadius.TryGetValue(row.RadiusPx, out var wideRow))
                    {
                        wideRow = new WideKernelRadiusRow(row.RadiusPx, row.RadiusNorm);
                        rowsByRadius.Add(row.RadiusPx, wideRow);
                    }

                    if (!wideRow.RadiusNorm.HasValue && row.RadiusNorm.HasValue)
                    {
                        wideRow.RadiusNorm = row.RadiusNorm;
                    }

                    wideRow.ValuesByPressure[source.PressureHeaderSuffix] = row.Metrics;
                }
            }

            var sb = new StringBuilder(capacity: Math.Max(32 * 1024, rowsByRadius.Count * Math.Max(128, 24 * Math.Max(1, sources.Count))));
            sb.Append("# source=kernel sweep wide aggregate").AppendLine();
            sb.Append("# file_count=").Append(files.Count.ToString(CultureInfo.InvariantCulture)).AppendLine();
            for (var i = 0; i < sources.Count; i++)
            {
                sb.Append("# source_file_").Append(i.ToString(CultureInfo.InvariantCulture)).Append('=')
                    .Append(sources[i].FileName).Append(" p=")
                    .Append(sources[i].PressureHeaderSuffix).AppendLine();
            }

            sb.Append("r_px,r_norm");
            foreach (var source in sources)
            {
                for (var metricIndex = 0; metricIndex < metricNames.Count; metricIndex++)
                {
                    sb.Append(',').Append(metricNames[metricIndex]).Append("_p").Append(source.PressureHeaderSuffix);
                }
            }
            sb.AppendLine();

            foreach (var pair in rowsByRadius)
            {
                var row = pair.Value;
                sb.Append(row.RadiusPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                if (row.RadiusNorm.HasValue)
                {
                    sb.Append(row.RadiusNorm.Value.ToString("0.########", CultureInfo.InvariantCulture));
                }
                sb.Append(',');

                for (var sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
                {
                    var source = sources[sourceIndex];
                    row.ValuesByPressure.TryGetValue(source.PressureHeaderSuffix, out var metrics);
                    for (var metricIndex = 0; metricIndex < metricNames.Count; metricIndex++)
                    {
                        if (sourceIndex != 0 || metricIndex != 0)
                        {
                            sb.Append(',');
                        }

                        if (metrics != null && metrics.TryGetValue(metricNames[metricIndex], out var valueText))
                        {
                            sb.Append(valueText);
                        }
                    }
                }

                sb.AppendLine();
            }

            var outName = $"kernel-sweep-wide-count{sources.Count}.csv";
            var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            await new ContentDialog
            {
                Title = "カーネル断面CSV(wide集約)",
                Content = $"完了: wide集約CSVを書き出しました。\n\nfile={outFile.Path}\ncount={sources.Count}",
                CloseButtonText = "OK"
            }.ShowAsync();
        }

        internal static async Task ExportKernelRawCsvAsync(MainPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            var state = InkDrawGenUiReader.Read(page);
            var scale = Math.Max(1, state.Scale);
            var size = Math.Max(1.0, state.S.Start);
            var angleStepDeg = state.KernelAngleStepDeg;
            if (double.IsNaN(angleStepDeg) || double.IsInfinity(angleStepDeg) || angleStepDeg <= 0)
            {
                angleStepDeg = 60.0;
            }

            var obsPxX = ReadIntFromTextBox(page, "KernelObsPxXTextBox", 0);
            var obsPxY = ReadIntFromTextBox(page, "KernelObsPxYTextBox", 100);
            var sampleCanvasPx = ReadIntFromTextBox(page, "KernelSampleCanvasPxTextBox", 9);
            if (sampleCanvasPx <= 0) sampleCanvasPx = 1;
            if ((sampleCanvasPx % 2) == 0) sampleCanvasPx += 1;

            var sDip = state.S.Start;
            var pressure = (float)Math.Clamp(state.P.Start, 0.0, 1.0);
            var opacity = (float)Math.Clamp(state.Opacity.Start, 0.01, 5.0);
            var dpi = (float)Math.Max(1.0, state.Dpi);

            var folder = await PickOutputFolderBestEffortAsync(page, state.OutputFolder);
            if (folder is null)
            {
                return;
            }

            var obsDip = new Point(obsPxX / (double)scale, obsPxY / (double)scale);
            var local = sampleCanvasPx / 2;
            var roiDip = new Rect(
                x: obsDip.X - (local / (double)scale),
                y: obsDip.Y - (local / (double)scale),
                width: sampleCanvasPx / (double)scale,
                height: sampleCanvasPx / (double)scale);

            var effectiveRadiusPx = 0.5 * size * scale;
            var maxRadiusPx = Math.Max(0, (int)Math.Ceiling(effectiveRadiusPx));
            var anglesDeg = BuildAngleList(angleStepDeg);

            var sb = new StringBuilder(capacity: Math.Max(64 * 1024, (maxRadiusPx + 1) * Math.Max(1, anglesDeg.Count) * 32));
            sb.Append("# source=InkDrawGen generated dot obs=")
                .Append(obsPxX.ToString(CultureInfo.InvariantCulture))
                .Append('_')
                .Append(obsPxY.ToString(CultureInfo.InvariantCulture))
                .Append(" canvas=")
                .Append(sampleCanvasPx.ToString(CultureInfo.InvariantCulture))
                .Append(" pressure=")
                .Append(pressure.ToString("0.####", CultureInfo.InvariantCulture))
                .Append(" opacity=")
                .Append(opacity.ToString("0.####", CultureInfo.InvariantCulture))
                .AppendLine();
            sb.Append("# size=").Append(size.ToString("0.####", CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("# scale=").Append(scale.ToString(CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("# angle_step_deg=").Append(angleStepDeg.ToString("0.####", CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("# angle_count=").Append(anglesDeg.Count.ToString(CultureInfo.InvariantCulture)).AppendLine();
            sb.AppendLine("# observation=center_pixel_only");
            sb.AppendLine("angle_deg,r_px,r_norm,alpha_byte,alpha01,is_observed");

            var device = CanvasDevice.GetSharedDevice();
            using (var target = new CanvasRenderTarget(device, sampleCanvasPx, sampleCanvasPx, dpi))
            {
                foreach (var angleDeg in anglesDeg)
                {
                    var angleRad = angleDeg * Math.PI / 180.0;
                    var cos = Math.Cos(angleRad);
                    var sin = Math.Sin(angleRad);

                    for (var rPx = 0; rPx <= maxRadiusPx; rPx++)
                    {
                        var centerDip = new Point(
                            x: obsDip.X + ((rPx * cos) / scale),
                            y: obsDip.Y - ((rPx * sin) / scale));

                        var bytes = RenderKernelSampleBytes(target, true, sampleCanvasPx, roiDip, scale, sDip, pressure, opacity, centerDip);
                        var i = ((local * sampleCanvasPx) + local) * 4;
                        byte alphaByte = 0;
                        if (i >= 0 && (i + 3) < bytes.Length)
                        {
                            alphaByte = bytes[i + 3];
                        }

                        var alpha01 = alphaByte / 255.0;
                        var observed = alphaByte > 0 ? 1 : 0;

                        sb.Append(angleDeg.ToString("0.####", CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(rPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                        sb.Append((rPx / (double)scale).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(alphaByte.ToString(CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(alpha01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                        sb.Append(observed.ToString(CultureInfo.InvariantCulture)).AppendLine();
                    }
                }
            }

            var outName = BuildRawFileName(sDip, pressure, opacity, scale, obsPxX, obsPxY, sampleCanvasPx, angleStepDeg, maxRadiusPx);
            var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var dlg = new ContentDialog
            {
                Title = "kernel raw CSV",
                Content = $"完了: CSVを書き出しました。\n\nfile={outFile.Path}\nobs=({obsPxX},{obsPxY}) canvas={sampleCanvasPx}px angleStep={angleStepDeg.ToString("0.####", CultureInfo.InvariantCulture)} angleCount={anglesDeg.Count} maxR={maxRadiusPx}",
                CloseButtonText = "OK"
            };
            await dlg.ShowAsync();
        }

        private static AngleProfile SampleAngleProfile(
            CanvasRenderTarget target,
            bool transparent,
            int sampleCanvasPx,
            int local,
            Rect roiDip,
            Point obsDip,
            int scale,
            double sDip,
            float pressure,
            float opacity,
            int maxRadiusPx,
            int stableRadiusPx,
            double angleDeg)
        {
            var angleRad = angleDeg * Math.PI / 180.0;
            var cos = Math.Cos(angleRad);
            var sin = Math.Sin(angleRad);
            var samples = new double?[maxRadiusPx + 1];

            for (var rPx = 0; rPx <= maxRadiusPx; rPx++)
            {
                var centerDip = new Point(
                    x: obsDip.X + ((rPx * cos) / scale),
                    y: obsDip.Y - ((rPx * sin) / scale));

                var bytes = RenderKernelSampleBytes(target, transparent, sampleCanvasPx, roiDip, scale, sDip, pressure, opacity, centerDip);
                var i = ((local * sampleCanvasPx) + local) * 4;
                if (i < 0 || (i + 3) >= bytes.Length)
                {
                    samples[rPx] = null;
                    continue;
                }

                var alpha01 = bytes[i + 3] / 255.0;
                if (alpha01 > 0)
                {
                    samples[rPx] = alpha01;
                    continue;
                }

                samples[rPx] = null;
            }

            var stableValues = new List<double>(stableRadiusPx + 1);
            for (var rPx = 0; rPx <= stableRadiusPx && rPx < samples.Length; rPx++)
            {
                var value = samples[rPx];
                if (!value.HasValue) continue;
                if (value.Value <= 0) continue;
                stableValues.Add(value.Value);
            }

            var normalization = ComputeMedian(stableValues);
            if (!(normalization > 0))
            {
                return new AngleProfile(angleDeg, new double?[samples.Length], false);
            }

            var normalized = new double?[samples.Length];
            for (var rPx = 0; rPx < samples.Length; rPx++)
            {
                var value = samples[rPx];
                if (!value.HasValue) continue;
                normalized[rPx] = Math.Clamp(value.Value / normalization, 0.0, 1.0);
            }

            return new AngleProfile(angleDeg, normalized, true);
        }

        private static byte[] RenderKernelSampleBytes(
            int sampleCanvasPx,
            int local,
            Rect roiDip,
            Point obsDip,
            int scale,
            double sDip,
            float pressure,
            float opacity,
            int radiusPx,
            double angleDeg,
            float dpi,
            bool transparent)
        {
            var angleRad = angleDeg * Math.PI / 180.0;
            var centerDip = new Point(
                x: obsDip.X + ((radiusPx * Math.Cos(angleRad)) / scale),
                y: obsDip.Y - ((radiusPx * Math.Sin(angleRad)) / scale));

            var device = CanvasDevice.GetSharedDevice();
            using (var target = new CanvasRenderTarget(device, sampleCanvasPx, sampleCanvasPx, dpi))
            {
                return RenderKernelSampleBytes(target, transparent, sampleCanvasPx, roiDip, scale, sDip, pressure, opacity, centerDip);
            }
        }

        private static byte[] RenderKernelSampleBytes(
            CanvasRenderTarget target,
            bool transparent,
            int sampleCanvasPx,
            Rect roiDip,
            int scale,
            double sDip,
            float pressure,
            float opacity,
            Point centerDip)
        {
            var stroke = InkStrokeBuildService.BuildSDotStroke(centerDip, sDip, pressure, opacity);

            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(transparent ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                // ROIを原点へ平行移動してからscaleする。
                ds.Transform = System.Numerics.Matrix3x2.CreateTranslation(-(float)roiDip.X, -(float)roiDip.Y)
                    * System.Numerics.Matrix3x2.CreateScale(scale);
                ds.DrawInk(new[] { stroke });
            }

            return target.GetPixelBytes();
        }

        private static void InterpolateInternalMissing(double?[] values)
        {
            var lastValid = -1;
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i].HasValue)
                {
                    lastValid = i;
                    continue;
                }

                var gapStart = i;
                while (i < values.Length && !values[i].HasValue)
                {
                    i++;
                }

                if (lastValid < 0 || i >= values.Length)
                {
                    break;
                }

                var nextValid = i;
                var v0 = values[lastValid].Value;
                var v1 = values[nextValid].Value;
                var span = nextValid - lastValid;
                if (span <= 1)
                {
                    continue;
                }

                for (var k = gapStart; k < nextValid; k++)
                {
                    var t = (k - lastValid) / (double)span;
                    values[k] = ((1.0 - t) * v0) + (t * v1);
                }

                lastValid = nextValid;
            }
        }

        private static List<KernelRow> BuildKernelRows(IReadOnlyList<AngleProfile> profiles, int maxRadiusPx, int scale)
        {
            var rows = new List<KernelRow>(maxRadiusPx + 1);
            var lastKernel = 1.0;

            for (var rPx = 0; rPx <= maxRadiusPx; rPx++)
            {
                var values = new List<double>(profiles.Count);
                foreach (var profile in profiles)
                {
                    if (!profile.IsValid) continue;
                    var value = profile.Values[rPx];
                    if (!value.HasValue) continue;
                    values.Add(Math.Clamp(value.Value, 0.0, 1.0));
                }

                var validCount = values.Count;
                var mean = validCount > 0 ? ComputeMean(values) : 0.0;
                var min = validCount > 0 ? ComputeMin(values) : 0.0;
                var max = validCount > 0 ? ComputeMax(values) : 0.0;
                var stddev = validCount > 0 ? ComputeStddev(values, mean) : 0.0;

                var kernel = validCount > 0 ? Math.Clamp(ComputeMedian(values), 0.0, 1.0) : 0.0;
                if (rPx == 0 && validCount > 0)
                {
                    kernel = 1.0;
                }
                else if (rPx > 0 && kernel > lastKernel)
                {
                    kernel = lastKernel;
                }

                lastKernel = kernel;

                rows.Add(new KernelRow(
                    RadiusPx: rPx,
                    RadiusNorm: rPx / (double)scale,
                    Kernel01: kernel,
                    ValidAngleCount: validCount,
                    Mean01: mean,
                    Min01: min,
                    Max01: max,
                    Stddev01: stddev));
            }

            return rows;
        }

        private static StringBuilder BuildKernelCsv(
            IReadOnlyList<KernelRow> rows,
            double size,
            int scale,
            float pressure,
            float opacity,
            int obsPxX,
            int obsPxY,
            int sampleCanvasPx,
            double angleStepDeg,
            int angleCount,
            IReadOnlyList<string>? extraMetadata = null)
        {
            var sb = new StringBuilder(capacity: Math.Max(32 * 1024, rows.Count * 96));
            sb.Append("# source=InkDrawGen generated dot obs=")
                .Append(obsPxX.ToString(CultureInfo.InvariantCulture))
                .Append('_')
                .Append(obsPxY.ToString(CultureInfo.InvariantCulture))
                .Append(" canvas=")
                .Append(sampleCanvasPx.ToString(CultureInfo.InvariantCulture))
                .Append(" pressure=")
                .Append(pressure.ToString("0.####", CultureInfo.InvariantCulture))
                .Append(" opacity=")
                .Append(opacity.ToString("0.####", CultureInfo.InvariantCulture))
                .AppendLine();
            sb.Append("# size=").Append(size.ToString("0.####", CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("# scale=").Append(scale.ToString(CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("# angle_step_deg=").Append(angleStepDeg.ToString("0.####", CultureInfo.InvariantCulture)).AppendLine();
            sb.Append("# angle_count=").Append(angleCount.ToString(CultureInfo.InvariantCulture)).AppendLine();
            if (extraMetadata != null)
            {
                foreach (var line in extraMetadata)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    sb.AppendLine(line);
                }
            }
            sb.AppendLine("# zero_policy=keep_zero_as_missing, no_interpolation");
            sb.AppendLine("# aggregate=median");
            sb.AppendLine("r_px,r_norm,kernel01,valid_angle_count,mean01,min01,max01,stddev01");

            foreach (var row in rows)
            {
                sb.Append(row.RadiusPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.RadiusNorm.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.Kernel01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.ValidAngleCount.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.Mean01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.Min01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.Max01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.Stddev01.ToString("0.########", CultureInfo.InvariantCulture)).AppendLine();
            }

            return sb;
        }

        private static List<double> BuildAngleList(double angleStepDeg)
        {
            var angles = new List<double>();
            if (double.IsNaN(angleStepDeg) || double.IsInfinity(angleStepDeg) || angleStepDeg <= 0)
            {
                angleStepDeg = 60.0;
            }

            for (var angle = 0.0; angle < 360.0 - 1e-9; angle += angleStepDeg)
            {
                angles.Add(angle);
                if (angles.Count >= 720)
                {
                    break;
                }
            }

            if (angles.Count == 0)
            {
                angles.Add(0.0);
            }

            return angles;
        }

        private static double ComputeMedian(List<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            values.Sort();
            var mid = values.Count / 2;
            if ((values.Count & 1) == 1)
            {
                return values[mid];
            }

            return 0.5 * (values[mid - 1] + values[mid]);
        }

        private static double ComputeMean(List<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            double sum = 0;
            for (var i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }

            return sum / values.Count;
        }

        private static double ComputeMin(List<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            var min = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if (values[i] < min) min = values[i];
            }

            return min;
        }

        private static double ComputeMax(List<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            var max = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if (values[i] > max) max = values[i];
            }

            return max;
        }

        private static double ComputeStddev(List<double> values, double mean)
        {
            if (values == null || values.Count == 0) return 0.0;
            double sumSq = 0;
            for (var i = 0; i < values.Count; i++)
            {
                var d = values[i] - mean;
                sumSq += d * d;
            }

            return Math.Sqrt(sumSq / values.Count);
        }

        private static int ResolveMaxParallelism(int requested)
        {
            var cpuCount = Math.Max(1, Environment.ProcessorCount);
            if (requested <= 0) return cpuCount;
            return Math.Max(1, Math.Min(requested, cpuCount));
        }

        private static async Task<StorageFolder?> PickOutputFolderBestEffortAsync(MainPage page, string outputFolderPath)
        {
            if (!string.IsNullOrWhiteSpace(outputFolderPath))
            {
                try
                {
                    return await StorageFolder.GetFolderFromPathAsync(outputFolderPath);
                }
                catch
                {
                    // フォルダパスが無効な場合はピッカーにフォールバック
                }
            }

            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            };
            picker.FileTypeFilter.Add(".csv");

            return await picker.PickSingleFolderAsync();
        }

        private static async Task ShowDoneDialogAsync(MainPage page, string outPath, double sDip, float pressure, int scale, int obsPxX, int obsPxY, int sampleCanvasPx, double angleStepDeg, int angleCount, int maxRadiusPx, int requestedParallelism, int effectiveParallelism)
        {
            var dlg = new ContentDialog
            {
                Title = "カーネル断面CSV",
                Content = $"完了: CSVを書き出しました。\n\nfile={outPath}\nS={sDip.ToString("0.####", CultureInfo.InvariantCulture)} P={pressure.ToString("0.####", CultureInfo.InvariantCulture)} scale={scale}\nobs=({obsPxX},{obsPxY}) angleStep={angleStepDeg.ToString("0.####", CultureInfo.InvariantCulture)} angleCount={angleCount} maxR={maxRadiusPx} canvas={sampleCanvasPx}px requestedParallel={requestedParallelism} effectiveParallel={effectiveParallelism}",
                CloseButtonText = "OK"
            };
            await dlg.ShowAsync();
        }

        private static string BuildFileName(double sDip, float pressure, float opacity, int scale, int obsPxX, int obsPxY, int sampleCanvasPx, double angleStepDeg, int maxRadiusPx, string? extraTag = null)
        {
            var s = ((int)Math.Round(sDip, MidpointRounding.AwayFromZero)).ToString("D4", CultureInfo.InvariantCulture);
            var p = ((int)Math.Round(pressure * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            var op = ((int)Math.Round(opacity * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            var angle = angleStepDeg.ToString("0.####", CultureInfo.InvariantCulture).Replace('.', '_');
            var extra = string.IsNullOrWhiteSpace(extraTag) ? string.Empty : $"-{extraTag}";
            return $"kernel-sweep-multiangle-S{s}-P{p}-Op{op}-scale{scale}-obs{obsPxX}_{obsPxY}-angle{angle}-maxr{maxRadiusPx}-canvas{sampleCanvasPx}{extra}.csv";
        }

        private static string BuildDebugFileName(double sDip, float pressure, float opacity, int scale, int obsPxX, int obsPxY, int sampleCanvasPx, int radiusPx, double angleDeg, byte centerAlpha)
        {
            var s = ((int)Math.Round(sDip, MidpointRounding.AwayFromZero)).ToString("D4", CultureInfo.InvariantCulture);
            var p = ((int)Math.Round(pressure * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            var op = ((int)Math.Round(opacity * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            var angle = angleDeg.ToString("0.####", CultureInfo.InvariantCulture).Replace('.', '_');
            return $"kernel-sweep-debug-S{s}-P{p}-Op{op}-scale{scale}-obs{obsPxX}_{obsPxY}-r{radiusPx}-angle{angle}-canvas{sampleCanvasPx}-a{centerAlpha:D3}.png";
        }

        private static string BuildRawFileName(double sDip, float pressure, float opacity, int scale, int obsPxX, int obsPxY, int sampleCanvasPx, double angleStepDeg, int maxRadiusPx)
        {
            var s = ((int)Math.Round(sDip, MidpointRounding.AwayFromZero)).ToString("D4", CultureInfo.InvariantCulture);
            var p = ((int)Math.Round(pressure * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            var op = ((int)Math.Round(opacity * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            var angle = angleStepDeg.ToString("0.####", CultureInfo.InvariantCulture).Replace('.', '_');
            return $"kernel-sweep-raw-S{s}-P{p}-Op{op}-scale{scale}-obs{obsPxX}_{obsPxY}-angle{angle}-maxr{maxRadiusPx}-canvas{sampleCanvasPx}.csv";
        }

        private readonly struct AngleProfile
        {
            internal AngleProfile(double angleDeg, double?[] values, bool isValid)
            {
                AngleDeg = angleDeg;
                Values = values;
                IsValid = isValid;
            }

            internal double AngleDeg { get; }
            internal double?[] Values { get; }
            internal bool IsValid { get; }
        }

        private readonly struct KernelRow
        {
            internal KernelRow(int RadiusPx, double RadiusNorm, double Kernel01, int ValidAngleCount, double Mean01, double Min01, double Max01, double Stddev01)
            {
                this.RadiusPx = RadiusPx;
                this.RadiusNorm = RadiusNorm;
                this.Kernel01 = Kernel01;
                this.ValidAngleCount = ValidAngleCount;
                this.Mean01 = Mean01;
                this.Min01 = Min01;
                this.Max01 = Max01;
                this.Stddev01 = Stddev01;
            }

            internal int RadiusPx { get; }
            internal double RadiusNorm { get; }
            internal double Kernel01 { get; }
            internal int ValidAngleCount { get; }
            internal double Mean01 { get; }
            internal double Min01 { get; }
            internal double Max01 { get; }
            internal double Stddev01 { get; }
        }

        private sealed class AlphaImage
        {
            internal AlphaImage(string name, int width, int height, byte[] bgra)
            {
                Name = name;
                Width = width;
                Height = height;
                Bgra = bgra;
            }

            internal string Name { get; }
            internal int Width { get; }
            internal int Height { get; }
            internal byte[] Bgra { get; }
        }

        private readonly struct PixelPoint
        {
            internal PixelPoint(int x, int y, byte alpha)
            {
                X = x;
                Y = y;
                Alpha = alpha;
            }

            internal int X { get; }
            internal int Y { get; }
            internal byte Alpha { get; }
        }

        private static PixelPoint FindRenderedMedianLikePoint(InkDrawGenUiState state, double sDip, float pressure, float opacity, int seedPxX, int seedPxY, int windowPx)
        {
            var scale = Math.Max(1, state.Scale);
            var dpi = (float)Math.Max(1.0, state.Dpi);

            var sampleCanvasPx = Math.Max(1, windowPx);
            if ((sampleCanvasPx % 2) == 0) sampleCanvasPx += 1;
            var local = sampleCanvasPx / 2;
            var obsDip = new Point(seedPxX / (double)scale, seedPxY / (double)scale);
            var roiDip = new Rect(
                x: obsDip.X - (local / (double)scale),
                y: obsDip.Y - (local / (double)scale),
                width: sampleCanvasPx / (double)scale,
                height: sampleCanvasPx / (double)scale);

            var bytes = RenderKernelSampleBytes(sampleCanvasPx, local, roiDip, obsDip, scale, sDip, pressure, opacity, 0, 0.0, dpi, transparent: true);
            var localPoint = FindMedianLikePoint(bytes, sampleCanvasPx, sampleCanvasPx, local, local, sampleCanvasPx, excludeZero: true);
            return new PixelPoint(seedPxX + (localPoint.X - local), seedPxY + (localPoint.Y - local), localPoint.Alpha);
        }

        private static PixelPoint FindMedianLikePoint(byte[] bgra, int width, int height, int centerX, int centerY, int windowPx, bool excludeZero)
        {
            var half = Math.Max(0, windowPx / 2);
            var x0 = Math.Max(0, centerX - half);
            var x1 = Math.Min(width - 1, centerX + half);
            var y0 = Math.Max(0, centerY - half);
            var y1 = Math.Min(height - 1, centerY + half);

            var values = new List<double>((x1 - x0 + 1) * (y1 - y0 + 1));
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var alpha = bgra[((y * width) + x) * 4 + 3];
                    if (excludeZero && alpha == 0)
                    {
                        continue;
                    }

                    values.Add(alpha);
                }
            }

            if (values.Count == 0)
            {
                var fallbackX = Math.Clamp(centerX, 0, Math.Max(0, width - 1));
                var fallbackY = Math.Clamp(centerY, 0, Math.Max(0, height - 1));
                var fallbackAlpha = bgra[((fallbackY * width) + fallbackX) * 4 + 3];
                return new PixelPoint(fallbackX, fallbackY, fallbackAlpha);
            }

            var median = ComputeMedian(values);
            var best = new PixelPoint(Math.Clamp(centerX, 0, Math.Max(0, width - 1)), Math.Clamp(centerY, 0, Math.Max(0, height - 1)), 0);
            double bestDiff = double.MaxValue;
            var bestDist2 = long.MaxValue;

            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var alpha = bgra[((y * width) + x) * 4 + 3];
                    if (excludeZero && alpha == 0)
                    {
                        continue;
                    }

                    var diff = Math.Abs(alpha - median);
                    var dx = x - centerX;
                    var dy = y - centerY;
                    var dist2 = (long)dx * dx + (long)dy * dy;
                    if (diff < bestDiff - 1e-9
                        || (Math.Abs(diff - bestDiff) <= 1e-9 && dist2 < bestDist2)
                        || (Math.Abs(diff - bestDiff) <= 1e-9 && dist2 == bestDist2 && (x < best.X || (x == best.X && y < best.Y))))
                    {
                        best = new PixelPoint(x, y, alpha);
                        bestDiff = diff;
                        bestDist2 = dist2;
                    }
                }
            }

            return best;
        }

        private static async Task<AlphaImage> LoadAlphaImageAsync(StorageFile file)
        {
            using (var stream = await file.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                var bytes = pixelData.DetachPixelData();
                return new AlphaImage(file.Name, (int)decoder.PixelWidth, (int)decoder.PixelHeight, bytes);
            }
        }

        private static async Task<WideKernelSource> LoadWideKernelSourceAsync(StorageFile file)
        {
            var text = await FileIO.ReadTextAsync(file);
            var lines = SplitLines(text);
            var header = string.Empty;
            var metricNames = new List<string>();
            var rows = new List<WideKernelDataRow>();

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                header = line;
                break;
            }

            if (string.IsNullOrWhiteSpace(header))
            {
                throw new InvalidOperationException($"CSVヘッダーが読み取れません: {file.Name}");
            }

            var columns = header.Split(',');
            if (columns.Length < 3 || !string.Equals(columns[0], "r_px", StringComparison.OrdinalIgnoreCase) || !string.Equals(columns[1], "r_norm", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"kernel CSV形式ではありません: {file.Name}");
            }

            for (var i = 2; i < columns.Length; i++)
            {
                metricNames.Add(columns[i].Trim());
            }

            var headerPassed = false;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (!headerPassed)
                {
                    headerPassed = true;
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length < columns.Length) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var radiusPx)) continue;

                double? radiusNorm = null;
                if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var radiusNormValue))
                {
                    radiusNorm = radiusNormValue;
                }

                var metrics = new Dictionary<string, string>(metricNames.Count, StringComparer.OrdinalIgnoreCase);
                for (var metricIndex = 0; metricIndex < metricNames.Count; metricIndex++)
                {
                    metrics[metricNames[metricIndex]] = parts[metricIndex + 2].Trim();
                }

                rows.Add(new WideKernelDataRow(radiusPx, radiusNorm, metrics));
            }

            var pressureSuffix = BuildPressureHeaderSuffix(file.Name);
            var pressureSortKey = ParsePressureSortKey(file.Name);
            return new WideKernelSource(file.Name, pressureSuffix, pressureSortKey, metricNames, rows);
        }

        private static async Task<WideKernelAnalysisSource> LoadWideKernelAnalysisSourceAsync(StorageFile file)
        {
            var text = await FileIO.ReadTextAsync(file);
            var lines = SplitLines(text);
            string[] headerColumns = null;

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                headerColumns = line.Split(',');
                break;
            }

            if (headerColumns == null || headerColumns.Length < 3)
            {
                throw new InvalidOperationException($"wide CSVヘッダーが読み取れません: {file.Name}");
            }

            if (!string.Equals(headerColumns[0], "r_px", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(headerColumns[1], "r_norm", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"wide CSV形式ではありません: {file.Name}");
            }

            var pressures = new List<WideKernelPressureColumn>();
            for (var i = 2; i < headerColumns.Length; i++)
            {
                var column = headerColumns[i].Trim();
                if (!column.StartsWith("kernel01_p", StringComparison.OrdinalIgnoreCase)) continue;
                var suffix = column.Substring("kernel01_p".Length);
                if (!TryParsePressureSuffixAsDecimal(suffix, out var pressureValue))
                {
                    throw new InvalidOperationException($"P値をヘッダーから解釈できません: {column}");
                }

                pressures.Add(new WideKernelPressureColumn(suffix, pressureValue, i));
            }

            if (pressures.Count == 0)
            {
                throw new InvalidOperationException($"wide CSVに kernel01_p* 列がありません: {file.Name}");
            }

            pressures.Sort((a, b) => a.PressureValue.CompareTo(b.PressureValue));

            var rows = new List<WideKernelAnalysisRow>(lines.Count);
            var headerPassed = false;
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (!headerPassed)
                {
                    headerPassed = true;
                    continue;
                }

                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var radiusPx)) continue;

                double? radiusNorm = null;
                if (double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var radiusNormValue))
                {
                    radiusNorm = radiusNormValue;
                }

                var values = new Dictionary<string, decimal?>(pressures.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var pressure in pressures)
                {
                    decimal? kernel01 = null;
                    if (pressure.ColumnIndex < parts.Length && decimal.TryParse(parts[pressure.ColumnIndex], NumberStyles.Float, CultureInfo.InvariantCulture, out var kernel01Value))
                    {
                        kernel01 = kernel01Value;
                    }
                    values[pressure.HeaderSuffix] = kernel01;
                }

                rows.Add(new WideKernelAnalysisRow(radiusPx, radiusNorm, values));
            }

            rows.Sort((a, b) => a.RadiusPx.CompareTo(b.RadiusPx));
            return new WideKernelAnalysisSource(file.Name, pressures, rows);
        }

        private static List<KernelPlateau> BuildKernelPlateaus(IReadOnlyList<WideKernelAnalysisRow> rows, string pressureSuffix, decimal pressureValue)
        {
            var effectiveRows = new List<KernelEffectiveRow>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                if (!rows[i].Kernel01ByPressure.TryGetValue(pressureSuffix, out var kernel01) || !kernel01.HasValue)
                {
                    continue;
                }

                var levelEff01 = Decimal.Round(kernel01.Value * pressureValue, 10, MidpointRounding.AwayFromZero);
                effectiveRows.Add(new KernelEffectiveRow(rows[i].RadiusPx, rows[i].RadiusNorm, levelEff01));
            }

            var lastNonZeroIndex = -1;
            for (var i = 0; i < effectiveRows.Count; i++)
            {
                if (effectiveRows[i].LevelEff01 > 0)
                {
                    lastNonZeroIndex = i;
                }
            }

            if (lastNonZeroIndex < 0)
            {
                return new List<KernelPlateau>();
            }

            var plateaus = new List<KernelPlateau>();
            var plateauIndex = 1;
            var start = effectiveRows[0];
            var currentLevel = effectiveRows[0].LevelEff01;
            var prevRadiusPx = effectiveRows[0].RadiusPx;

            for (var i = 1; i <= lastNonZeroIndex; i++)
            {
                var current = effectiveRows[i];
                var contiguous = current.RadiusPx == (prevRadiusPx + 1);
                if (contiguous && current.LevelEff01 == currentLevel)
                {
                    prevRadiusPx = current.RadiusPx;
                    continue;
                }

                var end = effectiveRows[i - 1];
                plateaus.Add(new KernelPlateau(
                    plateauIndex++,
                    start.RadiusPx,
                    end.RadiusPx,
                    start.RadiusNorm,
                    end.RadiusNorm,
                    end.RadiusPx - start.RadiusPx + 1,
                    currentLevel));

                start = current;
                currentLevel = current.LevelEff01;
                prevRadiusPx = current.RadiusPx;
            }

            var finalEnd = effectiveRows[lastNonZeroIndex];
            plateaus.Add(new KernelPlateau(
                plateauIndex,
                start.RadiusPx,
                finalEnd.RadiusPx,
                start.RadiusNorm,
                finalEnd.RadiusNorm,
                finalEnd.RadiusPx - start.RadiusPx + 1,
                currentLevel));

            return plateaus;
        }

        private static KernelObservedMetrics BuildKernelObservedMetrics(
            string pressureHeaderSuffix,
            decimal pressureValue,
            IReadOnlyList<KernelPlateau> plateaus,
            int maxObservedRadiusPx,
            double? radiusNormScale,
            KernelStairSummaryRow? summaryRow)
        {
            var pressure = (double)pressureValue;
            var first = plateaus[0];
            var last = plateaus[plateaus.Count - 1];

            var risers = new List<double>(Math.Max(0, plateaus.Count - 1));
            for (var i = 0; i + 1 < plateaus.Count; i++)
            {
                risers.Add((double)(plateaus[i].LevelEff01 - plateaus[i + 1].LevelEff01));
            }

            var observedRiser = risers.Count > 0 ? ComputeMean(risers) : 0.0;
            var frontFit23 = FitPlateauLine(plateaus, 2, 3);
            var frontFit24 = FitPlateauLine(plateaus, 2, 4);
            var majorChange = FindMajorChange(plateaus, pressure);
            var jointCount = summaryRow.HasValue ? summaryRow.Value.PlateauCount : plateaus.Count;
            var jointStep = pressure > 0 ? observedRiser / pressure : 0.0;
            var localSlopeVector = BuildLocalSlopeVector(plateaus, pressure);
            var treadValues = new List<double>(plateaus.Count);
            for (var i = 0; i < plateaus.Count; i++)
            {
                treadValues.Add(plateaus[i].TreadPx);
            }

            var jointSpan = summaryRow.HasValue ? summaryRow.Value.MedianTreadPx : ComputeMedian(treadValues);
            var maxTreadPx = summaryRow.HasValue ? summaryRow.Value.MaxTreadPx : ComputeMax(treadValues);
            double? rootLock = null;
            double? rootLockAlt = null;
            if (jointSpan > 0)
            {
                rootLock = first.TreadPx / jointSpan;
                rootLockAlt = maxTreadPx / jointSpan;
            }

            var tailSegment = FindTailStableSegment(plateaus);
            var tailFit = FitPlateauLine(plateaus, tailSegment.StartPlateauIndex, tailSegment.EndPlateauIndex);
            var estimatedTerminalPx = tailFit.IsValid && Math.Abs(tailFit.Slope) > 1e-12
                ? -tailFit.Intercept / tailFit.Slope
                : (double?)null;

            double? estimatedTerminalNorm = null;
            if (estimatedTerminalPx.HasValue && radiusNormScale.HasValue)
            {
                estimatedTerminalNorm = estimatedTerminalPx.Value * radiusNormScale.Value;
            }

            var observedTerminalNorm = summaryRow.HasValue && summaryRow.Value.LastNonZeroRadiusNorm.HasValue
                ? summaryRow.Value.LastNonZeroRadiusNorm
                : last.EndRadiusNorm;
            var observedTerminalPx = summaryRow.HasValue
                ? summaryRow.Value.LastNonZeroRadiusPx
                : last.EndRadiusPx;
            double? terminalHeadroom = null;
            if (observedTerminalNorm.HasValue)
            {
                terminalHeadroom = 100.0 - observedTerminalNorm.Value;
            }

            double? curvatureBudget = null;
            if (majorChange.RelativeDropToPrev01.HasValue && jointStep > 1e-12)
            {
                curvatureBudget = majorChange.RelativeDropToPrev01.Value / jointStep;
            }

            double? tailMedianTread = null;
            if (tailSegment.StartPlateauIndex > 0 && tailSegment.EndPlateauIndex >= tailSegment.StartPlateauIndex)
            {
                var tailTreads = new List<double>();
                for (var i = 0; i < plateaus.Count; i++)
                {
                    var plateauIndex = plateaus[i].PlateauIndex;
                    if (plateauIndex < tailSegment.StartPlateauIndex || plateauIndex > tailSegment.EndPlateauIndex) continue;
                    tailTreads.Add(plateaus[i].TreadPx);
                }

                if (tailTreads.Count > 0)
                {
                    tailMedianTread = ComputeMedian(tailTreads);
                }
            }

            return new KernelObservedMetrics(
                pressureHeaderSuffix,
                pressure,
                observedRiser,
                jointCount,
                jointStep,
                localSlopeVector,
                jointSpan,
                rootLock,
                rootLockAlt,
                terminalHeadroom,
                curvatureBudget,
                frontFit23,
                frontFit24,
                first.EndRadiusPx,
                first.EndRadiusNorm,
                majorChange,
                tailFit,
                tailMedianTread,
                observedTerminalPx,
                observedTerminalNorm,
                observedTerminalPx >= maxObservedRadiusPx,
                estimatedTerminalPx,
                estimatedTerminalNorm);
        }

        private static KernelPredictionComparisonRow BuildKernelPredictionComparisonRow(
            KernelObservedMetrics current,
            IReadOnlyList<KernelObservedMetrics> anchors,
            int maxObservedRadiusPx,
            double? radiusNormScale)
        {
            var predictedRiser = InterpolateByPressure(current.PressureValue, anchors, row => row.ObservedRiser01);
            var predictedJointCountRaw = InterpolateByPressure(current.PressureValue, anchors, row => row.JointCount);
            var predictedJointCount = predictedJointCountRaw.HasValue
                ? (int?)Math.Max(0, (int)Math.Round(predictedJointCountRaw.Value, MidpointRounding.AwayFromZero))
                : null;
            var predictedJointStep = InterpolateByPressure(current.PressureValue, anchors, row => row.JointStep);
            var predictedLocalSlopeVector = BuildPredictedLocalSlopeVector(current.PressureValue, anchors);
            var predictedJointSpan = InterpolateByPressure(current.PressureValue, anchors, row => row.JointSpan);
            var predictedRootLock = InterpolateByPressure(current.PressureValue, anchors, row => row.RootLock);
            var predictedRootLockAlt = InterpolateByPressure(current.PressureValue, anchors, row => row.RootLockAlt);
            var predictedTerminalHeadroom = InterpolateByPressure(current.PressureValue, anchors, row => row.TerminalHeadroom);
            var predictedFrontSlope23 = InterpolateByPressure(current.PressureValue, anchors, row => row.FrontFit23.IsValid ? row.FrontFit23.Slope : (double?)null);
            var predictedFrontSlope = InterpolateByPressure(current.PressureValue, anchors, row => row.FrontFit24.IsValid ? row.FrontFit24.Slope : (double?)null);
            var predictedFrontIntercept = InterpolateByPressure(current.PressureValue, anchors, row => row.FrontFit24.IsValid ? row.FrontFit24.Intercept : (double?)null);
            var predictedMajorRelativeDrop = InterpolateByPressure(current.PressureValue, anchors, row => row.MajorChange.RelativeDropToPrev01);
            var predictedTailSlope = InterpolateByPressure(current.PressureValue, anchors, row => row.TailFit.IsValid ? row.TailFit.Slope : (double?)null);
            var predictedEstimatedTerminalPx = InterpolateByPressure(current.PressureValue, anchors, row => row.EstimatedTrueTerminalPx);
            double? predictedCurvatureBudget = null;
            if (predictedMajorRelativeDrop.HasValue && predictedJointStep.HasValue && predictedJointStep.Value > 1e-12)
            {
                predictedCurvatureBudget = predictedMajorRelativeDrop.Value / predictedJointStep.Value;
            }

            double? predictedInitialEndPx = null;
            double? predictedInitialEndNorm = null;
            if (predictedRiser.HasValue && predictedFrontSlope.HasValue && predictedFrontIntercept.HasValue && Math.Abs(predictedFrontSlope.Value) > 1e-12)
            {
                predictedInitialEndPx = ((current.PressureValue - predictedRiser.Value) - predictedFrontIntercept.Value) / predictedFrontSlope.Value;
                if (radiusNormScale.HasValue)
                {
                    predictedInitialEndNorm = predictedInitialEndPx.Value * radiusNormScale.Value;
                }
            }

            int? predictedMajorIndex = null;
            double? predictedMajorStartPx = null;
            double? predictedMajorStartNorm = null;
            if (predictedMajorRelativeDrop.HasValue && predictedRiser.HasValue && predictedRiser.Value > 0)
            {
                var prevSteps = (int)Math.Round((predictedMajorRelativeDrop.Value * current.PressureValue) / predictedRiser.Value, MidpointRounding.AwayFromZero);
                if (prevSteps < 1) prevSteps = 1;
                predictedMajorIndex = prevSteps + 2;

                if (predictedFrontSlope.HasValue && predictedFrontIntercept.HasValue && Math.Abs(predictedFrontSlope.Value) > 1e-12)
                {
                    var targetLevel = current.PressureValue - ((predictedMajorIndex.Value - 1) * predictedRiser.Value);
                    predictedMajorStartPx = (targetLevel - predictedFrontIntercept.Value) / predictedFrontSlope.Value;
                    if (radiusNormScale.HasValue)
                    {
                        predictedMajorStartNorm = predictedMajorStartPx.Value * radiusNormScale.Value;
                    }
                }
            }

            double? predictedEstimatedTerminalNorm = null;
            if (predictedEstimatedTerminalPx.HasValue && radiusNormScale.HasValue)
            {
                predictedEstimatedTerminalNorm = predictedEstimatedTerminalPx.Value * radiusNormScale.Value;
            }

            double? predictedObservedTerminalPx = null;
            double? predictedObservedTerminalNorm = null;
            bool? predictedCensored = null;
            if (predictedEstimatedTerminalPx.HasValue)
            {
                predictedCensored = predictedEstimatedTerminalPx.Value > maxObservedRadiusPx;
                predictedObservedTerminalPx = predictedCensored.Value ? maxObservedRadiusPx : predictedEstimatedTerminalPx.Value;
                if (radiusNormScale.HasValue)
                {
                    predictedObservedTerminalNorm = predictedObservedTerminalPx.Value * radiusNormScale.Value;
                }
            }

            return new KernelPredictionComparisonRow(
                current,
                predictedRiser,
                predictedJointCount,
                predictedJointStep,
                predictedLocalSlopeVector,
                predictedJointSpan,
                predictedRootLock,
                predictedRootLockAlt,
                predictedTerminalHeadroom,
                predictedCurvatureBudget,
                predictedFrontSlope23,
                predictedFrontSlope,
                predictedFrontIntercept,
                predictedInitialEndPx,
                predictedInitialEndNorm,
                predictedMajorRelativeDrop,
                predictedMajorIndex,
                predictedMajorStartPx,
                predictedMajorStartNorm,
                predictedTailSlope,
                predictedEstimatedTerminalPx,
                predictedEstimatedTerminalNorm,
                predictedObservedTerminalPx,
                predictedObservedTerminalNorm,
                predictedCensored);
        }

        private static List<KernelPredictionComparisonRow> BuildKernelPredictionComparisonRows(
            IReadOnlyList<KernelObservedMetrics> observedRows)
        {
            var rows = new List<KernelPredictionComparisonRow>(observedRows.Count);
            var maxObservedRadiusPx = 0;
            for (var i = 0; i < observedRows.Count; i++)
            {
                if (observedRows[i].ObservedTerminalRadiusPx > maxObservedRadiusPx)
                {
                    maxObservedRadiusPx = observedRows[i].ObservedTerminalRadiusPx;
                }
            }

            var radiusNormScale = ResolveRadiusNormScale(observedRows);
            for (var i = 0; i < observedRows.Count; i++)
            {
                var anchors = new List<KernelObservedMetrics>(Math.Max(0, observedRows.Count - 1));
                for (var j = 0; j < observedRows.Count; j++)
                {
                    if (i == j) continue;
                    anchors.Add(observedRows[j]);
                }

                rows.Add(BuildKernelPredictionComparisonRow(observedRows[i], anchors, maxObservedRadiusPx, radiusNormScale));
            }

            return rows;
        }

        private static string BuildKernelPredictionComparisonCsv(string inputFileName, IReadOnlyList<KernelPredictionComparisonRow> rows)
        {
            var sb = new StringBuilder(capacity: Math.Max(16 * 1024, rows.Count * 256));
            sb.Append("# source=kernel stair prediction compare").AppendLine();
            sb.Append("# input_file=").Append(inputFileName).AppendLine();
            sb.Append("p_header,p_value,obs_riser01,pred_riser01,err_riser01,obs_front_slope23,pred_front_slope23,err_front_slope23,obs_front_slope24,pred_front_slope24,err_front_slope24,obs_front_intercept24,pred_front_intercept24,err_front_intercept24,obs_initial_end_r_px,pred_initial_end_r_px,err_initial_end_r_px,obs_initial_end_r_norm,pred_initial_end_r_norm,err_initial_end_r_norm,obs_major_change_index,pred_major_change_index,err_major_change_index,obs_major_change_r_px,pred_major_change_r_px,err_major_change_r_px,obs_major_change_r_norm,pred_major_change_r_norm,err_major_change_r_norm,obs_major_relative_drop,pred_major_relative_drop,err_major_relative_drop,obs_tail_slope,pred_tail_slope,err_tail_slope,obs_estimated_terminal_r_px,pred_estimated_terminal_r_px,err_estimated_terminal_r_px,obs_estimated_terminal_r_norm,pred_estimated_terminal_r_norm,err_estimated_terminal_r_norm,obs_observed_terminal_r_px,pred_observed_terminal_r_px,err_observed_terminal_r_px,obs_observed_terminal_r_norm,pred_observed_terminal_r_norm,err_observed_terminal_r_norm,obs_censored_terminal,pred_censored_terminal,obs_joint_count,pred_joint_count,err_joint_count,obs_joint_step,pred_joint_step,err_joint_step");
            AppendLocalSlopeVectorHeader(sb);
            sb.Append(",obs_joint_span,pred_joint_span,err_joint_span,obs_root_lock,pred_root_lock,err_root_lock,obs_root_lock_alt,pred_root_lock_alt,err_root_lock_alt,obs_terminal_headroom,pred_terminal_headroom,err_terminal_headroom,obs_curvature_budget,pred_curvature_budget,err_curvature_budget").AppendLine();

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                sb.AppendLine();
                sb.Append(row.Observed.PressureHeaderSuffix).Append(',');
                sb.Append(row.Observed.PressureValue.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.Observed.ObservedRiser01.ToString("0.##########", CultureInfo.InvariantCulture)).Append(',');
                AppendNullableDouble(sb, row.PredictedRiser01, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.ObservedRiser01, row.PredictedRiser01), "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.FrontFit23.IsValid ? row.Observed.FrontFit23.Slope : (double?)null, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedFrontSlope23, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.FrontFit23.IsValid ? row.Observed.FrontFit23.Slope : (double?)null, row.PredictedFrontSlope23), "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.FrontFit24.IsValid ? row.Observed.FrontFit24.Slope : (double?)null, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedFrontSlope24, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.FrontFit24.IsValid ? row.Observed.FrontFit24.Slope : (double?)null, row.PredictedFrontSlope24), "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.FrontFit24.IsValid ? row.Observed.FrontFit24.Intercept : (double?)null, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedFrontIntercept24, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.FrontFit24.IsValid ? row.Observed.FrontFit24.Intercept : (double?)null, row.PredictedFrontIntercept24), "0.##########"); sb.Append(',');
                sb.Append(row.Observed.InitialEndRadiusPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                AppendNullableDouble(sb, row.PredictedInitialEndRadiusPx, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.InitialEndRadiusPx, row.PredictedInitialEndRadiusPx), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.InitialEndRadiusNorm, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedInitialEndRadiusNorm, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.InitialEndRadiusNorm, row.PredictedInitialEndRadiusNorm), "0.########"); sb.Append(',');
                sb.Append(row.Observed.MajorChange.PlateauIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
                AppendNullableInt(sb, row.PredictedMajorChangeIndex); sb.Append(',');
                AppendNullableDouble(sb, ComputeError((double)row.Observed.MajorChange.PlateauIndex, row.PredictedMajorChangeIndex.HasValue ? (double?)row.PredictedMajorChangeIndex.Value : null), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.MajorChange.StartRadiusPx, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedMajorChangeRadiusPx, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.MajorChange.StartRadiusPx, row.PredictedMajorChangeRadiusPx), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.MajorChange.StartRadiusNorm, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedMajorChangeRadiusNorm, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.MajorChange.StartRadiusNorm, row.PredictedMajorChangeRadiusNorm), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.MajorChange.RelativeDropToPrev01, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedMajorRelativeDrop01, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.MajorChange.RelativeDropToPrev01, row.PredictedMajorRelativeDrop01), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.TailFit.IsValid ? row.Observed.TailFit.Slope : (double?)null, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedTailSlope, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.TailFit.IsValid ? row.Observed.TailFit.Slope : (double?)null, row.PredictedTailSlope), "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.EstimatedTrueTerminalPx, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedEstimatedTrueTerminalPx, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.EstimatedTrueTerminalPx, row.PredictedEstimatedTrueTerminalPx), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.EstimatedTrueTerminalNorm, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedEstimatedTrueTerminalNorm, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.EstimatedTrueTerminalNorm, row.PredictedEstimatedTrueTerminalNorm), "0.########"); sb.Append(',');
                sb.Append(row.Observed.ObservedTerminalRadiusPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                AppendNullableDouble(sb, row.PredictedObservedTerminalPx, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.ObservedTerminalRadiusPx, row.PredictedObservedTerminalPx), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.ObservedTerminalRadiusNorm, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedObservedTerminalNorm, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.ObservedTerminalRadiusNorm, row.PredictedObservedTerminalNorm), "0.########"); sb.Append(',');
                sb.Append(row.Observed.CensoredTerminal ? "true" : "false").Append(',');
                if (row.PredictedCensoredTerminal.HasValue)
                {
                    sb.Append(row.PredictedCensoredTerminal.Value ? "true" : "false");
                }

                sb.Append(',');
                sb.Append(row.Observed.JointCount.ToString(CultureInfo.InvariantCulture)).Append(',');
                AppendNullableInt(sb, row.PredictedJointCount); sb.Append(',');
                AppendNullableDouble(sb, ComputeError((double)row.Observed.JointCount, row.PredictedJointCount.HasValue ? (double?)row.PredictedJointCount.Value : null), "0.########"); sb.Append(',');
                sb.Append(row.Observed.JointStep.ToString("0.##########", CultureInfo.InvariantCulture)).Append(',');
                AppendNullableDouble(sb, row.PredictedJointStep, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.JointStep, row.PredictedJointStep), "0.##########"); sb.Append(',');
                AppendLocalSlopeVectorValues(sb, row.Observed.LocalSlopeVector, row.PredictedLocalSlopeVector);
                sb.Append(row.Observed.JointSpan.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                AppendNullableDouble(sb, row.PredictedJointSpan, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.JointSpan, row.PredictedJointSpan), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.RootLock, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedRootLock, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.RootLock, row.PredictedRootLock), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.RootLockAlt, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedRootLockAlt, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.RootLockAlt, row.PredictedRootLockAlt), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.TerminalHeadroom, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedTerminalHeadroom, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.TerminalHeadroom, row.PredictedTerminalHeadroom), "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Observed.CurvatureBudget, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.PredictedCurvatureBudget, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(row.Observed.CurvatureBudget, row.PredictedCurvatureBudget), "0.########");
            }

            return sb.ToString();
        }

        private static LinearFitResult FitPlateauLine(IReadOnlyList<KernelPlateau> plateaus, int startPlateauIndex, int endPlateauIndex)
        {
            var points = new List<KernelLinePoint>();
            for (var i = 0; i < plateaus.Count; i++)
            {
                var plateauIndex = plateaus[i].PlateauIndex;
                if (plateauIndex < startPlateauIndex || plateauIndex > endPlateauIndex) continue;
                points.Add(new KernelLinePoint(plateaus[i].StartRadiusPx, (double)plateaus[i].LevelEff01));
            }

            return FitLine(points);
        }

        private static LinearFitResult FitLine(IReadOnlyList<KernelLinePoint> points)
        {
            if (points == null || points.Count < 2)
            {
                return LinearFitResult.Invalid;
            }

            double sx = 0;
            double sy = 0;
            double sxx = 0;
            double sxy = 0;
            for (var i = 0; i < points.Count; i++)
            {
                sx += points[i].X;
                sy += points[i].Y;
                sxx += points[i].X * points[i].X;
                sxy += points[i].X * points[i].Y;
            }

            var n = points.Count;
            var denom = (n * sxx) - (sx * sx);
            if (Math.Abs(denom) <= 1e-12)
            {
                return LinearFitResult.Invalid;
            }

            var slope = ((n * sxy) - (sx * sy)) / denom;
            var intercept = (sy - (slope * sx)) / n;
            var meanY = sy / n;
            double ssTot = 0;
            double ssRes = 0;
            for (var i = 0; i < points.Count; i++)
            {
                var predicted = intercept + (slope * points[i].X);
                var dy = points[i].Y - meanY;
                ssTot += dy * dy;
                var residual = points[i].Y - predicted;
                ssRes += residual * residual;
            }

            var r2 = ssTot <= 1e-12 ? 1.0 : 1.0 - (ssRes / ssTot);
            return new LinearFitResult(true, slope, intercept, r2, points.Count);
        }

        private static KernelMajorChangeMetrics FindMajorChange(IReadOnlyList<KernelPlateau> plateaus, double pressure)
        {
            var bestIndex = -1;
            double? bestLog2 = null;
            for (var i = 1; i + 1 < plateaus.Count; i++)
            {
                var prevTread = plateaus[i - 1].TreadPx;
                var currentTread = plateaus[i].TreadPx;
                if (prevTread <= 0 || currentTread <= 0) continue;

                var log2Ratio = Math.Log(currentTread / (double)prevTread, 2.0);
                if (!bestLog2.HasValue || log2Ratio < bestLog2.Value)
                {
                    bestLog2 = log2Ratio;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                return new KernelMajorChangeMetrics(0, null, null, null, null, null);
            }

            var current = plateaus[bestIndex];
            var previous = bestIndex > 0 ? plateaus[bestIndex - 1] : current;
            var dropToMajor = pressure > 0 ? (pressure - (double)current.LevelEff01) / pressure : (double?)null;
            var dropToPrev = pressure > 0 ? (pressure - (double)previous.LevelEff01) / pressure : (double?)null;
            return new KernelMajorChangeMetrics(
                current.PlateauIndex,
                current.StartRadiusPx,
                current.StartRadiusNorm,
                dropToMajor,
                dropToPrev,
                bestLog2);
        }

        private static KernelTailSegment FindTailStableSegment(IReadOnlyList<KernelPlateau> plateaus)
        {
            if (plateaus.Count < 2)
            {
                return new KernelTailSegment(0, 0);
            }

            var candidateIndexes = new List<int>();
            for (var i = 1; i < plateaus.Count; i++)
            {
                var prevTread = plateaus[i - 1].TreadPx;
                var currentTread = plateaus[i].TreadPx;
                if (prevTread <= 0 || currentTread <= 0) continue;
                var log2Ratio = Math.Log(currentTread / (double)prevTread, 2.0);
                if (Math.Abs(log2Ratio) <= 0.07)
                {
                    candidateIndexes.Add(i);
                }
            }

            if (candidateIndexes.Count == 0)
            {
                return new KernelTailSegment(0, 0);
            }

            var tailEnd = candidateIndexes[candidateIndexes.Count - 1];
            var tailStart = tailEnd;
            for (var i = candidateIndexes.Count - 2; i >= 0; i--)
            {
                if (candidateIndexes[i] != (tailStart - 1)) break;
                tailStart = candidateIndexes[i];
            }

            return new KernelTailSegment(plateaus[tailStart].PlateauIndex, plateaus[tailEnd].PlateauIndex);
        }

        private static double? ResolveRadiusNormScale(IReadOnlyList<WideKernelAnalysisRow> rows)
        {
            if (rows == null) return null;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].RadiusPx <= 0) continue;
                if (!rows[i].RadiusNorm.HasValue) continue;
                return rows[i].RadiusNorm.Value / rows[i].RadiusPx;
            }

            return null;
        }

        private static double? ResolveRadiusNormScale(IReadOnlyList<KernelObservedMetrics> rows)
        {
            if (rows == null) return null;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].ObservedTerminalRadiusPx > 0 && rows[i].ObservedTerminalRadiusNorm.HasValue)
                {
                    return rows[i].ObservedTerminalRadiusNorm.Value / rows[i].ObservedTerminalRadiusPx;
                }

                if (rows[i].InitialEndRadiusPx > 0 && rows[i].InitialEndRadiusNorm.HasValue)
                {
                    return rows[i].InitialEndRadiusNorm.Value / rows[i].InitialEndRadiusPx;
                }
            }

            return null;
        }

        private static double? ResolveRadiusNormScale(IReadOnlyList<KernelStairDetailRow> rows)
        {
            if (rows == null) return null;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].StartRadiusPx > 0 && rows[i].StartRadiusNorm.HasValue)
                {
                    return rows[i].StartRadiusNorm.Value / rows[i].StartRadiusPx;
                }

                if (rows[i].EndRadiusPx > 0 && rows[i].EndRadiusNorm.HasValue)
                {
                    return rows[i].EndRadiusNorm.Value / rows[i].EndRadiusPx;
                }
            }

            return null;
        }

        private static double? InterpolateByPressure(double targetPressure, IReadOnlyList<KernelObservedMetrics> rows, Func<KernelObservedMetrics, double?> selector)
        {
            var points = new List<KernelMetricPoint>();
            for (var i = 0; i < rows.Count; i++)
            {
                var value = selector(rows[i]);
                if (!value.HasValue) continue;
                points.Add(new KernelMetricPoint(rows[i].PressureValue, value.Value));
            }

            if (points.Count == 0) return null;
            points.Sort((a, b) => a.Pressure.CompareTo(b.Pressure));
            if (points.Count == 1) return points[0].Value;

            if (targetPressure <= points[0].Pressure)
            {
                return InterpolateLinear(points[0], points[1], targetPressure);
            }

            if (targetPressure >= points[points.Count - 1].Pressure)
            {
                return InterpolateLinear(points[points.Count - 2], points[points.Count - 1], targetPressure);
            }

            for (var i = 0; i + 1 < points.Count; i++)
            {
                if (targetPressure < points[i].Pressure || targetPressure > points[i + 1].Pressure) continue;
                return InterpolateLinear(points[i], points[i + 1], targetPressure);
            }

            return points[points.Count - 1].Value;
        }

        private static List<double?> BuildPredictedLocalSlopeVector(double targetPressure, IReadOnlyList<KernelObservedMetrics> rows)
        {
            var values = new List<double?>(LocalSlopeVectorAnchors.Length);
            for (var i = 0; i < LocalSlopeVectorAnchors.Length; i++)
            {
                var anchorIndex = i;
                values.Add(InterpolateByPressure(targetPressure, rows, row =>
                {
                    if (row.LocalSlopeVector == null || anchorIndex >= row.LocalSlopeVector.Count)
                    {
                        return null;
                    }

                    return row.LocalSlopeVector[anchorIndex];
                }));
            }

            return values;
        }

        private static List<double?> BuildLocalSlopeVector(IReadOnlyList<KernelPlateau> plateaus, double pressure)
        {
            var points = new List<KernelAxisPoint>();
            if (plateaus != null && pressure > 1e-12)
            {
                for (var i = 1; i + 1 < plateaus.Count; i++)
                {
                    var current = plateaus[i];
                    var next = plateaus[i + 1];
                    var dx = next.StartRadiusPx - current.StartRadiusPx;
                    if (dx <= 0)
                    {
                        continue;
                    }

                    var currentLevel = (double)current.LevelEff01;
                    var nextLevel = (double)next.LevelEff01;
                    var slope = (nextLevel - currentLevel) / dx;
                    var midLevel = 0.5 * (currentLevel + nextLevel);
                    var u = (pressure - midLevel) / pressure;
                    points.Add(new KernelAxisPoint(u, slope));
                }
            }

            return SampleAxisValues(LocalSlopeVectorAnchors, points);
        }

        private static List<double?> SampleAxisValues(IReadOnlyList<double> anchors, IReadOnlyList<KernelAxisPoint> points)
        {
            var values = new List<double?>(anchors.Count);
            if (anchors == null || anchors.Count == 0)
            {
                return values;
            }

            if (points == null || points.Count == 0)
            {
                for (var i = 0; i < anchors.Count; i++) values.Add(null);
                return values;
            }

            var sorted = new List<KernelAxisPoint>(points.Count);
            for (var i = 0; i < points.Count; i++)
            {
                sorted.Add(points[i]);
            }

            sorted.Sort((a, b) => a.X.CompareTo(b.X));
            for (var i = 0; i < anchors.Count; i++)
            {
                values.Add(InterpolateByAxis(anchors[i], sorted));
            }

            return values;
        }

        private static double? InterpolateByAxis(double targetX, IReadOnlyList<KernelAxisPoint> points)
        {
            if (points == null || points.Count == 0) return null;
            if (points.Count == 1) return points[0].Value;

            if (targetX <= points[0].X)
            {
                return InterpolateLinear(points[0], points[1], targetX);
            }

            if (targetX >= points[points.Count - 1].X)
            {
                return InterpolateLinear(points[points.Count - 2], points[points.Count - 1], targetX);
            }

            for (var i = 0; i + 1 < points.Count; i++)
            {
                if (targetX < points[i].X || targetX > points[i + 1].X) continue;
                return InterpolateLinear(points[i], points[i + 1], targetX);
            }

            return points[points.Count - 1].Value;
        }

        private static double InterpolateLinear(KernelMetricPoint left, KernelMetricPoint right, double targetPressure)
        {
            var dx = right.Pressure - left.Pressure;
            if (Math.Abs(dx) <= 1e-12) return left.Value;
            var t = (targetPressure - left.Pressure) / dx;
            return left.Value + ((right.Value - left.Value) * t);
        }

        private static double InterpolateLinear(KernelAxisPoint left, KernelAxisPoint right, double targetX)
        {
            var dx = right.X - left.X;
            if (Math.Abs(dx) <= 1e-12) return left.Value;
            var t = (targetX - left.X) / dx;
            return left.Value + ((right.Value - left.Value) * t);
        }

        private static double? ComputeError(double observed, double? predicted)
        {
            if (!predicted.HasValue) return null;
            return Math.Abs(observed - predicted.Value);
        }

        private static double? ComputeError(double? observed, double? predicted)
        {
            if (!observed.HasValue || !predicted.HasValue) return null;
            return Math.Abs(observed.Value - predicted.Value);
        }

        private static List<KernelObservedMetrics> BuildKernelObservedMetricsFromStairRows(
            IReadOnlyList<KernelStairDetailRow> detailRows,
            IReadOnlyDictionary<string, KernelStairSummaryRow> summaryRows)
        {
            var rowsByPressure = new Dictionary<string, List<KernelStairDetailRow>>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < detailRows.Count; i++)
            {
                if (!rowsByPressure.TryGetValue(detailRows[i].PressureHeaderSuffix, out var rows))
                {
                    rows = new List<KernelStairDetailRow>();
                    rowsByPressure.Add(detailRows[i].PressureHeaderSuffix, rows);
                }

                rows.Add(detailRows[i]);
            }

            var orderedKeys = new List<string>(rowsByPressure.Keys);
            orderedKeys.Sort((a, b) =>
            {
                var left = rowsByPressure[a][0].PressureValue;
                var right = rowsByPressure[b][0].PressureValue;
                var cmp = left.CompareTo(right);
                if (cmp != 0) return cmp;
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });

            var maxObservedRadiusPx = 0;
            for (var i = 0; i < detailRows.Count; i++)
            {
                if (detailRows[i].EndRadiusPx > maxObservedRadiusPx)
                {
                    maxObservedRadiusPx = detailRows[i].EndRadiusPx;
                }
            }

            var radiusNormScale = ResolveRadiusNormScale(detailRows);
            var observedRows = new List<KernelObservedMetrics>(orderedKeys.Count);
            for (var i = 0; i < orderedKeys.Count; i++)
            {
                var key = orderedKeys[i];
                if (!summaryRows.TryGetValue(key, out var summaryRow))
                {
                    throw new InvalidOperationException($"階段summaryに pressure={key} の行が見つかりません。");
                }

                var plateaus = BuildKernelPlateausFromDetailRows(rowsByPressure[key]);
                if (plateaus.Count == 0)
                {
                    continue;
                }

                observedRows.Add(BuildKernelObservedMetrics(
                    key,
                    summaryRow.PressureValue,
                    plateaus,
                    maxObservedRadiusPx,
                    radiusNormScale,
                    summaryRow));
            }

            return observedRows;
        }

        private static List<KernelPlateau> BuildKernelPlateausFromDetailRows(IReadOnlyList<KernelStairDetailRow> rows)
        {
            var plateaus = new List<KernelPlateau>(rows.Count);
            for (var i = 0; i < rows.Count; i++)
            {
                plateaus.Add(new KernelPlateau(
                    rows[i].PlateauIndex,
                    rows[i].StartRadiusPx,
                    rows[i].EndRadiusPx,
                    rows[i].StartRadiusNorm,
                    rows[i].EndRadiusNorm,
                    rows[i].TreadPx,
                    rows[i].LevelEff01));
            }

            return plateaus;
        }

        private static async Task<List<KernelStairDetailRow>> LoadKernelStairDetailRowsAsync(StorageFile file)
        {
            var text = await FileIO.ReadTextAsync(file);
            var lines = SplitLines(text);
            var rows = new List<KernelStairDetailRow>();
            var headerIndex = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                headerIndex = i;
                break;
            }

            if (headerIndex < 0)
            {
                throw new InvalidOperationException($"階段detail CSVのヘッダーが見つかりません: {file.Name}");
            }

            for (var i = headerIndex + 1; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                var cells = line.Split(',');
                if (cells.Length < 13)
                {
                    continue;
                }

                if (!TryParsePressureSuffixAsDecimal(cells[0], out var pressureValue))
                {
                    continue;
                }

                rows.Add(new KernelStairDetailRow(
                    cells[0],
                    ParseDecimalInvariant(cells[1]),
                    ParseIntInvariant(cells[2]),
                    ParseIntInvariant(cells[3]),
                    ParseIntInvariant(cells[4]),
                    ParseNullableDoubleInvariant(cells[5]),
                    ParseNullableDoubleInvariant(cells[6]),
                    ParseIntInvariant(cells[7]),
                    ParseDecimalInvariant(cells[8]),
                    ParseNullableDecimalInvariant(cells[9]),
                    ParseNullableIntInvariant(cells[10]),
                    ParseNullableDoubleInvariant(cells[11]),
                    ParseNullableDoubleInvariant(cells[12])));
            }

            return rows;
        }

        private static async Task<Dictionary<string, KernelStairSummaryRow>> LoadKernelStairSummaryRowsAsync(StorageFile file)
        {
            var text = await FileIO.ReadTextAsync(file);
            var lines = SplitLines(text);
            var rows = new Dictionary<string, KernelStairSummaryRow>(StringComparer.OrdinalIgnoreCase);
            var headerIndex = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                headerIndex = i;
                break;
            }

            if (headerIndex < 0)
            {
                throw new InvalidOperationException($"階段summary CSVのヘッダーが見つかりません: {file.Name}");
            }

            for (var i = headerIndex + 1; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;

                var cells = line.Split(',');
                if (cells.Length < 12)
                {
                    continue;
                }

                rows[cells[0]] = new KernelStairSummaryRow(
                    cells[0],
                    ParseDecimalInvariant(cells[1]),
                    ParseIntInvariant(cells[2]),
                    ParseIntInvariant(cells[3]),
                    ParseDoubleInvariant(cells[4]),
                    ParseDoubleInvariant(cells[5]),
                    ParseDoubleInvariant(cells[6]),
                    ParseDoubleInvariant(cells[7]),
                    ParseDoubleInvariant(cells[8]),
                    ParseIntInvariant(cells[9]),
                    ParseNullableDoubleInvariant(cells[10]),
                    ParseNullableDecimalInvariant(cells[11]));
            }

            return rows;
        }

        private static async Task<StorageFile?> TryResolveSiblingCsvAsync(StorageFile file, string siblingName)
        {
            if (string.IsNullOrWhiteSpace(file.Path) || string.IsNullOrWhiteSpace(siblingName))
            {
                return null;
            }

            var directoryPath = Path.GetDirectoryName(file.Path);
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                return null;
            }

            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(directoryPath);
                return await folder.GetFileAsync(siblingName);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        private static string BuildSiblingSummaryFileName(string detailFileName)
        {
            const string detailSuffix = "-stair-detail.csv";
            if (detailFileName.EndsWith(detailSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return detailFileName.Substring(0, detailFileName.Length - detailSuffix.Length) + "-stair-summary.csv";
            }

            return detailFileName;
        }

        private static string RemovePredictionInputSuffix(string baseName)
        {
            const string detailSuffix = "-stair-detail";
            const string summarySuffix = "-stair-summary";
            if (baseName.EndsWith(detailSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return baseName.Substring(0, baseName.Length - detailSuffix.Length);
            }

            if (baseName.EndsWith(summarySuffix, StringComparison.OrdinalIgnoreCase))
            {
                return baseName.Substring(0, baseName.Length - summarySuffix.Length);
            }

            return baseName;
        }

        private static decimal ParseDecimalInvariant(string text)
        {
            return decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static double ParseDoubleInvariant(string text)
        {
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static int ParseIntInvariant(string text)
        {
            return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        }

        private static decimal? ParseNullableDecimalInvariant(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return ParseDecimalInvariant(text);
        }

        private static double? ParseNullableDoubleInvariant(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return ParseDoubleInvariant(text);
        }

        private static int? ParseNullableIntInvariant(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return ParseIntInvariant(text);
        }

        private static double ComputeMean(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            double sum = 0;
            for (var i = 0; i < values.Count; i++)
            {
                sum += values[i];
            }

            return sum / values.Count;
        }

        private static double ComputeMedian(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            var copy = new List<double>(values.Count);
            for (var i = 0; i < values.Count; i++)
            {
                copy.Add(values[i]);
            }

            copy.Sort();
            var mid = copy.Count / 2;
            if ((copy.Count % 2) == 0)
            {
                return (copy[mid - 1] + copy[mid]) * 0.5;
            }

            return copy[mid];
        }

        private static double ComputeMax(IReadOnlyList<double> values)
        {
            if (values == null || values.Count == 0) return 0.0;
            var max = values[0];
            for (var i = 1; i < values.Count; i++)
            {
                if (values[i] > max)
                {
                    max = values[i];
                }
            }

            return max;
        }

        private static string BuildKernelStairDetailCsv(string inputFileName, IReadOnlyList<KernelStairDetailRow> rows)
        {
            var sb = new StringBuilder(capacity: Math.Max(16 * 1024, rows.Count * 128));
            sb.Append("# source=kernel stair detail").AppendLine();
            sb.Append("# input_file=").Append(inputFileName).AppendLine();
            sb.AppendLine("p_header,p_value,plateau_index,start_r_px,end_r_px,start_r_norm,end_r_norm,tread_px,level_eff01,riser_to_next01,delta_tread_to_prev,tread_ratio_to_prev,log2_tread_ratio_to_prev");

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                sb.Append(row.PressureHeaderSuffix).Append(',');
                sb.Append(row.PressureValue.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.PlateauIndex.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.StartRadiusPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.EndRadiusPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                AppendNullableDouble(sb, row.StartRadiusNorm, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.EndRadiusNorm, "0.########"); sb.Append(',');
                sb.Append(row.TreadPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.LevelEff01.ToString("0.##########", CultureInfo.InvariantCulture)).Append(',');
                AppendNullableDecimal(sb, row.RiserToNext01, "0.##########"); sb.Append(',');
                AppendNullableInt(sb, row.DeltaTreadToPrev); sb.Append(',');
                AppendNullableDouble(sb, row.TreadRatioToPrev, "0.########"); sb.Append(',');
                AppendNullableDouble(sb, row.Log2TreadRatioToPrev, "0.########");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string BuildKernelStairSummaryCsv(string inputFileName, IReadOnlyList<KernelStairSummaryRow> rows)
        {
            var sb = new StringBuilder(capacity: Math.Max(8 * 1024, rows.Count * 128));
            sb.Append("# source=kernel stair summary").AppendLine();
            sb.Append("# input_file=").Append(inputFileName).AppendLine();
            sb.AppendLine("p_header,p_value,plateau_count,transition_count,mean_tread_px,median_tread_px,max_tread_px,mean_riser01,std_riser01,last_nonzero_r_px,last_nonzero_r_norm,start_level_eff01");

            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                sb.Append(row.PressureHeaderSuffix).Append(',');
                sb.Append(row.PressureValue.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.PlateauCount.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.TransitionCount.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.MeanTreadPx.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.MedianTreadPx.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.MaxTreadPx.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.MeanRiser01.ToString("0.##########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.StdRiser01.ToString("0.##########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(row.LastNonZeroRadiusPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                AppendNullableDouble(sb, row.LastNonZeroRadiusNorm, "0.########"); sb.Append(',');
                AppendNullableDecimal(sb, row.StartLevelEff01, "0.##########");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static bool TryParsePressureSuffixAsDecimal(string suffix, out decimal pressureValue)
        {
            pressureValue = 0;
            if (string.IsNullOrWhiteSpace(suffix)) return false;
            var normalized = suffix.Replace('_', '.');
            return decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out pressureValue);
        }

        private static void AppendNullableDouble(StringBuilder sb, double? value, string format)
        {
            if (!value.HasValue) return;
            sb.Append(value.Value.ToString(format, CultureInfo.InvariantCulture));
        }

        private static void AppendNullableDecimal(StringBuilder sb, decimal? value, string format)
        {
            if (!value.HasValue) return;
            sb.Append(value.Value.ToString(format, CultureInfo.InvariantCulture));
        }

        private static void AppendNullableInt(StringBuilder sb, int? value)
        {
            if (!value.HasValue) return;
            sb.Append(value.Value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendLocalSlopeVectorHeader(StringBuilder sb)
        {
            for (var i = 0; i < LocalSlopeVectorAnchors.Length; i++)
            {
                var label = BuildLocalSlopeAnchorLabel(LocalSlopeVectorAnchors[i]);
                sb.Append(",obs_local_slope_").Append(label)
                    .Append(",pred_local_slope_").Append(label)
                    .Append(",err_local_slope_").Append(label);
            }
        }

        private static void AppendLocalSlopeVectorValues(StringBuilder sb, IReadOnlyList<double?> observed, IReadOnlyList<double?> predicted)
        {
            for (var i = 0; i < LocalSlopeVectorAnchors.Length; i++)
            {
                var observedValue = observed != null && i < observed.Count ? observed[i] : null;
                var predictedValue = predicted != null && i < predicted.Count ? predicted[i] : null;
                AppendNullableDouble(sb, observedValue, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, predictedValue, "0.##########"); sb.Append(',');
                AppendNullableDouble(sb, ComputeError(observedValue, predictedValue), "0.##########"); sb.Append(',');
            }
        }

        private static string BuildLocalSlopeAnchorLabel(double anchor)
        {
            var scaled = (int)Math.Round(anchor * 1000.0, MidpointRounding.AwayFromZero);
            return "u" + scaled.ToString("000", CultureInfo.InvariantCulture);
        }

        private static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;

            using (var reader = new System.IO.StringReader(text))
            {
                while (true)
                {
                    var line = reader.ReadLine();
                    if (line == null) break;
                    lines.Add(line);
                }
            }

            return lines;
        }

        private static string BuildPressureHeaderSuffix(string fileName)
        {
            var pToken = ExtractPressureToken(fileName);
            if (string.IsNullOrWhiteSpace(pToken))
            {
                return "unknown";
            }

            if (int.TryParse(pToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milli))
            {
                var pressure = milli / 1000.0;
                return pressure.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', '_');
            }

            if (double.TryParse(pToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var direct))
            {
                return direct.ToString("0.###", CultureInfo.InvariantCulture).Replace('.', '_');
            }

            return pToken.Replace('.', '_');
        }

        private static double ParsePressureSortKey(string fileName)
        {
            var pToken = ExtractPressureToken(fileName);
            if (string.IsNullOrWhiteSpace(pToken)) return double.PositiveInfinity;

            if (int.TryParse(pToken, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milli))
            {
                return milli / 1000.0;
            }

            if (double.TryParse(pToken, NumberStyles.Float, CultureInfo.InvariantCulture, out var direct))
            {
                return direct;
            }

            return double.PositiveInfinity;
        }

        private static string ExtractPressureToken(string fileName)
        {
            var name = fileName;
            if (name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }

            var parts = name.Split('-');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length >= 2 && (part[0] == 'P' || part[0] == 'p'))
                {
                    return part.Substring(1);
                }
            }

            return string.Empty;
        }

        private static async Task<StorageFile?> PickPngFileAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".png");
            return await picker.PickSingleFileAsync();
        }

        private static async Task<IReadOnlyList<StorageFile>?> PickCsvFilesAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".csv");
            return await picker.PickMultipleFilesAsync();
        }

        private static async Task<StorageFile?> PickCsvFileAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".csv");
            return await picker.PickSingleFileAsync();
        }

        private static string RemoveExtensionSafe(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            var dot = fileName.LastIndexOf('.');
            if (dot <= 0) return fileName;
            return fileName.Substring(0, dot);
        }

        private sealed class WideKernelSource
        {
            internal WideKernelSource(string fileName, string pressureHeaderSuffix, double pressureSortKey, IReadOnlyList<string> metricNames, IReadOnlyList<WideKernelDataRow> rows)
            {
                FileName = fileName;
                PressureHeaderSuffix = pressureHeaderSuffix;
                PressureSortKey = pressureSortKey;
                MetricNames = metricNames;
                Rows = rows;
            }

            internal string FileName { get; }
            internal string PressureHeaderSuffix { get; }
            internal double PressureSortKey { get; }
            internal IReadOnlyList<string> MetricNames { get; }
            internal IReadOnlyList<WideKernelDataRow> Rows { get; }
        }

        private sealed class WideKernelRadiusRow
        {
            internal WideKernelRadiusRow(int radiusPx, double? radiusNorm)
            {
                RadiusPx = radiusPx;
                RadiusNorm = radiusNorm;
                ValuesByPressure = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            }

            internal int RadiusPx { get; }
            internal double? RadiusNorm { get; set; }
            internal Dictionary<string, Dictionary<string, string>> ValuesByPressure { get; }
        }

        private readonly struct WideKernelDataRow
        {
            internal WideKernelDataRow(int radiusPx, double? radiusNorm, Dictionary<string, string> metrics)
            {
                RadiusPx = radiusPx;
                RadiusNorm = radiusNorm;
                Metrics = metrics;
            }

            internal int RadiusPx { get; }
            internal double? RadiusNorm { get; }
            internal Dictionary<string, string> Metrics { get; }
        }

        private sealed class WideKernelAnalysisSource
        {
            internal WideKernelAnalysisSource(string fileName, IReadOnlyList<WideKernelPressureColumn> pressures, IReadOnlyList<WideKernelAnalysisRow> rows)
            {
                FileName = fileName;
                Pressures = pressures;
                Rows = rows;
            }

            internal string FileName { get; }
            internal IReadOnlyList<WideKernelPressureColumn> Pressures { get; }
            internal IReadOnlyList<WideKernelAnalysisRow> Rows { get; }
        }

        private readonly struct WideKernelPressureColumn
        {
            internal WideKernelPressureColumn(string headerSuffix, decimal pressureValue, int columnIndex)
            {
                HeaderSuffix = headerSuffix;
                PressureValue = pressureValue;
                ColumnIndex = columnIndex;
            }

            internal string HeaderSuffix { get; }
            internal decimal PressureValue { get; }
            internal int ColumnIndex { get; }
        }

        private readonly struct WideKernelAnalysisRow
        {
            internal WideKernelAnalysisRow(int radiusPx, double? radiusNorm, Dictionary<string, decimal?> kernel01ByPressure)
            {
                RadiusPx = radiusPx;
                RadiusNorm = radiusNorm;
                Kernel01ByPressure = kernel01ByPressure;
            }

            internal int RadiusPx { get; }
            internal double? RadiusNorm { get; }
            internal Dictionary<string, decimal?> Kernel01ByPressure { get; }
        }

        private readonly struct KernelEffectiveRow
        {
            internal KernelEffectiveRow(int radiusPx, double? radiusNorm, decimal levelEff01)
            {
                RadiusPx = radiusPx;
                RadiusNorm = radiusNorm;
                LevelEff01 = levelEff01;
            }

            internal int RadiusPx { get; }
            internal double? RadiusNorm { get; }
            internal decimal LevelEff01 { get; }
        }

        private readonly struct KernelLinePoint
        {
            internal KernelLinePoint(double x, double y)
            {
                X = x;
                Y = y;
            }

            internal double X { get; }
            internal double Y { get; }
        }

        private readonly struct KernelMetricPoint
        {
            internal KernelMetricPoint(double pressure, double value)
            {
                Pressure = pressure;
                Value = value;
            }

            internal double Pressure { get; }
            internal double Value { get; }
        }

        private readonly struct KernelAxisPoint
        {
            internal KernelAxisPoint(double x, double value)
            {
                X = x;
                Value = value;
            }

            internal double X { get; }
            internal double Value { get; }
        }

        private readonly struct LinearFitResult
        {
            internal static readonly LinearFitResult Invalid = new LinearFitResult(false, 0.0, 0.0, 0.0, 0);

            internal LinearFitResult(bool isValid, double slope, double intercept, double r2, int pointCount)
            {
                IsValid = isValid;
                Slope = slope;
                Intercept = intercept;
                R2 = r2;
                PointCount = pointCount;
            }

            internal bool IsValid { get; }
            internal double Slope { get; }
            internal double Intercept { get; }
            internal double R2 { get; }
            internal int PointCount { get; }
        }

        private readonly struct KernelMajorChangeMetrics
        {
            internal KernelMajorChangeMetrics(int plateauIndex, double? startRadiusPx, double? startRadiusNorm, double? relativeDropToMajor01, double? relativeDropToPrev01, double? log2TreadRatio)
            {
                PlateauIndex = plateauIndex;
                StartRadiusPx = startRadiusPx;
                StartRadiusNorm = startRadiusNorm;
                RelativeDropToMajor01 = relativeDropToMajor01;
                RelativeDropToPrev01 = relativeDropToPrev01;
                Log2TreadRatio = log2TreadRatio;
            }

            internal int PlateauIndex { get; }
            internal double? StartRadiusPx { get; }
            internal double? StartRadiusNorm { get; }
            internal double? RelativeDropToMajor01 { get; }
            internal double? RelativeDropToPrev01 { get; }
            internal double? Log2TreadRatio { get; }
        }

        private readonly struct KernelTailSegment
        {
            internal KernelTailSegment(int startPlateauIndex, int endPlateauIndex)
            {
                StartPlateauIndex = startPlateauIndex;
                EndPlateauIndex = endPlateauIndex;
            }

            internal int StartPlateauIndex { get; }
            internal int EndPlateauIndex { get; }
        }

        private sealed class KernelObservedMetrics
        {
            internal KernelObservedMetrics(
                string pressureHeaderSuffix,
                double pressureValue,
                double observedRiser01,
                int jointCount,
                double jointStep,
                IReadOnlyList<double?> localSlopeVector,
                double jointSpan,
                double? rootLock,
                double? rootLockAlt,
                double? terminalHeadroom,
                double? curvatureBudget,
                LinearFitResult frontFit23,
                LinearFitResult frontFit24,
                int initialEndRadiusPx,
                double? initialEndRadiusNorm,
                KernelMajorChangeMetrics majorChange,
                LinearFitResult tailFit,
                double? tailReferenceTreadPx,
                int observedTerminalRadiusPx,
                double? observedTerminalRadiusNorm,
                bool censoredTerminal,
                double? estimatedTrueTerminalPx,
                double? estimatedTrueTerminalNorm)
            {
                PressureHeaderSuffix = pressureHeaderSuffix;
                PressureValue = pressureValue;
                ObservedRiser01 = observedRiser01;
                JointCount = jointCount;
                JointStep = jointStep;
                LocalSlopeVector = localSlopeVector;
                JointSpan = jointSpan;
                RootLock = rootLock;
                RootLockAlt = rootLockAlt;
                TerminalHeadroom = terminalHeadroom;
                CurvatureBudget = curvatureBudget;
                FrontFit23 = frontFit23;
                FrontFit24 = frontFit24;
                InitialEndRadiusPx = initialEndRadiusPx;
                InitialEndRadiusNorm = initialEndRadiusNorm;
                MajorChange = majorChange;
                TailFit = tailFit;
                TailReferenceTreadPx = tailReferenceTreadPx;
                ObservedTerminalRadiusPx = observedTerminalRadiusPx;
                ObservedTerminalRadiusNorm = observedTerminalRadiusNorm;
                CensoredTerminal = censoredTerminal;
                EstimatedTrueTerminalPx = estimatedTrueTerminalPx;
                EstimatedTrueTerminalNorm = estimatedTrueTerminalNorm;
            }

            internal string PressureHeaderSuffix { get; }
            internal double PressureValue { get; }
            internal double ObservedRiser01 { get; }
            internal int JointCount { get; }
            internal double JointStep { get; }
            internal IReadOnlyList<double?> LocalSlopeVector { get; }
            internal double JointSpan { get; }
            internal double? RootLock { get; }
            internal double? RootLockAlt { get; }
            internal double? TerminalHeadroom { get; }
            internal double? CurvatureBudget { get; }
            internal LinearFitResult FrontFit23 { get; }
            internal LinearFitResult FrontFit24 { get; }
            internal int InitialEndRadiusPx { get; }
            internal double? InitialEndRadiusNorm { get; }
            internal KernelMajorChangeMetrics MajorChange { get; }
            internal LinearFitResult TailFit { get; }
            internal double? TailReferenceTreadPx { get; }
            internal int ObservedTerminalRadiusPx { get; }
            internal double? ObservedTerminalRadiusNorm { get; }
            internal bool CensoredTerminal { get; }
            internal double? EstimatedTrueTerminalPx { get; }
            internal double? EstimatedTrueTerminalNorm { get; }
        }

        private sealed class KernelPredictionComparisonRow
        {
            internal KernelPredictionComparisonRow(
                KernelObservedMetrics observed,
                double? predictedRiser01,
                int? predictedJointCount,
                double? predictedJointStep,
                IReadOnlyList<double?> predictedLocalSlopeVector,
                double? predictedJointSpan,
                double? predictedRootLock,
                double? predictedRootLockAlt,
                double? predictedTerminalHeadroom,
                double? predictedCurvatureBudget,
                double? predictedFrontSlope23,
                double? predictedFrontSlope24,
                double? predictedFrontIntercept24,
                double? predictedInitialEndRadiusPx,
                double? predictedInitialEndRadiusNorm,
                double? predictedMajorRelativeDrop01,
                int? predictedMajorChangeIndex,
                double? predictedMajorChangeRadiusPx,
                double? predictedMajorChangeRadiusNorm,
                double? predictedTailSlope,
                double? predictedEstimatedTrueTerminalPx,
                double? predictedEstimatedTrueTerminalNorm,
                double? predictedObservedTerminalPx,
                double? predictedObservedTerminalNorm,
                bool? predictedCensoredTerminal)
            {
                Observed = observed;
                PredictedRiser01 = predictedRiser01;
                PredictedJointCount = predictedJointCount;
                PredictedJointStep = predictedJointStep;
                PredictedLocalSlopeVector = predictedLocalSlopeVector;
                PredictedJointSpan = predictedJointSpan;
                PredictedRootLock = predictedRootLock;
                PredictedRootLockAlt = predictedRootLockAlt;
                PredictedTerminalHeadroom = predictedTerminalHeadroom;
                PredictedCurvatureBudget = predictedCurvatureBudget;
                PredictedFrontSlope23 = predictedFrontSlope23;
                PredictedFrontSlope24 = predictedFrontSlope24;
                PredictedFrontIntercept24 = predictedFrontIntercept24;
                PredictedInitialEndRadiusPx = predictedInitialEndRadiusPx;
                PredictedInitialEndRadiusNorm = predictedInitialEndRadiusNorm;
                PredictedMajorRelativeDrop01 = predictedMajorRelativeDrop01;
                PredictedMajorChangeIndex = predictedMajorChangeIndex;
                PredictedMajorChangeRadiusPx = predictedMajorChangeRadiusPx;
                PredictedMajorChangeRadiusNorm = predictedMajorChangeRadiusNorm;
                PredictedTailSlope = predictedTailSlope;
                PredictedEstimatedTrueTerminalPx = predictedEstimatedTrueTerminalPx;
                PredictedEstimatedTrueTerminalNorm = predictedEstimatedTrueTerminalNorm;
                PredictedObservedTerminalPx = predictedObservedTerminalPx;
                PredictedObservedTerminalNorm = predictedObservedTerminalNorm;
                PredictedCensoredTerminal = predictedCensoredTerminal;
            }

            internal KernelObservedMetrics Observed { get; }
            internal double? PredictedRiser01 { get; }
            internal int? PredictedJointCount { get; }
            internal double? PredictedJointStep { get; }
            internal IReadOnlyList<double?> PredictedLocalSlopeVector { get; }
            internal double? PredictedJointSpan { get; }
            internal double? PredictedRootLock { get; }
            internal double? PredictedRootLockAlt { get; }
            internal double? PredictedTerminalHeadroom { get; }
            internal double? PredictedCurvatureBudget { get; }
            internal double? PredictedFrontSlope23 { get; }
            internal double? PredictedFrontSlope24 { get; }
            internal double? PredictedFrontIntercept24 { get; }
            internal double? PredictedInitialEndRadiusPx { get; }
            internal double? PredictedInitialEndRadiusNorm { get; }
            internal double? PredictedMajorRelativeDrop01 { get; }
            internal int? PredictedMajorChangeIndex { get; }
            internal double? PredictedMajorChangeRadiusPx { get; }
            internal double? PredictedMajorChangeRadiusNorm { get; }
            internal double? PredictedTailSlope { get; }
            internal double? PredictedEstimatedTrueTerminalPx { get; }
            internal double? PredictedEstimatedTrueTerminalNorm { get; }
            internal double? PredictedObservedTerminalPx { get; }
            internal double? PredictedObservedTerminalNorm { get; }
            internal bool? PredictedCensoredTerminal { get; }
        }

        private readonly struct KernelPlateau
        {
            internal KernelPlateau(int plateauIndex, int startRadiusPx, int endRadiusPx, double? startRadiusNorm, double? endRadiusNorm, int treadPx, decimal levelEff01)
            {
                PlateauIndex = plateauIndex;
                StartRadiusPx = startRadiusPx;
                EndRadiusPx = endRadiusPx;
                StartRadiusNorm = startRadiusNorm;
                EndRadiusNorm = endRadiusNorm;
                TreadPx = treadPx;
                LevelEff01 = levelEff01;
            }

            internal int PlateauIndex { get; }
            internal int StartRadiusPx { get; }
            internal int EndRadiusPx { get; }
            internal double? StartRadiusNorm { get; }
            internal double? EndRadiusNorm { get; }
            internal int TreadPx { get; }
            internal decimal LevelEff01 { get; }
        }

        private readonly struct KernelStairDetailRow
        {
            internal KernelStairDetailRow(string pressureHeaderSuffix, decimal pressureValue, int plateauIndex, int startRadiusPx, int endRadiusPx, double? startRadiusNorm, double? endRadiusNorm, int treadPx, decimal levelEff01, decimal? riserToNext01, int? deltaTreadToPrev, double? treadRatioToPrev, double? log2TreadRatioToPrev)
            {
                PressureHeaderSuffix = pressureHeaderSuffix;
                PressureValue = pressureValue;
                PlateauIndex = plateauIndex;
                StartRadiusPx = startRadiusPx;
                EndRadiusPx = endRadiusPx;
                StartRadiusNorm = startRadiusNorm;
                EndRadiusNorm = endRadiusNorm;
                TreadPx = treadPx;
                LevelEff01 = levelEff01;
                RiserToNext01 = riserToNext01;
                DeltaTreadToPrev = deltaTreadToPrev;
                TreadRatioToPrev = treadRatioToPrev;
                Log2TreadRatioToPrev = log2TreadRatioToPrev;
            }

            internal string PressureHeaderSuffix { get; }
            internal decimal PressureValue { get; }
            internal int PlateauIndex { get; }
            internal int StartRadiusPx { get; }
            internal int EndRadiusPx { get; }
            internal double? StartRadiusNorm { get; }
            internal double? EndRadiusNorm { get; }
            internal int TreadPx { get; }
            internal decimal LevelEff01 { get; }
            internal decimal? RiserToNext01 { get; }
            internal int? DeltaTreadToPrev { get; }
            internal double? TreadRatioToPrev { get; }
            internal double? Log2TreadRatioToPrev { get; }
        }

        private readonly struct KernelStairSummaryRow
        {
            internal KernelStairSummaryRow(string pressureHeaderSuffix, decimal pressureValue, int plateauCount, int transitionCount, double meanTreadPx, double medianTreadPx, double maxTreadPx, double meanRiser01, double stdRiser01, int lastNonZeroRadiusPx, double? lastNonZeroRadiusNorm, decimal? startLevelEff01)
            {
                PressureHeaderSuffix = pressureHeaderSuffix;
                PressureValue = pressureValue;
                PlateauCount = plateauCount;
                TransitionCount = transitionCount;
                MeanTreadPx = meanTreadPx;
                MedianTreadPx = medianTreadPx;
                MaxTreadPx = maxTreadPx;
                MeanRiser01 = meanRiser01;
                StdRiser01 = stdRiser01;
                LastNonZeroRadiusPx = lastNonZeroRadiusPx;
                LastNonZeroRadiusNorm = lastNonZeroRadiusNorm;
                StartLevelEff01 = startLevelEff01;
            }

            internal string PressureHeaderSuffix { get; }
            internal decimal PressureValue { get; }
            internal int PlateauCount { get; }
            internal int TransitionCount { get; }
            internal double MeanTreadPx { get; }
            internal double MedianTreadPx { get; }
            internal double MaxTreadPx { get; }
            internal double MeanRiser01 { get; }
            internal double StdRiser01 { get; }
            internal int LastNonZeroRadiusPx { get; }
            internal double? LastNonZeroRadiusNorm { get; }
            internal decimal? StartLevelEff01 { get; }
        }

        private static int ReadIntFromTextBox(MainPage page, string name, int fallback)
        {
            var tb = page.FindName(name) as TextBox;
            if (tb == null) return fallback;
            var s = (tb.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s)) return fallback;

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return v;
            return fallback;
        }
    }
}
