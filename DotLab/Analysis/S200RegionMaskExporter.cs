using Microsoft.Win32;
using SkiaSharp;
using System.Globalization;
using System.IO;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace DotLab.Analysis;

internal static class S200RegionMaskExporter
{
    internal static async Task ExportAsync(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var s = window.MaskSNumberBox?.Value ?? 200;
        if (s <= 0) s = 200;

        var rDip = s / 2.0;

        var periodDip = window.MaskPeriodDipNumberBox?.Value ?? 1.75;
        if (periodDip <= 0) periodDip = 1.75;

        var scale = (int)(window.MaskScaleNumberBox?.Value ?? 10);
        if (scale <= 0) scale = 10;

        var yMarginPx = (int)(window.MaskYMarginPxNumberBox?.Value ?? 0);
        if (yMarginPx < 0) yMarginPx = 0;
        var yMarginDip = yMarginPx / (double)scale;

        var includeMargin = window.MaskInclude1DipMarginCheckBox?.IsChecked == true;
        var marginDip = includeMargin ? 1.0 : 0.0;

        // Step1: 基準円（中心=0）と、左に (periodDip*scale)*10 px 相当だけ移動した円の交差領域。
        // Step2: さらに左に (periodDip*scale)*11 px 相当だけ移動した円との交差部分を除外する。
        // ここで periodDip は「DIP上の周期」だが、aligned実験と同様に scale を掛けた量子化寄りの移動量にする。
        var dDip = periodDip;
        var stepDip = dDip * scale;
        var shiftDip = stepDip * 10.0;
        var c0x = 0.0;
        var c1x = shiftDip;
        var c2x = (stepDip * 11.0);
        var dist = Math.Abs(c1x - c0x);

        if (dist >= 2 * rDip)
        {
            System.Windows.MessageBox.Show(window, "移動量が大きすぎて円が交差しません。", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // 外接矩形は2円交差領域から見積もる
        var ixDip = dist / 2.0;
        var iyDip = Math.Sqrt(Math.Max(0.0, (rDip * rDip) - (ixDip * ixDip)));
        var minXDip = Math.Min(c0x, c1x) + (dist - rDip);
        var maxXDip = Math.Max(c0x, c1x) - (dist - rDip);
        var minYDip = -iyDip;
        var maxYDip = iyDip;

        // マージンを左側に足す（要求：maskの(0,0)は11番左端,0。必要なら左に1DIP余白も含める）
        var originXDip = minXDip - marginDip;
        var originYDip = minYDip;

        // 出力キャンバスサイズを固定する
        // - Height(px) = (S + 2*YMarginDip) * Scale
        // - Width(px)  = (PeriodDip * Scale) * 10
        //   ※PeriodDipはDIPなので、pxにするにはscaleを1回掛けるだけ。
        var wPx = Math.Max(1, (int)Math.Round(dDip * scale * 10.0, MidpointRounding.AwayFromZero));
        var hPx = Math.Max(1, (int)Math.Round((s + 2 * yMarginDip) * scale, MidpointRounding.AwayFromZero));

        if (wPx > 10000 || hPx > 10000)
        {
            System.Windows.MessageBox.Show(window, $"Mask size too large: {wPx}x{hPx}. Check PeriodDip and Scale inputs.", "DotLab", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return;
        }

        // マスク形状（計算上の外接矩形）をキャンバス内で上下中央に配置する
        var shapeWidthDip = (maxXDip - minXDip) + marginDip;
        var shapeHeightDip = (maxYDip - minYDip);
        if (shapeWidthDip <= 0 || shapeHeightDip <= 0) return;

        // 左端はキャンバス左端に合わせ、上下は中央揃え
        originXDip = minXDip - marginDip;
        var canvasHeightDip = s + 2 * yMarginDip;
        var shapeTopDip = (canvasHeightDip - shapeHeightDip) / 2.0;
        originYDip = minYDip - shapeTopDip;

        using var bmp = new SKBitmap(wPx, hPx, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Transparent);

        // ピクセルごとに (Circle0 ∩ Circle1) \ (Circle2) の領域判定を行いマスク化する。
        // 境界のギザつきを抑えるため、スーパーサンプリングで被覆率を推定する。
        for (var y = 0; y < hPx; y++)
        {
            for (var x = 0; x < wPx; x++)
            {
                // 2x2 サブサンプル
                var hit = 0;
                for (var sy = 0; sy < 2; sy++)
                {
                    var yDip = originYDip + (y + (0.25 + 0.5 * sy)) / scale;
                    for (var sx = 0; sx < 2; sx++)
                    {
                        var xDip = originXDip + (x + (0.25 + 0.5 * sx)) / scale;

                        var dx0 = xDip - c0x;
                        var dy0 = yDip;
                        var dx1 = xDip - c1x;
                        var dy1 = yDip;
                        var dx2 = xDip - c2x;
                        var dy2 = yDip;

                        var in0 = (dx0 * dx0 + dy0 * dy0) <= rDip * rDip;
                        var in1 = (dx1 * dx1 + dy1 * dy1) <= rDip * rDip;
                        if (!in0 || !in1) continue;

                        // 3つ目にも入っている部分は削除
                        var in2 = (dx2 * dx2 + dy2 * dy2) <= rDip * rDip;
                        if (!in2)
                        {
                            hit++;
                        }
                    }
                }

                if (hit > 0)
                {
                    var a = (byte)(hit * 255 / 4);
                    bmp.SetPixel(x, y, new SKColor(255, 255, 255, a));
                }
            }
        }

        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });
        picker.SuggestedFileName = $"mask-s{(int)s}-r{rDip.ToString("0.##", CultureInfo.InvariantCulture)}-d{dDip.ToString("0.###", CultureInfo.InvariantCulture)}-scale{scale}" + (includeMargin ? "-margin1dip" : "");

        InitializeWithWindow.Initialize(picker, new System.Windows.Interop.WindowInteropHelper(window).Handle);
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        using var stream = await file.OpenStreamForWriteAsync();
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        data.SaveTo(stream);
    }
}
