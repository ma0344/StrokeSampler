using System;
using System.Globalization;
using Windows.Foundation;
using Windows.UI.Xaml.Controls;

namespace InkDrawGen.Helpers
{
    internal static class InkDrawGenUiReader
    {
        internal static InkDrawGenUiState Read(MainPage page)
        {
            var state = new InkDrawGenUiState();

            var jobType = GetSelectedJobType(Find<ComboBox>(page, "JobTypeComboBox"));
            state.OutputFolder = (Find<TextBox>(page, "OutputFolderTextBox").Text ?? "").Trim();
            state.JobType = jobType;
            state.RunTag = (Find<TextBox>(page, "RunTagTextBox").Text ?? "").Trim();

            state.S = new RangeSpec
            {
                Start = ReadDouble(Find<TextBox>(page, "SStartTextBox").Text, 200),
                End = ReadDouble(Find<TextBox>(page, "SEndTextBox").Text, 200),
                Step = ReadDouble(Find<TextBox>(page, "SStepTextBox").Text, 0),
            };

            state.P = new RangeSpec
            {
                Start = ReadDouble(Find<TextBox>(page, "PStartTextBox").Text, 1),
                End = ReadDouble(Find<TextBox>(page, "PEndTextBox").Text, 1),
                Step = ReadDouble(Find<TextBox>(page, "PStepTextBox").Text, 0),
            };

            state.Opacity = new OpacityRangeSpec
            {
                Start = Math.Clamp(ReadDouble(Find<TextBox>(page, "OpStartTextBox").Text, 1), 0.01, 5.0),
                End = Math.Clamp(ReadDouble(Find<TextBox>(page, "OpEndTextBox").Text, 1), 0.01, 5.0),
                Step = ReadDouble(Find<TextBox>(page, "OpStepTextBox").Text, 0),
            };

            state.N = new IntRangeSpec
            {
                Start = ReadInt(Find<TextBox>(page, "NStartTextBox").Text, 1),
                End = ReadInt(Find<TextBox>(page, "NEndTextBox").Text, 1),
                Step = ReadInt(Find<TextBox>(page, "NStepTextBox").Text, 0),
            };
            state.Repeat = Math.Max(1, state.N.Start);

            state.Scale = ReadInt(Find<TextBox>(page, "ScaleTextBox").Text, 10);
            state.Dpi = ReadDouble(Find<TextBox>(page, "DpiTextBox").Text, 96);
            state.Transparent = Find<CheckBox>(page, "TransparentCheckBox").IsChecked == true;

            state.Start = new Point(
                ReadDouble(Find<TextBox>(page, "StartXTextBox").Text, 100),
                ReadDouble(Find<TextBox>(page, "StartYTextBox").Text, 100));
            state.Step = new Point(
                ReadDouble(Find<TextBox>(page, "StepXTextBox").Text, 0),
                ReadDouble(Find<TextBox>(page, "StepYTextBox").Text, 0));

            state.DotStepX = new RangeSpec
            {
                Start = ReadDouble(Find<TextBox>(page, "DotStepStartTextBox").Text, 4),
                End = ReadDouble(Find<TextBox>(page, "DotStepEndTextBox").Text, 1),
                Step = ReadDouble(Find<TextBox>(page, "DotStepStepTextBox").Text, -1),
            };
            state.DotStepTwoPoints = Find<CheckBox>(page, "DotStepTwoPointsCheckBox").IsChecked == true;
            state.DotStepFixedCount = Find<CheckBox>(page, "DotStepFixedCountCheckBox").IsChecked == true;
            state.DotStepCount = Math.Max(1, ReadInt(Find<TextBox>(page, "DotStepCountTextBox").Text, 2));
            state.DotStepCountRange = new IntRangeSpec
            {
                Start = Math.Max(1, ReadInt(Find<TextBox>(page, "DotStepCountStartTextBox").Text, state.DotStepCount)),
                End = Math.Max(1, ReadInt(Find<TextBox>(page, "DotStepCountEndTextBox").Text, state.DotStepCount)),
                Step = ReadInt(Find<TextBox>(page, "DotStepCountStepTextBox").Text, 0),
            };
            state.RepeatCount = Math.Max(0, ReadInt(Find<TextBox>(page, "RepeatCountTextBox").Text, 0));
            state.End = new Point(
                ReadDouble(Find<TextBox>(page, "EndXTextBox").Text, 500),
                ReadDouble(Find<TextBox>(page, "EndYTextBox").Text, 100));

            state.EndXSweep = new RangeSpec
            {
                Start = ReadDouble(Find<TextBox>(page, "EndXSweepStartTextBox").Text, 118),
                End = ReadDouble(Find<TextBox>(page, "EndXSweepEndTextBox").Text, 280),
                Step = ReadDouble(Find<TextBox>(page, "EndXSweepStepTextBox").Text, 18),
            };
            state.Roi = new Rect(
                ReadDouble(Find<TextBox>(page, "RoiXTextBox").Text, 0),
                ReadDouble(Find<TextBox>(page, "RoiYTextBox").Text, 0),
                ReadDouble(Find<TextBox>(page, "RoiWTextBox").Text, 18),
                ReadDouble(Find<TextBox>(page, "RoiHTextBox").Text, 202));

            state.OutWidthPx = ReadInt(Find<TextBox>(page, "OutWidthPxTextBox").Text, 180);
            state.OutHeightPx = ReadInt(Find<TextBox>(page, "OutHeightPxTextBox").Text, 2020);

            return state;
        }

        private static JobType GetSelectedJobType(ComboBox combo)
        {
            if (combo != null && combo.SelectedItem is ComboBoxItem item)
            {
                var s = item.Content != null ? item.Content.ToString() : "";
                if (string.Equals(s, "Line", StringComparison.OrdinalIgnoreCase)) return JobType.Line;
            }

            return JobType.Dot;
        }

        private static T Find<T>(MainPage page, string name) where T : class
        {
            var o = page.FindName(name);
            return o as T;
        }

        private static double ReadDouble(string s, double fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim();
            double v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) return v;
            return fallback;
        }

        private static int ReadInt(string s, int fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim();
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.CurrentCulture, out v)) return v;
            return fallback;
        }
    }
}
