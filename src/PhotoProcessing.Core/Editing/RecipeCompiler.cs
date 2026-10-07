using PhotoProcessing.Core.Color;
using PhotoProcessing.Core.Darktable;
using M = PhotoProcessing.Core.Darktable.Modules;

namespace PhotoProcessing.Core.Editing;

/// <summary>Facts about the raw captured when its job is created.</summary>
public sealed record BaselineInfo(
    double AsShotX,
    double AsShotY,
    int SensorWidth,
    int SensorHeight)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public WhiteBalance.TempTint AsShot => WhiteBalance.FromXy(AsShotX, AsShotY);
}

/// <summary>
/// Turns a <see cref="Recipe"/> into a darktable history stack: modules darktable already
/// auto-applied (exposure, sigmoid, color calibration) are patched in place, everything else is
/// appended as a new history entry.
/// </summary>
public static class RecipeCompiler
{
    /// <summary>The recipe that reproduces the baseline render exactly.</summary>
    public static Recipe FromBaseline(DarktableXmp baseline, BaselineInfo info)
    {
        var exposure = Params(baseline, M.Exposure.Op, M.Exposure.Size);
        var sigmoid = Params(baseline, M.Sigmoid.Op, M.Sigmoid.Size);
        var asShot = info.AsShot;

        return new Recipe
        {
            Exposure = new ExposureSettings
            {
                Ev = Round(exposure.GetFloat(M.Exposure.ExposureEv)),
                BlackLevel = Round(exposure.GetFloat(M.Exposure.Black), 6),
            },
            WhiteBalance = new WhiteBalanceSettings
            {
                TemperatureK = Math.Round(asShot.Kelvin),
                Tint = Round(asShot.Tint, 2),
            },
            Tone = new ToneSettings
            {
                Contrast = Round(sigmoid.GetFloat(M.Sigmoid.Contrast)),
                Skew = Round(sigmoid.GetFloat(M.Sigmoid.Skew)),
                TargetBlackPct = Round(sigmoid.GetFloat(M.Sigmoid.BlackTarget), 5),
                TargetWhitePct = Round(sigmoid.GetFloat(M.Sigmoid.WhiteTarget)),
                PreserveHuePct = Round(sigmoid.GetFloat(M.Sigmoid.HuePreservation)),
            },
        };
    }

