using SkiaSharp;
using System;

namespace DotTester.Helpers;

public static class DotReproRenderer
{
    public enum OutAlphaModel
    {
        // 既存モデル: baseA01=f*P に対して、紙目由来のkを乗算（またはAddAlpha）してoutAを作る
        MultiplyK,

        // 実験モデル: 紙目を高さH=n01、壁wall=1-Hとみなし、baseA01が壁を超えた分だけ出す
        // threshold = 1 - clamp(baseA01 * WallBaseScale, 0..1) + WallThresholdBias
        // v = clamp((n01 - threshold) / WallK, 0..1)
        WallThrough,
    }

    public enum PaperNoiseApplyMode
    {
        MultiplyAlpha,
        AddAlpha,
    }

    public sealed class PointEvaluator
    {
        private readonly Options _opt;
        private readonly int _canvas;
        private readonly int _diameter;
        private readonly double _effectiveRadiusPx;
        private readonly double _cx;
        private readonly double _cy;
        private readonly double _kMin;
        private readonly double _kMax;
        private readonly bool _enableZClamp;
        private readonly double _zClampNegAbs;
        private readonly double _zClampPosAbs;
        private readonly double _kMean;
        private readonly double _falloffRNormScale;
        private readonly double _falloffGamma;
        private readonly bool _enableEdgeBoost;
        private readonly double _edgeBoost;
        private readonly double _edgeBoostGamma;
        private readonly double _wallK;
        private readonly double _wallBaseScale;
        private readonly double _wallThresholdBias;

        internal PointEvaluator(
            Options opt,
            int canvas,
            int diameter,
            double effectiveRadiusPx,
            double cx,
            double cy,
            double kMin,
            double kMax,
            bool enableZClamp,
            double zClampNegAbs,
            double zClampPosAbs,
            double kMean,
            double falloffRNormScale,
            double falloffGamma,
            bool enableEdgeBoost,
            double edgeBoost,
            double edgeBoostGamma,
            double wallK,
            double wallBaseScale,
            double wallThresholdBias)
        {
            _opt = opt;
            _canvas = canvas;
            _diameter = diameter;
            _effectiveRadiusPx = effectiveRadiusPx;
            _cx = cx;
            _cy = cy;
            _kMin = kMin;
            _kMax = kMax;
            _enableZClamp = enableZClamp;
            _zClampNegAbs = zClampNegAbs;
            _zClampPosAbs = zClampPosAbs;
            _kMean = kMean;
            _falloffRNormScale = falloffRNormScale;
            _falloffGamma = falloffGamma;
            _enableEdgeBoost = enableEdgeBoost;
            _edgeBoost = edgeBoost;
            _edgeBoostGamma = edgeBoostGamma;
            _wallK = wallK;
            _wallBaseScale = wallBaseScale;
            _wallThresholdBias = wallThresholdBias;
        }

        public byte EvalAlphaByte(int x, int y)
        {
            if ((uint)x >= (uint)_canvas) throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)_canvas) throw new ArgumentOutOfRangeException(nameof(y));

            // ピクセル中心(0.5, 1.5, ...)で距離を測る。
            var dx = (x + 0.5) - _cx;
            var dy = (y + 0.5) - _cy;
            var dist = Math.Sqrt((dx * dx) + (dy * dy));
            if (dist > _effectiveRadiusPx)
            {
                return 0;
            }

            // rNorm: S200基準の0..100
            var rNorm = dist * (200.0 / _diameter);
            rNorm *= _falloffRNormScale;

            var f = 1.0;
            if (_opt.FalloffLut != null)
            {
                f = _opt.FalloffLut.Eval(rNorm);
            }
            f = Math.Pow(Math.Clamp(f, 0.0, 1.0), _falloffGamma);
            f *= _opt.FalloffScale;
            f = Math.Clamp(f, 0.0, 1.0);

            var a01 = f * _opt.Pressure;

