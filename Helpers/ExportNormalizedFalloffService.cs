using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;
using static StrokeSampler.StrokeHelpers;

namespace StrokeSampler
{
    internal static class ExportNormalizedFalloffService
    {
        internal static async Task ExportAsync(MainPage mp)
        {
            var s0 = UIHelpers.GetNormalizedFalloffS0(mp);

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
            var samples = new List<(double s, double p, int n, double[] fr)>();

            var skipped = 0;
            foreach (var f in files)
            {
                if (!f.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!f.Name.StartsWith("radial-falloff-", StringComparison.OrdinalIgnoreCase) && !f.Name.StartsWith("radial-falloff-hires-", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var text = await FileIO.ReadTextAsync(f);
                double s;
                double p;
                int n;
                if (!ParseFalloffFilenameService.TryParseFalloffFilename(f.Name, out s, out p, out n))
                {
                    if (!TryParseFalloffHeader(text, out s, out p, out n))
                    {
                        skipped++;
                        continue;
                    }
                }

                if (!ParseFalloffCSV.TryParseFalloffCsv(text, out var fr))
                {
                    skipped++;
                    continue;
                }

                // S上限200前提（念のため）
                if (s <= 0 || s > 200)
                {
                    skipped++;
                    continue;
                }

                samples.Add((s: s, p: p, n: n, fr: fr));
            }

            if (samples.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "正規化mean/stddev",
                    Content = "対象CSVが見つかりませんでした（radial-falloff-S*-P*-N*.csv）。",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

            // P/Nが混在していると平均に意味が無いので、最多の(P,N)だけを採用する
            var groupCounts = new Dictionary<(double p, int n), int>();
            foreach (var s in samples)
            {
                var key = (s.p, s.n);
                groupCounts.TryGetValue(key, out var c);
                groupCounts[key] = c + 1;
            }

            (double p, int n) selected = default;
            var bestCount = -1;
            foreach (var kv in groupCounts)
            {
                if (kv.Value > bestCount)
                {
                    bestCount = kv.Value;
                    selected = kv.Key;
                }
            }

            var filtered = new List<(double s, double[] fr)>();
            foreach (var s in samples)
            {
                if (s.p == selected.p && s.n == selected.n)
                {
                    filtered.Add((s.s, s.fr));
                }
            }

            if (filtered.Count == 0)
            {
                var dlg = new ContentDialog
                {
                    Title = "正規化mean/stddev",
                    Content = "集計対象が空です。",
                    CloseButtonText = "OK"
                };
                await dlg.ShowAsync();
                return;
            }

            // r_norm軸は整数pxとして 0..(S0/2) を採用（dotの有効範囲を想定）
            var rMax = Math.Max(1, s0 / 2);
            var sum = new double[rMax + 1];
            var sumSq = new double[rMax + 1];

            foreach (var (s, fr) in filtered)
            {
                var scale = (double)s0 / s; // r_norm = r * scale

                for (var rNorm = 0; rNorm <= rMax; rNorm++)
                {
                    var r = rNorm / scale; // 元CSV半径に戻す
                    var v = SampleLinear(fr, r);
                    sum[rNorm] += v;
                    sumSq[rNorm] += v * v;
                }
            }

            var mean = new double[rMax + 1];
            var stddev = new double[rMax + 1];
            for (var i = 0; i <= rMax; i++)
            {
                var m = sum[i] / filtered.Count;
                var v = sumSq[i] / filtered.Count - m * m;
                mean[i] = m;
                stddev[i] = Math.Sqrt(Math.Max(0.0, v));
            }

            var csv = BuildNormalizedFalloffCsv(mean, stddev, filtered.Count, s0, selected.p, selected.n);
            var outName = $"normalized-falloff-S0{s0}-P{selected.p:0.###}-N{selected.n}.csv";
            var outFile = await folder.CreateFileAsync(outName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, csv, Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var done = new ContentDialog
            {
                Title = "正規化mean/stddev",
                Content = $"完了: {filtered.Count}件を集計しました。スキップ={skipped}件。\n出力={outName}",
                CloseButtonText = "OK"
            };
            await done.ShowAsync();
        }

        private static bool TryParseFalloffHeader(string csv, out double s, out double p, out int n)
        {
            s = default;
            p = default;
            n = default;
            if (string.IsNullOrWhiteSpace(csv)) return false;

            // 先頭付近のコメント行から `S=.. P=.. N=..` を拾う
            // 例: `# S=200 P=0.1 N=50 scale=8`
            var lines = csv.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length && i < 10; i++)
            {
                var line = lines[i].Trim();
                if (!line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                double? sOpt = null;
                double? pOpt = null;
                int? nOpt = null;
                var parts = line.TrimStart('#').Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    if (part.StartsWith("S=", StringComparison.OrdinalIgnoreCase) && double.TryParse(part.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var sv))
                    {
                        sOpt = sv;
                    }
                    else if (part.StartsWith("P=", StringComparison.OrdinalIgnoreCase) && double.TryParse(part.Substring(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var pv))
                    {
                        pOpt = pv;
                    }
                    else if (part.StartsWith("N=", StringComparison.OrdinalIgnoreCase) && int.TryParse(part.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out var nv))
                    {
                        nOpt = nv;
                    }
                }

                if (sOpt != null && pOpt != null && nOpt != null)
                {
                    s = sOpt.Value;
                    p = pOpt.Value;
                    n = nOpt.Value;
                    return true;
                }
            }

            return false;
        }
    }
}
