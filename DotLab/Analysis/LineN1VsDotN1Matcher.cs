using SkiaSharp;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Storage;

namespace DotLab.Analysis;

internal static class LineN1VsDotN1Matcher
{
    private const int RoiWidthPx = 18;
    private static readonly byte[] BinaryAlphaThresholds = new byte[] { 1, 2, 3 };
    private const double MinIouGate = 0.10;
    private const long MinUnionGateTh1 = 20;
    private const long MinUnionGateTh23 = 200;
    private const double MinCoverageGate = 0.0; // set per ROI size at runtime

    // Gate tuning for th=2/3 experiments (lower these to see low-P matches)
    private const double MinCovPxTh1 = 20.0;
    private const double MinCovPxTh23 = 20.0;

    // th=1で線が薄すぎる場合に2値化閾値を自動緩和するためのON画素数しきい値
    private const int Th1MinLineOnPxForThreshold1 = 10;
    private const int RoiY0 = 435;
    private const int RoiY1Exclusive = 1592;

    private const string DefaultAlphaLutCrvPath = "LUT/Dot P1 LUT.crv";

    // Debug aid: prefix every header/value with a column index so mismatches are visually detectable in CSV.
    private const bool PrefixCsvColumnsWithIndex = false;

    private static string PrefixCol(int colIndex1Based, string name)
        => PrefixCsvColumnsWithIndex ? $"c{colIndex1Based:D3}_{name}" : name;

    private static void AppendCsvValue(StringBuilder sb, ref int colIndex1Based, string value)
    {
        if (!PrefixCsvColumnsWithIndex)
        {
            sb.Append(value);
            colIndex1Based++;
            return;
        }

        sb.Append('c');
        sb.Append(colIndex1Based.ToString("D3", CultureInfo.InvariantCulture));
        sb.Append(':');
        sb.Append(value);
        colIndex1Based++;
    }

    private static void AppendCsvSep(StringBuilder sb) => sb.Append(',');

    private static void AppendHeaderShapeWithOverUnder(StringBuilder header, int th)
    {
        header.Append($",best_th{th}_dot_pressure,best_th{th}_dot_file,best_th{th}_dot_path,best_th{th}_bin_iou,best_th{th}_bin_mismatch,best_th{th}_cov_line,best_th{th}_cov_dot,best_th{th}_union");
        header.Append($",best_th{th}_over_area,best_th{th}_under_area,best_th{th}_over_alpha_median,best_th{th}_under_alpha_median,best_th{th}_over_alpha_p90,best_th{th}_under_alpha_p90,best_th{th}_over_alpha_max,best_th{th}_under_alpha_max");
        header.Append($",second_th{th}_dot_pressure,second_th{th}_dot_file,second_th{th}_dot_path,second_th{th}_bin_iou,second_th{th}_bin_mismatch,second_th{th}_cov_line,second_th{th}_cov_dot,second_th{th}_union");
        header.Append($",second_th{th}_over_area,second_th{th}_under_area,second_th{th}_over_alpha_median,second_th{th}_under_alpha_median,second_th{th}_over_alpha_p90,second_th{th}_under_alpha_p90,second_th{th}_over_alpha_max,second_th{th}_under_alpha_max");
    }

