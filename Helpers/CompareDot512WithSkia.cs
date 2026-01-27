using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;

namespace StrokeSampler
{
    internal class CompareDot512WithSkia
    {
        private const string UwpDotPrefix = "dot512-material-";
        private const string SkiaDotPrefix = "skia-dot512-";

        private static readonly int[] DefaultSampleRs = new[] { 0, 1, 2, 5, 10, 20, 50, 100 };

        internal static async Task CompareDot512WithSkiaAsync(MainPage mp)
        {
            if (mp is null)
            {
                throw new ArgumentNullException(nameof(mp));
            }

            var uwpFolder = await PickFolderAsync("観測(UWP) dot512-material フォルダを選択");
            if (uwpFolder == null)
            {
                return;
            }

            var skiaFolder = await PickFolderAsync("Skia出力 skia-dot512 フォルダを選択");
            if (skiaFolder == null)
            {
                return;
            }

            var uwpFiles = await uwpFolder.GetFilesAsync();
            var skiaFiles = await skiaFolder.GetFilesAsync();

            var uwpMap = BuildDotIndex(uwpFiles, expectedPrefix: UwpDotPrefix);
            var skiaMap = BuildDotIndex(skiaFiles, expectedPrefix: SkiaDotPrefix);

            var keys = uwpMap.Keys.Intersect(skiaMap.Keys).OrderBy(k => k, DotKeyComparer.Instance).ToList();
            if (keys.Count == 0)
            {
                await ShowMessageAsync("dot512比較", "一致するPNGペアが見つかりませんでした。ファイル名規則を確認してください。\n例: dot512-material-S{S}-P{P}-N{N}.png / skia-dot512-S{S}-P{P}-N{N}.png");
                return;
            }

            var rs = (mp.RadialSampleRsTextBox != null) ? UIHelpers.GetRadialSampleRs(mp) : DefaultSampleRs;
            if (rs == null || rs.Count == 0)
            {
                rs = DefaultSampleRs;
            }

            var sb = new StringBuilder(capacity: Math.Max(1024, keys.Count * 260));
            sb.Append("S,P,N,mae,rmse,uwp_center_alpha,skia_center_alpha,center_absdiff");
            foreach (var r in rs)
            {
                sb.Append(",uwp_a_r");
                sb.Append(r.ToString(CultureInfo.InvariantCulture));
                sb.Append(",skia_a_r");
                sb.Append(r.ToString(CultureInfo.InvariantCulture));
                sb.Append(",a_r");
                sb.Append(r.ToString(CultureInfo.InvariantCulture));
                sb.Append("_absdiff");
            }
            sb.Append(",uwp_png,skia_png");
            sb.AppendLine();

            var compared = 0;
            var skipped = 0;

            foreach (var key in keys)
            {
                var uwp = uwpMap[key];
                var skia = skiaMap[key];

                // 画像読み込み（Win2D）
                var device = CanvasDevice.GetSharedDevice();
                CanvasBitmap a = null;
                CanvasBitmap b = null;
                try
                {
                    a = await CanvasBitmap.LoadAsync(device, await uwp.OpenAsync(FileAccessMode.Read));
                    b = await CanvasBitmap.LoadAsync(device, await skia.OpenAsync(FileAccessMode.Read));
                }
                catch
                {
                    if (a != null) a.Dispose();
                    if (b != null) b.Dispose();
                    skipped++;
                    continue;
                }

                using (a)
                using (b)
                {
                    if (a.SizeInPixels.Width != b.SizeInPixels.Width || a.SizeInPixels.Height != b.SizeInPixels.Height)
                    {
                        skipped++;
                        continue;
                    }

                    // dot512前提で、中心円(直径S)のみを比較
                    var diameterPx = (int)Math.Round(key.S);
                    if (diameterPx <= 0)
                    {
                        skipped++;
                        continue;
                    }

                    var (mae, rmse) = CompareAlphaOnlyCenterCircle(a, b, diameterPx);

                    var uwpCenter = SampleAlphaAtRadiusMean(a, diameterPx, sampleR: 0);
                    var skiaCenter = SampleAlphaAtRadiusMean(b, diameterPx, sampleR: 0);
                    var centerAbsDiff = Math.Abs(uwpCenter - skiaCenter);

                    var uwpRs = new double[rs.Count];
                    var skiaRs = new double[rs.Count];
                    for (var i = 0; i < rs.Count; i++)
                    {
                        uwpRs[i] = SampleAlphaAtRadiusMean(a, diameterPx, rs[i]);
                        skiaRs[i] = SampleAlphaAtRadiusMean(b, diameterPx, rs[i]);
                    }

                    sb.Append(key.S.ToString("0.##", CultureInfo.InvariantCulture));
                    sb.Append(',');
                    sb.Append(key.P.ToString("0.####", CultureInfo.InvariantCulture));
                    sb.Append(',');
                    sb.Append(key.N.ToString(CultureInfo.InvariantCulture));
                    sb.Append(',');
                    sb.Append(mae.ToString("0.######", CultureInfo.InvariantCulture));
                    sb.Append(',');
                    sb.Append(rmse.ToString("0.######", CultureInfo.InvariantCulture));
                    sb.Append(',');
                    sb.Append(uwpCenter.ToString("0.######", CultureInfo.InvariantCulture));
                    sb.Append(',');
                    sb.Append(skiaCenter.ToString("0.######", CultureInfo.InvariantCulture));
                    sb.Append(',');
                    sb.Append(centerAbsDiff.ToString("0.######", CultureInfo.InvariantCulture));

                    for (var i = 0; i < rs.Count; i++)
                    {
                        sb.Append(',');
                        sb.Append(uwpRs[i].ToString("0.######", CultureInfo.InvariantCulture));
                        sb.Append(',');
                        sb.Append(skiaRs[i].ToString("0.######", CultureInfo.InvariantCulture));
                        sb.Append(',');
                        sb.Append(Math.Abs(uwpRs[i] - skiaRs[i]).ToString("0.######", CultureInfo.InvariantCulture));
                    }

                    sb.Append(',');
                    sb.Append(EscapeCsv(uwp.Path));
                    sb.Append(',');
                    sb.Append(EscapeCsv(skia.Path));
                    sb.AppendLine();
                    compared++;
                }
            }

            var outFile = await uwpFolder.CreateFileAsync("dot-compare-alpha-circle.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            await ShowMessageAsync("dot512比較", $"完了: 比較={compared}件, スキップ={skipped}件\n出力: {outFile.Path}");
        }

        private static (double mae, double rmse) CompareAlphaOnlyCenterCircle(CanvasBitmap a, CanvasBitmap b, int diameterPx)
        {
            // αのみ比較。512x512前提だが、入力サイズを尊重して中心を計算する。
            var w = a.SizeInPixels.Width;
            var h = a.SizeInPixels.Height;

            var cx = (w - 1) * 0.5;
            var cy = (h - 1) * 0.5;
            var radius = diameterPx * 0.5;
            var r2 = radius * radius;

            var ab = a.GetPixelBytes();
            var bb = b.GetPixelBytes();

            // BGRA8
            var sumAbs = 0.0;
            var sumSq = 0.0;
            var count = 0L;

            for (var y = 0; y < h; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < w; x++)
                {
                    var dx = x - cx;
                    if ((dx * dx + dy * dy) > r2)
                    {
                        continue;
                    }

                    var idx = (y * w + x) * 4;
                    var aA = ab[idx + 3] / 255.0;
                    var bA = bb[idx + 3] / 255.0;
                    var d = aA - bA;
                    sumAbs += Math.Abs(d);
                    sumSq += d * d;
                    count++;
                }
            }

            if (count <= 0)
            {
                return (double.NaN, double.NaN);
            }

            var mae = sumAbs / count;
            var rmse = Math.Sqrt(sumSq / count);
            return (mae, rmse);
        }

