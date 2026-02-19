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
    }
}
