using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// 空白ページの項目テンプレートについては、https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x411 を参照してください

namespace InkDrawGen
{
    /// <summary>
    /// それ自体で使用できる空白ページまたはフレーム内に移動できる空白ページ。
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        private async void PickOutputFolderButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.FolderPickerService.PickOutputFolderAsync(this);
        }

        private async void RunSingleButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.RunInkDrawJobsService.RunSingleAsync(this);
        }

        private async void RunSingleLine2PointsButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.RunInkDrawJobsService.RunSingleLine2PointsAsync(this);
        }

        private async void RunLine2PointsEndXSweepButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.RunInkDrawJobsService.RunLine2PointsEndXSweepAsync(this);
        }

        private async void RunLine2PointsStartXSweepButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.RunInkDrawJobsService.RunLine2PointsStartXSweepAsync(this);
        }

        private async void RunBatchFromCsvButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.RunInkDrawJobsService.RunBatchFromCsvAsync(this);
        }

        private async void ExportRadialAlphaCsvFromPngButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.RadialAlphaProfileExportService.ExportRadialAlphaCsvFromPngAsync(this);
        }

        private async void ExportRadialAlphaKneeSummaryButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.RadialAlphaProfileExportService.ExportRadialAlphaKneeSummaryAsync(this);
        }

        private async void ExportKernelSweepCsvButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.KernelSweepExportService.ExportKernelSweepCsvAsync(this);
        }

        private async void ExportKernelCanceledDotPngButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.KernelCanceledDotExportService.ExportKernelCanceledDotPngAsync(this);
        }

        private async void ExportPaperNoisePeriodicityCsvButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.PaperNoisePeriodicityAnalysisService.ExportPeriodicityCsvAsync(this);
        }

        private async void ExportNormalizedFalloffFromKernelSweepButton_Click(object sender, RoutedEventArgs e)
        {
            await Helpers.KernelSweepToNormalizedFalloffExportService.ExportNormalizedFalloffCsvFromKernelSweepAsync(this);
        }

        private void RoiHTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if(RoiHTextBox != null)
            {
                if (!string.IsNullOrWhiteSpace(RoiHTextBox.Text))
                {
                    if (ScaleTextBox != null)
                    {
                        if (int.TryParse(ScaleTextBox.Text, out var scale) && scale > 0)
                        {
                            if (double.TryParse(RoiHTextBox.Text, out var roiH) && roiH > 0)
                            {
                                OutHeightPxTextBox.Text = ((int)(roiH * scale)).ToString();
                            }
                        }
                    }
                }
            }
        }

        private void RoiWTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (RoiWTextBox != null)
            {
                if (!string.IsNullOrWhiteSpace(RoiWTextBox.Text))
                {
                    if (ScaleTextBox != null)
                    {
                        if (int.TryParse(ScaleTextBox.Text, out var scale) && scale > 0)
                        {
                            if (double.TryParse(RoiWTextBox.Text, out var roiW) && roiW > 0)
                            {
                                OutWidthPxTextBox.Text = ((int)(roiW * scale)).ToString();
                            }
                        }
                    }
                }
            }
        }

    }
}