        private static double SampleAlphaAtRadiusMean(CanvasBitmap bmp, int diameterPx, int sampleR)
        {
            // 指定半径(sampleR)付近のαを平均する。
            // 目的：中心だけでなく「外縁のズレ」も数値として追えるようにする。
            // 実装は簡易化のため、r±0.5 のリング（整数グリッド上）で平均する。

            // r=0 はリング条件だと中心が0件になりやすいので、中心4px平均を返す。
            if (sampleR == 0)
            {
                return SampleCenterAlpha4px(bmp);
            }

            var w = bmp.SizeInPixels.Width;
            var h = bmp.SizeInPixels.Height;

            var cx = (w - 1) * 0.5;
            var cy = (h - 1) * 0.5;

            var radiusLimit = diameterPx * 0.5;
            var sample = Math.Clamp(sampleR, 0, (int)Math.Ceiling(radiusLimit));

            var rMin = Math.Max(0.0, sample - 0.5);
            var rMax = sample + 0.5;
            var rMin2 = rMin * rMin;
            var rMax2 = rMax * rMax;

            var ab = bmp.GetPixelBytes();

            var sum = 0.0;
            var count = 0L;

            for (var y = 0; y < h; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < w; x++)
                {
                    var dx = x - cx;
                    var d2 = (dx * dx + dy * dy);

                    // dot外は対象外
                    if (d2 > radiusLimit * radiusLimit)
                    {
                        continue;
                    }

                    if (d2 < rMin2 || d2 > rMax2)
                    {
                        continue;
                    }

                    var idx = (y * w + x) * 4;
                    sum += ab[idx + 3] / 255.0;
                    count++;
                }
            }

