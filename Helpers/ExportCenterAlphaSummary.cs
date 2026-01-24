using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Windows.UI.Xaml.Controls;

namespace StrokeSampler
{
    internal static class ExportCenterAlphaSummary
    {
        internal static async Task ExportAsync(MainPage mp)
        {
            // 中心αサマリCSVの収集・生成処理は `CenterAlphaSummaryCsvBuilder` に共通化している。
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

            var result = await CenterAlphaSummaryCsvBuilder.BuildFromFolderAsync(
                folder,
                isTargetCsvFile: name =>
                    name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
                    && name.StartsWith("radial-falloff-", StringComparison.OrdinalIgnoreCase),
                tryParseKey: name =>
                {
                    if (!ParseFalloffFilenameService.TryParseFalloffFilename(name, out var s, out var p, out var n))
                    {
                        return (false, default, default, default);
                    }

                    return (true, s, p, n);
                },
                tryReadCenterAlpha: text =>
                {
                    if (!ReadCenterACSV.TryReadCenterAlphaFromFalloffCsv(text, out var centerAlpha))
                    {
                        return (false, default);
                    }

                    return (true, centerAlpha);
                });

            if (result.Rows == 0)
            {
                var dlg0 = new ContentDialog
                {
                    Title = "中心αサマリCSV",
                    Content = "対象CSVが見つかりませんでした（radial-falloff-S*-P*-N*.csv）。",
                    CloseButtonText = "OK"
                };
                await dlg0.ShowAsync();
                return;
            }

            var outName = "center-alpha-vs-N-vs-P.csv";
            await CenterAlphaSummaryCsvBuilder.SaveAsUtf8Async(folder, outName, result.CsvText);

            var dlg = new ContentDialog
            {
                Title = "中心αサマリCSV",
                Content = $"完了: {result.Rows}行を書き出しました。スキップ={result.Skipped}件。\n出力={outName}",
                CloseButtonText = "OK"
            };
            await dlg.ShowAsync();
        }
    }
}
