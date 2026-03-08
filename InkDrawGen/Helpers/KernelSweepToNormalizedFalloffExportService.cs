using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;

namespace InkDrawGen.Helpers
{
    internal static class KernelSweepToNormalizedFalloffExportService
    {
        internal static async Task ExportNormalizedFalloffCsvFromKernelSweepAsync(MainPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            var state = InkDrawGenUiReader.Read(page);
            var scale = Math.Max(1, state.Scale);

            // kernel-sweep CSVを選択
            var csvFile = await PickKernelSweepCsvAsync();
            if (csvFile is null)
            {
                return;
            }

            // 出力先フォルダ
            var outFolder = await PickOutputFolderAsync(state.OutputFolder);
            if (outFolder is null)
            {
                return;
            }

            // UIのstart値をメタ情報として埋める（DotTesterの既定ファイル名に合わせる）
            var s0 = (int)Math.Round(state.S.Start, MidpointRounding.AwayFromZero);
            var p = state.P.Start;
            var n = Math.Max(1, state.N.Start);

            var (rNormMax, alphaByRNorm) = await ReadKernelSweepAlphaByRNormAsync(csvFile, scale);

            // normalized-falloff形式へ
            // Note: mean_alphaは「中心で1に正規化」ではなく、観測alpha01の絶対値を出す（DotLab/Sampleと同形式）
            var sb = new StringBuilder(capacity: 32 * 1024);
            sb.Append("# normalized-falloff S0=").Append(s0.ToString(CultureInfo.InvariantCulture))
                .Append(" P=").Append(p.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" N=").Append(n.ToString(CultureInfo.InvariantCulture))
                .Append(" count=1")
                .AppendLine();
            sb.AppendLine("r_norm,mean_alpha,stddev_alpha,count");

            for (var r = 0; r <= rNormMax; r++)
            {
                alphaByRNorm.TryGetValue(r, out var a01);
                a01 = Math.Clamp(a01, 0.0, 1.0);
                sb.Append(r.ToString(CultureInfo.InvariantCulture)).Append(',');
                sb.Append(a01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                sb.Append('0').Append(',');
                sb.Append('1');
                sb.AppendLine();
            }

            var outName = BuildOutFileName(s0, p, n);
            var outFile = await outFolder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var dlg = new ContentDialog
            {
                Title = "normalized-falloff CSV",
                Content = $"完了: CSVを書き出しました。\n\nfile={outFile.Path}\nsource={csvFile.Name}\nscale={scale} S0={s0} P={p:0.###} N={n} rNormMax={rNormMax}",
                CloseButtonText = "OK"
            };
            await dlg.ShowAsync();
        }

        private static async Task<(int rNormMax, Dictionary<int, double> alphaByRNorm)> ReadKernelSweepAlphaByRNormAsync(StorageFile csvFile, int scale)
        {
            var text = await FileIO.ReadTextAsync(csvFile, Windows.Storage.Streams.UnicodeEncoding.Utf8);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("kernel-sweep CSVが空です。", nameof(csvFile));
            }

            var alphaByDx = new Dictionary<int, double>();

            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("dx_px", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(',');
                if (parts.Length < 6) continue;

                if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var dxPx))
                {
                    continue;
                }

                if (!double.TryParse(parts[5], NumberStyles.Float, CultureInfo.InvariantCulture, out var a01))
                {
                    continue;
                }

                alphaByDx[dxPx] = Math.Clamp(a01, 0.0, 1.0);
            }

            if (alphaByDx.Count == 0)
            {
                throw new InvalidOperationException("kernel-sweep CSVの読み取りに失敗しました（データ行がありません）。");
            }

            // dx_pxの昇順配列を作って、dxTargetの前後2点で線形補間できるようにする。
            // （従来の「最寄り1点」だと、scaleや欠損の影響でfalloffの落ち方が段付きになりやすい）
            var dxSorted = alphaByDx.Keys.ToArray();
            Array.Sort(dxSorted);

            var maxDx = alphaByDx.Keys.Max();
            var rNormMax = Math.Max(1, (int)Math.Floor(maxDx / (double)scale));

            var alphaByRNorm = new Dictionary<int, double>(capacity: rNormMax + 1)
            {
                [0] = alphaByDx.TryGetValue(0, out var a0) ? a0 : 0.0
            };

            for (var r = 1; r <= rNormMax; r++)
            {
                var dxTarget = r * scale;
                if (alphaByDx.TryGetValue(dxTarget, out var a))
                {
                    alphaByRNorm[r] = a;
                    continue;
                }

                // dxTargetの前後2点で線形補間する（範囲外は端でクランプ）
                var idx = Array.BinarySearch(dxSorted, dxTarget);
                if (idx >= 0)
                {
                    // 通常ここには来ないが、念のため。
                    alphaByRNorm[r] = alphaByDx.TryGetValue(dxSorted[idx], out var a3) ? a3 : 0.0;
                    continue;
                }

                var ins = ~idx;
                if (ins <= 0)
                {
                    alphaByRNorm[r] = alphaByDx.TryGetValue(dxSorted[0], out var aMin) ? aMin : 0.0;
                    continue;
                }
                if (ins >= dxSorted.Length)
                {
                    alphaByRNorm[r] = alphaByDx.TryGetValue(dxSorted[dxSorted.Length - 1], out var aMax) ? aMax : 0.0;
                    continue;
                }

                var dx0 = dxSorted[ins - 1];
                var dx1 = dxSorted[ins];
                if (dx1 <= dx0)
                {
                    alphaByRNorm[r] = alphaByDx.TryGetValue(dx0, out var aFlat) ? aFlat : 0.0;
                    continue;
                }

                var aLower = alphaByDx.TryGetValue(dx0, out var a0v) ? a0v : 0.0;
                var aUpper = alphaByDx.TryGetValue(dx1, out var a1v) ? a1v : 0.0;

                var t = (dxTarget - dx0) / (double)(dx1 - dx0);
                alphaByRNorm[r] = (1.0 - t) * aLower + t * aUpper;
            }

            return (rNormMax, alphaByRNorm);
        }

        private static async Task<StorageFile?> PickKernelSweepCsvAsync()
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".csv");

            return await picker.PickSingleFileAsync();
        }

        private static async Task<StorageFolder?> PickOutputFolderAsync(string outputFolderPath)
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

        private static string BuildOutFileName(int s0, double p, int n)
        {
            // DotTester既定（DotLab/Sample）に合わせる
            var sText = s0.ToString("D4", CultureInfo.InvariantCulture);
            var pText = p.ToString("0.###", CultureInfo.InvariantCulture);
            return $"normalized-falloff-S{sText}-P{pText}-N{n}.csv";
        }
    }
}
