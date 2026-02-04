using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class AlignedDiffSeriesMaskedAnalyzer
{
    private static readonly Regex AlignedNRegex = new(@"alignedN(?<n>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex PressureRegex = new(@"-P(?<p>\d+(?:\.\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static async Task AnalyzeAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // alignedN*.png を選択
        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = true,
            Title = "alignedN*.png を複数選択"
        };
        if (open.ShowDialog(window) != true) return;

        var paths = open.FileNames
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .ToArray();
        if (paths.Length < 2) return;

        // mask.png を選択（白=対象、A>0=対象）
        open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "mask PNG を選択（白/α>0 が対象）"
        };
        if (open.ShowDialog(window) != true) return;
        var maskPath = open.FileName;

        using var maskBmp = SKBitmap.Decode(maskPath);
        if (maskBmp is null) return;

        // maskサイズが一致しない場合、解析対象の画像サイズに合わせて最近傍でリサイズする。
        // （マスクは2値/被覆率前提で、最近傍が適する）
        SKBitmap? resizedMask = null;

        var items = paths
            .Select(p => new { Path = p, N = TryParseAlignedN(p), P = TryParsePressure(p) })
            .Where(x => x.N is not null)
            .Select(x => new Item(x.Path, x.N!.Value, x.P))
            .ToList();

        var groups = items
            .GroupBy(x => x.PKey)
            .OrderBy(g => g.Key)
            .ToList();

        if (groups.Sum(g => g.Count()) < 2)
        {
            System.Windows.MessageBox.Show(window, "alignedN<number> をファイル名から検出できませんでした。", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        folderPicker.FileTypeFilter.Add(".csv");
        var hwnd = new WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(folderPicker, hwnd);
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder is null) return;

        // ファイル名に P/S/Scale を含めて識別しやすくする（可能な範囲で抽出）
        var sTag = TryParseTagInt(paths, @"-S(?<v>\d+)")?.ToString(CultureInfo.InvariantCulture) ?? "S?";
        var scaleTag = TryParseTagInt(paths, @"-scale(?<v>\d+)")?.ToString(CultureInfo.InvariantCulture) ?? "scale?";
        var ps = items.Select(x => x.PKey).Distinct().OrderBy(x => x).ToArray();
        var pTag = ps.Length == 1 ? $"P{ps[0]}" : $"P{ps.FirstOrDefault() ?? "?"}-multi";
        var outName = $"aligned-diff-series-masked-{sTag}-{pTag}-{scaleTag}-{DateTime.Now:yyyyMMdd-HHmmss}.csv";
        outName = SanitizeFileName(outName);
        var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);

        var featuresTableName = Path.GetFileNameWithoutExtension(outName) + "-features-table.csv";
        featuresTableName = SanitizeFileName(featuresTableName);
        var featuresFile = await folder.CreateFileAsync(featuresTableName, CreationCollisionOption.ReplaceExisting);

        var sb = new StringBuilder(64 * 1024);
        var sbFeat = new StringBuilder(8 * 1024);
        sb.AppendLine("pressure,prev_file,next_file,prev_n,next_n,width,height,mask_nonzero_px,diff_nonzero_px,diff_nonzero_ratio,diff_abs_mean,diff_abs_stddev,diff_abs_max,diff_signed_mean,diff_signed_stddev,diff_signed_min,diff_signed_max");
        sbFeat.AppendLine("pressure,first_abs_mean01,first_nonzero_ratio,peak_abs_mean01,peak_nonzero_ratio,steady_tail_abs_mean01,steady_tail_nonzero_ratio,first_vs_peak_abs,first_vs_steady_abs,first_vs_peak_area,first_vs_steady_area");

        var skippedDecode = 0;
        var skippedSizeMismatch = 0;
        var skippedMaskEmpty = 0;

        var wroteAny = false;

        foreach (var g in groups)
        {
            var ordered = g.OrderBy(x => x.N).ToList();
            if (ordered.Count < 2) continue;

            // 仮想N0（全α=0）を追加して N0->N1 の統計を出せるようにする
            if (ordered[0].N == 1)
            {
                ordered.Insert(0, new Item(Path: "(virtual N0)", N: 0, Pressure01: ordered[0].Pressure01));
            }

            // group summary用の蓄積
            double? firstAbsMean01 = null;
            double? firstNonzeroRatio = null;
            double peakAbsMean01 = 0.0;
            double peakNonzeroRatio = 0.0;
            double sumTailAbsMean01 = 0.0;
            double sumTailNonzeroRatio = 0.0;
            int tailCount = 0;
            const int tailFromNextN = 9; // next_n >= 9 を末尾領域として扱う（N9..N12）

            for (var i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var next = ordered[i];

                using var bmpPrev = prev.N == 0 ? null : SKBitmap.Decode(prev.Path);
                using var bmpNext = SKBitmap.Decode(next.Path);
            if (bmpPrev is null || bmpNext is null)
            {
                    if (prev.N == 0 && bmpNext is not null)
                    {
                        // ok (virtual)
                    }
                    else
                    {
                        skippedDecode++;
                        continue;
                    }
            }
                if (bmpPrev is not null && (bmpPrev.Width != bmpNext.Width || bmpPrev.Height != bmpNext.Height))
            {
                skippedSizeMismatch++;
                continue;
            }

                var w = bmpNext.Width;
                var h = bmpNext.Height;

            // maskを必要に応じてリサイズ
            var mask = maskBmp;
            if (mask.Width != w || mask.Height != h)
            {
                resizedMask?.Dispose();
                resizedMask = new SKBitmap(w, h, maskBmp.ColorType, maskBmp.AlphaType);
                using var pixmap = resizedMask.PeekPixels();
                using var srcPixmap = maskBmp.PeekPixels();
                // 最近傍リサイズ
                for (var y = 0; y < h; y++)
                {
                    var sy = (int)Math.Floor(y * (maskBmp.Height / (double)h));
                    sy = Math.Clamp(sy, 0, maskBmp.Height - 1);
                    for (var x = 0; x < w; x++)
                    {
                        var sx = (int)Math.Floor(x * (maskBmp.Width / (double)w));
                        sx = Math.Clamp(sx, 0, maskBmp.Width - 1);
                        resizedMask.SetPixel(x, y, maskBmp.GetPixel(sx, sy));
                    }
                }
                mask = resizedMask;
            }

            long sumAbs = 0;
            long sumAbsSq = 0;
            var maxAbs = 0;

            long sumSigned = 0;
            long sumSignedSq = 0;
            var minSigned = int.MaxValue;
            var maxSigned = int.MinValue;
            long maskCount = 0;
            long diffNonzeroCount = 0;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var ma = mask.GetPixel(x, y).Alpha;
                    if (ma == 0) continue;
                    maskCount++;

                    var a0 = bmpPrev is null ? (byte)0 : bmpPrev.GetPixel(x, y).Alpha;
                    var a1 = bmpNext.GetPixel(x, y).Alpha;
                    var ds = (int)a1 - (int)a0;
                    var da = Math.Abs(ds);

                    if (da != 0)
                    {
                        diffNonzeroCount++;
                    }

                    sumAbs += da;
                    sumAbsSq += (long)da * da;
                    if (da > maxAbs) maxAbs = da;

                    sumSigned += ds;
                    sumSignedSq += (long)ds * ds;
                    if (ds < minSigned) minSigned = ds;
                    if (ds > maxSigned) maxSigned = ds;
                }
            }

            var absMean = maskCount == 0 ? 0.0 : sumAbs / (double)maskCount;
            var absVar = maskCount == 0 ? 0.0 : (sumAbsSq / (double)maskCount) - (absMean * absMean);
            var absStd = absVar <= 0 ? 0.0 : Math.Sqrt(absVar);

            var signedMean = maskCount == 0 ? 0.0 : sumSigned / (double)maskCount;
            var signedVar = maskCount == 0 ? 0.0 : (sumSignedSq / (double)maskCount) - (signedMean * signedMean);
            var signedStd = signedVar <= 0 ? 0.0 : Math.Sqrt(signedVar);

            if (maskCount == 0)
            {
                skippedMaskEmpty++;
                continue;
            }

            sb.Append(prev.PKey).Append(',');
            sb.Append(Escape(Path.GetFileName(prev.Path))).Append(',');
            sb.Append(Escape(Path.GetFileName(next.Path))).Append(',');
            sb.Append(prev.N.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(next.N.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(w.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(h.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(maskCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(diffNonzeroCount.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append((maskCount == 0 ? 0.0 : (diffNonzeroCount / (double)maskCount)).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            var absMean01 = absMean / 255.0;
            var absStd01 = absStd / 255.0;
            var maxAbs01 = maxAbs / 255.0;
            var signedMean01 = signedMean / 255.0;
            var signedStd01 = signedStd / 255.0;
            var minSigned01 = minSigned / 255.0;
            var maxSigned01 = maxSigned / 255.0;

            sb.Append(absMean01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(absStd01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(maxAbs01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(signedMean01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(signedStd01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(minSigned01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(maxSigned01.ToString("0.########", CultureInfo.InvariantCulture));
            sb.AppendLine();

            wroteAny = true;

            // summary更新
            if (prev.N == 0 && next.N == 1)
            {
                firstAbsMean01 = absMean01;
                firstNonzeroRatio = (maskCount == 0 ? 0.0 : (diffNonzeroCount / (double)maskCount));
            }
            if (next.N >= 2)
            {
                var nonzeroRatio = (maskCount == 0 ? 0.0 : (diffNonzeroCount / (double)maskCount));
                if (absMean01 > peakAbsMean01)
                {
                    peakAbsMean01 = absMean01;
                    peakNonzeroRatio = nonzeroRatio;
                }
                if (next.N >= tailFromNextN)
                {
                    sumTailAbsMean01 += absMean01;
                    sumTailNonzeroRatio += nonzeroRatio;
                    tailCount++;
                }
            }
            }

            // group summary行（first vs peak/steady）
            if (firstAbsMean01 is not null)
            {
                var steady = tailCount == 0 ? 0.0 : (sumTailAbsMean01 / tailCount);
                var steadyArea = tailCount == 0 ? 0.0 : (sumTailNonzeroRatio / tailCount);
                var firstVsPeak = peakAbsMean01 <= 0 ? 0.0 : (firstAbsMean01.Value / peakAbsMean01);
                var firstVsSteady = steady <= 0 ? 0.0 : (firstAbsMean01.Value / steady);

                var firstArea = firstNonzeroRatio ?? 0.0;
                var firstVsPeakArea = peakNonzeroRatio <= 0 ? 0.0 : (firstArea / peakNonzeroRatio);
                var firstVsSteadyArea = steadyArea <= 0 ? 0.0 : (firstArea / steadyArea);

                sb.Append("# summary_group,");
                sb.Append(g.Key).Append(',');
                sb.Append("first_abs_mean01=").Append(firstAbsMean01.Value.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("first_nonzero_ratio=").Append(firstArea.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("peak_abs_mean01=").Append(peakAbsMean01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("peak_nonzero_ratio=").Append(peakNonzeroRatio.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("steady_tail_abs_mean01=").Append(steady.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("steady_tail_nonzero_ratio=").Append(steadyArea.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("first_vs_peak=").Append(firstVsPeak.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("first_vs_steady=").Append(firstVsSteady.ToString("0.########", CultureInfo.InvariantCulture));
                sb.AppendLine();

                sb.Append("# features,");
                sb.Append(g.Key).Append(',');
                sb.Append("first_vs_peak_abs=").Append(firstVsPeak.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("first_vs_steady_abs=").Append(firstVsSteady.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("first_vs_peak_area=").Append(firstVsPeakArea.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("first_vs_steady_area=").Append(firstVsSteadyArea.ToString("0.########", CultureInfo.InvariantCulture));
                sb.AppendLine();

                // 別CSV（テーブル形式）にも追記
                sbFeat.Append(g.Key).Append(',');
                sbFeat.Append(firstAbsMean01.Value.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sbFeat.Append(firstArea.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sbFeat.Append(peakAbsMean01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sbFeat.Append(peakNonzeroRatio.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sbFeat.Append(steady.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sbFeat.Append(steadyArea.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sbFeat.Append(firstVsPeak.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sbFeat.Append(firstVsSteady.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sbFeat.Append(firstVsPeakArea.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sbFeat.Append(firstVsSteadyArea.ToString("0.########", CultureInfo.InvariantCulture));
                sbFeat.AppendLine();
            }
        }

        resizedMask?.Dispose();

        sb.AppendLine();
        sb.AppendLine($"# summary,wrote_any={wroteAny.ToString(CultureInfo.InvariantCulture)},skipped_decode={skippedDecode},skipped_size_mismatch={skippedSizeMismatch},skipped_mask_empty={skippedMaskEmpty}");

        await FileIO.WriteTextAsync(outFile, sb.ToString());
        await FileIO.WriteTextAsync(featuresFile, sbFeat.ToString());
    }

    private sealed record Item(string Path, int N, double? Pressure01)
    {
        public string PKey => Pressure01 is null ? "(unknown)" : Pressure01.Value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static int? TryParseAlignedN(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var m = AlignedNRegex.Match(name);
        if (!m.Success) return null;
        if (!int.TryParse(m.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return null;
        return n;
    }

    private static double? TryParsePressure(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var m = PressureRegex.Match(name);
        if (!m.Success) return null;
        if (!double.TryParse(m.Groups["p"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return null;
        return p;
    }

    private static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r'))
        {
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        return s;
    }

    private static int? TryParseTagInt(IEnumerable<string> paths, string pattern)
    {
        var re = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        foreach (var p in paths)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(p);
            var m = re.Match(name);
            if (!m.Success) continue;
            if (int.TryParse(m.Groups["v"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }
        }
        return null;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}
