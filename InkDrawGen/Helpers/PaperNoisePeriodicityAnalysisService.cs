using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls;

namespace InkDrawGen.Helpers
{
    internal static class PaperNoisePeriodicityAnalysisService
    {
        internal static async Task ExportPeriodicityCsvAsync(MainPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            var ui = InkDrawGenUiReader.Read(page);
            if (string.IsNullOrWhiteSpace(ui.OutputFolder))
            {
                await new MessageDialog("出力フォルダを選択してください。", "InkDrawGen").ShowAsync();
                return;
            }

            StorageFolder? outFolder;
            try
            {
                outFolder = await StorageFolder.GetFolderFromPathAsync(ui.OutputFolder);
            }
            catch
            {
                await new MessageDialog("出力フォルダが無効です。選択し直してください。", "InkDrawGen").ShowAsync();
                return;
            }

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary
            };
            picker.FileTypeFilter.Add(".png");

            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            // 大きい画像でも試せるように、間引き＋サンプル点抽出で相関を見に行く。
            const int downsample = 4;
            const int maxShiftPx = 1200;
            const int minShiftPx = 8;
            const int maxSamples = 60000;

            var device = CanvasDevice.GetSharedDevice();

            using var stream = await file.OpenAsync(FileAccessMode.Read);
            using var bmp = await CanvasBitmap.LoadAsync(device, stream);

            var w = (int)bmp.SizeInPixels.Width;
            var h = (int)bmp.SizeInPixels.Height;
            if (w <= 0 || h <= 0)
            {
                await new MessageDialog("PNGの読み込みに失敗しました。", "InkDrawGen").ShowAsync();
                return;
            }

            var bytes = bmp.GetPixelBytes();
            var (dw, dh, alpha) = DownsampleAlpha(bytes, w, h, downsample);

            var samples = BuildSamples(alpha, dw, dh, maxSamples);
            if (samples.Count < 2000)
            {
                await new MessageDialog($"有効なサンプル点が不足しています。samples={samples.Count}\n(α>0 の点が少なすぎる可能性があります)", "InkDrawGen").ShowAsync();
                return;
            }

            var minShiftDs = Math.Max(1, (int)Math.Round(minShiftPx / (double)downsample, MidpointRounding.AwayFromZero));
            var maxShiftDs = Math.Min(Math.Min(dw, dh) - 1, (int)Math.Round(maxShiftPx / (double)downsample, MidpointRounding.AwayFromZero));
            if (maxShiftDs <= minShiftDs)
            {
                await new MessageDialog("解析範囲が無効です（画像が小さすぎる可能性があります）。", "InkDrawGen").ShowAsync();
                return;
            }

            var rows = new List<ResultRow>(capacity: (maxShiftDs - minShiftDs + 1) * 2);

            // X方向
            for (var shift = minShiftDs; shift <= maxShiftDs; shift++)
            {
                var r = ComputeShiftStats(alpha, dw, dh, samples, shiftX: shift, shiftY: 0);
                r.Axis = "x";
                rows.Add(r);
            }

            // Y方向
            for (var shift = minShiftDs; shift <= maxShiftDs; shift++)
            {
                var r = ComputeShiftStats(alpha, dw, dh, samples, shiftX: 0, shiftY: shift);
                r.Axis = "y";
                rows.Add(r);
            }

            var baseName = $"paper-noise-periodicity-{SanitizeFileName(Path.GetFileNameWithoutExtension(file.Name))}-ds{downsample}-max{maxShiftPx}";
            var csv = await outFolder.CreateFileAsync(baseName + ".csv", CreationCollisionOption.ReplaceExisting);

