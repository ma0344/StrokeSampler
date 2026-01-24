using Microsoft.Graphics.Canvas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Xaml.Controls;
using static StrokeSampler.StrokeHelpers;
using Windows.Foundation;
using Windows.UI.Input.Inking;
using StrokeSampler.Helpers;
namespace StrokeSampler
{
    internal class ExportHelpers
    {
         internal static async Task ExportRadialFalloffBatchAsync(MainPage mp)
         {
             await RadialFalloffExportService.ExportRadialFalloffBatchAsync(mp);
         }
 
         internal static async System.Threading.Tasks.Task ExportS200LineAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)
         {

            var _lastGeneratedAttributes = mp._lastGeneratedAttributes;
            var _lastOverwritePressure = mp._lastOverwritePressure;
            var _lastMaxOverwrite = mp._lastMaxOverwrite;
            var _lastDotGridSpacing = mp._lastDotGridSpacing;
            var _lastWasDotGrid = mp._lastWasDotGrid;


             var picker = new FileSavePicker
             {
                 SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                 SuggestedFileName = suggestedFileName
             };
             picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

             var file = await picker.PickSaveFileAsync();
             if (file is null)
             {
                 return;
             }

             CachedFileManager.DeferUpdates(file);

             var attributes = CreatePencilAttributesFromToolbarBestEffort(mp);
             attributes.Size = new Size(200, 200);
             _lastGeneratedAttributes = attributes;
             _lastOverwritePressure = null;
             _lastMaxOverwrite = null;
             _lastDotGridSpacing = null;
             _lastWasDotGrid = false;

             var pressure = 1.0f;

             const int exportSize = 1024;
             const float x0 = 150f;
             const float x1 = 874f;

             var device = CanvasDevice.GetSharedDevice();
             using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
             using (var target = new CanvasRenderTarget(device, exportSize, exportSize, MainPage.Dot512Dpi))
             {
                 using (var ds = target.CreateDrawingSession())
                 {
                     ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);

                     // 1024x1024内で横線を引く（中心付近）
                     var y = (exportSize - 1) / 2f;

                     var stroke = CreatePencilStroke(x0, x1, y, pressure, attributes);
                     ds.DrawInk(new[] { stroke });

                     if (includeLabels)
                     {
                         DrawingHelpers.DrawS200LineLabels(mp, ds, attributes, pressure, exportSize, x0, x1);
                     }
                 }

                 await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
             }

             await CachedFileManager.CompleteUpdatesAsync(file);
         }

         internal static async Task ExportPngAsync(MainPage mp, bool isTransparentBackground, bool includeLabels, string suggestedFileName)
         {
             var width = UIHelpers.GetExportWidth(mp);
             var height = UIHelpers.GetExportHeight(mp);

             var strokes = mp.InkCanvasControl.InkPresenter.StrokeContainer.GetStrokes();
             if (strokes.Count == 0)
             {
                 return;
             }

             var picker = new FileSavePicker
             {
                 SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                 SuggestedFileName = suggestedFileName
             };
             picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

             var file = await picker.PickSaveFileAsync();
             if (file is null)
             {
                 return;
             }

             CachedFileManager.DeferUpdates(file);

             using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
             {
                 var device = CanvasDevice.GetSharedDevice();

                 using (var target = new CanvasRenderTarget(device, width, height, 96f))
                 {
                     using (var ds = target.CreateDrawingSession())
                     {
                         ds.Clear(isTransparentBackground ? Color.FromArgb(0, 0, 0, 0) : Colors.White);
                         ds.DrawInk(strokes);

                         if (includeLabels)
                         {
                             DrawingHelpers.DrawPreviewLabels(mp, ds);
                         }
                     }

                     await target.SaveAsync(stream, CanvasBitmapFileFormat.Png);
                 }
             }

             await CachedFileManager.CompleteUpdatesAsync(file);
         }

         internal static async Task ExportNormalizedFalloffAsync(MainPage mp)
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
                 if (!f.Name.StartsWith("radial-falloff-", StringComparison.OrdinalIgnoreCase))
                 {
                     continue;
                 }

                 if (!TryParseFalloffFilename(f.Name, out var s, out var p, out var n))
                 {
                     skipped++;
                     continue;
                 }

                 var text = await FileIO.ReadTextAsync(f);
                 if (!TryParseFalloffCsv(text, out var fr))
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

                 samples.Add((s, p, n, fr));
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


    }
}
