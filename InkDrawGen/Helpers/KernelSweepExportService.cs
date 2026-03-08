using Microsoft.Graphics.Canvas;
using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml.Controls;

namespace InkDrawGen.Helpers
{
    internal static class KernelSweepExportService
    {
        internal static async Task ExportKernelSweepCsvAsync(MainPage page)
        {
            if (page == null) throw new ArgumentNullException(nameof(page));

            var state = InkDrawGenUiReader.Read(page);
            var scale = Math.Max(1, state.Scale);

            // 観測点（出力px座標）
            var obsPxX = ReadIntFromTextBox(page, "KernelObsPxXTextBox", 0);
            var obsPxY = ReadIntFromTextBox(page, "KernelObsPxYTextBox", 100);

            // 中心のXオフセット（出力px基準）
            var dxFromPx = ReadIntFromTextBox(page, "KernelDxFromPxTextBox", 0);
            var dxToPx = ReadIntFromTextBox(page, "KernelDxToPxTextBox", 100);
            var dxStepPx = ReadIntFromTextBox(page, "KernelDxStepPxTextBox", 1);
            if (dxStepPx == 0) dxStepPx = 1;

            var sampleCanvasPx = ReadIntFromTextBox(page, "KernelSampleCanvasPxTextBox", 9);
            if (sampleCanvasPx <= 0) sampleCanvasPx = 1;
            if ((sampleCanvasPx % 2) == 0) sampleCanvasPx += 1;

            // S/P/Op は既存UIの start を使用（最短）
            var sDip = state.S.Start;
            var pressure = (float)Math.Clamp(state.P.Start, 0.0, 1.0);
            var opacity = (float)Math.Clamp(state.Opacity.Start, 0.01, 5.0);

            var dpi = (float)Math.Max(1.0, state.Dpi);

            // 出力先フォルダ
            var folder = await PickOutputFolderBestEffortAsync(page, state.OutputFolder);
            if (folder is null)
            {
                return;
            }

            // 観測点（DIP）
            var obsDip = new Point(obsPxX / (double)scale, obsPxY / (double)scale);

            // サンプルキャンバス中心のローカルpx座標
            var local = sampleCanvasPx / 2;

            // ROI（DIP）: 観測点がサンプルキャンバス中心に来るようにする
            var roiDip = new Rect(
                x: obsDip.X - (local / (double)scale),
                y: obsDip.Y - (local / (double)scale),
                width: sampleCanvasPx / (double)scale,
                height: sampleCanvasPx / (double)scale);

            // 透明背景でαを取得する（白背景だとαが潰れるため）
            var transparent = true;

            var dxStart = dxFromPx;
            var dxEnd = dxToPx;
            var step = dxStepPx;
            if (dxStart > dxEnd)
            {
                (dxStart, dxEnd) = (dxEnd, dxStart);
            }

            var sb = new StringBuilder(capacity: 32 * 1024);
            sb.AppendLine("dx_px,r_px,obs_x_px,obs_y_px,alpha_byte,alpha01,gray_on_white_byte");

            var device = CanvasDevice.GetSharedDevice();
            using (var target = new CanvasRenderTarget(device, sampleCanvasPx, sampleCanvasPx, dpi))
            {
                for (var dxPx = dxStart; dxPx <= dxEnd; dxPx += step)
                {
                    // 観測点を固定し、中心だけX方向へ動かす（dx=0で中心=観測点）
                    var centerDip = new Point(
                        x: obsDip.X + (dxPx / (double)scale),
                        y: obsDip.Y);

                    var stroke = InkStrokeBuildService.BuildSDotStroke(centerDip, sDip, pressure, opacity);

                    using (var ds = target.CreateDrawingSession())
                    {
                        ds.Clear(transparent ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                        // ROIを(0,0)へ持ってきてからスケールする。
                        ds.Transform = System.Numerics.Matrix3x2.CreateScale(scale)
                            * System.Numerics.Matrix3x2.CreateTranslation(-(float)roiDip.X, -(float)roiDip.Y);

                        ds.DrawInk(new[] { stroke });
                    }

                    var bytes = target.GetPixelBytes();
                    var i = ((local * sampleCanvasPx) + local) * 4;
                    if (i < 0 || (i + 3) >= bytes.Length)
                    {
                        continue;
                    }

                    // Win2DのGetPixelBytesはBGRA
                    var a = bytes[i + 3];
                    var a01 = a / 255.0;
                    var grayOnWhite = (byte)(255 - a);

                    sb.Append(dxPx.ToString(CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(Math.Abs(dxPx).ToString(CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(obsPxX.ToString(CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(obsPxY.ToString(CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(a.ToString(CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(a01.ToString("0.########", CultureInfo.InvariantCulture)).Append(',');
                    sb.Append(grayOnWhite.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine();
                }
            }

            var fileName = BuildFileName(sDip, pressure, opacity, scale, obsPxX, obsPxY, dxStart, dxEnd, step, sampleCanvasPx);
            var outFile = await folder.CreateFileAsync(fileName, CreationCollisionOption.ReplaceExisting);
            await FileIO.WriteTextAsync(outFile, sb.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);

            await ShowDoneDialogAsync(page, outFile.Path, sDip, pressure, scale, obsPxX, obsPxY, dxStart, dxEnd, step, sampleCanvasPx);
        }

        private static async Task<StorageFolder?> PickOutputFolderBestEffortAsync(MainPage page, string outputFolderPath)
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

        private static async Task ShowDoneDialogAsync(MainPage page, string outPath, double sDip, float pressure, int scale, int obsPxX, int obsPxY, int dxFromPx, int dxToPx, int dxStepPx, int sampleCanvasPx)
        {
            var dlg = new ContentDialog
            {
                Title = "カーネル断面CSV",
                Content = $"完了: CSVを書き出しました。\n\nfile={outPath}\nS={sDip.ToString("0.####", CultureInfo.InvariantCulture)} P={pressure.ToString("0.####", CultureInfo.InvariantCulture)} scale={scale}\nobs=({obsPxX},{obsPxY}) dx={dxFromPx}..{dxToPx} step={dxStepPx} canvas={sampleCanvasPx}px",
                CloseButtonText = "OK"
            };
            await dlg.ShowAsync();
        }

        private static string BuildFileName(double sDip, float pressure, float opacity, int scale, int obsPxX, int obsPxY, int dxFromPx, int dxToPx, int dxStepPx, int sampleCanvasPx)
        {
            var s = ((int)Math.Round(sDip, MidpointRounding.AwayFromZero)).ToString("D4", CultureInfo.InvariantCulture);
            var p = ((int)Math.Round(pressure * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            var op = ((int)Math.Round(opacity * 1000, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
            return $"kernel-sweep-S{s}-P{p}-Op{op}-scale{scale}-obs{obsPxX}_{obsPxY}-dx{dxFromPx}_{dxToPx}_step{dxStepPx}-canvas{sampleCanvasPx}.csv";
        }

        private static int ReadIntFromTextBox(MainPage page, string name, int fallback)
        {
            var tb = page.FindName(name) as TextBox;
            if (tb == null) return fallback;
            var s = (tb.Text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(s)) return fallback;

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return v;
            return fallback;
        }
    }
}
