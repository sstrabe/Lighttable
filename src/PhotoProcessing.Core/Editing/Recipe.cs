using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoProcessing.Core.Editing;

/// <summary>
/// The edit Claude controls, in darktable GUI units (percentages where the GUI shows %).
/// <see cref="RecipeCompiler"/> turns it into a darktable history stack.
/// Keep editor/CLAUDE.md in sync when changing fields or ranges.
/// </summary>
public sealed class Recipe
{
    public ExposureSettings Exposure { get; set; } = new();
    public WhiteBalanceSettings WhiteBalance { get; set; } = new();
    public ToneSettings Tone { get; set; } = new();
    public ShadowsHighlightsSettings ShadowsHighlights { get; set; } = new();
    public ColorSettings Color { get; set; } = new();
    public LocalContrastSettings LocalContrast { get; set; } = new();
    public DenoiseSettings Denoise { get; set; } = new();
    public SharpenSettings Sharpen { get; set; } = new();
    public GeometrySettings Geometry { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public static Recipe Load(string path) =>
        JsonSerializer.Deserialize<Recipe>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"{path} is empty");

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions) + "\n");

    public Recipe Clone() => JsonSerializer.Deserialize<Recipe>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>Clamps every value to its supported range and returns a message per clamped value.</summary>
    public List<string> Clamp()
    {
        var warnings = new List<string>();

        double C(string name, double value, double min, double max)
        {
            if (double.IsNaN(value)) { warnings.Add($"{name} was NaN, set to {min}"); return min; }
            if (value >= min && value <= max) return value;
            var clamped = Math.Clamp(value, min, max);
            warnings.Add($"{name}={value} out of range [{min}, {max}], clamped to {clamped}");
            return clamped;
        }

        Exposure.Ev = C("exposure.ev", Exposure.Ev, -3, 4);
        Exposure.BlackLevel = C("exposure.black_level", Exposure.BlackLevel, -0.1, 0.1);

        WhiteBalance.TemperatureK = C("white_balance.temperature_k", WhiteBalance.TemperatureK, 2000, 12000);
        WhiteBalance.Tint = C("white_balance.tint", WhiteBalance.Tint, -30, 30);

        Tone.Contrast = C("tone.contrast", Tone.Contrast, 0.5, 3);
        Tone.Skew = C("tone.skew", Tone.Skew, -1, 1);
        Tone.TargetBlackPct = C("tone.target_black_pct", Tone.TargetBlackPct, 0, 2);
        Tone.TargetWhitePct = C("tone.target_white_pct", Tone.TargetWhitePct, 80, 120);
        Tone.PreserveHuePct = C("tone.preserve_hue_pct", Tone.PreserveHuePct, 0, 100);

        var sh = ShadowsHighlights;
        sh.Blacks = C("shadows_highlights.blacks", sh.Blacks, -2, 2);
        sh.DeepShadows = C("shadows_highlights.deep_shadows", sh.DeepShadows, -2, 2);
        sh.Shadows = C("shadows_highlights.shadows", sh.Shadows, -2, 2);
        sh.LightShadows = C("shadows_highlights.light_shadows", sh.LightShadows, -2, 2);
        sh.Midtones = C("shadows_highlights.midtones", sh.Midtones, -2, 2);
        sh.DarkHighlights = C("shadows_highlights.dark_highlights", sh.DarkHighlights, -2, 2);
        sh.Highlights = C("shadows_highlights.highlights", sh.Highlights, -2, 2);
        sh.Whites = C("shadows_highlights.whites", sh.Whites, -2, 2);
        sh.Speculars = C("shadows_highlights.speculars", sh.Speculars, -2, 2);

        var c = Color;
        c.Vibrance = C("color.vibrance", c.Vibrance, -100, 100);
        c.Contrast = C("color.contrast", c.Contrast, -100, 100);
        c.HueShiftDeg = C("color.hue_shift_deg", c.HueShiftDeg, -180, 180);
        foreach (var (name, set) in new[] { ("chroma", c.Chroma), ("saturation", c.Saturation), ("brilliance", c.Brilliance) })
        {
            set.Global = C($"color.{name}.global", set.Global, -100, 100);
            set.Shadows = C($"color.{name}.shadows", set.Shadows, -100, 100);
            set.Midtones = C($"color.{name}.midtones", set.Midtones, -100, 100);
            set.Highlights = C($"color.{name}.highlights", set.Highlights, -100, 100);
        }

        foreach (var (name, wheel) in new[]
                 {
                     ("shadows_lift", c.Grading.ShadowsLift), ("highlights_gain", c.Grading.HighlightsGain),
                     ("midtones_power", c.Grading.MidtonesPower), ("global_offset", c.Grading.GlobalOffset),
                 })
        {
            wheel.Luminance = C($"color.grading.{name}.luminance", wheel.Luminance, -100, 100);
            wheel.Chroma = C($"color.grading.{name}.chroma", wheel.Chroma, 0, 100);
            wheel.HueDeg = C($"color.grading.{name}.hue_deg", ((wheel.HueDeg % 360) + 360) % 360, 0, 360);
        }

        LocalContrast.DetailPct = C("local_contrast.detail_pct", LocalContrast.DetailPct, 0, 500);
        LocalContrast.HighlightsPct = C("local_contrast.highlights_pct", LocalContrast.HighlightsPct, 0, 100);
        LocalContrast.ShadowsPct = C("local_contrast.shadows_pct", LocalContrast.ShadowsPct, 0, 100);
        LocalContrast.MidtoneRange = C("local_contrast.midtone_range", LocalContrast.MidtoneRange, 0.001, 1);

        Denoise.Strength = C("denoise.strength", Denoise.Strength, 0.001, 10);

        Sharpen.Radius = C("sharpen.radius", Sharpen.Radius, 0, 8);
        Sharpen.Amount = C("sharpen.amount", Sharpen.Amount, 0, 2);
        Sharpen.Threshold = C("sharpen.threshold", Sharpen.Threshold, 0, 100);

        var g = Geometry;
        g.RotationDeg = C("geometry.rotation_deg", g.RotationDeg, -45, 45);
        g.Crop.Left = C("geometry.crop.left", g.Crop.Left, 0, 0.95);
        g.Crop.Top = C("geometry.crop.top", g.Crop.Top, 0, 0.95);
        g.Crop.Right = C("geometry.crop.right", g.Crop.Right, g.Crop.Left + 0.05, 1);
        g.Crop.Bottom = C("geometry.crop.bottom", g.Crop.Bottom, g.Crop.Top + 0.05, 1);

        return warnings;
    }
}