            if (count <= 0)
            {
                return double.NaN;
            }

            return sum / count;
        }

        private static double SampleCenterAlpha4px(CanvasBitmap bmp)
        {
            // 512x512の場合、中心は(255.5,255.5)なので、中心の4ピクセル平均が幾何的に自然。
            var w = bmp.SizeInPixels.Width;
            var h = bmp.SizeInPixels.Height;
            if (w <= 1 || h <= 1)
            {
                return double.NaN;
            }

            var x0 = (w / 2) - 1;
            var y0 = (h / 2) - 1;
            var x1 = x0 + 1;
            var y1 = y0 + 1;

            if (x0 < 0 || y0 < 0 || x1 >= w || y1 >= h)
            {
                return double.NaN;
            }

            // BGRA8
            var ab = bmp.GetPixelBytes();

            var a00 = ab[(y0 * w + x0) * 4 + 3] / 255.0;
            var a10 = ab[(y0 * w + x1) * 4 + 3] / 255.0;
            var a01 = ab[(y1 * w + x0) * 4 + 3] / 255.0;
            var a11 = ab[(y1 * w + x1) * 4 + 3] / 255.0;

            return (a00 + a10 + a01 + a11) * 0.25;
        }

        private static Dictionary<DotKey, StorageFile> BuildDotIndex(IReadOnlyList<StorageFile> files, string expectedPrefix)
        {
            var map = new Dictionary<DotKey, StorageFile>();
            foreach (var f in files)
            {
                if (!f.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!f.Name.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!TryParseDotFilename(f.Name, expectedPrefix, out var key))
                {
                    continue;
                }

                // 同一キー重複があれば先勝ち
                if (!map.ContainsKey(key))
                {
                    map[key] = f;
                }
            }
            return map;
        }

        private static bool TryParseDotFilename(string name, string prefix, out DotKey key)
        {
            // 例:
            // dot512-material-S100-P0.05-N10.png
            // skia-dot512-S100-P0.05-N10.png
            key = default;
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var body = name.Substring(prefix.Length);
            if (!body.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            body = body.Substring(0, body.Length - 4);
            // body: S{S}-P{P}-N{N}
            var parts = body.Split('-');
            if (parts.Length < 3)
            {
                return false;
            }

            if (!TryParseTaggedDouble(parts[0], 'S', out var s))
            {
                return false;
            }
            if (!TryParseTaggedDouble(parts[1], 'P', out var p))
            {
                return false;
            }
            if (!TryParseTaggedInt(parts[2], 'N', out var n))
            {
                return false;
            }

            // 表記ゆれ吸収（キーは丸めて固定）
            s = Math.Round(s, 2);
            p = Math.Round(p, 4);
            key = new DotKey(s, p, n);
            return true;
        }

        private static bool TryParseTaggedDouble(string token, char tag, out double value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(token) || token[0] != tag)
            {
                return false;
            }

            return double.TryParse(token.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseTaggedInt(string token, char tag, out int value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(token) || token[0] != tag)
            {
                return false;
            }

            return int.TryParse(token.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static async Task<StorageFolder> PickFolderAsync(string title)
        {
            // UWPのFolderPickerはTitleが設定できない（WinUI3と違う）ので、事前にダイアログで誘導
            if (!string.IsNullOrWhiteSpace(title))
            {
                await ShowMessageAsync("フォルダ選択", title);
            }

            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".png");
            return await picker.PickSingleFolderAsync();
        }

        private static async Task ShowMessageAsync(string title, string message)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "";
            }

            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }

        private readonly struct DotKey : IEquatable<DotKey>
        {
            internal DotKey(double s, double p, int n)
            {
                S = s;
                P = p;
                N = n;
            }

            internal double S { get; }
            internal double P { get; }
            internal int N { get; }

            public bool Equals(DotKey other)
                => S.Equals(other.S) && P.Equals(other.P) && N == other.N;

            public override bool Equals(object obj)
                => obj is DotKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = (hash * 31) + S.GetHashCode();
                    hash = (hash * 31) + P.GetHashCode();
                    hash = (hash * 31) + N.GetHashCode();
                    return hash;
                }
            }
        }

        private sealed class DotKeyComparer : IComparer<DotKey>
        {
            internal static DotKeyComparer Instance { get; } = new DotKeyComparer();

            public int Compare(DotKey x, DotKey y)
            {
                var c = x.S.CompareTo(y.S);
                if (c != 0) return c;
                c = x.P.CompareTo(y.P);
                if (c != 0) return c;
                return x.N.CompareTo(y.N);
            }
        }
    }
}
