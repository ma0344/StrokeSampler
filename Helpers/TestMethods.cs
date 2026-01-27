using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;
using Windows.UI;
using Windows.Foundation;
using Windows.Storage.Streams;
using static StrokeSampler.StrokeHelpers;

namespace StrokeSampler
{
    internal class TestMethods
    {
        internal static async Task ExportDot512PreSaveAlphaSummaryCsvAsync(MainPage mp)
            => await ExportDot512PreSaveAlphaSummaryCsvWithPressureSweepAsync(mp, pressureStartInclusive: 0.0100f, pressureEndInclusive: 0.0200f, pressureStep: 0.0001f);

        internal static async Task ExportDot512PreSaveAlphaSummaryCsvWithPressureSweepAsync(
            MainPage mp,
            float pressureStartInclusive,
            float pressureEndInclusive,
            float pressureStep)
        {
            if (mp is null)
            {
                throw new ArgumentNullException(nameof(mp));
            }

            var sizes = UIHelpers.GetDot512BatchSizes(mp);
            if (sizes.Count == 0)
            {
                sizes = new[] { 200.0 };
            }

            IReadOnlyList<float> ps;
            {
                var userPs = UIHelpers.GetDot512BatchPs(mp);
                ps = userPs.Count != 0
                    ? userPs
                    : CreatePressureSweep(pressureStartInclusive, pressureEndInclusive, pressureStep);
            }
            var ns = UIHelpers.GetDot512BatchNs(mp);
            if (ns.Count == 0)
            {
                ns = new[] { UIHelpers.GetDot512Overwrite(mp) };
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

            var attributesBase = CreatePencilAttributesFromToolbarBestEffort(mp);
            var device = CanvasDevice.GetSharedDevice();

            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var sb = new StringBuilder(1024);
            sb.AppendLine("S,P,N,center4_mean,max_alpha,nonzero_count,nonzero_ratio,center4_mean_circle,max_alpha_circle,nonzero_count_circle,nonzero_ratio_circle");

            var fileNameSuffix = "";
            if (sizes.Count == 1 && ns.Count == 1)
            {
                fileNameSuffix += $"-S{sizes[0]}-N{ns[0]}";
            }

            // 圧力範囲が指定されている場合はファイル名に残す（ユーザーP一覧指定時は除外）
            if (UIHelpers.GetDot512BatchPs(mp).Count == 0)
            {
                fileNameSuffix += $"-P{pressureStartInclusive:0.####}-to-{pressureEndInclusive:0.####}-step{pressureStep:0.####}";
            }

            foreach (var s in sizes)
            {
                var attributes = attributesBase;
                attributes.Size = new Size(s, s);

                foreach (var p in ps)
                {
                    foreach (var n in ns)
                    {
                        using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                        {
                            using (var ds = target.CreateDrawingSession())
                            {
                                ds.Clear(Color.FromArgb(0, 0, 0, 0));

                                for (var k = 0; k < n; k++)
                                {
                                    var dot = CreatePencilDot(cx, cy, p, attributes);
                                    ds.DrawInk(new[] { dot });
                                }
                            }

                            var bytes = target.GetPixelBytes();
                            var stats = ComputeAlphaStats(bytes, width: MainPage.Dot512Size, height: MainPage.Dot512Size);
                            var statsCircle = ComputeAlphaStatsCenterCircle(bytes, width: MainPage.Dot512Size, height: MainPage.Dot512Size, diameterPx: (int)Math.Round(s));

                            sb.Append(s.ToString("0.##", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(p.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(n.ToString(CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(stats.Center4Mean.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(stats.MaxAlpha.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(stats.NonZeroCount.ToString(CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(stats.NonZeroRatio.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(statsCircle.Center4Mean.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(statsCircle.MaxAlpha.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(statsCircle.NonZeroCount.ToString(CultureInfo.InvariantCulture));
                            sb.Append(',');
                            sb.Append(statsCircle.NonZeroRatio.ToString("0.######", CultureInfo.InvariantCulture));
                            sb.AppendLine();
                        }
                    }
                }
            }

            var outFile = await folder.CreateFileAsync($"uwp-pre-save-alpha-summary{fileNameSuffix}.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var dialog = new ContentDialog
            {
                Title = "保存前α観測",
                Content = $"完了: {outFile.Path}",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }

        internal static async Task ExportDot512PreSaveAlphaFloorBySizeCsvAsync(MainPage mp)
        {
            if (mp is null)
            {
                throw new ArgumentNullException(nameof(mp));
            }

            const int sMin = 2;
            const int sMax = 200;

            // まずは既知の傾向に合わせて範囲を固定（S>=2）
            const float pMin = 0.0000f;
            const float pMax = 0.0105f;
            const float pStep = 0.0001f;

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

            var attributesBase = CreatePencilAttributesFromToolbarBestEffort(mp);
            attributesBase.Size = new Size(1, 1);

            var device = CanvasDevice.GetSharedDevice();
            var cx = (MainPage.Dot512Size - 1) / 2f;
            var cy = (MainPage.Dot512Size - 1) / 2f;

            var sb = new StringBuilder(4096);
            sb.AppendLine("S,p_floor,max_alpha,nonzero_count");

            for (var s = sMin; s <= sMax; s++)
            {
                var attributes = attributesBase;
                attributes.Size = new Size(s, s);

                var (found, pFloor, stats) = FindPressureFloorByBinarySearch(
                    device,
                    cx,
                    cy,
                    attributes,
                    pMin,
                    pMax,
                    pStep);

                sb.Append(s.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append((found ? pFloor : float.NaN).ToString("0.######", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(stats.MaxAlpha.ToString("0.######", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(stats.NonZeroCount.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
            }

            var outFile = await folder.CreateFileAsync($"uwp-pre-save-alpha-floor-S{sMin}-to-S{sMax}-P{pMin:0.######}-to-{pMax:0.######}-step{pStep:0.######}.csv", CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            var dialog = new ContentDialog
            {
                Title = "保存前α床（S別）",
                Content = $"完了: {outFile.Path}",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }

        private static (bool Found, float PFlooor, AlphaStats StatsAtFloor) FindPressureFloorByBinarySearch(
            CanvasDevice device,
            float cx,
            float cy,
            Windows.UI.Input.Inking.InkDrawingAttributes attributes,
            float pMin,
            float pMax,
            float pStep)
        {
            // 判定関数: max_alpha > 0 なら描画あり
            (bool HasInk, AlphaStats Stats) Eval(float p)
            {
                using (var target = new CanvasRenderTarget(device, MainPage.Dot512Size, MainPage.Dot512Size, MainPage.Dot512Dpi))
                {
                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(Color.FromArgb(0, 0, 0, 0));
                        var dot = CreatePencilDot(cx, cy, p, attributes);
                        ds.DrawInk(new[] { dot });
                    }

                    var bytes = target.GetPixelBytes();
                    var stats = ComputeAlphaStats(bytes, width: MainPage.Dot512Size, height: MainPage.Dot512Size);
                    return (stats.MaxAlpha > 0, stats);
                }
            }

            // 範囲外（maxでも0）の場合は見つからない
            var (hasAtMax, statsAtMax) = Eval(pMax);
            if (!hasAtMax)
            {
                return (false, float.NaN, statsAtMax);
            }

            // pMinが既に非ゼロなら、pMinを返す
            var (hasAtMin, statsAtMin) = Eval(pMin);
            if (hasAtMin)
            {
                return (true, pMin, statsAtMin);
            }

            // 0.0001単位で境界を返すため、整数インデックスで二分探索する
            var minI = 0;
            var maxI = (int)Math.Round((pMax - pMin) / pStep);
            if (maxI < 1)
            {
                return (false, float.NaN, statsAtMax);
            }

            AlphaStats bestStats = statsAtMax;
            var bestI = maxI;

            while (minI + 1 < maxI)
            {
                var midI = (minI + maxI) / 2;
                var p = pMin + (pStep * midI);
                var (has, st) = Eval(p);
                if (has)
                {
                    bestI = midI;
                    bestStats = st;
                    maxI = midI;
                }
                else
                {
                    minI = midI;
                }
            }

            var pFloor = pMin + (pStep * bestI);
            pFloor = (float)Math.Round(pFloor, 4);
            return (true, pFloor, bestStats);
        }

        private static AlphaStats ComputeAlphaStatsCenterCircle(byte[] bgraBytes, int width, int height, int diameterPx)
        {
            // 直径Sの中心円内だけで統計を取る（S違いの面積差が反映される）
            if (diameterPx <= 0)
            {
                return new AlphaStats(double.NaN, double.NaN, 0, double.NaN);
            }

            var cx = (width - 1) * 0.5;
            var cy = (height - 1) * 0.5;
            var radius = diameterPx * 0.5;
            var r2 = radius * radius;

            long total = 0;
            long nonZero = 0;
            var maxA = 0;

            for (var y = 0; y < height; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < width; x++)
                {
                    var dx = x - cx;
                    if ((dx * dx + dy * dy) > r2)
                    {
                        continue;
                    }

                    total++;
                    var a = bgraBytes[(y * width + x) * 4 + 3];
                    if (a != 0)
                    {
                        nonZero++;
                    }

                    if (a > maxA)
                    {
                        maxA = a;
                    }
                }
            }

            // center4は幾何中心の4pxなので、円内/円外で値は変わらない想定だが、列としては同じ方式で出す
            var center4 = SampleCenterAlpha4px(bgraBytes, width, height);

            return new AlphaStats(
                Center4Mean: center4,
                MaxAlpha: maxA / 255.0,
                NonZeroCount: nonZero,
                NonZeroRatio: total > 0 ? (double)nonZero / total : double.NaN);
        }

        private static IReadOnlyList<float> CreatePressureSweep(float startInclusive, float endInclusive, float step)
        {
            if (step <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(step));
            }

            if (endInclusive < startInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(endInclusive));
            }

            var count = (int)Math.Floor((endInclusive - startInclusive) / step) + 1;
            count = Math.Clamp(count, 1, 10000);

            var list = new float[count];
            var digits = step >= 1
                ? 0
                : step >= 0.1f
                    ? 1
                    : step >= 0.01f
                        ? 2
                        : step >= 0.001f
                            ? 3
                            : 4;
            for (var i = 0; i < count; i++)
            {
                var p = startInclusive + (step * i);

                // float誤差の吸収と、CSV上の重複回避
                p = (float)Math.Round(p, digits);
                list[i] = p;
            }
            return list;
        }

        private static AlphaStats ComputeAlphaStats(byte[] bgraBytes, int width, int height)
        {
            // BGRA8 のA(0..255)を対象に簡易統計を取る
            var total = (long)width * height;
            long nonZero = 0;
            var maxA = 0;

            for (var i = 3; i < bgraBytes.Length; i += 4)
            {
                var a = bgraBytes[i];
                if (a != 0)
                {
                    nonZero++;
                }

                if (a > maxA)
                {
                    maxA = a;
                }
            }

            var center4 = SampleCenterAlpha4px(bgraBytes, width, height);
            return new AlphaStats(
                Center4Mean: center4,
                MaxAlpha: maxA / 255.0,
                NonZeroCount: nonZero,
                NonZeroRatio: total > 0 ? (double)nonZero / total : double.NaN);
        }

        private static double SampleCenterAlpha4px(byte[] bgraBytes, int width, int height)
        {
            if (width <= 1 || height <= 1)
            {
                return double.NaN;
            }

            var x0 = (width / 2) - 1;
            var y0 = (height / 2) - 1;
            var x1 = x0 + 1;
            var y1 = y0 + 1;
            if (x0 < 0 || y0 < 0 || x1 >= width || y1 >= height)
            {
                return double.NaN;
            }

            var a00 = bgraBytes[(y0 * width + x0) * 4 + 3] / 255.0;
            var a10 = bgraBytes[(y0 * width + x1) * 4 + 3] / 255.0;
            var a01 = bgraBytes[(y1 * width + x0) * 4 + 3] / 255.0;
            var a11 = bgraBytes[(y1 * width + x1) * 4 + 3] / 255.0;
            return (a00 + a10 + a01 + a11) * 0.25;
        }

        private readonly struct AlphaStats
        {
            internal AlphaStats(double Center4Mean, double MaxAlpha, long NonZeroCount, double NonZeroRatio)
            {
                this.Center4Mean = Center4Mean;
                this.MaxAlpha = MaxAlpha;
                this.NonZeroCount = NonZeroCount;
                this.NonZeroRatio = NonZeroRatio;
            }

            internal double Center4Mean { get; }
            internal double MaxAlpha { get; }
            internal long NonZeroCount { get; }
            internal double NonZeroRatio { get; }
        }
    }
}