            var sb = new StringBuilder(capacity: 64 * 1024);
            sb.AppendLine("axis,shift_px,shift_ds,corr,mae,count,downsample,src_w,src_h,ds_w,ds_h,samples");
            foreach (var r in rows)
            {
                var shiftPx = r.ShiftDs * downsample;
                sb.Append(r.Axis);
                sb.Append(',');
                sb.Append(shiftPx.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(r.ShiftDs.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(r.Corr.ToString("0.########", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(r.Mae.ToString("0.########", CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(r.Count.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(downsample.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(w.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(h.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(dw.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(dh.ToString(CultureInfo.InvariantCulture));
                sb.Append(',');
                sb.Append(samples.Count.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine();
            }

            await FileIO.WriteTextAsync(csv, sb.ToString());

            var topX = rows.Where(r => r.Axis == "x" && r.Count >= 2000)
                .OrderByDescending(r => r.Corr)
                .Take(10)
                .ToArray();
            var topY = rows.Where(r => r.Axis == "y" && r.Count >= 2000)
                .OrderByDescending(r => r.Corr)
                .Take(10)
                .ToArray();

            var summary = new StringBuilder(1024);
            summary.AppendLine("紙目周期解析（自己相関）");
            summary.AppendLine($"src={w}x{h}  ds={downsample} -> {dw}x{dh}  samples={samples.Count}");
            summary.AppendLine($"range=shift {minShiftDs * downsample}px .. {maxShiftDs * downsample}px");
            summary.AppendLine();
            summary.AppendLine("Top X (corr):");
            foreach (var r in topX)
            {
                summary.AppendLine($"  shift={r.ShiftDs * downsample}px  corr={r.Corr:0.####}  mae={r.Mae:0.###}  n={r.Count}");
            }
            summary.AppendLine();
            summary.AppendLine("Top Y (corr):");
            foreach (var r in topY)
            {
                summary.AppendLine($"  shift={r.ShiftDs * downsample}px  corr={r.Corr:0.####}  mae={r.Mae:0.###}  n={r.Count}");
            }
            summary.AppendLine();
            summary.AppendLine($"CSV: {csv.Path}");

            AppendLog(page, summary.ToString() + "\n");

            await new ContentDialog
            {
                Title = "InkDrawGen",
                Content = summary.ToString(),
                CloseButtonText = "OK"
            }.ShowAsync();
        }

        private static (int Dw, int Dh, byte[] Alpha) DownsampleAlpha(byte[] bgra, int w, int h, int step)
        {
            if (bgra == null) throw new ArgumentNullException(nameof(bgra));
            if (w <= 0) throw new ArgumentOutOfRangeException(nameof(w));
            if (h <= 0) throw new ArgumentOutOfRangeException(nameof(h));
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));

            var dw = (w + step - 1) / step;
            var dh = (h + step - 1) / step;
            var a = new byte[dw * dh];

            for (var y = 0; y < dh; y++)
            {
                var sy = Math.Min(h - 1, y * step);
                for (var x = 0; x < dw; x++)
                {
                    var sx = Math.Min(w - 1, x * step);
                    var idx = ((sy * w) + sx) * 4;
                    a[(y * dw) + x] = bgra[idx + 3];
                }
            }

            return (dw, dh, a);
        }

        private static List<(int X, int Y)> BuildSamples(byte[] alpha, int w, int h, int maxSamples)
        {
            var all = new List<(int X, int Y)>(capacity: Math.Min(maxSamples, 100000));

            // α>0 の点から上限までサンプリングする（規則性がある前提なのでランダムで十分）。
            // UWPでの再現性確保のため固定seedにする。
            var rng = new Random(12345);

            // まず候補点を収集（粗く走査してコストを抑える）
            const int scanStride = 2;
            for (var y = 0; y < h; y += scanStride)
            {
                var row = y * w;
                for (var x = 0; x < w; x += scanStride)
                {
                    if (alpha[row + x] == 0) continue;
                    all.Add((x, y));
                }
            }

            if (all.Count <= maxSamples)
            {
                return all;
            }

            // Fisher-Yatesでシャッフルして先頭を取る
            for (var i = all.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                (all[i], all[j]) = (all[j], all[i]);
            }

            return all.Take(maxSamples).ToList();
        }

        private static ResultRow ComputeShiftStats(byte[] alpha, int w, int h, List<(int X, int Y)> samples, int shiftX, int shiftY)
        {
            double sumAB = 0;
            double sumA2 = 0;
            double sumB2 = 0;
            long sumAbs = 0;
            var count = 0;

            foreach (var (x, y) in samples)
            {
                var x2 = x + shiftX;
                var y2 = y + shiftY;
                if ((uint)x2 >= (uint)w || (uint)y2 >= (uint)h) continue;

                var a = alpha[(y * w) + x];
                var b = alpha[(y2 * w) + x2];
                if (a == 0 || b == 0) continue;

                // 0..255をそのまま使う（平均差と相関だけ見たい）
                sumAB += a * (double)b;
                sumA2 += a * (double)a;
                sumB2 += b * (double)b;
                sumAbs += Math.Abs(a - b);
                count++;
            }

            var corr = 0.0;
            if (count > 0 && sumA2 > 0 && sumB2 > 0)
            {
                corr = sumAB / Math.Sqrt(sumA2 * sumB2);
            }

            var mae = count > 0 ? (sumAbs / (double)count) : 0.0;

            return new ResultRow
            {
                Axis = "?",
                ShiftDs = Math.Max(shiftX, shiftY),
                Corr = corr,
                Mae = mae,
                Count = count,
            };
        }

        private static string SanitizeFileName(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "noname";
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            }
            return sb.ToString();
        }

        private static void AppendLog(MainPage page, string s)
        {
            var tb = page.FindName("LogTextBox") as TextBox;
            if (tb == null) return;
            tb.Text = (tb.Text ?? string.Empty) + s;
        }

        private sealed class ResultRow
        {
            public string Axis { get; set; }
            public int ShiftDs { get; set; }
            public double Corr { get; set; }
            public double Mae { get; set; }
            public int Count { get; set; }
        }
    }
}