            var k = 1.0;
            var paperMask01 = 1.0;
            if (_opt.UsePaperNoise && _opt.NoiseTile != null)
            {
                var nx = ((x + 0.5) + _opt.PaperNoiseOffsetX) / _opt.PaperNoiseScale;
                var ny = ((y + 0.5) + _opt.PaperNoiseOffsetY) / _opt.PaperNoiseScale;

                var n01 = _opt.NoiseTile.SampleAlpha01(nx, ny, _opt.NoiseSamplingMode);
                n01 = Math.Clamp(n01, 0.0, 1.0);

                var mean01 = _opt.NoiseTile.Mean01;
                if (!double.IsFinite(mean01) || mean01 <= 0)
                {
                    mean01 = 1.0;
                }

                double ComputeEdgeBoostScale(double distPx)
                {
                    if (!_enableEdgeBoost || _edgeBoost <= 0) return 1.0;
                    if (!double.IsFinite(_effectiveRadiusPx) || _effectiveRadiusPx <= 0) return 1.0;

                    var edge01 = distPx / _effectiveRadiusPx;
                    if (!double.IsFinite(edge01)) return 1.0;
                    edge01 = Math.Clamp(edge01, 0.0, 1.0);
                    return 1.0 + _edgeBoost * Math.Pow(edge01, _edgeBoostGamma);
                }

                if (_opt.OutAlphaModel == OutAlphaModel.WallThrough)
                {
                    // 壁貫通モデル: 紙目の寄与をStrengthで調整できるよう、n01を平均へブレンドする。
                    // - Strength=0 で n01=mean（紙目なし相当）、Strength=1 で n01をそのまま使う。
                    // - EdgeBoostをONにすると中心より外縁の方が強くなる。

                    var strength = _opt.PaperNoiseStrength * ComputeEdgeBoostScale(dist);
                    if (!double.IsFinite(strength)) strength = 0.0;
                    strength = Math.Clamp(strength, 0.0, 1.0);

                    var nEff = mean01 + (n01 - mean01) * strength;
                    nEff = Math.Clamp(nEff, 0.0, 1.0);

                    var denom = _wallK;
                    if (!double.IsFinite(denom) || denom <= 0)
                    {
                        denom = 1.0;
                    }

                    var baseA = a01;
                    var baseScale = _wallBaseScale;
                    if (!double.IsFinite(baseScale) || baseScale <= 0) baseScale = 1.0;
                    baseA = Math.Clamp(baseA * baseScale, 0.0, 1.0);

                    var bias = _wallThresholdBias;
                    if (!double.IsFinite(bias)) bias = 0.0;

                    var threshold = Math.Clamp(1.0 - baseA + bias, 0.0, 1.0);
                    a01 = Math.Clamp((nEff - threshold) / denom, 0.0, 1.0);
                    k = 1.0;
                }

                // PaperMask（外縁フォールオフ込み）
                if (_opt.PaperMaskMode != PaperMaskMode.None)
                {
                    var std01 = _opt.NoiseTile.Stddev01;
                    if (double.IsFinite(std01) && std01 > 0)
                    {
                        var falloffWeight = 1.0;
                        var thresholdAdj = _opt.PaperMaskThreshold01;
                        if (_opt.PaperMaskFalloffMode == PaperMaskFalloffMode.StrongerAtEdge)
                        {
                            var denom = Math.Max(0.15, Math.Clamp(f, 0.0, 1.0));
                            falloffWeight = Math.Clamp(1.0 / denom, 1.0, 6.0);
                        }
                        else if (_opt.PaperMaskFalloffMode == PaperMaskFalloffMode.ThresholdAtEdge)
                        {
                            var edge = 1.0 - Math.Clamp(f, 0.0, 1.0);
                            thresholdAdj = Math.Clamp(_opt.PaperMaskThreshold01 + 0.35 * edge, 0.0, 1.0);
                        }

                        paperMask01 = ComputePaperMask01(
                            _opt.PaperMaskMode,
                            thresholdAdj,
                            _opt.PaperMaskGain,
                            falloffWeight,
                            n01,
                            mean01,
                            std01,
                            _enableZClamp,
                            _zClampNegAbs,
                            _zClampPosAbs);
                    }
                }

                if (_opt.OutAlphaModel != OutAlphaModel.WallThrough && _opt.KMode == KDefinition.ZNormalized)
                {
                    var std01 = _opt.NoiseTile.Stddev01;
                    if (double.IsFinite(std01) && std01 > 0)
                    {
                        var z = (n01 - mean01) / std01;
                        if (_enableZClamp)
                        {
                            z = Math.Clamp(z, -_zClampNegAbs, _zClampPosAbs);
                        }

                        var sg = (_opt.PaperNoiseStrength * ComputeEdgeBoostScale(dist)) * _opt.PaperNoiseGain;
                        k = 1.0 + sg * z;
                        k = Math.Clamp(k, _kMin, _kMax);
                        k /= _kMean;
                    }
                else if (_opt.OutAlphaModel != OutAlphaModel.WallThrough)
                    {
                        k = 1.0;
                    }
                }
                else
                {
                    var ratio = n01 / mean01;
                    if (!double.IsFinite(ratio) || ratio < 0) ratio = 0;

                    var g = _opt.PaperNoiseGain;
                    if (!double.IsFinite(g) || g < 0) g = 0;

                    double k0;
                    if (_opt.KMode == KDefinition.Direct01)
                    {
                        k0 = (g > 0) ? Math.Pow(n01, g) : n01;
                    }
                    else
                    {
                        k0 = (g > 0) ? Math.Pow(ratio, g) : ratio;
                    }

                    if (_opt.KMode == KDefinition.BlendToMean1)
                    {
                        var strength = _opt.PaperNoiseStrength * ComputeEdgeBoostScale(dist);
                        strength = Math.Clamp(strength, 0.0, 1.0);
                        k = (1.0 - strength) + strength * k0;
                    }
                    else
                    {
                        k = k0;
                    }

                    k = Math.Clamp(k, _kMin, _kMax);
                }

                if (_opt.OutAlphaModel != OutAlphaModel.WallThrough && _opt.PaperNoiseApplyMode == PaperNoiseApplyMode.AddAlpha)
                {
                    var f0 = _opt.FalloffLut != null ? _opt.FalloffLut.Eval(0) : 1.0;
                    f0 = Math.Pow(Math.Clamp(f0, 0.0, 1.0), _falloffGamma);
                    var aRef = Math.Clamp(f0 * _opt.FalloffScale * _opt.Pressure, 0.0, 1.0);
                    a01 = Math.Clamp(a01 + aRef * (k - 1.0), 0.0, 1.0);
                }
                else if (_opt.OutAlphaModel != OutAlphaModel.WallThrough)
                {
                    a01 *= k;
                }
            }

