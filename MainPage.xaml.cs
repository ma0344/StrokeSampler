using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Input.Inking;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace StrokeSampler
{
    public sealed partial class MainPage : Page
    {

        public const double PencilStrokeWidthMin = 0.5;
        // 解析目的で大きいSizeも扱えるように上限を拡張する。
        // Dot512の端切れ防止は`GetDot512SizeOrNull`等で別途行う。
        public const double PencilStrokeWidthMax = 510.0;

        public static readonly float[] PressurePreset = {0.01f, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f };

        public const float DefaultOverwritePressure = 0.5f;
        public const int DefaultMaxOverwrite = 10;

        public const float DefaultStartX = 160f;
        public const float DefaultEndX = 1800f;
        public const float DefaultStartY = 120f;
        public const float DefaultSpacingY = 110f;

        public InkDrawingAttributes _lastGeneratedAttributes;
        public float? _lastOverwritePressure;
        public int? _lastMaxOverwrite;
        public int? _lastDotGridSpacing;
        public bool _lastWasDotGrid;

        public static readonly float[] DotGridPressurePreset = { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1.0f };
        public const float DefaultDotGridStartX = 260f;
        public const float DefaultDotGridStartY = 260f;
        public const int DefaultDotGridSpacing = 120;

        public const int PaperNoiseCropSize = 24;
        public const int PaperNoiseCropHalf = PaperNoiseCropSize / 2;

        public const int Dot512Size = 512;
        public const float Dot512Dpi = 96f;


        public static readonly int[] RadialAlphaThresholds = StrokeHelpers.CreateRadialAlphaThresholds();

        public MainPage()
        {
            InitializeComponent();

            InkCanvasControl.InkPresenter.InputDeviceTypes = Windows.UI.Core.CoreInputDeviceTypes.Mouse
                                                            | Windows.UI.Core.CoreInputDeviceTypes.Pen
                                                            | Windows.UI.Core.CoreInputDeviceTypes.Touch;
        }
        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateHelper.Generate(this);
        }

        private void GenerateOverwriteSamplesButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateHelper.GenerateOverwriteSamples(this);
        }

        private void GenerateDotGridButton_Click(object sender, RoutedEventArgs e)
        {
            GenerateHelper.GenerateDotGrid(this);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            InkCanvasControl.InkPresenter.StrokeContainer.Clear();

            _lastGeneratedAttributes = null;
            _lastOverwritePressure = null;
            _lastMaxOverwrite = null;
            _lastDotGridSpacing = null;
            _lastWasDotGrid = false;
        }

        private async void ExportRadialSamplesSummaryButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportRadialSamplesSummary.ExportAsync(this);
         }

        // XAMLのイベントハンドラが参照しているため、欠落するとビルドが失敗する。
        private async void ComparePaperNoiseModelsButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = "紙目モデル比較",
                Content = "このビルドでは紙目モデル比較の処理が無効です。必要なら機能を復元してください。",
                CloseButtonText = "OK"
            };
            await dialog.ShowAsync();
        }

        private async void ExportRadialFalloffBatchSizesNsButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialFalloffBatchSizesNsAsync(this);
        }

        private async void ExportRadialFalloffBatchPsSizesNsButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialFalloffBatchPsSizesNsAsync(this);
        }

        private async void ExportCenterAlphaSummaryButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportCenterAlphaSummary.ExportAsync(this);
        }

        private async void ExportRadialFalloffBatchButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialFalloffBatchAsync(this);
        }

        private async void ExportMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportPngService.ExportAsync(mp: this,isTransparentBackground: true,includeLabels: false,suggestedFileName: "pencil-material");
        }

        private async void ExportPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportPngService.ExportAsync(mp: this,isTransparentBackground: false,includeLabels: true,suggestedFileName: "pencil-preview");
        }
        private async void ExportDot512MaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512Async(this,isTransparentBackground: true, includeLabels: false, suggestedFileName: "dot512-material");
        }

        private async void ExportDot512PreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512Async(mp: this,isTransparentBackground: false, includeLabels: true, suggestedFileName: "dot512-preview");
        }

        private async void ExportDot512BatchMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512BatchAsync(mp:this,isTransparentBackground: true, includeLabels: false, defaultSuffix: "material");
        }

        private async void ExportDot512BatchPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512BatchAsync(mp:this,isTransparentBackground: false, includeLabels: true, defaultSuffix: "preview");
        }

        private async void ExportDot512BatchMaterialSizesButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512BatchSizesAsync(mp:this,isTransparentBackground: true, includeLabels: false, defaultSuffix: "material");
        }

        private async void ExportDot512BatchPreviewSizesButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512BatchSizesAsync(mp:this,isTransparentBackground: false, includeLabels: true, defaultSuffix: "preview");
        }

        private async void ExportDot512SlideMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512SlideAsync(mp:this,isTransparentBackground: true, includeLabels: false, defaultSuffix: "material");
        }

        private async void ExportDot512SlidePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportDot512.ExportDot512SlideAsync(mp:this,isTransparentBackground: false, includeLabels: true, defaultSuffix: "preview");
        }

        private async void ExportEstimatedPaperNoiseButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportEstimatedPaperNoise.ExportAsync(this);
         }

        private async void ExportS200LineMaterialButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportS200Service.ExportAsync(mp: this, isTransparentBackground: true, includeLabels: false, suggestedFileName: "pencil-material-line-s200");
        }

        private async void ExportS200LinePreviewButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportS200Service.ExportAsync(mp: this, isTransparentBackground: false, includeLabels: true, suggestedFileName: "pencil-preview-line-s200");
        }

        private async void ExportPaperNoiseCrop24Button_Click(object sender, RoutedEventArgs e)
        {
            await ExportPaperNoiseCrop24.ExportAsync(this);
         }

        private async void ExportRadialFalloffCsvButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialFalloffCsvAsync(this);
        }

        private async void ExportRadialAlphaCsvButton_Click(object sender, RoutedEventArgs e)
        {
            await RadialFalloffExportService.ExportRadialAlphaCsvAsync(this);
         }

        private async void ExportNormalizedFalloffButton_Click(object sender, RoutedEventArgs e)
        {
            await ExportNormalizedFalloffService.ExportAsync(this);
        }
    }
}
