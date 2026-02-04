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

internal static class AlignedDiffSeriesAnalyzer
{
    private static readonly Regex AlignedNRegex = new(@"alignedN(?<n>\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static async Task AnalyzeAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

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

        var items = paths
            .Select(p => new { Path = p, N = TryParseAlignedN(p) })
            .Where(x => x.N is not null)
            .Select(x => new Item(x.Path, x.N!.Value))
            .OrderBy(x => x.N)
            .ToList();

        if (items.Count < 2)
        {
            System.Windows.MessageBox.Show(window, "alignedN<number> をファイル名から検出できませんでした。", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // Nが歯抜けでも扱えるよう、連続ペア (prev,next) を作る
        var pairs = new List<(Item Prev, Item Next)>();
        for (var i = 1; i < items.Count; i++)
        {
            pairs.Add((items[i - 1], items[i]));
        }

        var folderPicker = new FolderPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        folderPicker.FileTypeFilter.Add(".csv");
        var hwnd = new WindowInteropHelper(window).Handle;
        InitializeWithWindow.Initialize(folderPicker, hwnd);
        var folder = await folderPicker.PickSingleFolderAsync();
        if (folder is null) return;

        var outFile = await folder.CreateFileAsync($"aligned-diff-series-{DateTime.Now:yyyyMMdd-HHmmss}.csv", CreationCollisionOption.ReplaceExisting);

        var sb = new StringBuilder(64 * 1024);
        sb.AppendLine("prev_file,next_file,prev_n,next_n,width,height,diff_abs_mean,diff_abs_stddev,diff_abs_max");

        foreach (var (prev, next) in pairs)
        {
            using var bmpPrev = SKBitmap.Decode(prev.Path);
            using var bmpNext = SKBitmap.Decode(next.Path);
            if (bmpPrev is null || bmpNext is null) continue;
            if (bmpPrev.Width != bmpNext.Width || bmpPrev.Height != bmpNext.Height) continue;

            var w = bmpPrev.Width;
            var h = bmpPrev.Height;

            long sum = 0;
            long sumSq = 0;
            var max = 0;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var a0 = bmpPrev.GetPixel(x, y).Alpha;
                    var a1 = bmpNext.GetPixel(x, y).Alpha;
                    var d = Math.Abs(a1 - a0);
                    sum += d;
                    sumSq += (long)d * d;
                    if (d > max) max = d;
                }
            }

            var count = (long)w * h;
            var mean = count == 0 ? 0.0 : sum / (double)count;
            var variance = count == 0 ? 0.0 : (sumSq / (double)count) - (mean * mean);
            var stddev = variance <= 0 ? 0.0 : Math.Sqrt(variance);

            sb.Append(Escape(Path.GetFileName(prev.Path))).Append(',');
            sb.Append(Escape(Path.GetFileName(next.Path))).Append(',');
            sb.Append(prev.N.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(next.N.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(w.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append(h.ToString(CultureInfo.InvariantCulture)).Append(',');
            sb.Append((mean / 255.0).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append((stddev / 255.0).ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
            sb.Append((max / 255.0).ToString("0.########", CultureInfo.InvariantCulture));
            sb.AppendLine();
        }

        await FileIO.WriteTextAsync(outFile, sb.ToString());
    }

    private sealed record Item(string Path, int N);

    private static int? TryParseAlignedN(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        var m = AlignedNRegex.Match(name);
        if (!m.Success) return null;
        if (!int.TryParse(m.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return null;
        return n;
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
}
