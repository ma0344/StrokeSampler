using System;
namespace SkiaTester.Helpers;

public static class PencilDotRenderer
{
    public enum BaseShapeMode
    {
        IdealCircle,
        PaperOnly,
    }

    public enum PaperOnlyFalloffMode
    {
        None,
        RadiusThreshold,
    }

    public enum PaperCapMode
    {
        None,
        CapOutAlpha,
    }

    public enum PaperNoiseApplyMode
    {
        Alpha,
        StampCount,
    }

    public enum PaperMaskFalloffMode
    {
        None,
        StrongerAtEdge,
        ThresholdAtEdge,
    }

    public enum PaperNoiseApplyStage
    {
        // 既存挙動: 合成前（a01 または nEff）にkを適用
        PreComposite,
        // 実験用: 合成後（outA）にkを適用
        PostComposite,
    }

    public enum PaperMaskMode
    {
        None,
        MultiplyOutAlpha,
        SoftOutAlpha,
        ThresholdOutAlpha,
    }

    private static double ComputePaperMask01(
        PaperMaskMode mode,
        double threshold01,
        double gain,
        double falloffWeight,
        double n01,
        double noiseMean01,
        double noiseStd01)
    {
        if (mode == PaperMaskMode.None) return 1.0;
        if (gain <= 0) return 1.0;

        // 既存のk生成と同様にzへ正規化してから使う。
        // 「出っ張りが光る」イメージに近づけるため、zを0..1に落としてマスクにする。
        if (noiseStd01 <= 0) return 1.0;

        var z = (n01 - noiseMean01) / noiseStd01;
        z = Math.Clamp(z, -3.0, 3.0);

        // z=-3..+3 => t=0..1
        var t = (z + 3.0) / 6.0;

        // gainでコントラストを調整
        t = 0.5 + (t - 0.5) * gain;
        t = Math.Clamp(t, 0.0, 1.0);

        if (mode == PaperMaskMode.MultiplyOutAlpha)
        {
            return t;
        }

        if (mode == PaperMaskMode.SoftOutAlpha)
        {
            // 床付きの連続マスク: thresholdを境に滑らかに立ち上げる
            // gainは「どれだけ急に立ち上げるか」として使う
            var g = gain;
            if (g <= 0) g = 1.0;
            if (falloffWeight > 0) g *= falloffWeight;
            var m = (t - threshold01) * g;
            return Math.Clamp(m, 0.0, 1.0);
        }

        // ThresholdOutAlpha
        return t >= threshold01 ? 1.0 : 0.0;
    }

    public static SkiaSharp.SKBitmap Render(int canvasSizePx, int diameterPx, double pressure)
    {
        return Render(canvasSizePx, diameterPx, pressure, stampCount: 1, noise: null, paperNoiseStrength: 0.35, paperNoiseScale: 2.0, paperNoiseOffsetX: 0.0, paperNoiseOffsetY: 0.0, paperNoiseGain: 0.2, paperNoiseLowFreqScale: 4.0, paperNoiseLowFreqMix: 0.0, paperNoiseApplyMode: PaperNoiseApplyMode.Alpha, falloffLut: null, disableKMeanNormalization: false, paperNoiseApplyStage: PaperNoiseApplyStage.PreComposite, alphaCutoff01: 0.0, noiseDependentCutoff: false, paperMaskMode: PaperMaskMode.None, paperMaskThreshold01: 0.5, paperMaskGain: 1.0, paperMaskFalloffMode: PaperMaskFalloffMode.None, baseShapeMode: BaseShapeMode.IdealCircle, paperOnlyFalloffMode: PaperOnlyFalloffMode.None, paperOnlyRadiusThNorm: 1.0, paperCapMode: PaperCapMode.None, paperCapGain: 1.0);
    }

    public static SkiaSharp.SKBitmap Render(int canvasSizePx, int diameterPx, double pressure, PaperNoise? noise)
    {
        return Render(canvasSizePx, diameterPx, pressure, stampCount: 1, noise, paperNoiseStrength: 0.35, paperNoiseScale: 2.0, paperNoiseOffsetX: 0.0, paperNoiseOffsetY: 0.0, paperNoiseGain: 0.2, paperNoiseLowFreqScale: 4.0, paperNoiseLowFreqMix: 0.0, paperNoiseApplyMode: PaperNoiseApplyMode.Alpha, falloffLut: null, disableKMeanNormalization: false, paperNoiseApplyStage: PaperNoiseApplyStage.PreComposite, alphaCutoff01: 0.0, noiseDependentCutoff: false, paperMaskMode: PaperMaskMode.None, paperMaskThreshold01: 0.5, paperMaskGain: 1.0, paperMaskFalloffMode: PaperMaskFalloffMode.None, baseShapeMode: BaseShapeMode.IdealCircle, paperOnlyFalloffMode: PaperOnlyFalloffMode.None, paperOnlyRadiusThNorm: 1.0, paperCapMode: PaperCapMode.None, paperCapGain: 1.0);
    }