    private static void AppendHeaderCsvNames(StringBuilder header, ref int col, string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            header.Append(',');
            header.Append(PrefixCol(col++, names[i]));
        }
    }

    private static void AppendHeaderShapeWithOverUnder(StringBuilder header, ref int col, int th)
    {
        AppendHeaderCsvNames(header, ref col, new[]
        {
            $"best_th{th}_dot_pressure",$"best_th{th}_dot_file",$"best_th{th}_dot_path",$"best_th{th}_bin_iou",$"best_th{th}_bin_mismatch",$"best_th{th}_cov_line",$"best_th{th}_cov_dot",$"best_th{th}_union",
            $"best_th{th}_over_area",$"best_th{th}_under_area",$"best_th{th}_over_alpha_median",$"best_th{th}_under_alpha_median",$"best_th{th}_over_alpha_p90",$"best_th{th}_under_alpha_p90",$"best_th{th}_over_alpha_max",$"best_th{th}_under_alpha_max",
            $"second_th{th}_dot_pressure",$"second_th{th}_dot_file",$"second_th{th}_dot_path",$"second_th{th}_bin_iou",$"second_th{th}_bin_mismatch",$"second_th{th}_cov_line",$"second_th{th}_cov_dot",$"second_th{th}_union",
            $"second_th{th}_over_area",$"second_th{th}_under_area",$"second_th{th}_over_alpha_median",$"second_th{th}_under_alpha_median",$"second_th{th}_over_alpha_p90",$"second_th{th}_under_alpha_p90",$"second_th{th}_over_alpha_max",$"second_th{th}_under_alpha_max",
        });
    }

    internal static string BuildMatchCsv(string folderPath)
    {
        return BuildMatchCsv(folderPath, out _);
    }

    internal readonly record struct LutLoadResult(bool Loaded, string RequestedPath, string ResolvedPath, string Error);

    internal static string BuildMatchCsv(string folderPath, out LutLoadResult lut)
    {
        lut = default;
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath)) return "";

        var files = Directory.EnumerateFiles(folderPath, "*.png", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToArray();

        var dots = new List<Item>();
        var lines = new List<Item>();
        foreach (var f in files)
        {
            if (!TryParsePressure(f!, out var p)) continue;

            if (IsDotCandidate(f!)) dots.Add(new Item(f!, p));
            else if (IsLineCandidate(f!)) lines.Add(new Item(f!, p));
        }

        if (dots.Count == 0 || lines.Count == 0) return "";

        var dotRois = new List<DotRoi>(dots.Count);
        foreach (var d in dots.OrderBy(d => d.Pressure))
        {
            var path = Path.Combine(folderPath, d.FileName);
            using var bmp = SKBitmap.Decode(path);
            if (bmp == null) continue;
            var roi = ExtractLeftRoiAlpha(bmp);
            if (roi == null) continue;
            dotRois.Add(new DotRoi(d.Pressure, d.FileName, roi));
        }

        if (dotRois.Count == 0) return "";

        var header = new StringBuilder(16 * 1024);
        var hcol = 1;
        header.Append(PrefixCol(hcol++, "line_pressure"));
        header.Append(',');
        header.Append(PrefixCol(hcol++, "line_file"));
        header.Append(',');
        header.Append(PrefixCol(hcol++, "line_path"));
        header.Append(',');
        header.Append(PrefixCol(hcol++, "width"));
        header.Append(',');
        header.Append(PrefixCol(hcol++, "height"));
        header.Append(',');
        header.Append(PrefixCol(hcol++, "roi_w"));
        header.Append(',');
        header.Append(PrefixCol(hcol++, "roi_h"));

        // th=1 (detailed)
        AppendHeaderCsvNames(header, ref hcol, new[]
        {
            "best_th1_dot_pressure","best_th1_dot_file","best_th1_dot_path","best_th1_bin_iou","best_th1_bin_mismatch","best_th1_alpha_k","best_th1_alpha_l1_scaled","best_th1_cov_line","best_th1_cov_dot","best_th1_union","best_th1_over_area","best_th1_under_area","best_th1_over_alpha_median","best_th1_under_alpha_median","best_th1_over_alpha_p90","best_th1_under_alpha_p90","best_th1_over_alpha_max","best_th1_under_alpha_max","best_th1_lut_loaded","best_th1_alpha_l1_lut","best_th1_over_area_lut","best_th1_under_area_lut","best_th1_over_alpha_median_lut","best_th1_under_alpha_median_lut",
            "second_th1_dot_pressure","second_th1_dot_file","second_th1_dot_path","second_th1_bin_iou","second_th1_bin_mismatch","second_th1_alpha_k","second_th1_alpha_l1_scaled","second_th1_cov_line","second_th1_cov_dot","second_th1_union","second_th1_over_area","second_th1_under_area","second_th1_over_alpha_median","second_th1_under_alpha_median","second_th1_over_alpha_p90","second_th1_under_alpha_p90","second_th1_over_alpha_max","second_th1_under_alpha_max","second_th1_lut_loaded","second_th1_alpha_l1_lut","second_th1_over_area_lut","second_th1_under_area_lut","second_th1_over_alpha_median_lut","second_th1_under_alpha_median_lut",
            "best_th1_ou_dot_pressure","best_th1_ou_dot_file","best_th1_ou_dot_path","best_th1_ou_bin_iou","best_th1_ou_bin_mismatch","best_th1_ou_alpha_k","best_th1_ou_alpha_l1_scaled","best_th1_ou_cov_line","best_th1_ou_cov_dot","best_th1_ou_union","best_th1_ou_over_area","best_th1_ou_under_area","best_th1_ou_over_alpha_median","best_th1_ou_under_alpha_median","best_th1_ou_over_alpha_p90","best_th1_ou_under_alpha_p90","best_th1_ou_over_alpha_max","best_th1_ou_under_alpha_max",
            "best_th1_ou_score","best_th1_ou_candidates_checked","best_th1_ou_candidates_gated"
        });

        // th=2 and th=3 (shape + over/under)
        AppendHeaderShapeWithOverUnder(header, ref hcol, 2);
        AppendHeaderShapeWithOverUnder(header, ref hcol, 3);

        AppendHeaderCsvNames(header, ref hcol, new[]
        {
            "th1_effective_threshold",
            "th1_pass_iou","th1_pass_union","th1_pass_cov",
            "th2_pass_iou","th2_pass_union","th2_pass_cov",
            "th3_pass_iou","th3_pass_union","th3_pass_cov",
        });

        var headerText = header.ToString();
        var sb = new StringBuilder(Math.Max(64 * 1024, headerText.Length * 2));
        sb.AppendLine(headerText);
        var headerColumnCount = headerText.Count(c => c == ',') + 1;

        // Sanity check: expected columns = base(7) + th1(69) + th2(32) + th3(32) + diag(10) = 150
        // If this fails, header definition is missing columns and downstream row validation will trip.
        const int expectedHeaderColumns = 150;
        if (headerColumnCount != expectedHeaderColumns)
        {
            throw new InvalidOperationException($"CSV header column count mismatch: headerCols={headerColumnCount}, expected={expectedHeaderColumns}");
        }

        // LUT is intentionally disabled for the under/over-based shape match experiment.
        var hasLut = false;
        byte[]? alphaLut = null;
        lut = new LutLoadResult(false, DefaultAlphaLutCrvPath, "", "disabled");

        foreach (var l in lines.OrderBy(l => l.Pressure))
        {
            var path = Path.Combine(folderPath, l.FileName);
            var pngPath = Path.Combine(folderPath, "OutPNG");
            if (!Directory.Exists(pngPath))
            {
                Directory.CreateDirectory(pngPath);
            }


            using var bmp = SKBitmap.Decode(path);
            if (bmp == null) continue;

            var lineRoi = ExtractLeftRoiAlpha(bmp);
            if (lineRoi == null) continue;

            var resultsByTh = new Dictionary<byte, Match2>(BinaryAlphaThresholds.Length);
            foreach (var th in BinaryAlphaThresholds)
            {
                resultsByTh[th] = FindBestAndSecond(lineRoi, dotRois, th);
            }

            var ouBest = FindBestByOverUnder(lineRoi, dotRois, threshold: 1, out var ouDiag);

            // Export visualization for each threshold best pair (shape overlap map)
            foreach (var th in BinaryAlphaThresholds)
            {
                TryExportBestHeatmapPng(pngPath, folderPath, l, bmp, resultsByTh, th, fullWidth: false);
                TryExportBestHeatmapPng(pngPath, folderPath, l, bmp, resultsByTh, th, fullWidth: true);
            }

            // Export alpha diff magnitude visualization for th=1 best pair
            TryExportBestAlphaDiffPng(pngPath, folderPath, l, bmp, resultsByTh, th: 1, fullWidth: false);
            TryExportBestAlphaDiffPng(pngPath, folderPath, l, bmp, resultsByTh, th: 1, fullWidth: true);

            // Export LUT-applied dot visualization for th=1 best pair (GIMP .crv alpha samples)
            if (hasLut)
            {
                TryExportBestHeatmapPngLut(pngPath, folderPath, l, bmp, resultsByTh, th: 1, fullWidth: false, alphaLut);
                TryExportBestHeatmapPngLut(pngPath, folderPath, l, bmp, resultsByTh, th: 1, fullWidth: true, alphaLut);
                TryExportBestAlphaDiffPngLut(pngPath, folderPath, l, bmp, resultsByTh, th: 1, fullWidth: false, alphaLut);
                TryExportBestAlphaDiffPngLut(pngPath, folderPath, l, bmp, resultsByTh, th: 1, fullWidth: true, alphaLut);
            }

            var rowSb = new StringBuilder(4096);
            rowSb.Append(l.Pressure.ToString("0.########", CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(' '); // line_file (omit)
            rowSb.Append(',');
            rowSb.Append(' '); // line_path (omit)
            rowSb.Append(',');
            rowSb.Append(bmp.Width.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(Math.Min(bmp.Height, Math.Max(0, RoiY1Exclusive) - Math.Max(0, RoiY0)).ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(RoiWidthPx.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(Math.Min(bmp.Height, Math.Max(0, RoiY1Exclusive) - Math.Max(0, RoiY0)).ToString(CultureInfo.InvariantCulture));

            var diagEnabled = Math.Abs(l.Pressure - 0.2) < 0.0000001;
            var colsAfterBase = 0;
            var colsAfterTh1 = 0;
            var colsAfterTh2 = 0;
            var colsAfterTh3 = 0;

            if (diagEnabled)
            {
                colsAfterBase = rowSb.ToString().Count(c => c == ',') + 1;
            }

            foreach (var th in BinaryAlphaThresholds)
            {
                resultsByTh.TryGetValue(th, out var r);
                if (th == 1)
                {
                    AppendMatchTh1(rowSb, folderPath, r.Best, hasLut ? alphaLut : null);
                    AppendMatchTh1(rowSb, folderPath, r.Second, hasLut ? alphaLut : null);
                    AppendMatchOuTh1(rowSb, folderPath, ouBest, ouDiag);

                    if (diagEnabled)
                    {
                        colsAfterTh1 = rowSb.ToString().Count(c => c == ',') + 1;
                    }
                }
                else
                {
                    AppendMatchShapeWithOverUnder(rowSb, folderPath, r.Best, threshold: th);
                    AppendMatchShapeWithOverUnder(rowSb, folderPath, r.Second, threshold: th);

                    if (diagEnabled)
                    {
                        if (th == 2) colsAfterTh2 = rowSb.ToString().Count(c => c == ',') + 1;
                        else if (th == 3) colsAfterTh3 = rowSb.ToString().Count(c => c == ',') + 1;
                    }
                }
            }

            // Gate diagnostics columns
            if (!resultsByTh.TryGetValue(1, out var th1r)) th1r = new Match2(null, null, 1, 0, 0, 0);
            if (!resultsByTh.TryGetValue(2, out var th2r)) th2r = new Match2(null, null, 2, 0, 0, 0);
            if (!resultsByTh.TryGetValue(3, out var th3r)) th3r = new Match2(null, null, 3, 0, 0, 0);

            rowSb.Append(',');
            rowSb.Append(th1r.EffectiveThreshold.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(th1r.PassedIouGate.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(th1r.PassedUnionGate.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(th1r.PassedCovGate.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(th2r.PassedIouGate.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(th2r.PassedUnionGate.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(th2r.PassedCovGate.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(th3r.PassedIouGate.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(th3r.PassedUnionGate.ToString(CultureInfo.InvariantCulture));
            rowSb.Append(',');
            rowSb.Append(th3r.PassedCovGate.ToString(CultureInfo.InvariantCulture));

            var colCount = rowSb.ToString().Count(c => c == ',') + 1;
            if (colCount > headerColumnCount)
            {
                var diag = diagEnabled
                    ? $" base={colsAfterBase} th1={colsAfterTh1} th2={colsAfterTh2} th3={colsAfterTh3}"
                    : "";
                throw new InvalidOperationException($"CSV row has more columns than header: rowCols={colCount}, headerCols={headerColumnCount}, lineP={l.Pressure}{diag}");
            }
            for (var i = colCount; i < headerColumnCount; i++)
            {
                rowSb.Append(",0");
            }
            sb.AppendLine(rowSb.ToString());
        }

        return sb.ToString();
    }

    private readonly record struct OuDiagnostics(long BestScore, int CandidatesChecked, int CandidatesGated);

    private static Match? FindBestByOverUnder(byte[] lineAlpha, List<DotRoi> dotRois, byte threshold, out OuDiagnostics diag)
    {
        const long underWeight = 1000;

        // OU用の探索は「欠けを潰せる候補がゲートで落ちていないか」を見たいので、通常よりゲートを緩める。
        const double minIouGateOu = 0.0;
        const long minUnionGateOu = 1;
        const double minCovGateOu = 0.0;

        long bestScore = long.MaxValue;
        double bestIou = double.NegativeInfinity;
        double bestMismatch = double.PositiveInfinity;
        double bestK = 1.0;
        double bestL1Scaled = double.PositiveInfinity;
        double bestCovLine = 0;
        double bestCovDot = 0;
        long bestInter = 0;
        long bestUnion = 0;
        DotRoi? best = null;

        var checkedCount = 0;
        var gatedCount = 0;

        for (var i = 0; i < dotRois.Count; i++)
        {
            checkedCount++;
            var dotAlpha = dotRois[i].Alpha;
            if (dotAlpha.Length != lineAlpha.Length) continue;

            var stats = ComputeBinaryStats(lineAlpha, dotAlpha, threshold);
            if (stats.Iou < minIouGateOu) continue;

            if (stats.Union < minUnionGateOu) continue;
            if (stats.CoverageA < minCovGateOu) continue;
            if (stats.CoverageB < minCovGateOu) continue;

            gatedCount++;

            var ou = ComputeOverUnderStats(lineAlpha, dotAlpha, threshold);
            var score = checked(underWeight * ou.UnderArea + ou.OverArea);

            var (k, l1Scaled) = EstimateBestAlphaScaleAndL1(lineAlpha, dotAlpha);

            if (score < bestScore)
            {
                bestScore = score;
                bestIou = stats.Iou;
                bestMismatch = stats.Mismatch;
                bestK = k;
                bestL1Scaled = l1Scaled;
                bestCovLine = stats.CoverageA;
                bestCovDot = stats.CoverageB;
                bestInter = stats.Inter;
                bestUnion = stats.Union;
                best = dotRois[i];
                continue;
            }

            if (score == bestScore)
            {
                if (IsBetterByShape(stats.Iou, stats.Mismatch, l1Scaled, bestIou, bestMismatch, bestL1Scaled))
                {
                    bestIou = stats.Iou;
                    bestMismatch = stats.Mismatch;
                    bestK = k;
                    bestL1Scaled = l1Scaled;
                    bestCovLine = stats.CoverageA;
                    bestCovDot = stats.CoverageB;
                    bestInter = stats.Inter;
                    bestUnion = stats.Union;
                    best = dotRois[i];
                }
            }
        }

        diag = new OuDiagnostics(best.HasValue ? bestScore : long.MaxValue, checkedCount, gatedCount);

        return best.HasValue
            ? new Match(best.Value, bestMismatch, bestIou, bestK, bestL1Scaled, bestCovLine, bestCovDot, bestInter, bestUnion, lineAlpha, best.Value.Alpha)
            : null;
    }

    private static void AppendMatchOuTh1(StringBuilder sb, string folderPath, Match? match, OuDiagnostics diag)
    {
        sb.Append(',');
        if (match is null)
        {
            sb.Append("0");
            sb.Append(',');
            sb.Append(' ');
            sb.Append(',');
            sb.Append(' ');
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("Infinity");
            sb.Append(',');
            sb.Append("1");
            sb.Append(',');
            sb.Append("Infinity");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append(diag.BestScore.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(diag.CandidatesChecked.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(diag.CandidatesGated.ToString(CultureInfo.InvariantCulture));
            return;
        }

        sb.Append(match.Value.Dot.Pressure.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(' ');
        sb.Append(',');
        sb.Append(' ');
        sb.Append(',');
        sb.Append(match.Value.BinIou.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.BinMismatch.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.AlphaK.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.AlphaL1Scaled.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.CoverageA.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.CoverageB.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.Union.ToString(CultureInfo.InvariantCulture));

        var ou = ComputeOverUnderStats(match.Value.LineAlpha, match.Value.DotAlpha, threshold: 1);
        sb.Append(',');
        sb.Append(ou.OverArea.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderArea.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.OverAlphaMedian.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderAlphaMedian.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.OverAlphaP90.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderAlphaP90.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.OverAlphaMax.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderAlphaMax.ToString(CultureInfo.InvariantCulture));

        sb.Append(',');
        sb.Append(diag.BestScore.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(diag.CandidatesChecked.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(diag.CandidatesGated.ToString(CultureInfo.InvariantCulture));
    }

    private static bool IsDotCandidate(string fileName)
    {
        return (fileName.Contains("aligned-dot-index", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains("aligned-dot-index-", StringComparison.OrdinalIgnoreCase)
                || fileName.Contains("aligned-dot-index-single", StringComparison.OrdinalIgnoreCase))
            && fileName.Contains("-alignedN1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLineCandidate(string fileName)
    {
        return fileName.Contains("-alignedN1", StringComparison.OrdinalIgnoreCase)
            && fileName.Contains("N1N2", StringComparison.OrdinalIgnoreCase)
            && !fileName.Contains("aligned-dot-index", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParsePressure(string fileName, out double pressure)
    {
        pressure = 0;
        var m = Regex.Match(fileName, "-P(?<p>[0-9]+(?:\\.[0-9]+)?)-", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success) return false;
        return double.TryParse(m.Groups["p"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out pressure);
    }

    private static byte[]? ExtractLeftRoiAlpha(SKBitmap bmp)
    {
        if (bmp.Width <= 0 || bmp.Height <= 0) return null;
        var w = Math.Min(RoiWidthPx, bmp.Width);
        var y0 = Math.Clamp(RoiY0, 0, bmp.Height);
        var y1 = Math.Clamp(RoiY1Exclusive, 0, bmp.Height);
        if (y1 <= y0) return null;
        var h = y1 - y0;

        var buf = new byte[w * h];
        var k = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = 0; x < w; x++)
            {
                buf[k++] = bmp.GetPixel(x, y).Alpha;
            }
        }
        return buf;
    }

    private static double ComputeL1Score(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return double.PositiveInfinity;
        long sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += Math.Abs(a[i] - b[i]);
        }
        return sum / (double)a.Length;
    }

    private static (double mismatch, double iou) ComputeBinaryMismatchAndIou(byte[] a, byte[] b, byte threshold)
    {
        if (a.Length != b.Length) return (double.PositiveInfinity, 0.0);

        long mism = 0;
        long inter = 0;
        long uni = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var aa = a[i] >= threshold;
            var bb = b[i] >= threshold;
            if (aa != bb) mism++;
            if (aa && bb) inter++;
            if (aa || bb) uni++;
        }

        var mismatch = mism / (double)a.Length;
        var iou = uni == 0 ? 1.0 : inter / (double)uni;
        return (mismatch, iou);
    }

    private readonly record struct BinaryStats(double Mismatch, double Iou, double CoverageA, double CoverageB, long Inter, long Union);

    private static BinaryStats ComputeBinaryStats(byte[] a, byte[] b, byte threshold)
    {
        if (a.Length != b.Length) return new BinaryStats(double.PositiveInfinity, 0.0, 0.0, 0.0, 0, 0);

        long mism = 0;
        long aOn = 0;
        long bOn = 0;
        long inter = 0;
        long uni = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var aa = a[i] >= threshold;
            var bb = b[i] >= threshold;
            if (aa) aOn++;
            if (bb) bOn++;
            if (aa != bb) mism++;
            if (aa && bb) inter++;
            if (aa || bb) uni++;
        }

        var mismatch = mism / (double)a.Length;
        var iou = uni == 0 ? 1.0 : inter / (double)uni;
        var coverageA = aOn / (double)a.Length;
        var coverageB = bOn / (double)a.Length;
        return new BinaryStats(mismatch, iou, coverageA, coverageB, inter, uni);
    }

    private static int CountOn(byte[] a, byte threshold)
    {
        var on = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] >= threshold) on++;
        }
        return on;
    }

    private static bool IsBetterByShape(double iou, double mismatch, double l1Scaled, double bestIou, double bestMismatch, double bestL1Scaled)
    {
        if (iou > bestIou + 1e-12) return true;
        if (Math.Abs(iou - bestIou) <= 1e-12)
        {
            if (mismatch < bestMismatch - 1e-12) return true;
            if (Math.Abs(mismatch - bestMismatch) <= 1e-12)
            {
                if (l1Scaled < bestL1Scaled - 1e-12) return true;
            }
        }
        return false;
    }

    private static void TryExportBestHeatmapPng(string pngPath, string folderPath, Item line, SKBitmap lineBmp, Dictionary<byte, Match2> resultsByTh, byte th, bool fullWidth)
    {
        try
        {
            if (!resultsByTh.TryGetValue(th, out var r)) return;
            if (r.Best is null) return;

            var dotFile = r.Best.Value.Dot.FileName;
            var dotPath = Path.Combine(folderPath, dotFile);
            if (!File.Exists(dotPath)) return;
            using var dotBmp = SKBitmap.Decode(dotPath);
            if (dotBmp == null) return;

            var w = fullWidth ? Math.Min(lineBmp.Width, dotBmp.Width) : Math.Min(RoiWidthPx, Math.Min(lineBmp.Width, dotBmp.Width));
            var y0 = Math.Clamp(RoiY0, 0, Math.Min(lineBmp.Height, dotBmp.Height));
            var y1 = Math.Clamp(RoiY1Exclusive, 0, Math.Min(lineBmp.Height, dotBmp.Height));
            if (y1 <= y0) return;
            var h = y1 - y0;

            var outBmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var aLine = lineBmp.GetPixel(x, y0 + y).Alpha;
                    var aDot = dotBmp.GetPixel(x, y0 + y).Alpha;

                    var onLine = aLine >= th;
                    var onDot = aDot >= th;

                    // background white
                    var c = new SKColor(255, 255, 255, 255);
                    if (onLine && onDot)
                    {
                        // both on -> black
                        c = new SKColor(0, 0, 0, 255);
                    }
                    else if (onLine)
                    {
                        // line only -> green
                        c = new SKColor(0, 255, 0, 255);
                    }
                    else if (onDot)
                    {
                        // dot only -> blue
                        c = new SKColor(0, 0, 255, 255);
                    }

                    outBmp.SetPixel(x, y, c);
                }
            }

            var p = line.Pressure.ToString("0.########", CultureInfo.InvariantCulture);
            var suffix = fullWidth ? "-fullw" : "";
            var outName = $"lineN1-vs-dotN1-heatmap-th{th}{suffix}-P{p}.png";
            var outPath = Path.Combine(pngPath, outName);

            using var fs = File.Open(outPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            outBmp.Encode(fs, SKEncodedImageFormat.Png, 100);
        }
        catch
        {
            // ignore visualization errors
        }
    }

    private static void TryExportBestAlphaDiffPng(string pngPath, string folderPath, Item line, SKBitmap lineBmp, Dictionary<byte, Match2> resultsByTh, byte th, bool fullWidth)
    {
        try
        {
            if (!resultsByTh.TryGetValue(th, out var r)) return;
            if (r.Best is null) return;

            var dotFile = r.Best.Value.Dot.FileName;
            var dotPath = Path.Combine(folderPath, dotFile);
            if (!File.Exists(dotPath)) return;
            using var dotBmp = SKBitmap.Decode(dotPath);
            if (dotBmp == null) return;

            var w = fullWidth ? Math.Min(lineBmp.Width, dotBmp.Width) : Math.Min(RoiWidthPx, Math.Min(lineBmp.Width, dotBmp.Width));
            var y0 = Math.Clamp(RoiY0, 0, Math.Min(lineBmp.Height, dotBmp.Height));
            var y1 = Math.Clamp(RoiY1Exclusive, 0, Math.Min(lineBmp.Height, dotBmp.Height));
            if (y1 <= y0) return;
            var h = y1 - y0;

            var outBmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var aLine = lineBmp.GetPixel(x, y0 + y).Alpha;
                    var aDot = dotBmp.GetPixel(x, y0 + y).Alpha;
                    var d = Math.Abs(aLine - aDot); // 0..255

                    // Visualize magnitude: white=0, red=255
                    var v = (byte)d;
                    var rC = (byte)255;
                    var gb = (byte)(255 - v);
                    var c = new SKColor(rC, gb, gb, 255);
                    outBmp.SetPixel(x, y, c);
                }
            }

            var p = line.Pressure.ToString("0.########", CultureInfo.InvariantCulture);
            var suffix = fullWidth ? "-fullw" : "";
            var outName = $"lineN1-vs-dotN1-diffmag-th{th}{suffix}-P{p}.png";
            var outPath = Path.Combine(pngPath, outName);

            using var fs = File.Open(outPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            outBmp.Encode(fs, SKEncodedImageFormat.Png, 100);
        }
        catch
        {
            // ignore visualization errors
        }
    }

    private static bool IsBetter(double mismatch, double iou, double l1, double bestMismatch, double bestIou, double bestL1)
    {
        if (mismatch < bestMismatch - 1e-12) return true;
        if (Math.Abs(mismatch - bestMismatch) <= 1e-12)
        {
            // Prefer larger IoU when mismatch ties.
            if (iou > bestIou + 1e-12) return true;
            if (Math.Abs(iou - bestIou) <= 1e-12)
            {
                // Fall back to alpha similarity.
                if (l1 < bestL1 - 1e-12) return true;
            }
        }
        return false;
    }

    private static Match2 FindBestAndSecond(byte[] lineAlpha, List<DotRoi> dotRois, byte threshold)
    {
        var effectiveThreshold = threshold;
        if (threshold == 1)
        {
            // 線が薄すぎてth=1だと形状が成立しない場合、th=0へ自動緩和する。
            if (CountOn(lineAlpha, threshold: 1) < Th1MinLineOnPxForThreshold1)
            {
                effectiveThreshold = 0;
            }
        }

        double bestIou = double.NegativeInfinity;
        double bestMismatch = double.PositiveInfinity;
        double bestK = 1.0;
        double bestAlphaL1Scaled = double.PositiveInfinity;
        double bestCovLine = 0;
        double bestCovDot = 0;
        long bestInter = 0;
        long bestUnion = 0;
        DotRoi? best = null;

        double secondIou = double.NegativeInfinity;
        double secondMismatch = double.PositiveInfinity;
        double secondK = 1.0;
        double secondAlphaL1Scaled = double.PositiveInfinity;
        double secondCovLine = 0;
        double secondCovDot = 0;
        long secondInter = 0;
        long secondUnion = 0;
        DotRoi? second = null;

        var passedIouGate = 0;
        var passedUnionGate = 0;
        var passedCovGate = 0;

        for (var i = 0; i < dotRois.Count; i++)
        {
            var dotAlpha = dotRois[i].Alpha;
            if (dotAlpha.Length != lineAlpha.Length) continue;

            var stats = ComputeBinaryStats(lineAlpha, dotAlpha, effectiveThreshold);
            var mismatch = stats.Mismatch;
            var iou = stats.Iou;
            var (k, l1Scaled) = EstimateBestAlphaScaleAndL1(lineAlpha, dotAlpha);

            if (iou < MinIouGate) continue;
            passedIouGate++;

            if (threshold == 1)
            {
                if (stats.Union < MinUnionGateTh1) continue;
                passedUnionGate++;
                var minCov = MinCovPxTh1 / lineAlpha.Length;
                if (stats.CoverageA < minCov) continue;
                if (stats.CoverageB < minCov) continue;
                passedCovGate++;
            }
            else
            {
                if (stats.Union < MinUnionGateTh23) continue;
                passedUnionGate++;
                var minCov = MinCovPxTh23 / lineAlpha.Length;
                if (stats.CoverageA < minCov) continue;
                if (stats.CoverageB < minCov) continue;
                passedCovGate++;
            }

            if (IsBetterByShape(iou, mismatch, l1Scaled, bestIou, bestMismatch, bestAlphaL1Scaled))
            {
                secondIou = bestIou;
                secondMismatch = bestMismatch;
                secondK = bestK;
                secondAlphaL1Scaled = bestAlphaL1Scaled;
                secondCovLine = bestCovLine;
                secondCovDot = bestCovDot;
                secondInter = bestInter;
                secondUnion = bestUnion;
                second = best;

                bestIou = iou;
                bestMismatch = mismatch;
                bestK = k;
                bestAlphaL1Scaled = l1Scaled;
                bestCovLine = stats.CoverageA;
                bestCovDot = stats.CoverageB;
                bestInter = stats.Inter;
                bestUnion = stats.Union;
                best = dotRois[i];
                continue;
            }

            if (IsBetterByShape(iou, mismatch, l1Scaled, secondIou, secondMismatch, secondAlphaL1Scaled))
            {
                secondIou = iou;
                secondMismatch = mismatch;
                secondK = k;
                secondAlphaL1Scaled = l1Scaled;
                secondCovLine = stats.CoverageA;
                secondCovDot = stats.CoverageB;
                secondInter = stats.Inter;
                secondUnion = stats.Union;
                second = dotRois[i];
            }
        }

        return new Match2(
            Best: best.HasValue
                ? new Match(best.Value, bestMismatch, bestIou, bestK, bestAlphaL1Scaled, bestCovLine, bestCovDot, bestInter, bestUnion, lineAlpha, best.Value.Alpha)
                : null,
            Second: second.HasValue
                ? new Match(second.Value, secondMismatch, secondIou, secondK, secondAlphaL1Scaled, secondCovLine, secondCovDot, secondInter, secondUnion, lineAlpha, second.Value.Alpha)
                : null,
            EffectiveThreshold: effectiveThreshold,
            PassedIouGate: passedIouGate,
            PassedUnionGate: passedUnionGate,
            PassedCovGate: passedCovGate);
    }

    private static void AppendMatchTh1(StringBuilder sb, string folderPath, Match? match, byte[]? alphaLut)
    {
        sb.Append(',');
        if (match is null)
        {
            sb.Append("0");
            sb.Append(',');
            sb.Append(' ');
            sb.Append(',');
            sb.Append(' ');
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("Infinity");
            sb.Append(',');
            sb.Append("1");
            sb.Append(',');
            sb.Append("Infinity");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");

            // Over/under stats (8)
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");

            // LUT stats (6)
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("Infinity");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            return;
        }

        sb.Append(match.Value.Dot.Pressure.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(' ');
        sb.Append(',');
        sb.Append(' ');
        sb.Append(',');
        sb.Append(match.Value.BinIou.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.BinMismatch.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.AlphaK.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.AlphaL1Scaled.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.CoverageA.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.CoverageB.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.Union.ToString(CultureInfo.InvariantCulture));

        var ou = ComputeOverUnderStats(match.Value.LineAlpha, match.Value.DotAlpha, threshold: 1);
        sb.Append(',');
        sb.Append(ou.OverArea.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderArea.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.OverAlphaMedian.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderAlphaMedian.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.OverAlphaP90.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderAlphaP90.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.OverAlphaMax.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderAlphaMax.ToString(CultureInfo.InvariantCulture));

        if (alphaLut is { Length: 256 })
        {
            var lutStats = ComputeLutStats(match.Value.LineAlpha, match.Value.DotAlpha, threshold: 1, alphaLut);
            sb.Append(',');
            sb.Append("1");
            sb.Append(',');
            sb.Append(lutStats.L1Mean.ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(lutStats.OverArea.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(lutStats.UnderArea.ToString(CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(lutStats.OverAlphaMedian.ToString("0.########", CultureInfo.InvariantCulture));
            sb.Append(',');
            sb.Append(lutStats.UnderAlphaMedian.ToString("0.########", CultureInfo.InvariantCulture));
        }
        else
        {
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("Infinity");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
            sb.Append(',');
            sb.Append("0");
        }
    }

    private static void AppendMatchShapeWithOverUnder(StringBuilder sb, string folderPath, Match? match, byte threshold)
    {
        sb.Append(',');
        if (match is null)
        {
            // Shape-only columns (8)
            sb.Append("0"); // dot_pressure
            sb.Append(',');
            sb.Append(' '); // dot_file
            sb.Append(',');
            sb.Append(' '); // dot_path
            sb.Append(',');
            sb.Append("0"); // bin_iou
            sb.Append(',');
            sb.Append("Infinity"); // bin_mismatch
            sb.Append(',');
            sb.Append("0"); // cov_line
            sb.Append(',');
            sb.Append("0"); // cov_dot
            sb.Append(',');
            sb.Append("0"); // union

            // Over/under stats columns (8)
            sb.Append(',');
            sb.Append("0"); // over_area
            sb.Append(',');
            sb.Append("0"); // under_area
            sb.Append(',');
            sb.Append("0"); // over_alpha_median
            sb.Append(',');
            sb.Append("0"); // under_alpha_median
            sb.Append(',');
            sb.Append("0"); // over_alpha_p90
            sb.Append(',');
            sb.Append("0"); // under_alpha_p90
            sb.Append(',');
            sb.Append("0"); // over_alpha_max
            sb.Append(',');
            sb.Append("0"); // under_alpha_max
            return;
        }

        sb.Append(match.Value.Dot.Pressure.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(' '); // dot_file
        sb.Append(',');
        sb.Append(' '); // dot_path
        sb.Append(',');
        sb.Append(match.Value.BinIou.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.BinMismatch.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.CoverageA.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.CoverageB.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(match.Value.Union.ToString(CultureInfo.InvariantCulture));

        var ou = ComputeOverUnderStats(match.Value.LineAlpha, match.Value.DotAlpha, threshold);
        sb.Append(',');
        sb.Append(ou.OverArea.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderArea.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.OverAlphaMedian.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderAlphaMedian.ToString("0.########", CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.OverAlphaP90.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderAlphaP90.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.OverAlphaMax.ToString(CultureInfo.InvariantCulture));
        sb.Append(',');
        sb.Append(ou.UnderAlphaMax.ToString(CultureInfo.InvariantCulture));
    }

    private readonly record struct Match2(
        Match? Best,
        Match? Second,
        byte EffectiveThreshold,
        int PassedIouGate,
        int PassedUnionGate,
        int PassedCovGate);
    private readonly record struct Match(DotRoi Dot, double BinMismatch, double BinIou, double AlphaK, double AlphaL1Scaled, double CoverageA, double CoverageB, long Inter, long Union, byte[] LineAlpha, byte[] DotAlpha);

    private static (double k, double l1Scaled) EstimateBestAlphaScaleAndL1(byte[] lineAlpha, byte[] dotAlpha)
    {
        if (lineAlpha.Length != dotAlpha.Length) return (1.0, double.PositiveInfinity);

        // Least-squares k for line ~= k * dot.
        // k = (sum(line*dot)) / (sum(dot*dot))
        double num = 0;
        double den = 0;
        for (var i = 0; i < lineAlpha.Length; i++)
        {
            var a = (double)lineAlpha[i];
            var b = (double)dotAlpha[i];
            num += a * b;
            den += b * b;
        }

        var k = den <= 0 ? 1.0 : (num / den);
        if (double.IsNaN(k) || double.IsInfinity(k)) k = 1.0;
        if (k < 0) k = 0;

        // Evaluate L1 with clipping to 0..255.
        double sum = 0;
        for (var i = 0; i < lineAlpha.Length; i++)
        {
            var scaled = k * dotAlpha[i];
            if (scaled < 0) scaled = 0;
            if (scaled > 255) scaled = 255;
            sum += Math.Abs(lineAlpha[i] - scaled);
        }
        var l1Scaled = sum / lineAlpha.Length;
        return (k, l1Scaled);
    }

    private readonly record struct OverUnderStats(
        long OverArea,
        long UnderArea,
        double OverAlphaMedian,
        double UnderAlphaMedian,
        int OverAlphaMax,
        int UnderAlphaMax,
        int OverAlphaP90,
        int UnderAlphaP90);

    private readonly record struct LutStats(double L1Mean, long OverArea, long UnderArea, double OverAlphaMedian, double UnderAlphaMedian);

    private static OverUnderStats ComputeOverUnderStats(byte[] lineAlpha, byte[] dotAlpha, byte threshold)
    {
        if (lineAlpha.Length != dotAlpha.Length) return new OverUnderStats(0, 0, 0, 0, 0, 0, 0, 0);

        var over = new List<int>();
        var under = new List<int>();
        over.Capacity = 256;
        under.Capacity = 256;

        for (var i = 0; i < lineAlpha.Length; i++)
        {
            var onLine = lineAlpha[i] >= threshold;
            var onDot = dotAlpha[i] >= threshold;
            if (onDot && !onLine)
            {
                over.Add(dotAlpha[i] - lineAlpha[i]);
            }
            else if (onLine && !onDot)
            {
                under.Add(lineAlpha[i] - dotAlpha[i]);
            }
        }

        return new OverUnderStats(
            OverArea: over.Count,
            UnderArea: under.Count,
            OverAlphaMedian: Median(over),
            UnderAlphaMedian: Median(under),
            OverAlphaMax: MaxOrZero(over),
            UnderAlphaMax: MaxOrZero(under),
            OverAlphaP90: PercentileOrZero(over, 0.90),
            UnderAlphaP90: PercentileOrZero(under, 0.90));
    }

    private static int MaxOrZero(List<int> values)
    {
        if (values.Count == 0) return 0;
        var max = 0;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] > max) max = values[i];
        }
        return max;
    }

    private static int PercentileOrZero(List<int> values, double p)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var idx = (int)Math.Floor(p * (values.Count - 1));
        if (idx < 0) idx = 0;
        if (idx >= values.Count) idx = values.Count - 1;
        return values[idx];
    }

    private static LutStats ComputeLutStats(byte[] lineAlpha, byte[] dotAlpha, byte threshold, byte[] lut)
    {
        if (lut.Length != 256) return new LutStats(double.PositiveInfinity, 0, 0, 0, 0);
        if (lineAlpha.Length != dotAlpha.Length) return new LutStats(double.PositiveInfinity, 0, 0, 0, 0);

        long sumAbs = 0;
        var over = new List<int>();
        var under = new List<int>();
        over.Capacity = 256;
        under.Capacity = 256;

        for (var i = 0; i < lineAlpha.Length; i++)
        {
            var aLine = lineAlpha[i];
            var aDotLut = lut[dotAlpha[i]];
            sumAbs += Math.Abs(aLine - aDotLut);

            var onLine = aLine >= threshold;
            var onDot = aDotLut >= threshold;
            if (onDot && !onLine)
            {
                over.Add(aDotLut - aLine);
            }
            else if (onLine && !onDot)
            {
                under.Add(aLine - aDotLut);
            }
        }

        return new LutStats(
            L1Mean: sumAbs / (double)lineAlpha.Length,
            OverArea: over.Count,
            UnderArea: under.Count,
            OverAlphaMedian: Median(over),
            UnderAlphaMedian: Median(under));
    }

    private static double Median(List<int> values)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var mid = values.Count / 2;
        if ((values.Count & 1) == 1) return values[mid];
        return 0.5 * (values[mid - 1] + values[mid]);
    }

    private static byte ScaleAlpha(byte a, double k)
    {
        var scaled = k * a;
        if (scaled < 0) scaled = 0;
        if (scaled > 255) scaled = 255;
        return (byte)Math.Round(scaled);
    }

    private static bool TryLoadAlphaLutFromGimpCrv(string crvPath, out byte[] lut, out LutLoadResult result)
    {
        result = default;
        lut = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(crvPath))
        {
            result = new LutLoadResult(false, crvPath, "", "LUT path is empty.");
            return false;
        }

        var candidates = new List<string>(4);
        if (Path.IsPathRooted(crvPath))
        {
            candidates.Add(crvPath);
        }
        else
        {
            candidates.Add(Path.GetFullPath(crvPath));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, crvPath));
        }

        var resolved = candidates.FirstOrDefault(File.Exists);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            result = new LutLoadResult(false, crvPath, "", "LUT file not found. Tried: " + string.Join(" | ", candidates));
            return false;
        }

        string text;
        try
        {
            text = File.ReadAllText(resolved);
        }
        catch (IOException ex)
        {
            result = new LutLoadResult(false, crvPath, resolved, ex.Message);
            return false;
        }

        // (channel alpha) ブロックを抽出してから samples を探す（括弧位置に依存しないようにする）
        var mBlock = Regex.Match(
            text,
            @"\(channel\s+alpha\)\s*\(curve(?<body>[\s\S]*?)\)\s*(?:\(channel\s+|#\s*end|\z)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!mBlock.Success)
        {
            result = new LutLoadResult(false, crvPath, resolved, "Failed to locate '(channel alpha)' block.");
            return false;
        }

        var body = mBlock.Groups["body"].Value;
        var mSamples = Regex.Match(
            body,
            @"\(samples\s+(?<n>\d+)\s+(?<vals>[\s\S]*?)\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!mSamples.Success)
        {
            result = new LutLoadResult(false, crvPath, resolved, "Failed to locate '(samples ...)'.");
            return false;
        }

        if (!int.TryParse(mSamples.Groups["n"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            result = new LutLoadResult(false, crvPath, resolved, "Invalid samples count.");
            return false;
        }
        if (n != 256)
        {
            result = new LutLoadResult(false, crvPath, resolved, $"Unsupported samples count: {n} (expected 256)." );
            return false;
        }

        var vals = mSamples.Groups["vals"].Value;
        var ms = Regex.Matches(vals, @"[-+]?(?:\d+\.\d+|\d+|\.\d+)(?:[eE][-+]?\d+)?", RegexOptions.CultureInvariant);
        if (ms.Count < 256)
        {
            result = new LutLoadResult(false, crvPath, resolved, $"Not enough sample values: {ms.Count} (expected 256)." );
            return false;
        }

        var b = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            if (!double.TryParse(ms[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                result = new LutLoadResult(false, crvPath, resolved, $"Failed to parse sample[{i}]='{ms[i].Value}'.");
                return false;
            }
            if (v < 0) v = 0;
            if (v > 1) v = 1;
            b[i] = (byte)Math.Round(v * 255.0);
        }

        lut = b;
        result = new LutLoadResult(true, crvPath, resolved, "");
        return true;
    }

    private static void TryExportBestHeatmapPngLut(string pngPath, string folderPath, Item line, SKBitmap lineBmp, Dictionary<byte, Match2> resultsByTh, byte th, bool fullWidth, byte[] lut)
    {
        try
        {
            if (lut.Length != 256) return;
            if (!resultsByTh.TryGetValue(th, out var r)) return;
            if (r.Best is null) return;

            var dotFile = r.Best.Value.Dot.FileName;
            var dotPath = Path.Combine(folderPath, dotFile);
            if (!File.Exists(dotPath)) return;
            using var dotBmp = SKBitmap.Decode(dotPath);
            if (dotBmp == null) return;

            var w = fullWidth ? Math.Min(lineBmp.Width, dotBmp.Width) : Math.Min(RoiWidthPx, Math.Min(lineBmp.Width, dotBmp.Width));
            var y0 = Math.Clamp(RoiY0, 0, Math.Min(lineBmp.Height, dotBmp.Height));
            var y1 = Math.Clamp(RoiY1Exclusive, 0, Math.Min(lineBmp.Height, dotBmp.Height));
            if (y1 <= y0) return;
            var h = y1 - y0;

            var outBmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var aLine = lineBmp.GetPixel(x, y0 + y).Alpha;
                    var aDot = dotBmp.GetPixel(x, y0 + y).Alpha;
                    var aDotLut = lut[aDot];

                    var onLine = aLine >= th;
                    var onDot = aDotLut >= th;

                    var c = new SKColor(255, 255, 255, 255);
                    if (onLine && onDot) c = new SKColor(0, 0, 0, 255);
                    else if (onLine) c = new SKColor(0, 255, 0, 255);
                    else if (onDot) c = new SKColor(0, 0, 255, 255);

                    outBmp.SetPixel(x, y, c);
                }
            }

            var p = line.Pressure.ToString("0.########", CultureInfo.InvariantCulture);
            var suffix = fullWidth ? "-fullw" : "";
            var outName = $"lineN1-vs-dotN1-heatmap-lut-th{th}{suffix}-P{p}.png";
            var outPath = Path.Combine(pngPath, outName);
            using var fs = File.Open(outPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            outBmp.Encode(fs, SKEncodedImageFormat.Png, 100);
        }
        catch
        {
            // ignore visualization errors
        }
    }

    private static void TryExportBestAlphaDiffPngLut(string pngPath, string folderPath, Item line, SKBitmap lineBmp, Dictionary<byte, Match2> resultsByTh, byte th, bool fullWidth, byte[] lut)
    {
        try
        {
            if (lut.Length != 256) return;
            if (!resultsByTh.TryGetValue(th, out var r)) return;
            if (r.Best is null) return;

            var dotFile = r.Best.Value.Dot.FileName;
            var dotPath = Path.Combine(folderPath, dotFile);
            if (!File.Exists(dotPath)) return;
            using var dotBmp = SKBitmap.Decode(dotPath);
            if (dotBmp == null) return;

            var w = fullWidth ? Math.Min(lineBmp.Width, dotBmp.Width) : Math.Min(RoiWidthPx, Math.Min(lineBmp.Width, dotBmp.Width));
            var y0 = Math.Clamp(RoiY0, 0, Math.Min(lineBmp.Height, dotBmp.Height));
            var y1 = Math.Clamp(RoiY1Exclusive, 0, Math.Min(lineBmp.Height, dotBmp.Height));
            if (y1 <= y0) return;
            var h = y1 - y0;

            var outBmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var aLine = lineBmp.GetPixel(x, y0 + y).Alpha;
                    var aDot = dotBmp.GetPixel(x, y0 + y).Alpha;
                    var aDotLut = lut[aDot];
                    var d = Math.Abs(aLine - aDotLut);

                    var v = (byte)d;
                    var rC = (byte)255;
                    var gb = (byte)(255 - v);
                    var c = new SKColor(rC, gb, gb, 255);
                    outBmp.SetPixel(x, y, c);
                }
            }

            var p = line.Pressure.ToString("0.########", CultureInfo.InvariantCulture);
            var suffix = fullWidth ? "-fullw" : "";
            var outName = $"lineN1-vs-dotN1-diffmag-lut-th{th}{suffix}-P{p}.png";
            var outPath = Path.Combine(pngPath, outName);
            using var fs = File.Open(outPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            outBmp.Encode(fs, SKEncodedImageFormat.Png, 100);
        }
        catch
        {
            // ignore visualization errors
        }
    }

    private readonly record struct Item(string FileName, double Pressure);

    private readonly record struct DotRoi(double Pressure, string FileName, byte[] Alpha);
}