    public static List<HistoryEntry> Compile(DarktableXmp baseline, BaselineInfo info, Recipe recipe)
    {
        var history = baseline.History.ToList();
        var template = history.Last(h => h.Operation == M.Exposure.Op);

        // exposure
        Patch(history, M.Exposure.Op, M.Exposure.Size, p =>
        {
            p.SetFloat(M.Exposure.ExposureEv, (float)recipe.Exposure.Ev);
            p.SetFloat(M.Exposure.Black, (float)recipe.Exposure.BlackLevel);
        });

        // white balance: only leave darktable's as-shot illuminant when the recipe moved away from it,
        // so an untouched recipe renders bit-identical to the baseline.
        var asShot = WhiteBalance.FromXy(info.AsShotX, info.AsShotY);
        var wb = recipe.WhiteBalance;
        if (Math.Abs(wb.TemperatureK - Math.Round(asShot.Kelvin)) > 0.5 || Math.Abs(wb.Tint - Round(asShot.Tint, 2)) > 0.005)
        {
            var (x, y) = WhiteBalance.ToXy(wb.TemperatureK, wb.Tint);
            Patch(history, M.ChannelMixerRgb.Op, M.ChannelMixerRgb.Size, p =>
            {
                p.SetInt(M.ChannelMixerRgb.Illuminant, M.ChannelMixerRgb.IlluminantCustom);
                p.SetFloat(M.ChannelMixerRgb.X, (float)x);
                p.SetFloat(M.ChannelMixerRgb.Y, (float)y);
                p.SetFloat(M.ChannelMixerRgb.Temperature, (float)wb.TemperatureK);
            });
        }

        // tone mapper
        Patch(history, M.Sigmoid.Op, M.Sigmoid.Size, p =>
        {
            p.SetFloat(M.Sigmoid.Contrast, (float)recipe.Tone.Contrast);
            p.SetFloat(M.Sigmoid.Skew, (float)recipe.Tone.Skew);
            p.SetFloat(M.Sigmoid.BlackTarget, (float)recipe.Tone.TargetBlackPct);
            p.SetFloat(M.Sigmoid.WhiteTarget, (float)recipe.Tone.TargetWhitePct);
            p.SetFloat(M.Sigmoid.HuePreservation, (float)recipe.Tone.PreserveHuePct);
        });

        if (recipe.ShadowsHighlights.Enabled)
        {
            var p = M.ToneEqualizer.Defaults();
            var bands = recipe.ShadowsHighlights.Bands();
            for (var i = 0; i < M.ToneEqualizer.BandCount; i++)
                p.SetFloat(M.ToneEqualizer.FirstBand + i * 4, (float)bands[i]);
            history.Add(NewEntry(template, M.ToneEqualizer.Op, M.ToneEqualizer.Version, p, M.BlendCst.RgbScene));
        }

        if (recipe.Color.Enabled)
            history.Add(NewEntry(template, M.ColorBalanceRgb.Op, M.ColorBalanceRgb.Version, ColorBalance(recipe.Color), M.BlendCst.RgbScene));

        if (recipe.LocalContrast.Enabled)
        {
            var lc = recipe.LocalContrast;
            var p = M.LocalContrast.Defaults();
            p.SetFloat(M.LocalContrast.Detail, (float)(lc.DetailPct / 100 - 1));
            p.SetFloat(M.LocalContrast.SigmaR, (float)(lc.HighlightsPct / 100));
            p.SetFloat(M.LocalContrast.SigmaS, (float)(lc.ShadowsPct / 100));
            p.SetFloat(M.LocalContrast.Midtone, (float)lc.MidtoneRange);
            history.Add(NewEntry(template, M.LocalContrast.Op, M.LocalContrast.Version, p, M.BlendCst.Lab));
        }

        if (recipe.Denoise.Enabled)
        {
            var p = M.DenoiseProfile.Defaults();
            p.SetFloat(M.DenoiseProfile.Strength, (float)recipe.Denoise.Strength);
            history.Add(NewEntry(template, M.DenoiseProfile.Op, M.DenoiseProfile.Version, p, M.BlendCst.RgbScene));
        }

        if (recipe.Sharpen.Enabled)
        {
            var p = new ParamsBuffer(M.Sharpen.Size);
            p.SetFloat(M.Sharpen.Radius, (float)recipe.Sharpen.Radius);
            p.SetFloat(M.Sharpen.Amount, (float)recipe.Sharpen.Amount);
            p.SetFloat(M.Sharpen.Threshold, (float)recipe.Sharpen.Threshold);
            history.Add(NewEntry(template, M.Sharpen.Op, M.Sharpen.Version, p, M.BlendCst.Lab));
        }

        var geometry = recipe.Geometry;
        if (Math.Abs(geometry.RotationDeg) >= 0.01)
        {
            var p = M.RotatePerspective.Create((float)geometry.RotationDeg, info.SensorWidth, info.SensorHeight);
            history.Add(NewEntry(template, M.RotatePerspective.Op, M.RotatePerspective.Version, p, M.BlendCst.None));
        }

        if (!geometry.Crop.IsFull)
        {
            var c = geometry.Crop;
            var p = M.Crop.Create((float)c.Left, (float)c.Top, (float)c.Right, (float)c.Bottom);
            history.Add(NewEntry(template, M.Crop.Op, M.Crop.Version, p, M.BlendCst.None));
        }

        return history;
    }