    public static SkiaSharp.SKBitmap Render(int canvasSizePx, int diameterPx, double pressure, int stampCount, PaperNoise? noise, double paperNoiseStrength, double paperNoiseScale, double paperNoiseOffsetX, double paperNoiseOffsetY, double paperNoiseGain, double paperNoiseLowFreqScale, double paperNoiseLowFreqMix, PaperNoiseApplyMode paperNoiseApplyMode, NormalizedFalloffLut? falloffLut, bool disableKMeanNormalization, PaperNoiseApplyStage paperNoiseApplyStage, double alphaCutoff01, bool noiseDependentCutoff, PaperMaskMode paperMaskMode, double paperMaskThreshold01, double paperMaskGain, PaperMaskFalloffMode paperMaskFalloffMode, BaseShapeMode baseShapeMode, PaperOnlyFalloffMode paperOnlyFalloffMode, double paperOnlyRadiusThNorm, PaperCapMode paperCapMode, double paperCapGain)
    {
        if (canvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(canvasSizePx));
        if (diameterPx <= 0) throw new ArgumentOutOfRangeException(nameof(diameterPx));
        if (stampCount <= 0) throw new ArgumentOutOfRangeException(nameof(stampCount));
        if (paperNoiseStrength < 0 || paperNoiseStrength > 1) throw new ArgumentOutOfRangeException(nameof(paperNoiseStrength));
        if (paperNoiseScale <= 0) throw new ArgumentOutOfRangeException(nameof(paperNoiseScale));
        if (paperNoiseGain < 0) throw new ArgumentOutOfRangeException(nameof(paperNoiseGain));

        var bitmap = new SkiaSharp.SKBitmap(canvasSizePx, canvasSizePx, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);

        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.Transparent);

            var pFloor = PencilPressureFloorTable.GetPFloor(diameterPx); 
            if (pressure <= pFloor)
            {
                return bitmap;
            }

            // 半径減衰（UWP観測）
            // - normalized-falloff(S0=200) LUTを優先して使用する（紙目込みradial-falloffの二重適用を避ける）
            // - falloffLutが未指定の場合のみ既存の配列にフォールバックする
            var falloff = UwpS200P1RadialFalloff;

            var radiusPx = diameterPx * 0.5;
            var cx = (canvasSizePx - 1) * 0.5;
            var cy = (canvasSizePx - 1) * 0.5;

            var pScale = Math.Clamp(pressure, 0.0, 1.0);

            // 1) kの平均を計測（半径内のみ）
            var kMean = 1.0;
            if (noise != null && paperNoiseStrength > 0 && paperNoiseGain > 0)
            {
                double kSum = 0;
                long kCount = 0;

                for (var y = 0; y < canvasSizePx; y++)
                {
                    var dy = y - cy;
                    for (var x = 0; x < canvasSizePx; x++)
                    {
                        var dx = x - cx;
                        var dist = Math.Sqrt(dx * dx + dy * dy);
                        if (dist > radiusPx) continue;

                        var nx = ((x + 0.5) + paperNoiseOffsetX) / paperNoiseScale;
                        var ny = ((y + 0.5) + paperNoiseOffsetY) / paperNoiseScale;
                        var n01 = noise.Sample01Mixed(nx, ny, paperNoiseLowFreqScale, paperNoiseLowFreqMix);
                        var mean = noise.Mean01;
                        var std = noise.Stddev01;
                        if (std <= 0) continue;

                        var z = (n01 - mean) / std;
                        z = Math.Clamp(z, -3.0, 3.0);

                        var k = 1.0 + (paperNoiseStrength * paperNoiseGain) * z;
                        k = Math.Clamp(k, 0.5, 1.5);
                        kSum += k;
                        kCount++;
                    }
                }

                if (kCount > 0)
                {
                    kMean = kSum / kCount;
                    if (double.IsNaN(kMean) || double.IsInfinity(kMean) || kMean <= 0)
                    {
                        kMean = 1.0;
                    }
                }
            }

            if (disableKMeanNormalization)
            {
                kMean = 1.0;
            }

