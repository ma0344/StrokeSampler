using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;

namespace StrokeSampler
{
    internal static class GenerateHelper
    {
        internal static void Generate(MainPage mp)
        {
            mp.InkCanvasControl.InkPresenter.StrokeContainer.Clear();

            var attributes = StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);
            mp._lastGeneratedAttributes = attributes;
            mp._lastOverwritePressure = null;
            mp._lastMaxOverwrite = null;
            mp._lastDotGridSpacing = null;
            mp._lastWasDotGrid = false;

            foreach (var stroke in PencilPressurePresetGenerator.Generate(
                attributes,
                MainPage.PressurePreset,
                MainPage.DefaultStartX,
                MainPage.DefaultEndX,
                MainPage.DefaultStartY,
                MainPage.DefaultSpacingY,
                StrokeHelpers.CreatePencilStroke))
            {
                mp.InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(stroke);
            }
        }

        internal static void GenerateOverwriteSamples(MainPage mp)
        {
            mp.InkCanvasControl.InkPresenter.StrokeContainer.Clear();

            var attributes = StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);
            mp._lastGeneratedAttributes = attributes;

            var pressure = UIHelpers.GetOverwritePressure(mp);
            var maxOverwrite = UIHelpers.GetMaxOverwrite(mp);
            mp._lastOverwritePressure = pressure;
            mp._lastMaxOverwrite = maxOverwrite;

            mp._lastDotGridSpacing = null;
            mp._lastWasDotGrid = false;

            foreach (var stroke in PencilOverwriteSampleGenerator.Generate(
                attributes,
                pressure,
                maxOverwrite,
                MainPage.DefaultStartX,
                MainPage.DefaultEndX,
                MainPage.DefaultStartY,
                MainPage.DefaultSpacingY,
                StrokeHelpers.CreatePencilStroke))
            {
                mp.InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(stroke);
            }
        }

        internal static void GenerateDotGrid(MainPage mp)
        {
            mp.InkCanvasControl.InkPresenter.StrokeContainer.Clear();

            var attributes = StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);
            mp._lastGeneratedAttributes = attributes;

            var maxOverwrite = UIHelpers.GetMaxOverwrite(mp);
            var spacing = UIHelpers.GetDotGridSpacing(mp);

            mp._lastOverwritePressure = null;
            mp._lastMaxOverwrite = maxOverwrite;
            mp._lastDotGridSpacing = spacing;
            mp._lastWasDotGrid = true;

            foreach (var dot in PencilDotGridGenerator.Generate(
                attributes,
                MainPage.DotGridPressurePreset,
                maxOverwrite,
                spacing,
                MainPage.DefaultDotGridStartX,
                MainPage.DefaultDotGridStartY,
                StrokeHelpers.CreatePencilDot))
            {
                mp.InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(dot);
            }
        }

        internal static void GenerateDotGridFixedCondition(MainPage mp)
        {
            mp.InkCanvasControl.InkPresenter.StrokeContainer.Clear();

            var attributes = StrokeHelpers.CreatePencilAttributesFromToolbarBestEffort(mp);

            var dotSize = UIHelpers.GetDot512SizeOrNull(mp);
            if (dotSize is double s)
            {
                attributes.Size = new Size(s, s);
            }

            mp._lastGeneratedAttributes = attributes;

            var pressure = UIHelpers.GetDot512Pressure(mp);
            var overwrite = UIHelpers.GetDot512Overwrite(mp);
            var spacing = UIHelpers.GetDotGridSpacing(mp);

            mp._lastOverwritePressure = pressure;
            mp._lastMaxOverwrite = overwrite;
            mp._lastDotGridSpacing = spacing;
            mp._lastWasDotGrid = true;

            // Keep it bounded to fit typical canvas sizes.
            const int columns = 12;
            const int rows = 10;

            foreach (var dot in PencilDotGridGenerator.GenerateFixedCondition(
                attributes,
                pressure,
                overwrite,
                spacing,
                MainPage.DefaultDotGridStartX,
                MainPage.DefaultDotGridStartY,
                columns,
                rows,
                StrokeHelpers.CreatePencilDot))
            {
                mp.InkCanvasControl.InkPresenter.StrokeContainer.AddStroke(dot);
            }
        }
    }
}