public sealed class ExposureSettings
{
    /// <summary>darktable exposure module, in EV. darktable's scene-referred default is +0.7.</summary>
    public double Ev { get; set; } = 0.7;

    /// <summary>Black level correction; negative lifts blacks, positive deepens them.</summary>
    public double BlackLevel { get; set; }
}

public sealed class WhiteBalanceSettings
{
    public double TemperatureK { get; set; } = 5000;

    /// <summary>Image effect: + more magenta / - greener; 1 unit = 0.001 Duv of the assumed illuminant.</summary>
    public double Tint { get; set; }
}

/// <summary>sigmoid tone mapper (the scene-to-display transform).</summary>
public sealed class ToneSettings
{
    public double Contrast { get; set; } = 1.5;
    public double Skew { get; set; }
    public double TargetBlackPct { get; set; } = 0.0152;
    public double TargetWhitePct { get; set; } = 100;
    public double PreserveHuePct { get; set; } = 100;
}

/// <summary>tone equalizer bands, EV of brightening (+) or darkening (-) per luminance zone.</summary>
public sealed class ShadowsHighlightsSettings
{
    public bool Enabled { get; set; }
    public double Blacks { get; set; }         // -8 EV
    public double DeepShadows { get; set; }    // -7 EV
    public double Shadows { get; set; }        // -6 EV
    public double LightShadows { get; set; }   // -5 EV
    public double Midtones { get; set; }       // -4 EV (scene middle grey)
    public double DarkHighlights { get; set; } // -3 EV
    public double Highlights { get; set; }     // -2 EV
    public double Whites { get; set; }         // -1 EV
    public double Speculars { get; set; }      //  0 EV

    public double[] Bands() =>
        [Blacks, DeepShadows, Shadows, LightShadows, Midtones, DarkHighlights, Highlights, Whites, Speculars];
}

/// <summary>color balance rgb; all values in % as darktable's GUI shows them, hues in degrees.</summary>
public sealed class ColorSettings
{
    public bool Enabled { get; set; }
    public double Vibrance { get; set; }
    public double Contrast { get; set; }
    public ToneZones Chroma { get; set; } = new();
    public ToneZones Saturation { get; set; } = new();
    public ToneZones Brilliance { get; set; } = new();
    public double HueShiftDeg { get; set; }
    public GradingSettings Grading { get; set; } = new();
}

public sealed class ToneZones
{
    public double Global { get; set; }
    public double Shadows { get; set; }
    public double Midtones { get; set; }
    public double Highlights { get; set; }
}

public sealed class GradingSettings
{
    public ColorWheel ShadowsLift { get; set; } = new();
    public ColorWheel HighlightsGain { get; set; } = new();
    public ColorWheel MidtonesPower { get; set; } = new();
    public ColorWheel GlobalOffset { get; set; } = new();
}

public sealed class ColorWheel
{
    public double Luminance { get; set; }
    public double Chroma { get; set; }
    public double HueDeg { get; set; }
}

/// <summary>local contrast (local laplacian), GUI percentages.</summary>
public sealed class LocalContrastSettings
{
    public bool Enabled { get; set; }
    public double DetailPct { get; set; } = 125;
    public double HighlightsPct { get; set; } = 50;
    public double ShadowsPct { get; set; } = 50;
    public double MidtoneRange { get; set; } = 0.5;
}

/// <summary>denoise (profiled), wavelets, noise profile auto-detected from camera and ISO.</summary>
public sealed class DenoiseSettings
{
    public bool Enabled { get; set; }
    public double Strength { get; set; } = 1.2;
}

public sealed class SharpenSettings
{
    public bool Enabled { get; set; }
    public double Radius { get; set; } = 2;
    public double Amount { get; set; } = 0.5;
    public double Threshold { get; set; } = 0.5;
}

public sealed class GeometrySettings
{
    /// <summary>Straightening angle in degrees; the image is auto-cropped to keep its aspect ratio.</summary>
    public double RotationDeg { get; set; }

    /// <summary>Crop edges as fractions (0..1) of the image after rotation.</summary>
    public CropRect Crop { get; set; } = new();
}

public sealed class CropRect
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Right { get; set; } = 1;
    public double Bottom { get; set; } = 1;

    [JsonIgnore]
    public bool IsFull => Left <= 0.0005 && Top <= 0.0005 && Right >= 0.9995 && Bottom >= 0.9995;
}