            // 2) 実描画（kを平均1に再正規化）
            for (var y = 0; y < canvasSizePx; y++)
            {
                var dy = y - cy;
                for (var x = 0; x < canvasSizePx; x++)
                {
                    var dx = x - cx;
                    var dist = Math.Sqrt(dx * dx + dy * dy);

                    if (dist > radiusPx)
                    {
                        continue;
                    }

                    var rNorm = dist * (200.0 / diameterPx);
                    double f;
                    if (baseShapeMode == BaseShapeMode.IdealCircle)
                    {
                        if (falloffLut != null)
                        {
                            f = falloffLut.Eval(rNorm);
                        }
                        else
                        {
                            var r = (int)Math.Floor(rNorm);
                            if ((uint)r >= (uint)falloff.Length)
                            {
                                continue;
                            }
                            f = falloff[r];
                        }
                    }
                    else
                    {
                    // PaperOnly: 既定はfalloff無効（互換）
                    if (paperOnlyFalloffMode == PaperOnlyFalloffMode.RadiusThreshold)
                    {
                        var th = Math.Clamp(paperOnlyRadiusThNorm, 0.0, 1.0);
                        f = (dist <= radiusPx * th) ? 1.0 : 0.0;
                    }
                    else
                    {
                        f = 1.0;
                    }
                    }

                    var a01 = f * pScale;
                    var nEff = (double)stampCount;
                    var kApplied = 1.0;
                    var paperMask01 = 1.0;
                    if (noise != null)
                    {
                        var mean = noise.Mean01;
                        var std = noise.Stddev01;
                        if (std > 0)
                        {
                            var nx = ((x + 0.5) + paperNoiseOffsetX) / paperNoiseScale;
                            var ny = ((y + 0.5) + paperNoiseOffsetY) / paperNoiseScale;
                            var n01 = noise.Sample01Mixed(nx, ny, paperNoiseLowFreqScale, paperNoiseLowFreqMix);

                            var falloffWeight = 1.0;
                            var thresholdAdj = paperMaskThreshold01;
                            if (paperMaskFalloffMode == PaperMaskFalloffMode.StrongerAtEdge)
                            {
                                // 外縁ほど強く：SoftOutAlphaの立ち上がり(gain)を外側で急にする
                                var denom = Math.Max(0.15, Math.Clamp(f, 0.0, 1.0));
                                falloffWeight = Math.Clamp(1.0 / denom, 1.0, 6.0);
                            }
                            else if (paperMaskFalloffMode == PaperMaskFalloffMode.ThresholdAtEdge)
                            {
                                // 外縁ほど厳しく：SoftOutAlphaの床(threshold)を外側で上げる
                                // f=1 => +0, f=0 => +0.35（クランプ）
                                var edge = 1.0 - Math.Clamp(f, 0.0, 1.0);
                                thresholdAdj = Math.Clamp(paperMaskThreshold01 + 0.35 * edge, 0.0, 1.0);
                            }

                            paperMask01 = ComputePaperMask01(paperMaskMode, thresholdAdj, paperMaskGain, falloffWeight, n01, mean, std);

                            var z = (n01 - mean) / std;
                            z = Math.Clamp(z, -3.0, 3.0);

                            kApplied = 1.0 + (paperNoiseStrength * paperNoiseGain) * z;
                            kApplied = Math.Clamp(kApplied, 0.5, 1.5);
                            kApplied /= kMean;
                            if (paperNoiseApplyStage == PaperNoiseApplyStage.PreComposite)
                            {
                                if (paperNoiseApplyMode == PaperNoiseApplyMode.StampCount)
                                {
                                    nEff *= kApplied;
                                }
                                else
                                {
                                    a01 *= kApplied;
                                }
                            }
                        }
                    }

                    if (a01 <= 0)
                    {
                        continue;
                    }

                    // 同一スタンプ（black, alpha=a01）をN回 SrcOver で重ねた結果:
                    // outA = 1 - (1-a)^N
                    // RGBは黒なのでAだけで決まる。
                    double outA;
                    if (baseShapeMode == BaseShapeMode.PaperOnly)
                    {
                        // 紙目＋圧力＋減衰（ただし理想円のN回SrcOver合成は使わない）
                        outA = Math.Clamp(a01, 0.0, 1.0);
                    }
                    else
                    {
                        outA = 1.0 - Math.Pow(1.0 - Math.Clamp(a01, 0.0, 1.0), nEff);
                    }
                    if (paperNoiseApplyStage == PaperNoiseApplyStage.PostComposite)
                    {
                        outA *= kApplied;
                        outA = Math.Clamp(outA, 0.0, 1.0);
                    }

                    if (paperMaskMode != PaperMaskMode.None)
                    {
                        outA *= paperMask01;
                    }

                    if (paperCapMode == PaperCapMode.CapOutAlpha && noise != null)
                    {
                        // 紙目で上限を決める（出っ張りほど上限が高い）
                        var cap01 = Math.Clamp(paperMask01 * paperCapGain, 0.0, 1.0);
                        if (outA > cap01) outA = cap01;
                    }

                    var localCutoff = alphaCutoff01;
                    if (noiseDependentCutoff)
                    {
                        localCutoff *= kApplied;
                    }
                    if (localCutoff > 0 && outA < localCutoff)
                    {
                        continue;
                    }
                    var a8 = (byte)Math.Clamp((int)Math.Round(outA * 255.0), 0, 255);
                    if (a8 == 0) continue;

                    bitmap.SetPixel(x, y, new SkiaSharp.SKColor(0, 0, 0, a8));
                }
            }
        }

        return bitmap;
    }

    public static double[] RenderOutAlpha01(int canvasSizePx, int diameterPx, double pressure, int stampCount, PaperNoise? noise, double paperNoiseStrength, double paperNoiseScale, double paperNoiseOffsetX, double paperNoiseOffsetY, NormalizedFalloffLut? falloffLut)
    {
        return RenderOutAlpha01(canvasSizePx, diameterPx, pressure, stampCount, noise, paperNoiseStrength, paperNoiseScale, paperNoiseOffsetX, paperNoiseOffsetY, paperNoiseGain: 0.2, paperNoiseLowFreqScale: 4.0, paperNoiseLowFreqMix: 0.0, paperNoiseApplyMode: PaperNoiseApplyMode.Alpha, falloffLut, disableKMeanNormalization: false, paperNoiseApplyStage: PaperNoiseApplyStage.PreComposite, alphaCutoff01: 0.0, noiseDependentCutoff: false, paperMaskMode: PaperMaskMode.None, paperMaskThreshold01: 0.5, paperMaskGain: 1.0, paperMaskFalloffMode: PaperMaskFalloffMode.None, baseShapeMode: BaseShapeMode.IdealCircle, paperOnlyFalloffMode: PaperOnlyFalloffMode.None, paperOnlyRadiusThNorm: 1.0, paperCapMode: PaperCapMode.None, paperCapGain: 1.0, out _);
    }

    public readonly struct OutA01Parts
    {
        public readonly double[] OutABase01;
        public readonly double[] OutAMasked01;
        public readonly double[] PaperMask01;
        public readonly double[] FalloffF01;

        public OutA01Parts(double[] outABase01, double[] outAMasked01, double[] paperMask01, double[] falloffF01)
        {
            OutABase01 = outABase01;
            OutAMasked01 = outAMasked01;
            PaperMask01 = paperMask01;
            FalloffF01 = falloffF01;
        }
    }

    public static OutA01Parts RenderOutAlpha01Parts(
        int canvasSizePx,
        int diameterPx,
        double pressure,
        int stampCount,
        PaperNoise? noise,
        double paperNoiseStrength,
        double paperNoiseScale,
        double paperNoiseOffsetX,
        double paperNoiseOffsetY,
        double paperNoiseGain,
        double paperNoiseLowFreqScale,
        double paperNoiseLowFreqMix,
        PaperNoiseApplyMode paperNoiseApplyMode,
        NormalizedFalloffLut? falloffLut,
        bool disableKMeanNormalization,
        PaperNoiseApplyStage paperNoiseApplyStage,
        double alphaCutoff01,
        bool noiseDependentCutoff,
        PaperMaskMode paperMaskMode,
        double paperMaskThreshold01,
        double paperMaskGain,
        PaperMaskFalloffMode paperMaskFalloffMode,
        BaseShapeMode baseShapeMode,
        PaperOnlyFalloffMode paperOnlyFalloffMode,
        double paperOnlyRadiusThNorm)
    {
        var outABase = new double[canvasSizePx * canvasSizePx];
        var outAMasked = new double[canvasSizePx * canvasSizePx];
        var mask01 = new double[canvasSizePx * canvasSizePx];
        var f01 = new double[canvasSizePx * canvasSizePx];

        if (canvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(canvasSizePx));
        if (diameterPx <= 0) throw new ArgumentOutOfRangeException(nameof(diameterPx));
        if (stampCount <= 0) throw new ArgumentOutOfRangeException(nameof(stampCount));
        if (paperNoiseStrength < 0 || paperNoiseStrength > 1) throw new ArgumentOutOfRangeException(nameof(paperNoiseStrength));
        if (paperNoiseScale <= 0) throw new ArgumentOutOfRangeException(nameof(paperNoiseScale));
        if (paperNoiseGain < 0) throw new ArgumentOutOfRangeException(nameof(paperNoiseGain));

        var pFloor = PencilPressureFloorTable.GetPFloor(diameterPx);
        if (pressure <= pFloor)
        {
            return new OutA01Parts(outABase, outAMasked, mask01, f01);
        }

        var falloff = UwpS200P1RadialFalloff;
        var radiusPx = diameterPx * 0.5;
        var cx = (canvasSizePx - 1) * 0.5;
        var cy = (canvasSizePx - 1) * 0.5;
        var pScale = Math.Clamp(pressure, 0.0, 1.0);

        // kの平均を先に計測して、平均1に再正規化する
        var kMeanNorm = 1.0;
        if (noise != null && paperNoiseStrength > 0 && paperNoiseGain > 0)
        {
            double kSum0 = 0;
            long kCount0 = 0;

            for (var y0 = 0; y0 < canvasSizePx; y0++)
            {
                var dy0 = y0 - cy;
                for (var x0 = 0; x0 < canvasSizePx; x0++)
                {
                    var dx0 = x0 - cx;
                    var dist0 = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
                    if (dist0 > radiusPx) continue;

                    var mean0 = noise.Mean01;
                    var std0 = noise.Stddev01;
                    if (std0 <= 0) continue;

                    var nx0 = ((x0 + 0.5) + paperNoiseOffsetX) / paperNoiseScale;
                    var ny0 = ((y0 + 0.5) + paperNoiseOffsetY) / paperNoiseScale;
                    var n010 = noise.Sample01Mixed(nx0, ny0, paperNoiseLowFreqScale, paperNoiseLowFreqMix);
                    var z0 = (n010 - mean0) / std0;
                    z0 = Math.Clamp(z0, -3.0, 3.0);

                    var k0 = 1.0 + (paperNoiseStrength * paperNoiseGain) * z0;
                    k0 = Math.Clamp(k0, 0.5, 1.5);
                    kSum0 += k0;
                    kCount0++;
                }
            }

            if (!disableKMeanNormalization && kCount0 > 0)
            {
                kMeanNorm = kSum0 / kCount0;
                if (double.IsNaN(kMeanNorm) || double.IsInfinity(kMeanNorm) || kMeanNorm <= 0)
                {
                    kMeanNorm = 1.0;
                }
            }
        }

        for (var y = 0; y < canvasSizePx; y++)
        {
            var dy = y - cy;
            for (var x = 0; x < canvasSizePx; x++)
            {
                var dx = x - cx;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radiusPx)
                {
                    continue;
                }

                var rNorm = dist * (200.0 / diameterPx);
                double f;
                if (baseShapeMode == BaseShapeMode.IdealCircle)
                {
                    if (falloffLut != null)
                    {
                        f = falloffLut.Eval(rNorm);
                    }
                    else
                    {
                        var r = (int)Math.Floor(rNorm);
                        if ((uint)r >= (uint)falloff.Length)
                        {
                            continue;
                        }
                        f = falloff[r];
                    }
                }
                else
                {
                    if (paperOnlyFalloffMode == PaperOnlyFalloffMode.RadiusThreshold)
                    {
                        var th = Math.Clamp(paperOnlyRadiusThNorm, 0.0, 1.0);
                        f = (dist <= radiusPx * th) ? 1.0 : 0.0;
                    }
                    else
                    {
                        f = 1.0;
                    }
                }

                var a01 = f * pScale;
                var nEff = (double)stampCount;
                var kApplied = 1.0;
                var paperMask01 = 1.0;
                if (noise != null)
                {
                    var nx = ((x + 0.5) + paperNoiseOffsetX) / paperNoiseScale;
                    var ny = ((y + 0.5) + paperNoiseOffsetY) / paperNoiseScale;
                    var n01 = noise.Sample01Mixed(nx, ny, paperNoiseLowFreqScale, paperNoiseLowFreqMix);
                    var mean = noise.Mean01;
                    var std = noise.Stddev01;
                    if (std > 0)
                    {
                        var falloffWeight = 1.0;
                        var thresholdAdj = paperMaskThreshold01;
                        if (paperMaskFalloffMode == PaperMaskFalloffMode.StrongerAtEdge)
                        {
                            var denom = Math.Max(0.15, Math.Clamp(f, 0.0, 1.0));
                            falloffWeight = Math.Clamp(1.0 / denom, 1.0, 6.0);
                        }
                        else if (paperMaskFalloffMode == PaperMaskFalloffMode.ThresholdAtEdge)
                        {
                            var edge = 1.0 - Math.Clamp(f, 0.0, 1.0);
                            thresholdAdj = Math.Clamp(paperMaskThreshold01 + 0.35 * edge, 0.0, 1.0);
                        }

                        paperMask01 = ComputePaperMask01(paperMaskMode, thresholdAdj, paperMaskGain, falloffWeight, n01, mean, std);

                        var z = (n01 - mean) / std;
                        z = Math.Clamp(z, -3.0, 3.0);

                        kApplied = 1.0 + (paperNoiseStrength * paperNoiseGain) * z;
                        kApplied = Math.Clamp(kApplied, 0.5, 1.5);
                        kApplied /= kMeanNorm;
                        if (paperNoiseApplyStage == PaperNoiseApplyStage.PreComposite)
                        {
                            if (paperNoiseApplyMode == PaperNoiseApplyMode.StampCount)
                            {
                                nEff *= kApplied;
                            }
                            else
                            {
                                a01 *= kApplied;
                            }
                        }
                    }
                }

                if (a01 <= 0)
                {
                    continue;
                }

                var outA0 = 1.0 - Math.Pow(1.0 - Math.Clamp(a01, 0.0, 1.0), nEff);
                if (baseShapeMode == BaseShapeMode.PaperOnly)
                {
                    outA0 = Math.Clamp(a01, 0.0, 1.0);
                }
                if (paperNoiseApplyStage == PaperNoiseApplyStage.PostComposite)
                {
                    outA0 *= kApplied;
                    if (outA0 < 0) outA0 = 0;
                    if (outA0 > 1) outA0 = 1;
                }

                var outA1 = outA0;
                if (paperMaskMode != PaperMaskMode.None)
                {
                    outA1 *= paperMask01;
                    outA1 = Math.Clamp(outA1, 0.0, 1.0);
                }

                var localCutoff = alphaCutoff01;
                if (noiseDependentCutoff)
                {
                    localCutoff *= kApplied;
                }
                if (localCutoff > 0)
                {
                    if (outA0 < localCutoff) outA0 = 0.0;
                    if (outA1 < localCutoff) outA1 = 0.0;
                }

                var idx = y * canvasSizePx + x;
                outABase[idx] = outA0;
                outAMasked[idx] = outA1;
                mask01[idx] = paperMaskMode == PaperMaskMode.None ? 1.0 : paperMask01;
                f01[idx] = f;
            }
        }

        return new OutA01Parts(outABase, outAMasked, mask01, f01);
    }

    public static double[] RenderOutAlpha01(int canvasSizePx, int diameterPx, double pressure, int stampCount, PaperNoise? noise, double paperNoiseStrength, double paperNoiseScale, double paperNoiseOffsetX, double paperNoiseOffsetY, double paperNoiseGain, double paperNoiseLowFreqScale, double paperNoiseLowFreqMix, PaperNoiseApplyMode paperNoiseApplyMode, NormalizedFalloffLut? falloffLut, bool disableKMeanNormalization, PaperNoiseApplyStage paperNoiseApplyStage, double alphaCutoff01, bool noiseDependentCutoff, PaperMaskMode paperMaskMode, double paperMaskThreshold01, double paperMaskGain, PaperMaskFalloffMode paperMaskFalloffMode, BaseShapeMode baseShapeMode, PaperOnlyFalloffMode paperOnlyFalloffMode, double paperOnlyRadiusThNorm, PaperCapMode paperCapMode, double paperCapGain, out (double kMin, double kMax, double kMean, double kStddev, double kMeanNorm) kStats)
    {
        if (canvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(canvasSizePx));
        if (diameterPx <= 0) throw new ArgumentOutOfRangeException(nameof(diameterPx));
        if (stampCount <= 0) throw new ArgumentOutOfRangeException(nameof(stampCount));
        if (paperNoiseStrength < 0 || paperNoiseStrength > 1) throw new ArgumentOutOfRangeException(nameof(paperNoiseStrength));
        if (paperNoiseScale <= 0) throw new ArgumentOutOfRangeException(nameof(paperNoiseScale));
        if (paperNoiseGain < 0) throw new ArgumentOutOfRangeException(nameof(paperNoiseGain));

        var pFloor = PencilPressureFloorTable.GetPFloor(diameterPx);
        var outAlpha = new double[canvasSizePx * canvasSizePx];
        kStats = (kMin: 1.0, kMax: 1.0, kMean: 1.0, kStddev: 0.0, kMeanNorm: 1.0);
        if (pressure <= pFloor)
        {
            return outAlpha;
        }

        var falloff = UwpS200P1RadialFalloff;
        var radiusPx = diameterPx * 0.5;
        var cx = (canvasSizePx - 1) * 0.5;
        var cy = (canvasSizePx - 1) * 0.5;
        var pScale = Math.Clamp(pressure, 0.0, 1.0);

        // kの平均を先に計測して、平均1に再正規化する
        var kMeanNorm = 1.0;
        if (noise != null && paperNoiseStrength > 0 && paperNoiseGain > 0)
        {
            double kSum0 = 0;
            long kCount0 = 0;

            for (var y0 = 0; y0 < canvasSizePx; y0++)
            {
                var dy0 = y0 - cy;
                for (var x0 = 0; x0 < canvasSizePx; x0++)
                {
                    var dx0 = x0 - cx;
                    var dist0 = Math.Sqrt(dx0 * dx0 + dy0 * dy0);
                    if (dist0 > radiusPx) continue;

                    var mean0 = noise.Mean01;
                    var std0 = noise.Stddev01;
                    if (std0 <= 0) continue;

                    var nx0 = ((x0 + 0.5) + paperNoiseOffsetX) / paperNoiseScale;
                    var ny0 = ((y0 + 0.5) + paperNoiseOffsetY) / paperNoiseScale;
                    var n010 = noise.Sample01Mixed(nx0, ny0, paperNoiseLowFreqScale, paperNoiseLowFreqMix);
                    var z0 = (n010 - mean0) / std0;
                    z0 = Math.Clamp(z0, -3.0, 3.0);

                    var k0 = 1.0 + (paperNoiseStrength * paperNoiseGain) * z0;
                    k0 = Math.Clamp(k0, 0.5, 1.5);
                    kSum0 += k0;
                    kCount0++;
                }
            }

        if (disableKMeanNormalization)
        {
            kMeanNorm = 1.0;
        }

            if (kCount0 > 0)
            {
                kMeanNorm = kSum0 / kCount0;
                if (double.IsNaN(kMeanNorm) || double.IsInfinity(kMeanNorm) || kMeanNorm <= 0)
                {
                    kMeanNorm = 1.0;
                }
            }
        }

        double kSum = 0;
        double kSumSq = 0;
        double kMin = 1.0;
        double kMax = 1.0;
        long kCount = 0;

        for (var y = 0; y < canvasSizePx; y++)
        {
            var dy = y - cy;
            for (var x = 0; x < canvasSizePx; x++)
            {
                var dx = x - cx;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radiusPx)
                {
                    continue;
                }

                var rNorm = dist * (200.0 / diameterPx);
                double f;
                if (baseShapeMode == BaseShapeMode.IdealCircle)
                {
                    if (falloffLut != null)
                    {
                        f = falloffLut.Eval(rNorm);
                    }
                    else
                    {
                        var r = (int)Math.Floor(rNorm);
                        if ((uint)r >= (uint)falloff.Length)
                        {
                            continue;
                        }
                        f = falloff[r];
                    }
                }
                else
                {
                    f = 1.0;
                }

                var a01 = f * pScale;
                var nEff = (double)stampCount;
                var kApplied = 1.0;
                var paperMask01 = 1.0;
                if (noise != null)
                {
                    var nx = ((x + 0.5) + paperNoiseOffsetX) / paperNoiseScale;
                    var ny = ((y + 0.5) + paperNoiseOffsetY) / paperNoiseScale;
                    var n01 = noise.Sample01Mixed(nx, ny, paperNoiseLowFreqScale, paperNoiseLowFreqMix);
                    var mean = noise.Mean01;
                    var std = noise.Stddev01;
                    if (std > 0)
                    {
                        var falloffWeight = 1.0;
                        var thresholdAdj = paperMaskThreshold01;
                        if (paperMaskFalloffMode == PaperMaskFalloffMode.StrongerAtEdge)
                        {
                            var denom = Math.Max(0.15, Math.Clamp(f, 0.0, 1.0));
                            falloffWeight = Math.Clamp(1.0 / denom, 1.0, 6.0);
                        }
                        else if (paperMaskFalloffMode == PaperMaskFalloffMode.ThresholdAtEdge)
                        {
                            var edge = 1.0 - Math.Clamp(f, 0.0, 1.0);
                            thresholdAdj = Math.Clamp(paperMaskThreshold01 + 0.35 * edge, 0.0, 1.0);
                        }

                        paperMask01 = ComputePaperMask01(paperMaskMode, thresholdAdj, paperMaskGain, falloffWeight, n01, mean, std);

                        // 平均1.0を維持するため、ノイズを中心化して対称に揺らす
                        var z = (n01 - mean) / std;
                        z = Math.Clamp(z, -3.0, 3.0);

                        kApplied = 1.0 + (paperNoiseStrength * paperNoiseGain) * z;
                        kApplied = Math.Clamp(kApplied, 0.5, 1.5);
                        kApplied /= kMeanNorm;
                        if (paperNoiseApplyStage == PaperNoiseApplyStage.PreComposite)
                        {
                            if (paperNoiseApplyMode == PaperNoiseApplyMode.StampCount)
                            {
                                nEff *= kApplied;
                            }
                            else
                            {
                                a01 *= kApplied;
                            }
                        }
                    }
                }

                kMin = Math.Min(kMin, kApplied);
                kMax = Math.Max(kMax, kApplied);
                kSum += kApplied;
                kSumSq += kApplied * kApplied;
                kCount++;

                if (a01 <= 0)
                {
                    continue;
                }

                var outA = 1.0 - Math.Pow(1.0 - Math.Clamp(a01, 0.0, 1.0), nEff);
                if (baseShapeMode == BaseShapeMode.PaperOnly)
                {
                    outA = Math.Clamp(a01, 0.0, 1.0);
                }
                if (paperNoiseApplyStage == PaperNoiseApplyStage.PostComposite)
                {
                    outA *= kApplied;
                    if (outA < 0) outA = 0;
                    if (outA > 1) outA = 1;
                }

                if (paperMaskMode != PaperMaskMode.None)
                {
                    outA *= paperMask01;
                    outA = Math.Clamp(outA, 0.0, 1.0);
                }

                if (paperCapMode == PaperCapMode.CapOutAlpha && noise != null)
                {
                    var cap01 = Math.Clamp(paperMask01 * paperCapGain, 0.0, 1.0);
                    if (outA > cap01) outA = cap01;
                }

                var localCutoff = alphaCutoff01;
                if (noiseDependentCutoff)
                {
                    localCutoff *= kApplied;
                }
                if (localCutoff > 0 && outA < localCutoff)
                {
                    outA = 0.0;
                }
                outAlpha[y * canvasSizePx + x] = outA;
            }
        }

        if (kCount > 0)
        {
            var kMean = kSum / kCount;
            var kVar = (kSumSq / kCount) - (kMean * kMean);
            if (kVar < 0) kVar = 0;
            kStats = (kMin, kMax, kMean, Math.Sqrt(kVar), kMeanNorm);
        }

        return outAlpha;
    }

    // UWP観測: Sample/Compair/CSV/radial-falloff-S200-P1-N1.csv
    // r=0..100 は非ゼロ。101以降は0。
    private static readonly double[] UwpS200P1RadialFalloff =
    {
        0.59901961,
        0.5995098,
        0.58392157,
        0.58490196,
        0.58627451,
        0.58321078,
        0.59046346,
        0.5800905,
        0.57941176,
        0.57509804,
        0.58102653,
        0.57254902,
        0.57693947,
        0.57414861,
        0.57219608,
        0.56875,
        0.57654902,
        0.56652142,
        0.56138763,
        0.56619048,
        0.5573975,
        0.56063577,
        0.56196655,
        0.56268908,
        0.56064751,
        0.55856553,
        0.55829747,
        0.55350763,
        0.55437756,
        0.55029838,
        0.54779912,
        0.54946175,
        0.54832202,
        0.54926951,
        0.54498705,
        0.54643665,
        0.54369977,
        0.54281581,
        0.53468338,
        0.52985125,
        0.52633484,
        0.52224736,
        0.51613191,
        0.50832643,
        0.50610329,
        0.49650819,
        0.49514006,
        0.49016846,
        0.4838901,
        0.48121775,
        0.47453401,
        0.47039725,
        0.46426351,
        0.4566204,
        0.45246914,
        0.44421679,
        0.44035948,
        0.4339151,
        0.42723426,
        0.42031464,
        0.41534572,
        0.40685323,
        0.39751436,
        0.39297059,
        0.39076534,
        0.38006231,
        0.37214367,
        0.36341396,
        0.3571926,
        0.34988688,
        0.34307135,
        0.33111171,
        0.31950554,
        0.31073179,
        0.30041447,
        0.28816115,
        0.28232714,
        0.26532759,
        0.25377255,
        0.24512195,
        0.23145425,
        0.22474892,
        0.21261039,
        0.19555053,
        0.18767507,
        0.16905499,
        0.160999,
        0.14869048,
        0.13434277,
        0.12364066,
        0.10901261,
        0.09764966,
        0.08128465,
        0.06729303,
        0.05820673,
        0.04565732,
        0.03358324,
        0.02559618,
        0.01611638,
        0.00959428,
        0.0018728,
        0.0,
    };
}