    private static ParamsBuffer ColorBalance(ColorSettings c)
    {
        static float Pct(double v) => (float)(v / 100);
        var p = M.ColorBalanceRgb.Defaults();
        p.SetFloat(M.ColorBalanceRgb.Vibrance, Pct(c.Vibrance));
        p.SetFloat(M.ColorBalanceRgb.Contrast, Pct(c.Contrast));
        p.SetFloat(M.ColorBalanceRgb.HueAngle, (float)c.HueShiftDeg);

        p.SetFloat(M.ColorBalanceRgb.ChromaGlobal, Pct(c.Chroma.Global));
        p.SetFloat(M.ColorBalanceRgb.ChromaShadows, Pct(c.Chroma.Shadows));
        p.SetFloat(M.ColorBalanceRgb.ChromaMidtones, Pct(c.Chroma.Midtones));
        p.SetFloat(M.ColorBalanceRgb.ChromaHighlights, Pct(c.Chroma.Highlights));

        p.SetFloat(M.ColorBalanceRgb.SaturationGlobal, Pct(c.Saturation.Global));
        p.SetFloat(M.ColorBalanceRgb.SaturationShadows, Pct(c.Saturation.Shadows));
        p.SetFloat(M.ColorBalanceRgb.SaturationMidtones, Pct(c.Saturation.Midtones));
        p.SetFloat(M.ColorBalanceRgb.SaturationHighlights, Pct(c.Saturation.Highlights));

        p.SetFloat(M.ColorBalanceRgb.BrillianceGlobal, Pct(c.Brilliance.Global));
        p.SetFloat(M.ColorBalanceRgb.BrillianceShadows, Pct(c.Brilliance.Shadows));
        p.SetFloat(M.ColorBalanceRgb.BrillianceMidtones, Pct(c.Brilliance.Midtones));
        p.SetFloat(M.ColorBalanceRgb.BrillianceHighlights, Pct(c.Brilliance.Highlights));

        void Wheel(ColorWheel w, int yOffset, int cOffset, int hOffset)
        {
            p.SetFloat(yOffset, Pct(w.Luminance));
            p.SetFloat(cOffset, Pct(w.Chroma));
            p.SetFloat(hOffset, (float)w.HueDeg);
        }

        Wheel(c.Grading.ShadowsLift, M.ColorBalanceRgb.ShadowsY, M.ColorBalanceRgb.ShadowsC, M.ColorBalanceRgb.ShadowsH);
        Wheel(c.Grading.MidtonesPower, M.ColorBalanceRgb.MidtonesY, M.ColorBalanceRgb.MidtonesC, M.ColorBalanceRgb.MidtonesH);
        Wheel(c.Grading.HighlightsGain, M.ColorBalanceRgb.HighlightsY, M.ColorBalanceRgb.HighlightsC, M.ColorBalanceRgb.HighlightsH);
        Wheel(c.Grading.GlobalOffset, M.ColorBalanceRgb.GlobalY, M.ColorBalanceRgb.GlobalC, M.ColorBalanceRgb.GlobalH);
        return p;
    }

    private static ParamsBuffer Params(DarktableXmp baseline, string op, int size)
    {
        var entry = baseline.History.LastOrDefault(h => h.Operation == op)
            ?? throw new InvalidOperationException(
                $"baseline history has no '{op}' entry; is darktable's workflow still 'scene-referred (sigmoid)'?");
        if (entry.Params.Length != size)
            throw new InvalidOperationException(
                $"'{op}' params are {entry.Params.Length} bytes, expected {size}: darktable's module version changed (now v{entry.ModVersion})");
        return new ParamsBuffer(entry.Params);
    }

    private static void Patch(List<HistoryEntry> history, string op, int size, Action<ParamsBuffer> edit)
    {
        var index = history.FindLastIndex(h => h.Operation == op);
        if (index < 0)
            throw new InvalidOperationException($"baseline history has no '{op}' entry");
        var entry = history[index];
        if (entry.Params.Length != size)
            throw new InvalidOperationException(
                $"'{op}' params are {entry.Params.Length} bytes, expected {size}: darktable's module version changed (now v{entry.ModVersion})");

        var p = new ParamsBuffer(entry.Params);
        edit(p);
        history[index] = entry with { Params = p.ToArray() };
    }

    private static HistoryEntry NewEntry(HistoryEntry blendTemplate, string op, int version, ParamsBuffer p, int blendCst)
    {
        var blend = new ParamsBuffer(ParamsCodec.Decode(blendTemplate.BlendopParams));
        blend.SetInt(4, blendCst); // dt_develop_blend_params_t.blend_cst
        return new HistoryEntry(op, true, version, p.ToArray(), "", 0,
            blendTemplate.BlendopVersion, ParamsCodec.Encode(blend.ToArray()));
    }

    private static double Round(double value, int digits = 3) => Math.Round(value, digits);
}
