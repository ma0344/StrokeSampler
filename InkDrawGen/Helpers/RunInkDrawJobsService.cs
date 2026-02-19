using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Imaging;

namespace InkDrawGen.Helpers
{
    internal static class RunInkDrawJobsService
    {
        internal static async Task RunSingleLine2PointsAsync(MainPage page)
        {
            var s = InkDrawGenUiReader.Read(page);
            if (string.IsNullOrWhiteSpace(s.OutputFolder))
            {
                await new MessageDialog("出力フォルダを選択してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            if (s.Scale <= 0)
            {
                await new MessageDialog("scale は 1 以上を指定してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            if (s.Roi.Width <= 0 || s.Roi.Height <= 0)
            {
                await new MessageDialog("ROI の w/h は 1 以上を指定してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            s.JobType = JobType.Line;
            s.DotStepX = new RangeSpec { Start = 0, End = 0, Step = 0 };
            s.DotStepTwoPoints = false;

            var images = await RunSingleFromStateAsync(page, s);
            AppendLog(page, string.Format("線(2点)生成: 完了 images={0}\n", images));
        }

        internal static async Task RunLine2PointsEndXSweepAsync(MainPage page)
        {
            var s = InkDrawGenUiReader.Read(page);
            if (string.IsNullOrWhiteSpace(s.OutputFolder))
            {
                await new MessageDialog("出力フォルダを選択してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            if (s.Scale <= 0)
            {
                await new MessageDialog("scale は 1 以上を指定してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            if (s.Roi.Width <= 0 || s.Roi.Height <= 0)
            {
                await new MessageDialog("ROI の w/h は 1 以上を指定してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            // 通常Line(2点)のスイープ。疑似線(dotStep)は無効化する。
            s.JobType = JobType.Line;
            s.DotStepX = new RangeSpec { Start = 0, End = 0, Step = 0 };
            s.DotStepTwoPoints = false;

            var images = 0;
            foreach (var endX in s.EndXSweep.Expand())
            {
                var st = s;
                st.End = new Point(endX, s.End.Y);
                images += await RunSingleFromStateAsync(page, st);
            }

            AppendLog(page, string.Format("線(2点) EndXスイープ: 完了 images={0}\n", images));
        }

        internal static async Task RunLine2PointsStartXSweepAsync(MainPage page)
        {
            var s = InkDrawGenUiReader.Read(page);
            if (string.IsNullOrWhiteSpace(s.OutputFolder))
            {
                await new MessageDialog("出力フォルダを選択してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            if (s.Scale <= 0)
            {
                await new MessageDialog("scale は 1 以上を指定してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            if (s.Roi.Width <= 0 || s.Roi.Height <= 0)
            {
                await new MessageDialog("ROI の w/h は 1 以上を指定してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            // 通常Line(2点)のスイープ。疑似線(dotStep)は無効化する。
            s.JobType = JobType.Line;
            s.DotStepX = new RangeSpec { Start = 0, End = 0, Step = 0 };
            s.DotStepTwoPoints = false;

            var images = 0;
            foreach (var startX in s.EndXSweep.Expand())
            {
                var st = s;
                st.Start = new Point(startX, s.Start.Y);
                images += await RunSingleFromStateAsync(page, st);
            }

            AppendLog(page, string.Format("線(2点) StartXスイープ: 完了 images={0}\n", images));
        }

        internal static async Task RunSingleAsync(MainPage page)
        {
            var s = InkDrawGenUiReader.Read(page);
            if (string.IsNullOrWhiteSpace(s.OutputFolder))
            {
                await new MessageDialog("出力フォルダを選択してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            if (s.Scale <= 0)
            {
                await new MessageDialog("scale は 1 以上を指定してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            if (s.Roi.Width <= 0 || s.Roi.Height <= 0)
            {
                await new MessageDialog("ROI の w/h は 1 以上を指定してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            var images = await RunSingleFromStateAsync(page, s);
            AppendLog(page, string.Format("単体生成: 完了 images={0}\n", images));

            // ここから先にInk描画を追加していく
        }

        internal static async Task RunBatchFromCsvAsync(MainPage page)
        {
            var uiState = InkDrawGenUiReader.Read(page);
            if (string.IsNullOrWhiteSpace(uiState.OutputFolder))
            {
                await new MessageDialog("出力フォルダを選択してください。", "InkDrawGen").ShowAsync().AsTask();
                return;
            }

            var file = await FolderPickerService.PickCsvAsync();
            if (file is null) return;

            string csv;
            using (IRandomAccessStream ras = await file.OpenAsync(FileAccessMode.Read))
            using (var s = ras.AsStreamForRead())
            using (var sr = new System.IO.StreamReader(s))
            {
                csv = await sr.ReadToEndAsync();
            }

            var baseState = uiState;

            var rowCount = 0;
            var imageCount = 0;
            var started = DateTimeOffset.Now;

            var rows = JobsCsvService.Read(csv);
            foreach (var row in rows)
            {
                rowCount++;
                var st = baseState;

                st.S = new RangeSpec { Start = row.SStart, End = row.SEnd, Step = row.SStep };
                st.P = new RangeSpec { Start = row.PressureStart, End = row.PressureEnd, Step = row.PressureStep };
                st.N = new IntRangeSpec { Start = row.NStart, End = row.NEnd, Step = row.NStep };
                st.Scale = row.Scale;
                st.Dpi = row.Dpi;
                st.Transparent = row.Transparent;
                st.Start = new Windows.Foundation.Point(row.StartX, row.StartY);
                st.Step = new Windows.Foundation.Point(row.StepX, row.StepY);
                st.RepeatCount = row.RepeatCount;
                st.End = new Windows.Foundation.Point(row.EndX, row.EndY);

                st.DotStepFixedCount = row.DotStepFixedCount;
                st.DotStepCount = row.DotStepCount;
                if (row.DotStepCountStart > 0 || row.DotStepCountEnd > 0 || row.DotStepCountStep != 0)
                {
                    st.DotStepCountRange = new IntRangeSpec
                    {
                        Start = Math.Max(1, row.DotStepCountStart > 0 ? row.DotStepCountStart : row.DotStepCount),
                        End = Math.Max(1, row.DotStepCountEnd > 0 ? row.DotStepCountEnd : row.DotStepCount),
                        Step = row.DotStepCountStep,
                    };
                }
                st.Roi = new Windows.Foundation.Rect(row.RoiX, row.RoiY, row.RoiW, row.RoiH);
                st.RunTag = row.RunTag;

                if (!string.IsNullOrWhiteSpace(row.JobType))
                {
                    if (string.Equals(row.JobType, "dot", StringComparison.OrdinalIgnoreCase)) st.JobType = JobType.Dot;
                    else if (string.Equals(row.JobType, "line", StringComparison.OrdinalIgnoreCase)) st.JobType = JobType.Line;
                }

                imageCount += await RunSingleFromStateAsync(page, st);
            }

            var elapsed = DateTimeOffset.Now - started;
            AppendLog(page, string.Format("CSVバッチ完了: file={0}\n", file.Path));
            AppendLog(page, string.Format("  rows={0} images={1} elapsed={2:0.###}s\n", rowCount, imageCount, elapsed.TotalSeconds));
        }

        private static async Task<int> RunSingleFromStateAsync(MainPage page, InkDrawGenUiState s)
        {
            if (s.Scale <= 0 || s.Roi.Width <= 0 || s.Roi.Height <= 0) return 0;

            var outW = s.OutWidthPx > 0 ? s.OutWidthPx : Math.Max(1, (int)Math.Round(s.Roi.Width * s.Scale));
            var outH = s.OutHeightPx > 0 ? s.OutHeightPx : Math.Max(1, (int)Math.Round(s.Roi.Height * s.Scale));

            var sList = s.S.Expand();
            var pList = s.P.Expand();
            var opList = s.Opacity.Expand();
            var nList = s.N.Expand();
            var dotStepList = s.DotStepX.Expand();
            var dotCountList = (s.DotStepFixedCount && !s.DotStepTwoPoints)
                ? s.DotStepCountRange.Expand().ToArray()
                : Array.Empty<int>();
            if (dotCountList.Length == 0) dotCountList = new[] { Math.Max(1, s.DotStepCount) };

            var count = 0;
            foreach (var sDip in sList)
            {
                foreach (var p01 in pList)
                {
                    var pressure = (float)Math.Max(0.0, Math.Min(1.0, p01));

                    foreach (var n in nList)
                    {
                        var repeat = Math.Max(1, n);
                        var multiDrawCount = Math.Max(1, s.RepeatCount + 1);

                        foreach (var op01 in opList)
                        {
                            // Opは0.0001刻みでファイル名に出すため、ここでも小数第4位に正規化する。
                            var opacityTag = Math.Round(Math.Max(0.01, Math.Min(5.0, op01)), 5, MidpointRounding.AwayFromZero);
                            var opacity = (float)opacityTag;

                            var dotSteps = (s.JobType == JobType.Line) ? dotStepList : Enumerable.Repeat(0.0, 1);
                            var dotCounts = (s.DotStepFixedCount && !s.DotStepTwoPoints)
                                ? s.DotStepCountRange.Expand().ToArray()
                                : Array.Empty<int>();
                            if (dotCounts.Length == 0) dotCounts = new[] { Math.Max(1, s.DotStepCount) };

                            foreach (var dotStep0 in dotSteps)
                            {
                                var dotStep = dotStep0;
                                foreach (var dotCount in dotCounts)
                                {
                                    var jobType = s.JobType == JobType.Line ? "line" : "dot";
                                    var xTag = "StartX" + ((int)Math.Round(s.Start.X)).ToString(CultureInfo.InvariantCulture)
                                        + "-EndX" + ((int)Math.Round(s.End.X)).ToString(CultureInfo.InvariantCulture);

                                    WriteableBitmap bmp;
                                    if (s.JobType == JobType.Line && dotStep > 0)
                                    {
                                        // dotStepが指定されている場合は、線の代わりにDotを並べた疑似線を出力する。
                                        // （StepX/StepY/RepeatCountは従来通り multiDraw のオフセットとして扱う）
                                        var strokes = new List<Windows.UI.Input.Inking.InkStroke>(256);
                                        for (var mi = 0; mi < multiDrawCount; mi++)
                                        {
                                            var mdx = s.Step.X * mi;
                                            var mdy = s.Step.Y * mi;
                                            var x0 = s.Start.X + mdx;
                                            var y0 = s.Start.Y + mdy;
                                            var x1 = s.End.X + mdx;

                                            if (s.DotStepTwoPoints)
                                            {
                                                // 2点疑似線: 始点と「更新点1つ分」を固定で描画する（点間隔のみをスイープ）。
                                                var p0 = new Windows.Foundation.Point(x0, y0);
                                                var p1 = new Windows.Foundation.Point(x0 + dotStep, y0);
                                                var s0 = InkStrokeBuildService.BuildSDotStroke(p0, sDip, pressure, opacity);
                                                var s1 = InkStrokeBuildService.BuildSDotStroke(p1, sDip, pressure, opacity);
                                                strokes.Add(s0);
                                                strokes.Add(s1);

                                                if (count < 3)
                                                {
                                                    AppendLog(page, string.Format(
                                                        CultureInfo.InvariantCulture,
                                                        "dot2 dbg: dotStep={0:0.#####} p0=({1:0.#####},{2:0.#####}) p1=({3:0.#####},{4:0.#####}) br0=({5:0.#####},{6:0.#####},{7:0.#####},{8:0.#####}) br1=({9:0.#####},{10:0.#####},{11:0.#####},{12:0.#####})\n",
                                                        dotStep,
                                                        p0.X, p0.Y,
                                                        p1.X, p1.Y,
                                                        s0.BoundingRect.X, s0.BoundingRect.Y, s0.BoundingRect.Width, s0.BoundingRect.Height,
                                                        s1.BoundingRect.X, s1.BoundingRect.Y, s1.BoundingRect.Width, s1.BoundingRect.Height));
                                                }
                                            }
                                            else if (s.DotStepFixedCount)
                                            {
                                                // N個疑似線: StartX を基準に dotStep 間隔で N 個のDotを描画する（EndXは使わない）。
                                                var countLine = Math.Max(1, dotCount);
                                                for (var i = 0; i < countLine; i++)
                                                {
                                                    var x = x0 + (dotStep * i);
                                                    var center = new Windows.Foundation.Point(x, y0);
                                                    strokes.Add(InkStrokeBuildService.BuildSDotStroke(center, sDip, pressure, opacity));
                                                }
                                            }
                                            else
                                            {
                                                var len = Math.Max(0.0, x1 - x0);
                                                if (len <= 0) continue;
                                                var countLine = (int)Math.Floor(len / dotStep) + 1;
                                                countLine = Math.Max(1, Math.Min(200_000, countLine));

                                                for (var i = 0; i < countLine; i++)
                                                {
                                                    var x = x0 + (dotStep * i);
                                                    var center = new Windows.Foundation.Point(x, y0);
                                                    strokes.Add(InkStrokeBuildService.BuildSDotStroke(center, sDip, pressure, opacity));
                                                }
                                            }
                                        }
                                        bmp = await InkOffscreenRenderService.RenderStrokesCroppedAsync(strokes.ToArray(), outW, outH, s.Roi, s.Transparent, (float)s.Dpi, s.Scale, repeat);
                                        if (s.DotStepTwoPoints)
                                        {
                                            jobType = "dot2-step" + dotStep.ToString("0.#####", CultureInfo.InvariantCulture);
                                        }
                                        else if (s.DotStepFixedCount)
                                        {
                                            jobType = "dotN" + dotCount.ToString(CultureInfo.InvariantCulture)
                                                + "-step" + dotStep.ToString("0.#####", CultureInfo.InvariantCulture);
                                        }
                                        else
                                        {
                                            jobType = "dotstepline-step" + dotStep.ToString("0.#####", CultureInfo.InvariantCulture);
                                        }
                                    }
                                    else if (s.JobType == JobType.Line)
                                    {
                                        var strokes = new Windows.UI.Input.Inking.InkStroke[multiDrawCount];
                                        for (var i = 0; i < multiDrawCount; i++)
                                        {
                                            var dx = s.Step.X * i;
                                            var dy = s.Step.Y * i;
                                            var start = new Windows.Foundation.Point(s.Start.X + dx, s.Start.Y + dy);
                                            var end = new Windows.Foundation.Point(s.End.X + dx, s.End.Y + dy);
                                            strokes[i] = InkStrokeBuildService.BuildSLineStroke2Points(start, end, sDip, pressure, opacity);
                                        }
                                        bmp = await InkOffscreenRenderService.RenderStrokesCroppedAsync(strokes, outW, outH, s.Roi, s.Transparent, (float)s.Dpi, s.Scale, repeat);
                                    }
                                    else
                                    {
                                        var strokes = new Windows.UI.Input.Inking.InkStroke[multiDrawCount];
                                        for (var i = 0; i < multiDrawCount; i++)
                                        {
                                            var dx = s.Step.X * i;
                                            var dy = s.Step.Y * i;
                                            var center = new Windows.Foundation.Point(s.Start.X + dx, s.Start.Y + dy);
                                            strokes[i] = InkStrokeBuildService.BuildSDotStroke(center, sDip, pressure, opacity);
                                        }
                                        bmp = await InkOffscreenRenderService.RenderStrokesCroppedAsync(strokes, outW, outH, s.Roi, s.Transparent, (float)s.Dpi, s.Scale, repeat);
                                    }

                                    var fileName = FileNameBuilder.BuildStrokeSamplerLike(
                                        prefix: "pencil-highres",
                                        outWpx: outW,
                                        outHpx: outH,
                                        dpi: (float)s.Dpi,
                                        s: sDip,
                                        p: p01,
                                        n: repeat,
                                        runTag: s.RunTag,
                                        extraSuffix: jobType + "-" + xTag,
                                        scale: s.Scale,
                                        transparent: s.Transparent,
                                        opacity: opacityTag);

                                    var fullPath = System.IO.Path.Combine(s.OutputFolder, fileName);
                                    await PngExportService.SaveAsync(bmp, fullPath);
                                    count++;
                                }
                            }
                        }
                    }
                }
            }

            return count;
        }

        private static void AppendLog(MainPage page, string s)
        {
            var tb = page.FindName("LogTextBox") as TextBox;
            if (tb == null) return;
            tb.Text = (tb.Text ?? string.Empty) + s;
        }
    }
}
