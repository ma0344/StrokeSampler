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
    private static readonly Regex DupRegex = new(@"-dup(?<dup>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static async Task AnalyzeAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.Title = "DotLab - AlignedDiffSeries (running...)";

        // Input folder (auto enumerate)
        var inputPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        inputPicker.FileTypeFilter.Add(".png");
        var hwnd = new WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(inputPicker, hwnd);
        var inputFolder = await inputPicker.PickSingleFolderAsync();
        if (inputFolder is null) return;

        var paths = (await inputFolder.GetFilesAsync())
            .Select(f => f.Path)
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p) && TryParseAlignedN(p) is not null)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length < 2)
        {
            System.Windows.MessageBox.Show(window, "フォルダ内に alignedN<number> を含むPNGが見つかりませんでした。", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            window.Title = "DotLab";
            return;
        }

        // Optional mask
        var openMask = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = false,
            Title = "mask PNG を選択（任意。キャンセルで全画面）"
        };

        SKBitmap? maskBmp = null;
        if (openMask.ShowDialog(window) == true)
        {
            var maskPath = openMask.FileName;
            if (!string.IsNullOrWhiteSpace(maskPath) && File.Exists(maskPath))
            {
                maskBmp = SKBitmap.Decode(maskPath);
            }
        }

        SKBitmap? resizedMask = null;

        var items = paths
            .Select(p => new { Path = p, N = TryParseAlignedN(p), P = TryParsePressureText(p), Dup = TryParseDupText(p) })
            .Where(x => x.N is not null)
            .Select(x => new Item(x.Path, x.N!.Value, x.P, x.Dup))
            .ToList();

        if (items.Count == 0)
        {
            System.Windows.MessageBox.Show(window, "No alignedN images found.", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            window.Title = "DotLab";
            return;
        }

        static decimal? ParsePressureAsDecimal(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        }

        static string? ExtractPressureFromGroupKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var i = key.IndexOf('|');
            return i < 0 ? key : key[..i];
        }

        var groups = items
            .GroupBy(x => x.GroupKey)
            .OrderBy(g => ParsePressureAsDecimal(ExtractPressureFromGroupKey(g.Key)) ?? decimal.MaxValue)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();

        if (groups.Sum(g => g.Count()) < 2)
        {
            System.Windows.MessageBox.Show(window, "alignedN<number> をファイル名から検出できませんでした。", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            window.Title = "DotLab";
            return;
        }

        // Output folder
        var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        folderPicker.FileTypeFilter.Add(".csv");
        InitializeWithWindow.Initialize(folderPicker, hwnd);
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder is null)
        {
            window.Title = "DotLab";
            return;
        }

        var sTag = TryParseTagInt(paths, @"-S(?<v>\d+)")?.ToString(CultureInfo.InvariantCulture) ?? "S?";
        var scaleTag = TryParseTagInt(paths, @"-scale(?<v>\d+)")?.ToString(CultureInfo.InvariantCulture) ?? "scale?";
        var ps = items
            .Select(x => x.PKey)
            .Distinct()
            .OrderBy(x => ParsePressureAsDecimal(x) ?? decimal.MaxValue)
            .ThenBy(x => x, StringComparer.Ordinal)
            .ToArray();
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
        var decodeFailSamples = new List<string>();

        var wroteAny = false;

        var totalGroups = groups.Count;
        var processedGroups = 0;
        var lastUiUpdate = DateTime.UtcNow;

        foreach (var g in groups)
        {
            processedGroups++;
            if ((DateTime.UtcNow - lastUiUpdate).TotalSeconds >= 1)
            {
                window.Title = $"DotLab - AlignedDiffSeries ({processedGroups}/{totalGroups})";
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                lastUiUpdate = DateTime.UtcNow;
            }

            var ordered = g.OrderBy(x => x.N).ToList();
            if (ordered.Count < 2) continue;

            if (ordered[0].N == 1)
            {
                ordered.Insert(0, new Item(Path: "(virtual N0)", N: 0, PressureText: ordered[0].PressureText, DupText: ordered[0].DupText));
            }

            double? firstAbsMean01 = null;
            double? firstNonzeroRatio = null;
            double peakAbsMean01 = 0.0;
            double peakNonzeroRatio = 0.0;
            double sumTailAbsMean01 = 0.0;
            double sumTailNonzeroRatio = 0.0;
            var tailCount = 0;
            const int tailFromNextN = 9;

            for (var i = 1; i < ordered.Count; i++)
            {
                var prev = ordered[i - 1];
                var next = ordered[i];

                using var bmpPrev = prev.N == 0 ? null : TryDecodeBitmap(prev.Path, decodeFailSamples);
                using var bmpNext = TryDecodeBitmap(next.Path, decodeFailSamples);

                if (bmpNext is null || (prev.N != 0 && bmpPrev is null))
                {
                    skippedDecode++;
                    continue;
                }

                if (bmpPrev is not null && (bmpPrev.Width != bmpNext.Width || bmpPrev.Height != bmpNext.Height))
                {
                    skippedSizeMismatch++;
                    continue;
                }

                var w = bmpNext.Width;
                var h = bmpNext.Height;

                var mask = maskBmp;
                if (mask is null)
                {
                    if (resizedMask is null || resizedMask.Width != w || resizedMask.Height != h)
                    {
                        resizedMask?.Dispose();
                        resizedMask = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
                        resizedMask.Erase(new SKColor(255, 255, 255, 255));
                    }
                    mask = resizedMask;
                }

                if (mask.Width != w || mask.Height != h)
                {
                    resizedMask?.Dispose();
                    resizedMask = new SKBitmap(w, h, mask.ColorType, mask.AlphaType);
                    for (var y = 0; y < h; y++)
                    {
                        var sy = (int)Math.Floor(y * (mask.Height / (double)h));
                        sy = Math.Clamp(sy, 0, mask.Height - 1);
                        for (var x = 0; x < w; x++)
                        {
                            var sx = (int)Math.Floor(x * (mask.Width / (double)w));
                            sx = Math.Clamp(sx, 0, mask.Width - 1);
                            resizedMask.SetPixel(x, y, mask.GetPixel(sx, sy));
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

                if (!TryAccumulateDiffStatsFast(
                    bmpPrev,
                    bmpNext,
                    mask,
                    w,
                    h,
                    ref sumAbs,
                    ref sumAbsSq,
                    ref maxAbs,
                    ref sumSigned,
                    ref sumSignedSq,
                    ref minSigned,
                    ref maxSigned,
                    ref maskCount,
                    ref diffNonzeroCount))
                {
                    // fallback (should be rare)
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

                            if (da != 0) diffNonzeroCount++;
                            sumAbs += da;
                            sumAbsSq += (long)da * da;
                            if (da > maxAbs) maxAbs = da;
                            sumSigned += ds;
                            sumSignedSq += (long)ds * ds;
                            if (ds < minSigned) minSigned = ds;
                            if (ds > maxSigned) maxSigned = ds;
                        }
                    }
                }

                if (maskCount == 0)
                {
                    skippedMaskEmpty++;
                    continue;
                }

                var absMean = sumAbs / (double)maskCount;
                var absVar = (sumAbsSq / (double)maskCount) - (absMean * absMean);
                var absStd = absVar <= 0 ? 0.0 : Math.Sqrt(absVar);

                var signedMean = sumSigned / (double)maskCount;
                var signedVar = (sumSignedSq / (double)maskCount) - (signedMean * signedMean);
                var signedStd = signedVar <= 0 ? 0.0 : Math.Sqrt(signedVar);

                sb.Append(prev.PKey).Append(',');
                sb.Append(Escape(Path.GetFileName(prev.Path))).Append(',');
                sb.Append(Escape(Path.GetFileName(next.Path))).Append(',');
                sb.Append(prev.N.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(next.N.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(w.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(h.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(maskCount.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(diffNonzeroCount.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append((diffNonzeroCount / (double)maskCount).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');

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

                if (prev.N == 0 && next.N == 1)
                {
                    firstAbsMean01 = absMean01;
                    firstNonzeroRatio = diffNonzeroCount / (double)maskCount;
                }

                if (next.N >= 2)
                {
                    var nonzeroRatio = diffNonzeroCount / (double)maskCount;
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

            if (firstAbsMean01 is not null)
            {
                var steady = tailCount == 0 ? 0.0 : (sumTailAbsMean01 / tailCount);
                var steadyArea = tailCount == 0 ? 0.0 : (sumTailNonzeroRatio / tailCount);
                var firstVsPeak = peakAbsMean01 <= 0 ? 0.0 : (firstAbsMean01.Value / peakAbsMean01);
                var firstVsSteady = steady <= 0 ? 0.0 : (firstAbsMean01.Value / steady);

                var firstArea = firstNonzeroRatio ?? 0.0;
                var firstVsPeakArea = peakNonzeroRatio <= 0 ? 0.0 : (firstArea / peakNonzeroRatio);
                var firstVsSteadyArea = steadyArea <= 0 ? 0.0 : (firstArea / steadyArea);

                var pKey = ExtractPressureFromGroupKey(g.Key) ?? "(unknown)";

                sb.Append("# summary_group,");
                sb.Append(pKey).Append(',');
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
                sb.Append(pKey).Append(',');
                sb.Append("first_vs_peak_abs=").Append(firstVsPeak.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("first_vs_steady_abs=").Append(firstVsSteady.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("first_vs_peak_area=").Append(firstVsPeakArea.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append("first_vs_steady_area=").Append(firstVsSteadyArea.ToString("0.########", CultureInfo.InvariantCulture));
                sb.AppendLine();

                sbFeat.Append(pKey).Append(',');
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
        maskBmp?.Dispose();

        sb.AppendLine();
        sb.AppendLine($"# summary,wrote_any={wroteAny.ToString(CultureInfo.InvariantCulture)},skipped_decode={skippedDecode},skipped_size_mismatch={skippedSizeMismatch},skipped_mask_empty={skippedMaskEmpty}");

        await FileIO.WriteTextAsync(outFile, sb.ToString());
        await FileIO.WriteTextAsync(featuresFile, sbFeat.ToString());

        if (!wroteAny)
        {
            System.Windows.MessageBox.Show(
                window,
                $"No rows were written.\n" +
                $"paths={paths.Length}\n" +
                $"groups={groups.Count}\n" +
                $"skipped_decode={skippedDecode}\n" +
                $"skipped_size_mismatch={skippedSizeMismatch}\n" +
                $"skipped_mask_empty={skippedMaskEmpty}\n" +
                (decodeFailSamples.Count > 0 ? ("decode_fail_samples:\n" + string.Join("\n", decodeFailSamples.Take(5))) : ""),
                "DotLab",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
        }

        window.Title = "DotLab";
    }

    private sealed record Item(string Path, int N, string? PressureText, string? DupText)
    {
        public string PKey => string.IsNullOrWhiteSpace(PressureText) ? "(unknown)" : PressureText;
        public string DupKey => string.IsNullOrWhiteSpace(DupText) ? "(nodup)" : DupText;
        public string GroupKey => $"{PKey}|{DupKey}";
    }

    private static int? TryParseAlignedN(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var m = AlignedNRegex.Match(name);
        if (!m.Success) return null;
        if (!int.TryParse(m.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return null;
        return n;
    }

    private static string? TryParsePressureText(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var m = PressureRegex.Match(name);
        if (!m.Success) return null;
        return m.Groups["p"].Value;
    }

    private static string? TryParseDupText(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var m = DupRegex.Match(name);
        if (!m.Success) return null;
        return m.Groups["dup"].Value;
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

    private static SKBitmap? TryDecodeBitmap(string path, List<string> failSamples)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                if (failSamples.Count < 20) failSamples.Add($"(missing) {path}");
                return null;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var bmp = SKBitmap.Decode(fs);
            if (bmp is null && failSamples.Count < 20)
            {
                failSamples.Add(path);
            }
            return bmp;
        }
        catch (Exception ex)
        {
            if (failSamples.Count < 20) failSamples.Add($"{path} ({ex.GetType().Name})");
            return null;
        }
    }

    private static unsafe bool TryAccumulateDiffStatsFast(
        SKBitmap? bmpPrev,
        SKBitmap bmpNext,
        SKBitmap mask,
        int w,
        int h,
        ref long sumAbs,
        ref long sumAbsSq,
        ref int maxAbs,
        ref long sumSigned,
        ref long sumSignedSq,
        ref int minSigned,
        ref int maxSigned,
        ref long maskCount,
        ref long diffNonzeroCount)
    {
        using var pixMask = mask.PeekPixels();
        using var pixNext = bmpNext.PeekPixels();
        using var pixPrev = bmpPrev is null ? null : bmpPrev.PeekPixels();

        if (!TryGetAlphaOffset(pixMask.ColorType, out var aOffMask)) return false;
        if (!TryGetAlphaOffset(pixNext.ColorType, out var aOffNext)) return false;

        var hasPrev = pixPrev is not null;
        var aOffPrev = 0;
        if (hasPrev && !TryGetAlphaOffset(pixPrev!.ColorType, out aOffPrev)) return false;

        var bytesMask = new ReadOnlySpan<byte>((void*)pixMask.GetPixels(), pixMask.RowBytes * pixMask.Height);
        var bytesNext = new ReadOnlySpan<byte>((void*)pixNext.GetPixels(), pixNext.RowBytes * pixNext.Height);
        ReadOnlySpan<byte> bytesPrev = default;
        if (hasPrev)
        {
            bytesPrev = new ReadOnlySpan<byte>((void*)pixPrev!.GetPixels(), pixPrev.RowBytes * pixPrev.Height);
        }

        for (var y = 0; y < h; y++)
        {
            var rowMask = y * pixMask.RowBytes;
            var rowNext = y * pixNext.RowBytes;
            var rowPrev = hasPrev ? (y * pixPrev!.RowBytes) : 0;

            for (var x = 0; x < w; x++)
            {
                var ma = bytesMask[rowMask + x * 4 + aOffMask];
                if (ma == 0) continue;
                maskCount++;

                var a1 = bytesNext[rowNext + x * 4 + aOffNext];
                var a0 = hasPrev ? bytesPrev[rowPrev + x * 4 + aOffPrev] : (byte)0;

                var ds = (int)a1 - (int)a0;
                var da = ds < 0 ? -ds : ds;

                if (da != 0) diffNonzeroCount++;
                sumAbs += da;
                sumAbsSq += (long)da * da;
                if (da > maxAbs) maxAbs = da;

                sumSigned += ds;
                sumSignedSq += (long)ds * ds;
                if (ds < minSigned) minSigned = ds;
                if (ds > maxSigned) maxSigned = ds;
            }
        }

        return true;
    }

    private static bool TryGetAlphaOffset(SKColorType colorType, out int alphaOffset)
    {
        // Expect 32bpp decoded PNGs.
        switch (colorType)
        {
            case SKColorType.Bgra8888:
            case SKColorType.Rgba8888:
                alphaOffset = 3;
                return true;
            default:
                alphaOffset = 0;
                return false;
        }
    }
}
