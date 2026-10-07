using System.Text.Json;
using PhotoProcessing.Core.Color;
using PhotoProcessing.Core.Darktable;
using PhotoProcessing.Core.Editing;
using M = PhotoProcessing.Core.Darktable.Modules;

namespace PhotoProcessing.Tests;

public class EditingTests
{
    [Theory]
    [InlineData(2800, 0)]
    [InlineData(4166, 2.97)]
    [InlineData(6500, -5)]
    [InlineData(9000, 10)]
    public void White_balance_round_trips(double kelvin, double tint)
    {
        var (x, y) = WhiteBalance.ToXy(kelvin, tint);
        var back = WhiteBalance.FromXy(x, y);
        Assert.Equal(kelvin, back.Kelvin, 0);
        Assert.Equal(tint, back.Tint, 2);
    }

    [Fact]
    public void D65_is_about_6500K_slightly_green()
    {
        var d65 = WhiteBalance.FromXy(0.3127, 0.3290);
        Assert.InRange(d65.Kelvin, 6450, 6560);
        Assert.InRange(d65.Tint, 2.5, 4); // D65 sits ~0.003 Duv above the Planckian locus
    }

    [Fact]
    public void Untouched_recipe_reproduces_the_baseline()
    {
        var baseline = DarktableTests.Baseline();
        var recipe = RecipeCompiler.FromBaseline(baseline, DarktableTests.Info);
        Assert.Equal(0.7, recipe.Exposure.Ev, 3);
        Assert.Equal(1.5, recipe.Tone.Contrast, 3);
        Assert.Equal(4166, recipe.WhiteBalance.TemperatureK, 0);

        var history = RecipeCompiler.Compile(baseline, DarktableTests.Info, recipe);
        Assert.Equal(baseline.History.Select(h => h.Operation), history.Select(h => h.Operation));
        var cm = M.ChannelMixerRgb.Op;
        Assert.Equal(baseline.History.Single(h => h.Operation == cm).Params, history.Single(h => h.Operation == cm).Params);
    }

    [Fact]
    public void Full_recipe_appends_every_module_with_the_right_layout()
    {
        var baseline = DarktableTests.Baseline();
        var recipe = RecipeCompiler.FromBaseline(baseline, DarktableTests.Info);
        recipe.Exposure.Ev = 1.25;
        recipe.WhiteBalance.TemperatureK = 5000;
        recipe.ShadowsHighlights.Enabled = true;
        recipe.ShadowsHighlights.Shadows = 0.6;
        recipe.Color.Enabled = true;
        recipe.Color.Vibrance = 20;
        recipe.LocalContrast.Enabled = true;
        recipe.Denoise.Enabled = true;
        recipe.Sharpen.Enabled = true;
        recipe.Geometry.RotationDeg = 1.5;
        recipe.Geometry.Crop = new CropRect { Left = 0.1, Top = 0.1, Right = 0.9, Bottom = 0.9 };

        var history = RecipeCompiler.Compile(baseline, DarktableTests.Info, recipe);
        ParamsBuffer P(string op) => new(history.Single(h => h.Operation == op).Params);

        Assert.Equal(1.25f, P(M.Exposure.Op).GetFloat(M.Exposure.ExposureEv), 4);
        Assert.Equal(M.ChannelMixerRgb.IlluminantCustom, P(M.ChannelMixerRgb.Op).GetInt(M.ChannelMixerRgb.Illuminant));
        Assert.Equal(0.6f, P(M.ToneEqualizer.Op).GetFloat(M.ToneEqualizer.FirstBand + 2 * 4), 4);
        Assert.Equal(0.2f, P(M.ColorBalanceRgb.Op).GetFloat(M.ColorBalanceRgb.Vibrance), 4);
        Assert.Equal(0.25f, P(M.LocalContrast.Op).GetFloat(M.LocalContrast.Detail), 4);
        Assert.Equal(-1f, P(M.DenoiseProfile.Op).GetFloat(M.DenoiseProfile.A0));

        foreach (var (op, size) in new[]
                 {
                     (M.ToneEqualizer.Op, M.ToneEqualizer.Size), (M.ColorBalanceRgb.Op, M.ColorBalanceRgb.Size),
                     (M.LocalContrast.Op, M.LocalContrast.Size), (M.DenoiseProfile.Op, M.DenoiseProfile.Size),
                     (M.Sharpen.Op, M.Sharpen.Size), (M.RotatePerspective.Op, M.RotatePerspective.Size), (M.Crop.Op, M.Crop.Size),
                 })
            Assert.Equal(size, P(op).Size);

        // New modules get a valid blendop blob with the blend colorspace patched in.
        var bilatBlend = new ParamsBuffer(ParamsCodec.Decode(history.Single(h => h.Operation == M.LocalContrast.Op).BlendopParams));
        Assert.Equal(M.BlendCst.Lab, bilatBlend.GetInt(4));
    }

    [Fact]
    public void Recipe_rejects_unknown_fields_and_clamps_ranges()
    {
        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<Recipe>("""{ "exposure": { "evv": 1 } }""", Recipe.JsonOptions));

        var recipe = new Recipe();
        recipe.Exposure.Ev = 9;
        recipe.Color.Grading.ShadowsLift.HueDeg = -30;
        var warnings = recipe.Clamp();
        Assert.Equal(4, recipe.Exposure.Ev);
        Assert.Equal(330, recipe.Color.Grading.ShadowsLift.HueDeg);
        Assert.Single(warnings);
    }

    [Fact]
    public void Recipe_tolerates_comments_and_trailing_commas()
    {
        var recipe = JsonSerializer.Deserialize<Recipe>("""
            {
              // brighter
              "exposure": { "ev": 1.2, },
            }
            """, Recipe.JsonOptions)!;
        Assert.Equal(1.2, recipe.Exposure.Ev);
    }
}
