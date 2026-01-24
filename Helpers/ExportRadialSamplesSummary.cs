using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;

namespace StrokeSampler
{
    internal static class ExportRadialSamplesSummary
    {
        internal static async Task ExportAsync(MainPage mp)
        {
            var rs = UIHelpers.GetRadialSampleRs(mp);
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

                if (!ParseFalloffFilenameService.TryParseFalloffFilename(f.Name, out var s, out var p, out var n))
                {
                    skipped++;
                    continue;
                }

                var text = await FileIO.ReadTextAsync(f);
                if (!ReadASamplesCSV.TryReadAlphaSamplesFromFalloffCsv(text, rs, out var a))
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
    }
}
