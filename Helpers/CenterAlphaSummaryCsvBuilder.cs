using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;

using Windows.Storage;

namespace StrokeSampler
{
    internal static class CenterAlphaSummaryCsvBuilder
    {
        public readonly struct BuildResult
        {
            public BuildResult(string csvText, int rows, int skipped)
            {
                CsvText = csvText;
                Rows = rows;
                Skipped = skipped;
            }

            public string CsvText { get; }
            public int Rows { get; }
            public int Skipped { get; }
        }

        public static async Task<BuildResult> BuildFromFolderAsync(
            StorageFolder folder,
            Func<string, bool> isTargetCsvFile,
            Func<string, (bool ok, double s, double p, int n)> tryParseKey,
            Func<string, (bool ok, double centerAlpha)> tryReadCenterAlpha)
        {
            if (folder is null)
            {
                throw new ArgumentNullException(nameof(folder));
            }

            if (isTargetCsvFile is null)
            {
                throw new ArgumentNullException(nameof(isTargetCsvFile));
            }

            if (tryParseKey is null)
            {
                throw new ArgumentNullException(nameof(tryParseKey));
            }

            if (tryReadCenterAlpha is null)
            {
                throw new ArgumentNullException(nameof(tryReadCenterAlpha));
            }

            var files = await folder.GetFilesAsync();
            var rows = new List<(double s, double p, int n, double centerAlpha)>();

            var skipped = 0;
            foreach (var f in files)
            {
                if (!isTargetCsvFile(f.Name))
                {
                    continue;
                }

                var key = tryParseKey(f.Name);
                if (!key.ok)
                {
                    skipped++;
                    continue;
                }

                var text = await FileIO.ReadTextAsync(f);
                var alpha = tryReadCenterAlpha(text);
                if (!alpha.ok)
                {
                    skipped++;
                    continue;
                }

                rows.Add((key.s, key.p, key.n, alpha.centerAlpha));
            }

            if (rows.Count == 0)
            {
                return new BuildResult(string.Empty, rows: 0, skipped: skipped);
            }

            rows.Sort((a, b) =>
            {
                var c = a.s.CompareTo(b.s);
                if (c != 0) return c;
                c = a.p.CompareTo(b.p);
                if (c != 0) return c;
                return a.n.CompareTo(b.n);
            });

            var sb = new StringBuilder(capacity: Math.Max(1024, rows.Count * 40));
            sb.AppendLine("S,P,N,center_alpha");
            foreach (var r in rows)
            {
                sb.Append(r.s.ToString("0.##", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(r.p.ToString("0.####", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(r.n.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(r.centerAlpha.ToString("0.########", CultureInfo.InvariantCulture));
                sb.AppendLine();
            }

            return new BuildResult(sb.ToString(), rows.Count, skipped);
        }

        public static async Task SaveAsUtf8Async(StorageFolder folder, string fileName, string csvText)
        {
            if (folder is null)
            {
                throw new ArgumentNullException(nameof(folder));
            }

            if (fileName is null)
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            if (csvText is null)
            {
                throw new ArgumentNullException(nameof(csvText));
            }

            var outFile = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, csvText, Windows.Storage.Streams.UnicodeEncoding.Utf8);
        }
    }
}
