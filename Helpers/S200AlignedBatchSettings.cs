using System;
using System.Globalization;

namespace StrokeSampler.Helpers;

internal sealed class S200AlignedBatchSettings
{
    internal int OutWidthDip { get; }
    internal int OutHeightDip { get; }
    internal int ExportScale { get; }
    internal double PeriodStepDip { get; }
    internal int DotCount { get; }
    internal int Trials { get; }
    internal string RunTag { get; }
    internal double BatchPStart { get; }
    internal double BatchPEnd { get; }
    internal double BatchPStep { get; }
    internal double StartXDip { get; }
    internal double StartYDip { get; }
    internal double LineLengthDip { get; }
    internal bool IsTransparentBackground { get; }
    internal bool IsEraser { get; }
    internal bool IsPencil { get; }
    internal bool IsBlack { get; }
    internal bool IsWhite { get; }
    internal bool IsTransparent { get; }
    internal bool IsRed { get; }
    internal bool IsGreen { get; }
    internal bool IsBlue { get; }
    internal bool IsYellow { get; }

    internal S200AlignedBatchSettings(
        int outWidthDip,
        int outHeightDip,
        int exportScale,
        double periodStepDip,
        int dotCount,
        int trials,
        string runTag,
        double batchPStart,
        double batchPEnd,
        double batchPStep,
        double startXDip,
        double startYDip,
        double lineLengthDip,
        bool isTransparentBackground,
        bool isEraser=false,
        bool isPencil=true,
        bool isBlack=true,
        bool isWhite=false,
        bool isTransparent=false,
        bool isRed=false,
        bool isGreen=false,
        bool isBlue=false
        )
    {
        OutWidthDip = outWidthDip;
        OutHeightDip = outHeightDip;
        ExportScale = exportScale;
        PeriodStepDip = periodStepDip;
        DotCount = dotCount;
        Trials = trials;
        RunTag = runTag ?? "";
        BatchPStart = batchPStart;
        BatchPEnd = batchPEnd;
        BatchPStep = batchPStep;
        StartXDip = startXDip;
        StartYDip = startYDip;
        LineLengthDip = lineLengthDip;
        IsTransparentBackground = isTransparentBackground;
        IsEraser = isEraser;
        IsPencil = isPencil;
        IsBlack = isBlack;
        IsWhite = isWhite;
        IsTransparent = isTransparent;
        IsRed = isRed;
        IsGreen = isGreen;
        IsBlue = isBlue;
    }

    internal int DecimalsFromStep()
    {
        var step = Math.Abs(BatchPStep);
        for (var d = 0; d <= 8; d++)
        {
            var scaled = step * Math.Pow(10, d);
            if (Math.Abs(scaled - Math.Round(scaled)) < 1e-10) return d;
        }
        return 8;
    }

    internal string BuildEffectiveRunTag(int? trial)
    {
        if (string.IsNullOrWhiteSpace(RunTag))
        {
            var baseTag = "run" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            return trial is null ? baseTag : $"{baseTag}-t{trial.Value}";
        }

        if (trial is null) return RunTag;
        if (Trials <= 1) return RunTag;
        return $"{RunTag}-t{trial.Value}";
    }
}