            var outA01 = Math.Clamp(a01, 0.0, 1.0);
            if (_opt.PaperMaskMode != PaperMaskMode.None)
            {
                outA01 *= paperMask01;
            }
            if (outA01 <= 0)
            {
                return 0;
            }

            var cutoff = _opt.AlphaCutoff01;
            if (_opt.NoiseDependentCutoff)
            {
                if (double.IsFinite(k) && k > 0)
                {
                    cutoff *= k;
                }
            }
            if (cutoff > 0 && outA01 < cutoff)
            {
                return 0;
            }

            return (byte)Math.Clamp((int)Math.Round(outA01 * 255.0, MidpointRounding.AwayFromZero), 0, 255);
        }
    }

    public static PointEvaluator CreatePointEvaluator(Options opt, int kMeanStridePx = 8)
    {
        ArgumentNullException.ThrowIfNull(opt);

        if (opt.CanvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(opt.CanvasSizePx));
        if (opt.DiameterPx <= 0) throw new ArgumentOutOfRangeException(nameof(opt.DiameterPx));
        if (!double.IsFinite(opt.Pressure) || opt.Pressure < 0 || opt.Pressure > 1.0) throw new ArgumentOutOfRangeException(nameof(opt.Pressure));
        if (!double.IsFinite(opt.FalloffScale) || opt.FalloffScale < 0) throw new ArgumentOutOfRangeException(nameof(opt.FalloffScale));

        if (opt.UsePaperNoise)
        {
            if (opt.NoiseTile == null) throw new ArgumentException("UsePaperNoise=true ですが NoiseTile が null です。", nameof(opt));
            if (!double.IsFinite(opt.PaperNoiseScale) || opt.PaperNoiseScale <= 0) throw new ArgumentOutOfRangeException(nameof(opt.PaperNoiseScale));
            if (!double.IsFinite(opt.PaperNoiseStrength) || opt.PaperNoiseStrength < 0 || opt.PaperNoiseStrength > 1) throw new ArgumentOutOfRangeException(nameof(opt.PaperNoiseStrength));
            if (!double.IsFinite(opt.PaperNoiseGain) || opt.PaperNoiseGain < 0) throw new ArgumentOutOfRangeException(nameof(opt.PaperNoiseGain));
        }

        var canvas = opt.CanvasSizePx;
        var diameter = opt.DiameterPx;
        var radiusPx = diameter * 0.5;
        var effectiveRadiusPx = radiusPx + Math.Max(0.0, opt.RadiusPadPx);

        var falloffRNormScale = opt.FalloffRNormScale;
        if (!double.IsFinite(falloffRNormScale) || falloffRNormScale <= 0)
        {
            falloffRNormScale = 1.0;
        }

        var falloffGamma = opt.FalloffGamma;
        if (!double.IsFinite(falloffGamma) || falloffGamma <= 0)
        {
            falloffGamma = 1.0;
        }

        var cx = (canvas - 1) * 0.5;
        var cy = (canvas - 1) * 0.5;

        var kMin = opt.KClampMin;
        var kMax = opt.KClampMax;
        if (!double.IsFinite(kMin) || !double.IsFinite(kMax) || kMin <= 0 || kMax <= 0 || kMax < kMin)
        {
            kMin = 0.0001;
            kMax = 1.5;
        }

        var enableZClamp = opt.EnablePaperNoiseZClamp;
        var zClampAbs = opt.PaperNoiseZClampAbs;
        if (!double.IsFinite(zClampAbs) || zClampAbs <= 0) zClampAbs = 7.0;

        var zClampNegAbs = opt.PaperNoiseZClampNegAbs;
        if (!double.IsFinite(zClampNegAbs) || zClampNegAbs <= 0) zClampNegAbs = zClampAbs;

        var zClampPosAbs = opt.PaperNoiseZClampPosAbs;
        if (!double.IsFinite(zClampPosAbs) || zClampPosAbs <= 0) zClampPosAbs = zClampAbs;

        var enableEdgeBoost = opt.EnablePaperNoiseEdgeBoost;
        var edgeBoost = opt.PaperNoiseEdgeBoost;
        if (!double.IsFinite(edgeBoost) || edgeBoost < 0) edgeBoost = 0.0;

        var edgeBoostGamma = opt.PaperNoiseEdgeBoostGamma;
        if (!double.IsFinite(edgeBoostGamma) || edgeBoostGamma <= 0) edgeBoostGamma = 1.0;

        var wallK = opt.WallK;
        if (!double.IsFinite(wallK) || wallK <= 0)
        {
            wallK = 1.0;
        }

        var wallBaseScale = opt.WallBaseScale;
        if (!double.IsFinite(wallBaseScale) || wallBaseScale <= 0)
        {
            wallBaseScale = 1.0;
        }

        var wallThresholdBias = opt.WallThresholdBias;
        if (!double.IsFinite(wallThresholdBias))
        {
            wallThresholdBias = 0.0;
        }

        var stride = kMeanStridePx;
        if (stride <= 0) stride = 1;

        // SkiaTester互換: kMean（必要な場合のみ）。探索時はstrideで粗くできるようにする。
        var kMean = 1.0;
        if (opt.UsePaperNoise
            && opt.NoiseTile != null
            && opt.KMode == KDefinition.ZNormalized
            && !opt.DisableKMeanNormalization
            && opt.PaperNoiseStrength > 0
            && opt.PaperNoiseGain > 0
            && opt.OutAlphaModel != OutAlphaModel.WallThrough)
        {
            var mean01 = opt.NoiseTile.Mean01;
            var std01 = opt.NoiseTile.Stddev01;
            if (!double.IsFinite(mean01) || mean01 <= 0) mean01 = 1.0;

            if (double.IsFinite(std01) && std01 > 0)
            {
                double ComputeEdgeBoostScale(double distPx)
                {
                    if (!enableEdgeBoost || edgeBoost <= 0) return 1.0;
                    if (!double.IsFinite(effectiveRadiusPx) || effectiveRadiusPx <= 0) return 1.0;

                    var edge01 = distPx / effectiveRadiusPx;
                    if (!double.IsFinite(edge01)) return 1.0;
                    edge01 = Math.Clamp(edge01, 0.0, 1.0);
                    return 1.0 + edgeBoost * Math.Pow(edge01, edgeBoostGamma);
                }

                double kSum = 0;
                long kCount = 0;

                for (var y = 0; y < canvas; y += stride)
                {
                    var dy = (y + 0.5) - cy;
                    for (var x = 0; x < canvas; x += stride)
                    {
                        var dx = (x + 0.5) - cx;
                        var dist = Math.Sqrt((dx * dx) + (dy * dy));
                        if (dist > effectiveRadiusPx) continue;

                        var sg = (opt.PaperNoiseStrength * ComputeEdgeBoostScale(dist)) * opt.PaperNoiseGain;

                        var nx = ((x + 0.5) + opt.PaperNoiseOffsetX) / opt.PaperNoiseScale;
                        var ny = ((y + 0.5) + opt.PaperNoiseOffsetY) / opt.PaperNoiseScale;
                        var n01 = opt.NoiseTile.SampleAlpha01(nx, ny, opt.NoiseSamplingMode);
                        n01 = Math.Clamp(n01, 0.0, 1.0);

                        var z = (n01 - mean01) / std01;
                        if (enableZClamp)
                        {
                            z = Math.Clamp(z, -zClampNegAbs, zClampPosAbs);
                        }

                        var k = 1.0 + sg * z;
                        k = Math.Clamp(k, kMin, kMax);
                        kSum += k;
                        kCount++;
                    }
                }

                if (kCount > 0)
                {
                    kMean = kSum / kCount;
                    if (!double.IsFinite(kMean) || kMean <= 0) kMean = 1.0;
                }
            }
        }

        return new PointEvaluator(
            opt,
            canvas,
            diameter,
            effectiveRadiusPx,
            cx,
            cy,
            kMin,
            kMax,
            enableZClamp,
            zClampNegAbs,
            zClampPosAbs,
            kMean,
            falloffRNormScale,
            falloffGamma,
            enableEdgeBoost,
            edgeBoost,
            edgeBoostGamma,
            wallK,
            wallBaseScale,
            wallThresholdBias);
    }

    private static double ComputePaperMask01(
        PaperMaskMode mode,
        double threshold01,
        double gain,
        double falloffWeight,
        double n01,
        double noiseMean01,
        double noiseStd01,
        bool enableZClamp,
        double zClampNegAbs,
        double zClampPosAbs)
    {
        if (mode == PaperMaskMode.None) return 1.0;
        if (gain <= 0) return 1.0;
        if (noiseStd01 <= 0) return 1.0;

        // 既存のk生成と同様にzへ正規化してから使う。
        // 「出っ張りが光る」イメージに近づけるため、zを0..1に落としてマスクにする。
        var z = (n01 - noiseMean01) / noiseStd01;
        if (enableZClamp)
        {
            z = Math.Clamp(z, -zClampNegAbs, zClampPosAbs);
        }

        // z=-zClampNegAbs..+zClampPosAbs => t=0..1
        var zSpan = zClampNegAbs + zClampPosAbs;
        if (!double.IsFinite(zSpan) || zSpan <= 0)
        {
            return 1.0;
        }
        var t = (z + zClampNegAbs) / zSpan;

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

    public enum KDefinition
    {
        /// <summary>
        /// C) k = n01
        /// </summary>
        Direct01,

        /// <summary>
        /// A) k = n01 / mean01
        /// </summary>
        RatioToMean,

        /// <summary>
        /// B) k = (1-strength) + strength * (n01 / mean01)
        /// </summary>
        BlendToMean1,

        /// <summary>
        /// UWP/Skia互換) z正規化: z=(n01-mean01)/stddev01, k=1+(strength*gain)*z
        /// </summary>
        ZNormalized,
    }

    public enum PaperMaskMode
    {
        None,
        MultiplyOutAlpha,
        SoftOutAlpha,
        ThresholdOutAlpha,
    }

    public enum PaperMaskFalloffMode
    {
        None,
        StrongerAtEdge,
        ThresholdAtEdge,
    }

    public sealed record Options(
        int CanvasSizePx,
        int DiameterPx,
        double Pressure,
        NormalizedFalloffLut? FalloffLut,
        double FalloffScale,
        double FalloffRNormScale,
        double FalloffGamma,
        double RadiusPadPx,
        PaperNoiseTile? NoiseTile,
        bool UsePaperNoise,
        PaperNoiseTile.SamplingMode NoiseSamplingMode,
        double PaperNoiseScale,
        double PaperNoiseOffsetX,
        double PaperNoiseOffsetY,
        double PaperNoiseStrength,
        double PaperNoiseGain,
        KDefinition KMode,
        PaperNoiseApplyMode PaperNoiseApplyMode,
        OutAlphaModel OutAlphaModel,
        double WallK,
        double WallBaseScale,
        double WallThresholdBias,
        double KClampMin,
        double KClampMax,
        double AlphaCutoff01,
        bool NoiseDependentCutoff,
        bool DisableKMeanNormalization = false,
        bool EnablePaperNoiseZClamp = true,
        double PaperNoiseZClampAbs = 7.0,
        double PaperNoiseZClampNegAbs = 0.0,
        double PaperNoiseZClampPosAbs = 0.0,
        bool EnablePaperNoiseEdgeBoost = false,
        double PaperNoiseEdgeBoost = 0.0,
        double PaperNoiseEdgeBoostGamma = 1.0,
        PaperMaskMode PaperMaskMode = PaperMaskMode.None,
        double PaperMaskThreshold01 = 0.5,
        double PaperMaskGain = 1.0,
        PaperMaskFalloffMode PaperMaskFalloffMode = PaperMaskFalloffMode.None)
    {
        public static Options CreateDefault() => new(
            CanvasSizePx: 2020,
            DiameterPx: 2000,
            Pressure: 1.0,
            FalloffLut: null,
            FalloffScale: 1.0,
            FalloffRNormScale: 1.0,
            FalloffGamma: 1.0,
            RadiusPadPx: 0.0,
            NoiseTile: null,
            UsePaperNoise: false,
            NoiseSamplingMode: PaperNoiseTile.SamplingMode.Bilinear,
            PaperNoiseScale: 1.0,
            PaperNoiseOffsetX: 0.0,
            PaperNoiseOffsetY: 0.0,
            PaperNoiseStrength: 0.07,
            PaperNoiseGain: 1.0,
            KMode: KDefinition.RatioToMean,
            PaperNoiseApplyMode: PaperNoiseApplyMode.MultiplyAlpha,
            OutAlphaModel: OutAlphaModel.MultiplyK,
            WallK: 0.08,
            WallBaseScale: 1.0,
            WallThresholdBias: 0.0,
            KClampMin: 0.0001,
            KClampMax: 1.5,
            AlphaCutoff01: 0.0,
            NoiseDependentCutoff: false,
            DisableKMeanNormalization: false,
            EnablePaperNoiseZClamp: true,
            PaperNoiseZClampAbs: 7.0,
            PaperNoiseZClampNegAbs: 0.0,
            PaperNoiseZClampPosAbs: 0.0,
            EnablePaperNoiseEdgeBoost: false,
            PaperNoiseEdgeBoost: 0.0,
            PaperNoiseEdgeBoostGamma: 1.0,
            PaperMaskMode: PaperMaskMode.None,
            PaperMaskThreshold01: 0.5,
            PaperMaskGain: 1.0,
            PaperMaskFalloffMode: PaperMaskFalloffMode.None);
    }

    public static SKBitmap Render(Options opt)
    {
        ArgumentNullException.ThrowIfNull(opt);

        if (opt.CanvasSizePx <= 0) throw new ArgumentOutOfRangeException(nameof(opt.CanvasSizePx));
        if (opt.DiameterPx <= 0) throw new ArgumentOutOfRangeException(nameof(opt.DiameterPx));
        if (!double.IsFinite(opt.Pressure) || opt.Pressure < 0 || opt.Pressure > 1.0) throw new ArgumentOutOfRangeException(nameof(opt.Pressure));
        if (!double.IsFinite(opt.FalloffScale) || opt.FalloffScale < 0) throw new ArgumentOutOfRangeException(nameof(opt.FalloffScale));

        if (opt.PaperMaskMode != PaperMaskMode.None)
        {
            if (!double.IsFinite(opt.PaperMaskThreshold01) || opt.PaperMaskThreshold01 < 0 || opt.PaperMaskThreshold01 > 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(opt.PaperMaskThreshold01));
            }
            if (!double.IsFinite(opt.PaperMaskGain) || opt.PaperMaskGain < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(opt.PaperMaskGain));
            }
        }

        if (opt.UsePaperNoise)
        {
            if (opt.NoiseTile == null) throw new ArgumentException("UsePaperNoise=true ですが NoiseTile が null です。", nameof(opt));
            if (!double.IsFinite(opt.PaperNoiseScale) || opt.PaperNoiseScale <= 0) throw new ArgumentOutOfRangeException(nameof(opt.PaperNoiseScale));
            if (!double.IsFinite(opt.PaperNoiseStrength) || opt.PaperNoiseStrength < 0 || opt.PaperNoiseStrength > 1) throw new ArgumentOutOfRangeException(nameof(opt.PaperNoiseStrength));
            if (!double.IsFinite(opt.PaperNoiseGain) || opt.PaperNoiseGain < 0) throw new ArgumentOutOfRangeException(nameof(opt.PaperNoiseGain));
        }

        var canvas = opt.CanvasSizePx;
        var diameter = opt.DiameterPx;
        var radiusPx = diameter * 0.5;
        var effectiveRadiusPx = radiusPx + Math.Max(0.0, opt.RadiusPadPx);

        var falloffRNormScale = opt.FalloffRNormScale;
        if (!double.IsFinite(falloffRNormScale) || falloffRNormScale <= 0)
        {
            falloffRNormScale = 1.0;
        }

        var falloffGamma = opt.FalloffGamma;
        if (!double.IsFinite(falloffGamma) || falloffGamma <= 0)
        {
            falloffGamma = 1.0;
        }

        var cx = (canvas - 1) * 0.5;
        var cy = (canvas - 1) * 0.5;

        var kMin = opt.KClampMin;
        var kMax = opt.KClampMax;
        if (!double.IsFinite(kMin) || !double.IsFinite(kMax) || kMin <= 0 || kMax <= 0 || kMax < kMin)
        {
            kMin = 0.0001;
            kMax = 1.5;
        }

        var enableZClamp = opt.EnablePaperNoiseZClamp;
        var zClampAbs = opt.PaperNoiseZClampAbs;
        if (!double.IsFinite(zClampAbs) || zClampAbs <= 0) zClampAbs = 7.0;

        // 互換のため、neg/posが未指定(<=0)の場合はAbsを採用する。
        var zClampNegAbs = opt.PaperNoiseZClampNegAbs;
        if (!double.IsFinite(zClampNegAbs) || zClampNegAbs <= 0) zClampNegAbs = zClampAbs;

        var zClampPosAbs = opt.PaperNoiseZClampPosAbs;
        if (!double.IsFinite(zClampPosAbs) || zClampPosAbs <= 0) zClampPosAbs = zClampAbs;

        var enableEdgeBoost = opt.EnablePaperNoiseEdgeBoost;
        var edgeBoost = opt.PaperNoiseEdgeBoost;
        if (!double.IsFinite(edgeBoost) || edgeBoost < 0)
        {
            edgeBoost = 0.0;
        }
        var edgeBoostGamma = opt.PaperNoiseEdgeBoostGamma;
        if (!double.IsFinite(edgeBoostGamma) || edgeBoostGamma <= 0)
        {
            edgeBoostGamma = 1.0;
        }

        double ComputeEdgeBoostScale(double distPx)
        {
            if (!enableEdgeBoost || edgeBoost <= 0) return 1.0;
            if (!double.IsFinite(effectiveRadiusPx) || effectiveRadiusPx <= 0) return 1.0;

            var edge01 = distPx / effectiveRadiusPx;
            if (!double.IsFinite(edge01)) return 1.0;
            edge01 = Math.Clamp(edge01, 0.0, 1.0);

            // 中心=1、外縁で 1+edgeBoost になるようにする。
            return 1.0 + edgeBoost * Math.Pow(edge01, edgeBoostGamma);
        }

        // SkiaTester互換: kの平均を半径内で計測し、k/=kMean で平均1へ再正規化する（任意）
        var kMean = 1.0;
        if (opt.UsePaperNoise
            && opt.NoiseTile != null
            && opt.KMode == KDefinition.ZNormalized
            && !opt.DisableKMeanNormalization
            && opt.PaperNoiseStrength > 0
            && opt.PaperNoiseGain > 0
            && opt.OutAlphaModel != OutAlphaModel.WallThrough)
        {
            var mean01 = opt.NoiseTile.Mean01;
            var std01 = opt.NoiseTile.Stddev01;
            if (!double.IsFinite(mean01) || mean01 <= 0) mean01 = 1.0;

            // enableZClamp=false の場合はzクランプを行わず、kClampでのみ抑制する。

            if (double.IsFinite(std01) && std01 > 0)
            {
                double kSum = 0;
                long kCount = 0;

                for (var y = 0; y < canvas; y++)
                {
                    var dy = (y + 0.5) - cy;
                    for (var x = 0; x < canvas; x++)
                    {
                        var dx = (x + 0.5) - cx;
                        var dist = Math.Sqrt((dx * dx) + (dy * dy));
                        if (dist > effectiveRadiusPx) continue;

                            var sg = (opt.PaperNoiseStrength * ComputeEdgeBoostScale(dist)) * opt.PaperNoiseGain;

                        var nx = ((x + 0.5) + opt.PaperNoiseOffsetX) / opt.PaperNoiseScale;
                        var ny = ((y + 0.5) + opt.PaperNoiseOffsetY) / opt.PaperNoiseScale;
                        var n01 = opt.NoiseTile.SampleAlpha01(nx, ny, opt.NoiseSamplingMode);
                        n01 = Math.Clamp(n01, 0.0, 1.0);

                        var z = (n01 - mean01) / std01;
                        if (enableZClamp)
                        {
                            z = Math.Clamp(z, -zClampNegAbs, zClampPosAbs);
                        }

                        var k = 1.0 + sg * z;
                        k = Math.Clamp(k, kMin, kMax);
                        kSum += k;
                        kCount++;
                    }
                }

                if (kCount > 0)
                {
                    kMean = kSum / kCount;
                    if (!double.IsFinite(kMean) || kMean <= 0) kMean = 1.0;
                }
            }
        }

        // 量子化前のoutA(0..1)
        var outA = new double[canvas * canvas];

        for (var y = 0; y < canvas; y++)
        {
            // ピクセル中心(0.5, 1.5, ...)で距離を測る。
            // falloff CSVの観測系列（dx_pxが整数で増える）と合わせやすくするため。
            var dy = (y + 0.5) - cy;
            for (var x = 0; x < canvas; x++)
            {
                var dx = (x + 0.5) - cx;
                var dist = Math.Sqrt((dx * dx) + (dy * dy));
                if (dist > effectiveRadiusPx)
                {
                    continue;
                }

                // rNorm: S200基準の0..100
                var rNorm = dist * (200.0 / diameter);
                rNorm *= falloffRNormScale;

                var f = 1.0;
                if (opt.FalloffLut != null)
                {
                    f = opt.FalloffLut.Eval(rNorm);
                }
                f = Math.Pow(Math.Clamp(f, 0.0, 1.0), falloffGamma);
                f *= opt.FalloffScale;
                f = Math.Clamp(f, 0.0, 1.0);

                var a01 = f * opt.Pressure;

                var k = 1.0;
                var paperMask01 = 1.0;
                if (opt.UsePaperNoise && opt.NoiseTile != null)
                {
                    // ノイズ座標（ワールド固定）
                    // NOTE: UWP側の既知仕様に合わせ、offsetの向きはUIの説明に従う。
                    var nx = ((x + 0.5) + opt.PaperNoiseOffsetX) / opt.PaperNoiseScale;
                    var ny = ((y + 0.5) + opt.PaperNoiseOffsetY) / opt.PaperNoiseScale;

                    var n01 = opt.NoiseTile.SampleAlpha01(nx, ny, opt.NoiseSamplingMode);
                    n01 = Math.Clamp(n01, 0.0, 1.0);

                    var mean01 = opt.NoiseTile.Mean01;
                    if (!double.IsFinite(mean01) || mean01 <= 0)
                    {
                        mean01 = 1.0;
                    }

                    // PaperMask（外縁フォールオフ込み）
                    if (opt.PaperMaskMode != PaperMaskMode.None)
                    {
                        var std01 = opt.NoiseTile.Stddev01;
                        if (double.IsFinite(std01) && std01 > 0)
                        {
                            var falloffWeight = 1.0;
                            var thresholdAdj = opt.PaperMaskThreshold01;
                            if (opt.PaperMaskFalloffMode == PaperMaskFalloffMode.StrongerAtEdge)
                            {
                                // 外縁ほど強く：SoftOutAlphaの立ち上がり(gain)を外側で急にする
                                var denom = Math.Max(0.15, Math.Clamp(f, 0.0, 1.0));
                                falloffWeight = Math.Clamp(1.0 / denom, 1.0, 6.0);
                            }
                            else if (opt.PaperMaskFalloffMode == PaperMaskFalloffMode.ThresholdAtEdge)
                            {
                                // 外縁ほど厳しく：SoftOutAlphaの床(threshold)を外側で上げる
                                // f=1 => +0, f=0 => +0.35（クランプ）
                                var edge = 1.0 - Math.Clamp(f, 0.0, 1.0);
                                thresholdAdj = Math.Clamp(opt.PaperMaskThreshold01 + 0.35 * edge, 0.0, 1.0);
                            }

                            paperMask01 = ComputePaperMask01(
                                opt.PaperMaskMode,
                                thresholdAdj,
                                opt.PaperMaskGain,
                                falloffWeight,
                                n01,
                                mean01,
                                std01,
                                enableZClamp,
                                zClampNegAbs,
                                zClampPosAbs);
                        }
                    }

                    // 壁貫通モデル: baseA01（ノイズ適用前）と紙目高さから直接outAを作る。
                    if (opt.OutAlphaModel == OutAlphaModel.WallThrough)
                    {
                        // Strengthで紙目の寄与を調整できるよう、n01を平均へブレンドする。
                        // - Strength=0 で n01=mean（紙目なし相当）、Strength=1 で n01をそのまま使う。
                        // - EdgeBoostをONにすると中心より外縁の方が強くなる。
                        var strength = opt.PaperNoiseStrength * ComputeEdgeBoostScale(dist);
                        if (!double.IsFinite(strength)) strength = 0.0;
                        strength = Math.Clamp(strength, 0.0, 1.0);

                        var nEff = mean01 + (n01 - mean01) * strength;
                        nEff = Math.Clamp(nEff, 0.0, 1.0);

                        var denom = opt.WallK;
                        if (!double.IsFinite(denom) || denom <= 0)
                        {
                            denom = 1.0;
                        }

                        var baseScale = opt.WallBaseScale;
                        if (!double.IsFinite(baseScale) || baseScale <= 0)
                        {
                            baseScale = 1.0;
                        }

                        var bias = opt.WallThresholdBias;
                        if (!double.IsFinite(bias))
                        {
                            bias = 0.0;
                        }

                        var baseA = Math.Clamp(a01 * baseScale, 0.0, 1.0);
                        var threshold = Math.Clamp(1.0 - baseA + bias, 0.0, 1.0);
                        a01 = Math.Clamp((nEff - threshold) / denom, 0.0, 1.0);
                        k = 1.0;
                    }

                    if (opt.OutAlphaModel != OutAlphaModel.WallThrough && opt.KMode == KDefinition.ZNormalized)
                    {
                        // UWP/Skia互換: z正規化 + clamp +（任意で）kMean再正規化
                        var std01 = opt.NoiseTile.Stddev01;
                        if (double.IsFinite(std01) && std01 > 0)
                        {
                            var z = (n01 - mean01) / std01;
                            if (enableZClamp)
                            {
                                z = Math.Clamp(z, -zClampNegAbs, zClampPosAbs);
                            }

                            var sg = (opt.PaperNoiseStrength * ComputeEdgeBoostScale(dist)) * opt.PaperNoiseGain;
                            k = 1.0 + sg * z;
                            k = Math.Clamp(k, kMin, kMax);
                            k /= kMean;
                        }
                    else if (opt.OutAlphaModel != OutAlphaModel.WallThrough)
                        {
                            k = 1.0;
                        }
                    }
                    else
                    {

                        // 旧挙動: タイル値から直接kを構成する。
                        // - A: k = n01/mean
                        // - B: k = (1-strength) + strength*(n01/mean)
                        // - C: k = n01
                        var ratio = n01 / mean01;
                        if (!double.IsFinite(ratio) || ratio < 0) ratio = 0;

                        // Gainは「強調（ガンマ）」として扱う（z正規化は使わない）
                        var g = opt.PaperNoiseGain;
                        if (!double.IsFinite(g) || g < 0) g = 0;

                        double k0;
                        if (opt.KMode == KDefinition.Direct01)
                        {
                            k0 = (g > 0) ? Math.Pow(n01, g) : n01;
                        }
                        else
                        {
                            k0 = (g > 0) ? Math.Pow(ratio, g) : ratio;
                        }

                        if (opt.KMode == KDefinition.BlendToMean1)
                        {
                            var strength = opt.PaperNoiseStrength * ComputeEdgeBoostScale(dist);
                            strength = Math.Clamp(strength, 0.0, 1.0);
                            k = (1.0 - strength) + strength * k0;
                        }
                        else
                        {
                            k = k0;
                        }

                        k = Math.Clamp(k, kMin, kMax);
                    }

                    if (opt.OutAlphaModel != OutAlphaModel.WallThrough && opt.PaperNoiseApplyMode == PaperNoiseApplyMode.AddAlpha)
                    {
                        // 外縁でa01が小さすぎて紙目が量子化で見えなくなる場合があるので、加算モデルも試せるようにする。
                        // aRefは中心(f(0))のベースαを採用し、半径によらず紙目の絶対差分を出しやすくする。
                        var f0 = opt.FalloffLut != null ? opt.FalloffLut.Eval(0) : 1.0;
                        f0 = Math.Pow(Math.Clamp(f0, 0.0, 1.0), falloffGamma);
                        var aRef = Math.Clamp(f0 * opt.FalloffScale * opt.Pressure, 0.0, 1.0);

                        a01 = Math.Clamp(a01 + aRef * (k - 1.0), 0.0, 1.0);
                    }
                    else if (opt.OutAlphaModel != OutAlphaModel.WallThrough)
                    {
                        a01 *= k;
                    }
                }

                var outA01 = Math.Clamp(a01, 0.0, 1.0);
                if (opt.PaperMaskMode != PaperMaskMode.None)
                {
                    outA01 *= paperMask01;
                }
                if (outA01 <= 0)
                {
                    continue;
                }

                var cutoff = opt.AlphaCutoff01;
                if (opt.NoiseDependentCutoff)
                {
                    // SkiaTester互換: 谷(k<1)ほどカットオフを緩くし、山(k>1)ほど厳しくする。
                    // kはクランプ済みで0より大きい想定だが、念のため防御する。
                    if (double.IsFinite(k) && k > 0)
                    {
                        cutoff *= k;
                    }
                }
                if (cutoff > 0 && outA01 < cutoff)
                {
                    continue;
                }

                outA[(y * canvas) + x] = outA01;
            }
        }

        // 最後に1回だけ8bit化
        var bmp = new SKBitmap(canvas, canvas, SKColorType.Bgra8888, SKAlphaType.Premul);
        bmp.Erase(SKColors.Transparent);

        var pixels = bmp.Pixels;
        for (var i = 0; i < outA.Length; i++)
        {
            var a8 = (byte)Math.Clamp((int)Math.Round(outA[i] * 255.0, MidpointRounding.AwayFromZero), 0, 255);
            if (a8 == 0) continue;
            pixels[i] = new SKColor(0, 0, 0, a8);
        }
        bmp.Pixels = pixels;

        return bmp;
    }
}
