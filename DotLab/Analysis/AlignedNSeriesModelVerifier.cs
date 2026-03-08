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

internal static class AlignedNSeriesModelVerifier
{
    private enum ModelMode
    {
        ClosedForm,
        Stepwise,
        SourceOver255,
        SourceOver255Cap,
    }

    private enum QuantizeMode
    {
        Floor,
        RoundAwayFromZero,
    }

    internal static async Task AnalyzeAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var open = new OpenFileDialog
        {
            Filter = "PNG (*.png)|*.png",
            Multiselect = true,
            Title = "alignedN系列のPNG（N1, N2/4/8, 飽和N）をまとめて選択"
        };
        if (open.ShowDialog(window) != true) return;

        var paths = open.FileNames;
        if (paths is null || paths.Length == 0) return;

        var byN = new Dictionary<int, string>();
        var unparsed = new List<string>();

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;

            if (!TryParseAlignedNFromFileName(path, out var n) || n <= 0)
            {
                unparsed.Add(path);
                continue;
            }

            if (!byN.ContainsKey(n))
            {
                byN.Add(n, path);
            }
        }

        if (!byN.TryGetValue(1, out var n1Path))
        {
            System.Windows.MessageBox.Show(window, "alignedN1 のPNGが選択されていません。", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var capN = byN.Keys.Max();
        if (capN <= 1)
        {
            System.Windows.MessageBox.Show(window, "飽和（alignedNが最大のPNG）が見つかりません。alignedN1024 等を含めて選択してください。", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var capPath = byN[capN];

        using var n1Bmp = SKBitmap.Decode(n1Path);
        using var capBmp = SKBitmap.Decode(capPath);
        if (n1Bmp is null || capBmp is null)
        {
            System.Windows.MessageBox.Show(window, "PNGの読み込みに失敗しました。", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        if (n1Bmp.Width != capBmp.Width || n1Bmp.Height != capBmp.Height)
        {
            System.Windows.MessageBox.Show(window, "alignedN1と飽和PNGのサイズが一致しません。", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        var w = n1Bmp.Width;
        var h = n1Bmp.Height;
        var pixelCount = checked(w * h);

        var a = new byte[pixelCount];
        var cap = new byte[pixelCount];

        var n1Px = n1Bmp.Pixels;
        var capPx = capBmp.Pixels;

        for (var i = 0; i < pixelCount; i++)
        {
            a[i] = n1Px[i].Alpha;
            cap[i] = capPx[i].Alpha;
        }

        var folder = await PickOutputFolderAsync(window);
        if (folder is null) return;

        var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var baseName = "alignedN-model-verify-" + Path.GetFileNameWithoutExtension(n1Path);

        // 参照用にa/capをPNGで出力
        await SaveGray8PngAsync(folder, $"{baseName}-a-N1-{ts}.png", w, h, a);
        await SaveGray8PngAsync(folder, $"{baseName}-cap-N{capN}-{ts}.png", w, h, cap);

        var summary = new StringBuilder(16 * 1024);
        summary.AppendLine("n_actual,cap_n,model,quantize,mean_abs_diff,rmse,max_abs_diff,mismatch_px,actual_nonzero_px,pred_nonzero_px");

        var nsToCheck = new[] { 2, 4, 8 };
        var checkedAny = false;

        foreach (var nActual in nsToCheck)
        {
            if (!byN.TryGetValue(nActual, out var actualPath))
            {
                continue;
            }

            using var actualBmp = SKBitmap.Decode(actualPath);
            if (actualBmp is null)
            {
                continue;
            }
            if (actualBmp.Width != w || actualBmp.Height != h)
            {
                System.Windows.MessageBox.Show(window, $"alignedN{nActual} のサイズが一致しません。", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            checkedAny = true;

            var lut = BuildPredictionLut(nActual);

            var actualPx = actualBmp.Pixels;
            var actualAlpha = new byte[pixelCount];
            for (var i = 0; i < pixelCount; i++)
            {
                actualAlpha[i] = actualPx[i].Alpha;
            }

            var closedFloor = EvaluateClosedForm(nActual, capN, a, cap, actualAlpha, QuantizeMode.Floor, lut);
            var closedRound = EvaluateClosedForm(nActual, capN, a, cap, actualAlpha, QuantizeMode.RoundAwayFromZero, lut);

            var stepFloor = EvaluateStepwise(nActual, capN, a, cap, actualAlpha, QuantizeMode.Floor);
            var stepRound = EvaluateStepwise(nActual, capN, a, cap, actualAlpha, QuantizeMode.RoundAwayFromZero);

            var soFloor = EvaluateSourceOver255(nActual, capN, a, cap, actualAlpha, QuantizeMode.Floor, clampToCap: false);
            var soRound = EvaluateSourceOver255(nActual, capN, a, cap, actualAlpha, QuantizeMode.RoundAwayFromZero, clampToCap: false);
            var soCapFloor = EvaluateSourceOver255(nActual, capN, a, cap, actualAlpha, QuantizeMode.Floor, clampToCap: true);
            var soCapRound = EvaluateSourceOver255(nActual, capN, a, cap, actualAlpha, QuantizeMode.RoundAwayFromZero, clampToCap: true);

            AppendSummaryRow(summary, closedFloor);
            AppendSummaryRow(summary, closedRound);
            AppendSummaryRow(summary, stepFloor);
            AppendSummaryRow(summary, stepRound);
            AppendSummaryRow(summary, soFloor);
            AppendSummaryRow(summary, soRound);
            AppendSummaryRow(summary, soCapFloor);
            AppendSummaryRow(summary, soCapRound);

            var best = MinByRmse(closedFloor, closedRound, stepFloor, stepRound, soFloor, soRound, soCapFloor, soCapRound);

            // 目視確認用に、bestの予測PNGと差分PNGを出力
            var tag = $"m{best.ModelTag}-q{best.QuantizeTag}";
            await SaveGray8PngAsync(folder, $"{baseName}-pred-N{nActual}-{tag}-{ts}.png", w, h, best.PredAlpha);
            await SaveGray8PngAsync(folder, $"{baseName}-diffabs-N{nActual}-{tag}-{ts}.png", w, h, best.DiffAbs);
            await SaveGray8PngAsync(folder, $"{baseName}-diffabs-N{nActual}-{tag}-{ts}-vis16.png", w, h, best.DiffAbs, scale: 16);
            await SaveGray8PngAsync(folder, $"{baseName}-diffabs-N{nActual}-{tag}-{ts}-vis64.png", w, h, best.DiffAbs, scale: 64);

            // max_abs_diff が小さい（例: 1〜3）と vis16/vis64 でも暗いので、diff>0を二値化して分布を見える化する。
            var diffMask = BuildDiffMask(best.DiffAbs);
            await SaveGray8PngAsync(folder, $"{baseName}-diffmask-N{nActual}-{tag}-{ts}.png", w, h, diffMask);

            // 併せて、最大値が255になるように自動スケールした可視化も出す。
            var autoScale = best.MaxAbsDiff <= 0 ? 1 : Math.Min(255, (int)Math.Ceiling(255.0 / best.MaxAbsDiff));
            await SaveGray8PngAsync(folder, $"{baseName}-diffabs-N{nActual}-{tag}-{ts}-visAuto.png", w, h, best.DiffAbs, scale: autoScale);
        }

        var file = await folder.CreateFileAsync($"{baseName}-summary-{ts}.csv", CreationCollisionOption.ReplaceExisting);
        await FileIO.WriteTextAsync(file, summary.ToString());

        var msg = checkedAny
            ? $"完了: {file.Path}"
            : "alignedN2/4/8 のPNGが選択されていないため、要約CSVのみ出力しました（a/capのみ）。";

        if (unparsed.Count > 0)
        {
            msg += $"\n\nalignedNを解析できなかったファイル数: {unparsed.Count}";
        }

        System.Windows.MessageBox.Show(window, msg, "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private sealed record EvalResult(
        int ActualN,
        int CapN,
        ModelMode Model,
        string ModelTag,
        QuantizeMode Quantize,
        string QuantizeTag,
        double MeanAbsDiff,
        double Rmse,
        int MaxAbsDiff,
        long MismatchPx,
        long ActualNonZeroPx,
        long PredNonZeroPx,
        byte[] PredAlpha,
        byte[] DiffAbs);

    private static EvalResult EvaluateClosedForm(int nActual, int capN, byte[] a, byte[] cap, byte[] actualAlpha, QuantizeMode quantize, float[] lut)
    {
        var pixelCount = a.Length;
        if (cap.Length != pixelCount) throw new ArgumentException("capの長さが一致しません。", nameof(cap));
        if (actualAlpha.Length != pixelCount) throw new ArgumentException("actualAlphaの長さが一致しません。", nameof(actualAlpha));
        if (lut.Length != 256 * 256) throw new ArgumentException("lutの長さが不正です。", nameof(lut));

        var pred = new byte[pixelCount];
        var diffAbs = new byte[pixelCount];

        long sumAbs = 0;
        long sumSq = 0;
        int max = 0;
        long mismatch = 0;
        long actualNonZero = 0;
        long predNonZero = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var capA = cap[i];
            var aA = a[i];

            byte p;
            if (capA == 0 || aA == 0)
            {
                p = 0;
            }
            else
            {
                if (aA > capA) aA = capA;
                p = Quantize(lut[(capA << 8) | aA], capA, quantize);
            }

            pred[i] = p;

            var actual = actualAlpha[i];
            if (actual != 0) actualNonZero++;
            if (p != 0) predNonZero++;

            var d = Math.Abs(actual - p);
            diffAbs[i] = (byte)d;

            if (d != 0) mismatch++;
            sumAbs += d;
            sumSq += (long)d * d;
            if (d > max) max = d;
        }

        var n = pixelCount;
        var meanAbs = n > 0 ? sumAbs / (double)n : 0.0;
        var rmse = n > 0 ? Math.Sqrt(sumSq / (double)n) : 0.0;

        var tag = quantize == QuantizeMode.Floor ? "floor" : "round";
        return new EvalResult(
            ActualN: nActual,
            CapN: capN,
            Model: ModelMode.ClosedForm,
            ModelTag: "closed",
            Quantize: quantize,
            QuantizeTag: tag,
            MeanAbsDiff: meanAbs,
            Rmse: rmse,
            MaxAbsDiff: max,
            MismatchPx: mismatch,
            ActualNonZeroPx: actualNonZero,
            PredNonZeroPx: predNonZero,
            PredAlpha: pred,
            DiffAbs: diffAbs);
    }

    private static EvalResult EvaluateStepwise(int nActual, int capN, byte[] a, byte[] cap, byte[] actualAlpha, QuantizeMode quantize)
    {
        var pixelCount = a.Length;
        if (cap.Length != pixelCount) throw new ArgumentException("capの長さが一致しません。", nameof(cap));
        if (actualAlpha.Length != pixelCount) throw new ArgumentException("actualAlphaの長さが一致しません。", nameof(actualAlpha));

        var pred = new byte[pixelCount];
        var diffAbs = new byte[pixelCount];

        long sumAbs = 0;
        long sumSq = 0;
        int max = 0;
        long mismatch = 0;
        long actualNonZero = 0;
        long predNonZero = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var capA = cap[i];
            var aA = a[i];

            byte p;
            if (capA == 0 || aA == 0)
            {
                p = 0;
            }
            else
            {
                if (aA > capA) aA = capA;
                p = PredictStepwise(aA, capA, nActual, quantize);
            }

            pred[i] = p;

            var actual = actualAlpha[i];
            if (actual != 0) actualNonZero++;
            if (p != 0) predNonZero++;

            var d = Math.Abs(actual - p);
            diffAbs[i] = (byte)d;

            if (d != 0) mismatch++;
            sumAbs += d;
            sumSq += (long)d * d;
            if (d > max) max = d;
        }

        var n = pixelCount;
        var meanAbs = n > 0 ? sumAbs / (double)n : 0.0;
        var rmse = n > 0 ? Math.Sqrt(sumSq / (double)n) : 0.0;

        var tag = quantize == QuantizeMode.Floor ? "floor" : "round";
        return new EvalResult(
            ActualN: nActual,
            CapN: capN,
            Model: ModelMode.Stepwise,
            ModelTag: "step",
            Quantize: quantize,
            QuantizeTag: tag,
            MeanAbsDiff: meanAbs,
            Rmse: rmse,
            MaxAbsDiff: max,
            MismatchPx: mismatch,
            ActualNonZeroPx: actualNonZero,
            PredNonZeroPx: predNonZero,
            PredAlpha: pred,
            DiffAbs: diffAbs);
    }

    private static byte PredictStepwise(byte aA, byte capA, int n, QuantizeMode quantize)
    {
        // pred(N) = cap * (1 - (1 - a/cap)^N)
        // を「各ステップで8bit量子化が入る」前提で逐次更新したもの。
        // x_{t+1} = x_t + (cap - x_t) * (a/cap)
        var x = 0;
        for (var k = 0; k < n; k++)
        {
            var remain = capA - x;
            var numer = remain * aA;

            var delta = quantize == QuantizeMode.Floor
                ? numer / capA
                : (numer + (capA / 2)) / capA;

            x += delta;
            if (x >= capA)
            {
                x = capA;
                break;
            }
        }
        return (byte)x;
    }

    private static EvalResult EvaluateSourceOver255(int nActual, int capN, byte[] a, byte[] cap, byte[] actualAlpha, QuantizeMode quantize, bool clampToCap)
    {
        var pixelCount = a.Length;
        if (cap.Length != pixelCount) throw new ArgumentException("capの長さが一致しません。", nameof(cap));
        if (actualAlpha.Length != pixelCount) throw new ArgumentException("actualAlphaの長さが一致しません。", nameof(actualAlpha));

        var pred = new byte[pixelCount];
        var diffAbs = new byte[pixelCount];

        long sumAbs = 0;
        long sumSq = 0;
        int max = 0;
        long mismatch = 0;
        long actualNonZero = 0;
        long predNonZero = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var capA = cap[i];
            var aA = a[i];

            byte p;
            if (aA == 0)
            {
                p = 0;
            }
            else
            {
                p = PredictSourceOver255(aA, capA, nActual, quantize, clampToCap);
            }

            pred[i] = p;

            var actual = actualAlpha[i];
            if (actual != 0) actualNonZero++;
            if (p != 0) predNonZero++;

            var d = Math.Abs(actual - p);
            diffAbs[i] = (byte)d;

            if (d != 0) mismatch++;
            sumAbs += d;
            sumSq += (long)d * d;
            if (d > max) max = d;
        }

        var n = pixelCount;
        var meanAbs = n > 0 ? sumAbs / (double)n : 0.0;
        var rmse = n > 0 ? Math.Sqrt(sumSq / (double)n) : 0.0;

        var qTag = quantize == QuantizeMode.Floor ? "floor" : "round";
        var model = clampToCap ? ModelMode.SourceOver255Cap : ModelMode.SourceOver255;
        var modelTag = clampToCap ? "so255cap" : "so255";
        return new EvalResult(
            ActualN: nActual,
            CapN: capN,
            Model: model,
            ModelTag: modelTag,
            Quantize: quantize,
            QuantizeTag: qTag,
            MeanAbsDiff: meanAbs,
            Rmse: rmse,
            MaxAbsDiff: max,
            MismatchPx: mismatch,
            ActualNonZeroPx: actualNonZero,
            PredNonZeroPx: predNonZero,
            PredAlpha: pred,
            DiffAbs: diffAbs);
    }

    private static byte PredictSourceOver255(byte aA, byte capA, int n, QuantizeMode quantize, bool clampToCap)
    {
        // BGRA8上のsource-overのα更新を、各ステップで整数演算（8bit量子化）しながら適用する。
        // outA = dstA + (srcA * (255 - dstA) + 127) / 255  （round寄り）
        // floor寄りは +127 なし。
        var x = 0;
        for (var k = 0; k < n; k++)
        {
            var numer = aA * (255 - x);
            var inc = quantize == QuantizeMode.Floor
                ? numer / 255
                : (numer + 127) / 255;

            x += inc;
            if (x > 255) x = 255;

            if (clampToCap && capA < 255)
            {
                if (x > capA) x = capA;
            }
        }
        return (byte)x;
    }

    private static EvalResult MinByRmse(params EvalResult[] results)
    {
        if (results is null || results.Length == 0) throw new ArgumentException("resultsが空です。", nameof(results));

        var best = results[0];
        for (var i = 1; i < results.Length; i++)
        {
            if (results[i].Rmse < best.Rmse) best = results[i];
        }
        return best;
    }

    private static byte[] BuildDiffMask(byte[] diffAbs)
    {
        var mask = new byte[diffAbs.Length];
        for (var i = 0; i < diffAbs.Length; i++)
        {
            mask[i] = diffAbs[i] == 0 ? (byte)0 : (byte)255;
        }
        return mask;
    }

    private static float[] BuildPredictionLut(int n)
    {
        // key = (cap<<8)|a, value = 予測α(浮動小数)
        var lut = new float[256 * 256];
        for (var capA = 0; capA <= 255; capA++)
        {
            for (var aA = 0; aA <= 255; aA++)
            {
                float v;
                if (capA <= 0 || aA <= 0)
                {
                    v = 0;
                }
                else
                {
                    var aa = Math.Min(aA, capA);
                    var q = 1.0 - (aa / (double)capA);
                    var pred = capA * (1.0 - Math.Pow(q, n));
                    if (pred < 0) pred = 0;
                    if (pred > capA) pred = capA;
                    v = (float)pred;
                }

                lut[(capA << 8) | aA] = v;
            }
        }
        return lut;
    }

    private static byte Quantize(float pred, byte capA, QuantizeMode mode)
    {
        var v = mode switch
        {
            QuantizeMode.Floor => (int)Math.Floor(pred),
            QuantizeMode.RoundAwayFromZero => (int)Math.Round(pred, MidpointRounding.AwayFromZero),
            _ => (int)Math.Round(pred, MidpointRounding.AwayFromZero),
        };

        if (v < 0) v = 0;
        if (v > capA) v = capA;
        return (byte)v;
    }

    private static async Task<StorageFolder?> PickOutputFolderAsync(MainWindow window)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".csv");

        var hwnd = new WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(picker, hwnd);

        return await picker.PickSingleFolderAsync();
    }

    private static async Task SaveGray8PngAsync(StorageFolder folder, string fileName, int w, int h, byte[] gray, int scale = 1)
    {
        if (folder is null) throw new ArgumentNullException(nameof(folder));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileNameが空です。", nameof(fileName));
        if (w <= 0) throw new ArgumentOutOfRangeException(nameof(w));
        if (h <= 0) throw new ArgumentOutOfRangeException(nameof(h));
        if (gray is null) throw new ArgumentNullException(nameof(gray));
        if (gray.Length != w * h) throw new ArgumentException("grayの長さが一致しません。", nameof(gray));
        if (scale <= 0) scale = 1;

        using var bmp = new SKBitmap(w, h, SKColorType.Gray8, SKAlphaType.Opaque);

        // Gray8に直接書き込む
        unsafe
        {
            var dstBase = (byte*)bmp.GetPixels().ToPointer();
            var rowBytes = bmp.RowBytes;
            for (var y = 0; y < h; y++)
            {
                var dst = dstBase + (y * rowBytes);
                var srcRow = y * w;
                for (var x = 0; x < w; x++)
                {
                    var v = gray[srcRow + x];
                    if (scale != 1)
                    {
                        var s = v * scale;
                        v = (byte)Math.Min(255, s);
                    }
                    dst[x] = v;
                }
            }
        }

        var file = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
        using var fs = new FileStream(file.Path, FileMode.Create, FileAccess.Write, FileShare.Read);
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(fs);
        fs.Flush(flushToDisk: true);
    }

    private static void AppendSummaryRow(StringBuilder sb, EvalResult r)
    {
        sb.Append(r.ActualN.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.CapN.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.ModelTag).Append(',');
        sb.Append(r.QuantizeTag).Append(',');
        sb.Append(r.MeanAbsDiff.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.Rmse.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.MaxAbsDiff.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.MismatchPx.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.ActualNonZeroPx.ToString(CultureInfo.InvariantCulture)).Append(',');
        sb.Append(r.PredNonZeroPx.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine();
    }

    private static bool TryParseAlignedNFromFileName(string path, out int n)
    {
        n = 0;

        var name = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // 例: ...-alignedN1024-...
        var m = Regex.Match(name, @"alignedN(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
        {
            return false;
        }

        var s = m.Groups["n"].Value;
        if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
        {
            return false;
        }

        return true;
    }
}
