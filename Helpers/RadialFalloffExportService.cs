using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using static StrokeSampler.StrokeHelpers;

namespace StrokeSampler
{
    internal static class RadialFalloffExportService
    {
        internal static async Task ExportRadialAlphaKneeSummaryAsync(MainPage mp)
        {
            // 指定フォルダ内の radial-alpha-*.csv（p_ge_*付き）から、しきい値交点のrを自動抽出してサマリCSVを出力する。
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
            var rows = new List<(
                string fileName,
                double s,
                double p,
                int n,
                int bin,
                double maxR,
                double p50_max,
                double p100_max,
                double? p50_r099,
                double? p50_r095,
                double? p50_r090,
                double? p100_r099,
                double? p100_r095,
                double? p100_r090,
                double? p50_rMax099,
                double? p50_rMax095,
                double? p50_rMax090,
                double? p100_rMax099,
                double? p100_rMax095,
                double? p100_rMax090)>();

            var skipped = 0;

            foreach (var f in files)
            {
                if (!f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!f.Name.StartsWith("radial-alpha-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseRadialAlphaMetaFromFileName(f.Name, out var s, out var p, out var n, out var bin))
                {
                    skipped++;
                    continue;
                }

                var text = await FileIO.ReadTextAsync(f);
                if (!TryReadKneePointsFromRadialAlphaCsv(text, out var maxR, out var p50Points, out var p100Points))
                {
                    skipped++;
                    continue;
                }

                var p50Max = MaxOrZero(p50Points.y);
                var p100Max = MaxOrZero(p100Points.y);

                rows.Add((
                    fileName: f.Name,
                    s: s,
                    p: p,
                    n: n,
                    bin: bin,
                    maxR: maxR,
                    p50_max: p50Max,
                    p100_max: p100Max,
                    p50_r099: FindCrossingR(p50Points.r, p50Points.y, 0.99),
                    p50_r095: FindCrossingR(p50Points.r, p50Points.y, 0.95),
                    p50_r090: FindCrossingR(p50Points.r, p50Points.y, 0.90),
                    p100_r099: FindCrossingR(p100Points.r, p100Points.y, 0.99),
                    p100_r095: FindCrossingR(p100Points.r, p100Points.y, 0.95),
                    p100_r090: FindCrossingR(p100Points.r, p100Points.y, 0.90),
                    p50_rMax099: p50Max > 0 ? FindCrossingR(p50Points.r, p50Points.y, p50Max * 0.99) : null,
                    p50_rMax095: p50Max > 0 ? FindCrossingR(p50Points.r, p50Points.y, p50Max * 0.95) : null,
                    p50_rMax090: p50Max > 0 ? FindCrossingR(p50Points.r, p50Points.y, p50Max * 0.90) : null,
                    p100_rMax099: p100Max > 0 ? FindCrossingR(p100Points.r, p100Points.y, p100Max * 0.99) : null,
                    p100_rMax095: p100Max > 0 ? FindCrossingR(p100Points.r, p100Points.y, p100Max * 0.95) : null,
                    p100_rMax090: p100Max > 0 ? FindCrossingR(p100Points.r, p100Points.y, p100Max * 0.90) : null));
            }

            if (rows.Count == 0)
            {
                var dlg0 = new ContentDialog
                {
                    Title = "半径α kneeサマリCSV",
                    Content = "対象CSVが見つかりませんでした（radial-alpha-*.csv）。",
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
                c = x.n.CompareTo(y.n);
                if (c != 0) return c;
                return x.bin.CompareTo(y.bin);
            });

            var sb = new StringBuilder(capacity: Math.Max(1024, rows.Count * 240));
            sb.AppendLine("file,S,P,N,bin,max_r,p50_max,p100_max,p50_r099,p50_r095,p50_r090,p100_r099,p100_r095,p100_r090,p50_rMax099,p50_rMax095,p50_rMax090,p100_rMax099,p100_rMax095,p100_rMax090");

            foreach (var row in rows)
            {
                sb.Append(EscapeCsvCell(row.fileName));
                sb.Append(',');
                sb.Append(row.s.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(row.p.ToString("0.####", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(row.n.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(row.bin.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(row.maxR.ToString("0.###", CultureInfo.InvariantCulture));

                sb.Append(',');
                sb.Append(row.p50_max.ToString("0.######", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(row.p100_max.ToString("0.######", CultureInfo.InvariantCulture));

                AppendNullable(sb, row.p50_r099);
                AppendNullable(sb, row.p50_r095);
                AppendNullable(sb, row.p50_r090);
                AppendNullable(sb, row.p100_r099);
                AppendNullable(sb, row.p100_r095);
                AppendNullable(sb, row.p100_r090);

                AppendNullable(sb, row.p50_rMax099);
                AppendNullable(sb, row.p50_rMax095);
                AppendNullable(sb, row.p50_rMax090);
                AppendNullable(sb, row.p100_rMax099);
                AppendNullable(sb, row.p100_rMax095);
                AppendNullable(sb, row.p100_rMax090);

                sb.AppendLine();
            }

            var outName = "radial-alpha-knee-summary.csv";
            var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var done = new ContentDialog
            {
                Title = "半径α kneeサマリCSV",
                Content = $"完了: {rows.Count}行を書き出しました。スキップ={skipped}件。\n出力={outName}",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }

        private static double MaxOrZero(IReadOnlyList<double> a)
        {
            if (a is null || a.Count == 0) return 0;
            var max = a[0];
            for (var i = 1; i < a.Count; i++)
            {
                if (a[i] > max)
                {
                    max = a[i];
                }
            }
            return max;
        }

        internal static async Task ExportRadialAlphaBatchPsSizesNsAsync(MainPage mp)
        {
            var ps = UIHelpers.GetRadialFalloffBatchPs(mp);
            var sizes = UIHelpers.GetRadialFalloffBatchSizes(mp);
            var ns = UIHelpers.GetRadialFalloffBatchNs(mp);

            if (ps.Count == 0 || sizes.Count == 0 || ns.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "半径αCSV一括(P×S×N)",
                    Content = "P一覧 / Sizes / N一覧 のいずれかが空です。例: P=0.05,0.1,...  Sizes=5,12,...  N=1,2,...",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

            var binSize = UIHelpers.GetRadialBinSize(mp);
            if (binSize <= 0)
            {
                binSize = 1;
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

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var total = ps.Count * sizes.Count * ns.Count;
            var doneCount = 0;

            foreach (var p in ps)
            {
                foreach (var size in sizes)
                {
                    var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
                    attributes.Size = new Size(size, size);

                    foreach (var n in ns)
                    {
                        var pngName = $"dot512-material-S{size:0.##}-P{p:0.####}-N{n}.png";
                        var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

                        byte[] dotBytes;
                        using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
                        using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                        {
                            using (var ds = target.CreateDrawingSession())
                            {
                                ds.Clear(Color.FromArgb(0, 0, 0, 0));

                                for (var i = 0; i < n; i++)
                                {
                                    var dot = CreatePencilDot(cx, cy, p, attributes);
                                    ds.DrawInk(new[] { dot });
                                }
                            }

                            dotBytes = target.GetPixelBytes();
                            await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                        }

                        var analysis = RadialAlphaBinAnalyzer.Analyze(
                            dotBytes,
                            MainPage.Dot512Size,
                            MainPage.Dot512Size,
                            binSize,
                            MainPage.RadialAlphaThresholds);

                        var csv = RadialAlphaCsvBuilder.Build(
                            analysis.Bins,
                            binSize,
                            MainPage.RadialAlphaThresholds,
                            analysis.Total,
                            analysis.SumAlpha,
                            analysis.Hits);

                        var csvName = $"radial-alpha-S{size:0.##}-P{p:0.####}-N{n}-bin{binSize}.csv";
                        var csvFile = await folder.CreateFileAsync(csvName, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(csvFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);

                        doneCount++;
                    }
                }
            }

            var done = new ContentDialog
            {
                Title = "半径αCSV一括(P×S×N)",
                Content = $"完了: {doneCount}/{total} 個出力しました。（bin={binSize}px）",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }

        private static string EscapeCsvCell(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // UWP(.NET Native)では string.Contains(char) が使えないため、IndexOfで判定する。
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        private static void AppendNullable(StringBuilder sb, double? v)
        {
            sb.Append(',');
            if (v.HasValue)
            {
                sb.Append(v.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        private static bool TryParseRadialAlphaMetaFromFileName(string fileName, out double s, out double p, out int n, out int bin)
        {
            s = 0;
            p = 0;
            n = 0;
            bin = 0;

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
            var hasS = false;
            var hasP = false;
            var hasN = false;
            var hasBin = false;

            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (string.IsNullOrWhiteSpace(part)) continue;

                if (!hasS && part.Length >= 2 && part[0] == 'S')
                {
                    if (double.TryParse(part.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out s))
                    {
                        hasS = true;
                    }
                    continue;
                }
                if (!hasP && part.Length >= 2 && part[0] == 'P')
                {
                    if (double.TryParse(part.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out p))
                    {
                        hasP = true;
                    }
                    continue;
                }
                if (!hasN && part.Length >= 2 && part[0] == 'N')
                {
                    if (int.TryParse(part.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                    {
                        hasN = true;
                    }
                    continue;
                }
                if (!hasBin && part.StartsWith("bin", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(part.Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out bin))
                    {
                        hasBin = true;
                    }
                    continue;
                }
            }

            return hasS && hasP && hasN;
        }

        private static bool TryReadKneePointsFromRadialAlphaCsv(
            string csv,
            out double maxR,
            out (List<double> r, List<double> y) p50,
            out (List<double> r, List<double> y) p100)
        {
            maxR = 0;
            p50 = (new List<double>(512), new List<double>(512));
            p100 = (new List<double>(512), new List<double>(512));

            if (string.IsNullOrWhiteSpace(csv))
            {
                return false;
            }

            var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1)
            {
                return false;
            }

            var header = lines[0].Split(',');
            var idxPxFrom = IndexOf(header, "px_from");
            var idxPxTo = IndexOf(header, "px_to");
            var idxP50 = IndexOf(header, "p_ge_50");
            var idxP100 = IndexOf(header, "p_ge_100");

            if (idxPxFrom < 0 || idxPxTo < 0 || idxP50 < 0 || idxP100 < 0)
            {
                return false;
            }

            for (var i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                if (cols.Length <= Math.Max(Math.Max(idxPxFrom, idxPxTo), Math.Max(idxP50, idxP100)))
                {
                    continue;
                }

                if (!double.TryParse(cols[idxPxFrom], NumberStyles.Float, CultureInfo.InvariantCulture, out var pxFrom))
                {
                    continue;
                }
                if (!double.TryParse(cols[idxPxTo], NumberStyles.Float, CultureInfo.InvariantCulture, out var pxTo))
                {
                    continue;
                }

                var rCenter = (pxFrom + pxTo) * 0.5;

                if (!double.TryParse(cols[idxP50], NumberStyles.Float, CultureInfo.InvariantCulture, out var v50))
                {
                    continue;
                }
                if (!double.TryParse(cols[idxP100], NumberStyles.Float, CultureInfo.InvariantCulture, out var v100))
                {
                    continue;
                }

                p50.r.Add(rCenter);
                p50.y.Add(v50);
                p100.r.Add(rCenter);
                p100.y.Add(v100);

                if (rCenter > maxR)
                {
                    maxR = rCenter;
                }
            }

            return p50.r.Count > 0;
        }

        private static int IndexOf(string[] header, string col)
        {
            for (var i = 0; i < header.Length; i++)
            {
                if (string.Equals(header[i], col, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private static double? FindCrossingR(IReadOnlyList<double> r, IReadOnlyList<double> y, double level)
        {
            if (r is null || y is null) return null;
            if (r.Count == 0 || y.Count == 0) return null;
            if (r.Count != y.Count) return null;

            for (var i = 0; i < y.Count; i++)
            {
                if (y[i] >= level)
                {
                    continue;
                }

                // 最初に閾値を割り込んだ点を返す。
                if (i == 0)
                {
                    return r[i];
                }

                var r0 = r[i - 1];
                var r1 = r[i];
                var y0 = y[i - 1];
                var y1 = y[i];

                var dy = y1 - y0;
                if (Math.Abs(dy) < 1e-12)
                {
                    return r1;
                }

                // y0>=level, y1<level の想定（最初の割り込み点）。線形補間で交点を近似。
                var t = (level - y0) / dy;
                return r0 + (t * (r1 - r0));
            }

            return null;
        }

        internal static async Task ExportRadialFalloffCsvFromHiResPngAsync(MainPage mp)
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

            if (!ParseFalloffFilenameService.TryParseFalloffMeta(sourceFile.Name, out var meta) || meta.ExportScale is null)
            {
                var dlg = new ContentDialog
                {
                    Title = "HiRes radial-falloff",
                    Content = "ファイル名から S/P/N/scale を取得できませんでした。例: ...-S200-P0.1-N50-scale8-...png",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

            var savePicker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                SuggestedFileName = $"radial-falloff-hires-S{meta.S:0.##}-P{meta.P:0.####}-N{meta.N}-scale{meta.ExportScale}"
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

                var frPx = ComputeRadialMeanAlphaD(bytes, width, height);
                var frDip = ResampleRadialByExportScale(frPx, meta.ExportScale.Value);
                var csv = BuildRadialFalloffCsv(frDip, meta.S, meta.P, meta.N, meta.ExportScale);
                await FileIO.WriteTextAsync(saveFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }
        }
        internal static async Task ExportRadialFalloffBatchPsSizesNsAsync(MainPage mp)
        {
            var ps = UIHelpers.GetRadialFalloffBatchPs(mp);
            var sizes = UIHelpers.GetRadialFalloffBatchSizes(mp);
            var ns = UIHelpers.GetRadialFalloffBatchNs(mp);

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

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var total = ps.Count * sizes.Count * ns.Count;
            var doneCount = 0;

            foreach (var p in ps)
            {
                foreach (var size in sizes)
                {
                    var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
                    attributes.Size = new Size(size, size);

                    foreach (var n in ns)
                    {
                        var pngName = $"dot512-material-S{size:0.##}-P{p:0.####}-N{n}.png";
                        var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

                        using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
                        using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                        {
                            using (var ds = target.CreateDrawingSession())
                            {
                                ds.Clear(Color.FromArgb(0, 0, 0, 0));

                                for (var i = 0; i < n; i++)
                                {
                                    var dot = CreatePencilDot(cx, cy, p, attributes);
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

                        var fr = ComputeRadialMeanAlphaD(dotBytes, MainPage.Dot512Size, MainPage.Dot512Size);
                        var csv = BuildRadialFalloffCsv(fr);
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

        internal static async Task ExportRadialFalloffBatchSizesNsAsync(MainPage mp)
        {
            var ps = UIHelpers.GetRadialFalloffBatchPs(mp);
            var sizes = UIHelpers.GetRadialFalloffBatchSizes(mp);
            var ns = UIHelpers.GetRadialFalloffBatchNs(mp);

            if (ps.Count == 0 || sizes.Count == 0 || ns.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "距離減衰CSV一括",
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

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var total = ps.Count * sizes.Count * ns.Count;
            var doneCount = 0;

            foreach (var p in ps)
            {
                foreach (var size in sizes)
                {
                    var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
                    attributes.Size = new Size(size, size);

                    foreach (var n in ns)
                    {
                        var pngName = $"dot512-material-S{size:0.##}-P{p:0.####}-N{n}.png";
                        var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

                        using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
                        using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                        {
                            using (var ds = target.CreateDrawingSession())
                            {
                                ds.Clear(Color.FromArgb(0, 0, 0, 0));

                                for (var i = 0; i < n; i++)
                                {
                                    var dot = CreatePencilDot(cx, cy, p, attributes);
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

                        var fr = ComputeRadialMeanAlphaD(dotBytes, MainPage.Dot512Size, MainPage.Dot512Size);
                        var csv = BuildRadialFalloffCsv(fr);
                        var csvName = $"radial-falloff-S{size:0.##}-P{p:0.####}-N{n}.csv";
                        var csvFile = await folder.CreateFileAsync(csvName, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(csvFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);

                        doneCount++;
                    }
                }
            }

            var done = new ContentDialog
            {
                Title = "距離減衰CSV一括",
                Content = $"完了: {doneCount}/{total} 個出力しました。",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }

        internal static async Task ExportRadialAlphaCsvAsync(MainPage mp)
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

            var binSize = UIHelpers.GetRadialBinSize(mp);

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
                    MainPage.RadialAlphaThresholds);

                var csv = RadialAlphaCsvBuilder.Build(
                    analysis.Bins,
                    binSize,
                    MainPage.RadialAlphaThresholds,
                    analysis.Total,
                    analysis.SumAlpha,
                    analysis.Hits);

                await FileIO.WriteTextAsync(saveFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }
        }

        internal static async Task ExportRadialFalloffCsvAsync(MainPage mp)
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

            var binSize = UIHelpers.GetRadialBinSize(mp);

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
                    MainPage.RadialAlphaThresholds);

                var csv = RadialAlphaCsvBuilder.Build(
                    analysis.Bins,
                    binSize,
                    MainPage.RadialAlphaThresholds,
                    analysis.Total,
                    analysis.SumAlpha,
                    analysis.Hits);

                await FileIO.WriteTextAsync(saveFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            }
        }

        internal static async Task ExportRadialFalloffBatchAsync(MainPage mp)
        {
            var sizes = UIHelpers.GetRadialFalloffBatchSizes(mp);
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

            var pressure = UIHelpers.GetDot512Pressure(mp);
            var n = UIHelpers.GetDot512Overwrite(mp);

            var device = CanvasDevice.GetSharedDevice();

            foreach (var size in sizes)
            {
                var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
                attributes.Size = new Size(size, size);

                var cx = (MainPage.Dot512Size - 1) / 2f;
                var cy = (MainPage.Dot512Size - 1) / 2f;

                var pngName = $"dot512-material-S{size:0.##}-P{pressure:0.###}-N{n}.png";
                var pngFile = await folder.CreateFileAsync(pngName, CreationCollisionOption.ReplaceExisting);

                using (IRandomAccessStream stream = await pngFile.OpenAsync(FileAccessMode.ReadWrite))
                using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(Color.FromArgb(0, 0, 0, 0));

                        for (var i = 0; i < n; i++)
                        {
                            var dot = CreatePencilDot(cx, cy, pressure, attributes);
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

                var fr = ComputeRadialMeanAlphaD(dotBytes, MainPage.Dot512Size, MainPage.Dot512Size);
                var csv = BuildRadialFalloffCsv(fr);

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
    }
}
