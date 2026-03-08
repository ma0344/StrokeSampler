using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Controls;

namespace InkDrawGen.Helpers
{
    internal static class RadialAlphaProfileExportService
    {
        internal static async Task ExportRadialAlphaCsvFromPngAsync(MainPage page)
        {
            var binSize = ReadIntFromTextBox(page, "RadialBinSizeTextBox", 1);
            binSize = Math.Max(1, binSize);

            var useAlphaCentroid = ReadBoolFromCheckBox(page, "UseAlphaCentroidCheckBox", fallback: false);

            var pngPicker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            pngPicker.FileTypeFilter.Add(".png");

            var pngFiles = await pngPicker.PickMultipleFilesAsync();
            if (pngFiles == null || pngFiles.Count == 0)
            {
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

            var thresholds = CreateDefaultThresholds();
            var device = CanvasDevice.GetSharedDevice();

            var doneCount = 0;
            var skipped = 0;

            foreach (var pngFile in pngFiles)
            {
                try
                {
                    using (var stream = await pngFile.OpenAsync(FileAccessMode.Read))
                    using (var bitmap = await CanvasBitmap.LoadAsync(device, stream))
                    {
                        var bytes = bitmap.GetPixelBytes();
                        var width = (int)bitmap.SizeInPixels.Width;
                        var height = (int)bitmap.SizeInPixels.Height;

                        var analysis = AnalyzeRadialAlphaBins(bytes, width, height, binSize, thresholds, useAlphaCentroid);
                        var csv = BuildRadialAlphaCsv(analysis, thresholds);

                        var baseName = RemoveExtensionSafe(pngFile.Name);
                        var outName = $"radial-alpha-{baseName}-bin{binSize}.csv";
                        var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
                        await FileIO.WriteTextAsync(outFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);
                        doneCount++;
                    }
                }
                catch
                {
                    skipped++;
                }
            }

            var dlg = new ContentDialog
            {
                Title = "半径αCSV(PNG→CSV)",
                Content = $"完了: {doneCount}件を書き出しました。スキップ={skipped}件。\n出力先={folder.Path}",
                CloseButtonText = "OK"
            };
            await dlg.ShowAsync();
        }

        internal static async Task ExportRadialAlphaKneeSummaryAsync(MainPage page)
        {
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
                double? s,
                double? p,
                int? n,
                int? scale,
                double? dpi,
                int? bin,
                double maxR,
                double p5Max,
                double p40Max,
                double p50Max,
                double p100Max,
                double? p5R099,
                double? p5R095,
                double? p5R090,
                double? p40R099,
                double? p40R095,
                double? p40R090,
                double? p50R099,
                double? p50R095,
                double? p50R090,
                double? p100R099,
                double? p100R095,
                double? p100R090,
                double? p5RMax099,
                double? p5RMax095,
                double? p5RMax090,
                double? p40RMax099,
                double? p40RMax095,
                double? p40RMax090,
                double? p50RMax099,
                double? p50RMax095,
                double? p50RMax090,
                double? p100RMax099,
                double? p100RMax095,
                double? p100RMax090)>();

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
                if (string.Equals(f.Name, "radial-alpha-knee-summary.csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = await FileIO.ReadTextAsync(f);
                if (!TryReadKneePointsFromRadialAlphaCsv(text, out var maxR, out var p5, out var p40, out var p50, out var p100))
                {
                    skipped++;
                    continue;
                }

                var p5Max = MaxOrZero(p5.y);
                var p40Max = MaxOrZero(p40.y);
                var p50Max = MaxOrZero(p50.y);
                var p100Max = MaxOrZero(p100.y);

                // 絶対しきい値(0.99等)は、最大値がレベルに届かない場合は未定義として空欄にする。
                var p5R099 = p5Max >= 0.99 ? FindCrossingR(p5.r, p5.y, 0.99) : null;
                var p5R095 = p5Max >= 0.95 ? FindCrossingR(p5.r, p5.y, 0.95) : null;
                var p5R090 = p5Max >= 0.90 ? FindCrossingR(p5.r, p5.y, 0.90) : null;

                var p40R099 = p40Max >= 0.99 ? FindCrossingR(p40.r, p40.y, 0.99) : null;
                var p40R095 = p40Max >= 0.95 ? FindCrossingR(p40.r, p40.y, 0.95) : null;
                var p40R090 = p40Max >= 0.90 ? FindCrossingR(p40.r, p40.y, 0.90) : null;

                var p50R099 = p50Max >= 0.99 ? FindCrossingR(p50.r, p50.y, 0.99) : null;
                var p50R095 = p50Max >= 0.95 ? FindCrossingR(p50.r, p50.y, 0.95) : null;
                var p50R090 = p50Max >= 0.90 ? FindCrossingR(p50.r, p50.y, 0.90) : null;

                var p100R099 = p100Max >= 0.99 ? FindCrossingR(p100.r, p100.y, 0.99) : null;
                var p100R095 = p100Max >= 0.95 ? FindCrossingR(p100.r, p100.y, 0.95) : null;
                var p100R090 = p100Max >= 0.90 ? FindCrossingR(p100.r, p100.y, 0.90) : null;

                var meta = ParseMetaBestEffortFromRadialAlphaCsvFileName(f.Name);

                rows.Add((
                    fileName: f.Name,
                    s: meta.s,
                    p: meta.p,
                    n: meta.n,
                    scale: meta.scale,
                    dpi: meta.dpi,
                    bin: meta.bin,
                    maxR: maxR,
                    p5Max: p5Max,
                    p40Max: p40Max,
                    p50Max: p50Max,
                    p100Max: p100Max,
                    p5R099: p5R099,
                    p5R095: p5R095,
                    p5R090: p5R090,
                    p40R099: p40R099,
                    p40R095: p40R095,
                    p40R090: p40R090,
                    p50R099: p50R099,
                    p50R095: p50R095,
                    p50R090: p50R090,
                    p100R099: p100R099,
                    p100R095: p100R095,
                    p100R090: p100R090,
                    p5RMax099: p5Max > 0 ? FindCrossingR(p5.r, p5.y, p5Max * 0.99) : null,
                    p5RMax095: p5Max > 0 ? FindCrossingR(p5.r, p5.y, p5Max * 0.95) : null,
                    p5RMax090: p5Max > 0 ? FindCrossingR(p5.r, p5.y, p5Max * 0.90) : null,
                    p40RMax099: p40Max > 0 ? FindCrossingR(p40.r, p40.y, p40Max * 0.99) : null,
                    p40RMax095: p40Max > 0 ? FindCrossingR(p40.r, p40.y, p40Max * 0.95) : null,
                    p40RMax090: p40Max > 0 ? FindCrossingR(p40.r, p40.y, p40Max * 0.90) : null,
                    p50RMax099: p50Max > 0 ? FindCrossingR(p50.r, p50.y, p50Max * 0.99) : null,
                    p50RMax095: p50Max > 0 ? FindCrossingR(p50.r, p50.y, p50Max * 0.95) : null,
                    p50RMax090: p50Max > 0 ? FindCrossingR(p50.r, p50.y, p50Max * 0.90) : null,
                    p100RMax099: p100Max > 0 ? FindCrossingR(p100.r, p100.y, p100Max * 0.99) : null,
                    p100RMax095: p100Max > 0 ? FindCrossingR(p100.r, p100.y, p100Max * 0.95) : null,
                    p100RMax090: p100Max > 0 ? FindCrossingR(p100.r, p100.y, p100Max * 0.90) : null));
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
                var c = Nullable.Compare(x.s, y.s);
                if (c != 0) return c;
                c = Nullable.Compare(x.p, y.p);
                if (c != 0) return c;
                c = Nullable.Compare(x.n, y.n);
                if (c != 0) return c;
                return string.CompareOrdinal(x.fileName, y.fileName);
            });

            var sb = new StringBuilder(capacity: Math.Max(1024, rows.Count * 260));
            sb.AppendLine("file,S,P,N,scale,dpi,bin,max_r,p5_max,p40_max,p50_max,p100_max,p5_r099,p5_r095,p5_r090,p40_r099,p40_r095,p40_r090,p50_r099,p50_r095,p50_r090,p100_r099,p100_r095,p100_r090,p5_rMax099,p5_rMax095,p5_rMax090,p40_rMax099,p40_rMax095,p40_rMax090,p50_rMax099,p50_rMax095,p50_rMax090,p100_rMax099,p100_rMax095,p100_rMax090");

            foreach (var row in rows)
            {
                sb.Append(EscapeCsvCell(row.fileName));
                AppendNullable(sb, row.s);
                AppendNullable(sb, row.p);
                AppendNullable(sb, row.n);
                AppendNullable(sb, row.scale);
                AppendNullable(sb, row.dpi);
                AppendNullable(sb, row.bin);

                sb.Append(',');
                sb.Append(row.maxR.ToString("0.###", CultureInfo.InvariantCulture));

                sb.Append(',');
                sb.Append(row.p5Max.ToString("0.######", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(row.p40Max.ToString("0.######", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(row.p50Max.ToString("0.######", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(row.p100Max.ToString("0.######", CultureInfo.InvariantCulture));

                AppendNullable(sb, row.p5R099);
                AppendNullable(sb, row.p5R095);
                AppendNullable(sb, row.p5R090);
                AppendNullable(sb, row.p40R099);
                AppendNullable(sb, row.p40R095);
                AppendNullable(sb, row.p40R090);
                AppendNullable(sb, row.p50R099);
                AppendNullable(sb, row.p50R095);
                AppendNullable(sb, row.p50R090);
                AppendNullable(sb, row.p100R099);
                AppendNullable(sb, row.p100R095);
                AppendNullable(sb, row.p100R090);

                AppendNullable(sb, row.p5RMax099);
                AppendNullable(sb, row.p5RMax095);
                AppendNullable(sb, row.p5RMax090);
                AppendNullable(sb, row.p40RMax099);
                AppendNullable(sb, row.p40RMax095);
                AppendNullable(sb, row.p40RMax090);
                AppendNullable(sb, row.p50RMax099);
                AppendNullable(sb, row.p50RMax095);
                AppendNullable(sb, row.p50RMax090);
                AppendNullable(sb, row.p100RMax099);
                AppendNullable(sb, row.p100RMax095);
                AppendNullable(sb, row.p100RMax090);

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

        private static (double? s, double? p, int? n, int? scale, double? dpi, int? bin) ParseMetaBestEffortFromRadialAlphaCsvFileName(string csvName)
        {
            // radial-alpha-{pngName}-bin{bin}.csv を想定
            double? s = null;
            double? p = null;
            int? n = null;
            int? scale = null;
            double? dpi = null;
            int? bin = null;

            if (string.IsNullOrWhiteSpace(csvName))
            {
                return (s, p, n, scale, dpi, bin);
            }

            var name = RemoveExtensionSafe(csvName);
            if (name.StartsWith("radial-alpha-", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring("radial-alpha-".Length);
            }

            // bin
            var binTag = "-bin";
            var binPos = name.LastIndexOf(binTag, StringComparison.OrdinalIgnoreCase);
            if (binPos >= 0)
            {
                var binPart = name.Substring(binPos + binTag.Length);
                if (int.TryParse(binPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
                {
                    bin = b;
                }
                name = name.Substring(0, binPos);
            }

            var parts = name.Split('-');
            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;

                if (!s.HasValue && part.Length >= 2 && part[0] == 'S')
                {
                    if (double.TryParse(part.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        s = v;
                    }
                    continue;
                }

                if (!p.HasValue && part.Length >= 2 && part[0] == 'P')
                {
                    if (double.TryParse(part.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        p = v;
                    }
                    continue;
                }

                if (!dpi.HasValue && part.StartsWith("dpi", StringComparison.OrdinalIgnoreCase))
                {
                    if (double.TryParse(part.Substring(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    {
                        dpi = v;
                    }
                    continue;
                }

                if (!scale.HasValue && part.StartsWith("scale", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(part.Substring(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    {
                        scale = v;
                    }
                    continue;
                }

                if (!n.HasValue && part.StartsWith("alignedN", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(part.Substring("alignedN".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    {
                        n = v;
                    }
                    continue;
                }
            }

            return (s, p, n, scale, dpi, bin);
        }

        private static int[] CreateDefaultThresholds()
        {
            // 低圧のMaxαが小さいケースでも `p_ge_*` がゼロ行列になりにくいように、5を含める。
            var list = new List<int>(28) { 1, 5 };
            for (var t = 10; t <= 250; t += 10)
            {
                list.Add(t);
            }
            list.Add(255);

            list.Sort();
            return list.Distinct().ToArray();
        }

        private static string BuildRadialAlphaCsv(RadialAlphaBinAnalysis analysis, int[] thresholds)
        {
            var sb = new StringBuilder(capacity: Math.Max(1024, analysis.Bins.Count * (64 + thresholds.Length * 12)));

            sb.Append("r_bin,px_from,px_to,total,mean_alpha01,cx,cy,max_alpha");
            for (var i = 0; i < thresholds.Length; i++)
            {
                sb.Append(",p_ge_");
                sb.Append(thresholds[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.AppendLine();

            for (var i = 0; i < analysis.Bins.Count; i++)
            {
                var bin = analysis.Bins[i];
                if (bin.Total <= 0)
                {
                    continue;
                }

                var mean01 = (bin.SumAlpha / (double)bin.Total) / 255.0;

                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(bin.PxFrom.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(bin.PxTo.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(bin.Total.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(mean01.ToString("0.########", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(analysis.CenterX.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(analysis.CenterY.ToString("0.###", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(analysis.MaxAlpha.ToString(CultureInfo.InvariantCulture));

                for (var t = 0; t < thresholds.Length; t++)
                {
                    sb.Append(',');
                    var rate = bin.Total > 0 ? (bin.Hits[t] / (double)bin.Total) : 0;
                    sb.Append(rate.ToString("0.########", CultureInfo.InvariantCulture));
                }

                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static RadialAlphaBinAnalysis AnalyzeRadialAlphaBins(byte[] bgra, int width, int height, int binSize, int[] thresholds, bool useAlphaCentroid)
        {
            if (bgra is null) throw new ArgumentNullException(nameof(bgra));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (binSize <= 0) throw new ArgumentOutOfRangeException(nameof(binSize));
            if (thresholds is null) throw new ArgumentNullException(nameof(thresholds));

            var cx = (width - 1) * 0.5;
            var cy = (height - 1) * 0.5;

            // 既定は画像中心。必要なケース（位置ズレ/オフセンター）だけα重心に切り替える。
            if (useAlphaCentroid && TryComputeAlphaCentroid(bgra, width, height, out var ax, out var ay))
            {
                cx = ax;
                cy = ay;
            }
            var maxR = MaxDistanceToCorners(cx, cy, width, height);

            var binCount = Math.Max(1, (int)Math.Ceiling(maxR / binSize));
            var bins = new List<RadialAlphaBin>(binCount);
            for (var i = 0; i < binCount; i++)
            {
                var pxFrom = i * binSize;
                var pxTo = (i + 1) * binSize;
                bins.Add(new RadialAlphaBin(pxFrom, pxTo, thresholds.Length));
            }

            var stride = width * 4;
            byte maxA = 0;
            for (var y = 0; y < height; y++)
            {
                var row = y * stride;
                var dy = y - cy;
                for (var x = 0; x < width; x++)
                {
                    var dx = x - cx;
                    var r = Math.Sqrt(dx * dx + dy * dy);
                    var bin = (int)(r / binSize);
                    if ((uint)bin >= (uint)binCount) continue;

                    var a = bgra[row + (x * 4) + 3];
                    if (a > maxA)
                    {
                        maxA = a;
                    }
                    var b = bins[bin];
                    b.Total++;
                    b.SumAlpha += a;
                    for (var t = 0; t < thresholds.Length; t++)
                    {
                        if (a >= thresholds[t])
                        {
                            b.Hits[t]++;
                        }
                    }
                }
            }

            return new RadialAlphaBinAnalysis(bins, cx, cy, maxA);
        }

        private static bool TryComputeAlphaCentroid(byte[] bgra, int width, int height, out double cx, out double cy)
        {
            cx = 0;
            cy = 0;

            // BGRAのA成分を重みとした重心を求める。
            // すべて透明（sumA==0）の場合は未定義。
            long sumA = 0;
            long sumAx = 0;
            long sumAy = 0;

            var stride = width * 4;
            for (var y = 0; y < height; y++)
            {
                var row = y * stride;
                for (var x = 0; x < width; x++)
                {
                    var a = bgra[row + (x * 4) + 3];
                    if (a == 0) continue;
                    sumA += a;
                    sumAx += (long)a * x;
                    sumAy += (long)a * y;
                }
            }

            if (sumA <= 0)
            {
                return false;
            }

            cx = sumAx / (double)sumA;
            cy = sumAy / (double)sumA;
            return true;
        }

        private static double MaxDistanceToCorners(double cx, double cy, int width, int height)
        {
            var x0 = 0.0;
            var x1 = width - 1.0;
            var y0 = 0.0;
            var y1 = height - 1.0;

            var d00 = Distance(cx, cy, x0, y0);
            var d10 = Distance(cx, cy, x1, y0);
            var d01 = Distance(cx, cy, x0, y1);
            var d11 = Distance(cx, cy, x1, y1);

            return Math.Max(Math.Max(d00, d10), Math.Max(d01, d11));
        }

        private static double Distance(double x0, double y0, double x1, double y1)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool TryReadKneePointsFromRadialAlphaCsv(
            string csv,
            out double maxR,
            out (List<double> r, List<double> y) p5,
            out (List<double> r, List<double> y) p40,
            out (List<double> r, List<double> y) p50,
            out (List<double> r, List<double> y) p100)
        {
            maxR = 0;
            p5 = (new List<double>(512), new List<double>(512));
            p40 = (new List<double>(512), new List<double>(512));
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
            var idxP5 = IndexOf(header, "p_ge_5");
            var idxP40 = IndexOf(header, "p_ge_40");
            var idxP50 = IndexOf(header, "p_ge_50");
            var idxP100 = IndexOf(header, "p_ge_100");

            if (idxPxFrom < 0 || idxPxTo < 0 || idxP5 < 0 || idxP40 < 0 || idxP50 < 0 || idxP100 < 0)
            {
                return false;
            }

            for (var i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');
                var needMax = Math.Max(Math.Max(idxPxFrom, idxPxTo), Math.Max(Math.Max(idxP5, idxP40), Math.Max(idxP50, idxP100)));
                if (cols.Length <= needMax)
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

                if (!double.TryParse(cols[idxP5], NumberStyles.Float, CultureInfo.InvariantCulture, out var v5))
                {
                    continue;
                }
                if (!double.TryParse(cols[idxP40], NumberStyles.Float, CultureInfo.InvariantCulture, out var v40))
                {
                    continue;
                }
                if (!double.TryParse(cols[idxP50], NumberStyles.Float, CultureInfo.InvariantCulture, out var v50))
                {
                    continue;
                }
                if (!double.TryParse(cols[idxP100], NumberStyles.Float, CultureInfo.InvariantCulture, out var v100))
                {
                    continue;
                }

                p5.r.Add(rCenter);
                p5.y.Add(v5);
                p40.r.Add(rCenter);
                p40.y.Add(v40);
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

            // 最初にレベルを割り込んだ位置を返す（yは中心から外側に向けて単調減少を期待）。
            for (var i = 0; i < y.Count; i++)
            {
                if (y[i] >= level)
                {
                    continue;
                }

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

                var t = (level - y0) / dy;
                return r0 + (t * (r1 - r0));
            }

            return null;
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

        private static int ReadIntFromTextBox(MainPage page, string name, int fallback)
        {
            var tb = page.FindName(name) as TextBox;
            if (tb == null) return fallback;
            var s = (tb.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s)) return fallback;

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return v;
            return fallback;
        }

        private static bool ReadBoolFromCheckBox(MainPage page, string name, bool fallback)
        {
            var cb = page.FindName(name) as CheckBox;
            if (cb == null) return fallback;
            return cb.IsChecked == true;
        }

        private static void AppendNullable(StringBuilder sb, double? v)
        {
            sb.Append(',');
            if (v.HasValue)
            {
                sb.Append(v.Value.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        private static void AppendNullable(StringBuilder sb, int? v)
        {
            sb.Append(',');
            if (v.HasValue)
            {
                sb.Append(v.Value.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static string EscapeCsvCell(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            if (s.IndexOf(',') >= 0 || s.IndexOf('"') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\r') >= 0)
            {
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            }
            return s;
        }

        private static string RemoveExtensionSafe(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            var dot = fileName.LastIndexOf('.');
            if (dot <= 0) return fileName;
            return fileName.Substring(0, dot);
        }

        private sealed class RadialAlphaBinAnalysis
        {
            internal RadialAlphaBinAnalysis(List<RadialAlphaBin> bins, double centerX, double centerY, byte maxAlpha)
            {
                Bins = bins ?? throw new ArgumentNullException(nameof(bins));
                CenterX = centerX;
                CenterY = centerY;
                MaxAlpha = maxAlpha;
            }

            internal List<RadialAlphaBin> Bins { get; }

            internal double CenterX { get; }
            internal double CenterY { get; }

            internal byte MaxAlpha { get; }
        }

        private sealed class RadialAlphaBin
        {
            internal RadialAlphaBin(double pxFrom, double pxTo, int thresholdCount)
            {
                PxFrom = pxFrom;
                PxTo = pxTo;
                Hits = new int[thresholdCount];
            }

            internal double PxFrom { get; }
            internal double PxTo { get; }
            internal int Total { get; set; }
            internal long SumAlpha { get; set; }
            internal int[] Hits { get; }
        }
    }
}
