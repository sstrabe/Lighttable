namespace PhotoProcessing.Core.Darktable;

/// <summary>
/// Binary layouts of the darktable 5.6 module params this project edits. Offsets come from the
/// <c>dt_iop_*_params_t</c> structs in darktable's src/iop/*.c (release-5.6.1); every field is
/// 4 bytes. When darktable bumps a module version, these must be re-checked against the source —
/// <c>ModuleLayoutTests</c> compares the sizes with what darktable itself writes.
/// </summary>
public static class Modules
{
    /// <summary>Blend colorspaces (<c>dt_develop_blend_colorspace_t</c>).</summary>
    public static class BlendCst
    {
        public const int None = 0;
        public const int Lab = 2;
        public const int RgbScene = 4;
    }

    public static class Exposure
    {
        public const string Op = "exposure";
        public const int Version = 7, Size = 28;
        public const int Black = 4, ExposureEv = 8;
    }

    public static class Sigmoid
    {
        public const string Op = "sigmoid";
        public const int Version = 3, Size = 56;
        public const int Contrast = 0, Skew = 4, WhiteTarget = 8, BlackTarget = 12, HuePreservation = 20;
    }

    /// <summary>color calibration; used here only for its chromatic-adaptation illuminant.</summary>
    public static class ChannelMixerRgb
    {
        public const string Op = "channelmixerrgb";
        public const int Version = 3, Size = 160;
        public const int Illuminant = 120, X = 136, Y = 140, Temperature = 144;
        public const int IlluminantDaylight = 2, IlluminantCustom = 7, IlluminantCamera = 10;
    }

    public static class ColorBalanceRgb
    {
        public const string Op = "colorbalancergb";
        public const int Version = 5, Size = 132;

        // 4 ways: lift (shadows), power (mid-tones), gain (highlights), offset (global)
        public const int ShadowsY = 0, ShadowsC = 4, ShadowsH = 8;
        public const int MidtonesY = 12, MidtonesC = 16, MidtonesH = 20;
        public const int HighlightsY = 24, HighlightsC = 28, HighlightsH = 32;
        public const int GlobalY = 36, GlobalC = 40, GlobalH = 44;
        public const int ShadowsWeight = 48, WhiteFulcrum = 52, HighlightsWeight = 56;
        public const int ChromaShadows = 60, ChromaHighlights = 64, ChromaGlobal = 68, ChromaMidtones = 72;
        public const int SaturationGlobal = 76, SaturationHighlights = 80, SaturationMidtones = 84, SaturationShadows = 88;
        public const int HueAngle = 92;
        public const int BrillianceGlobal = 96, BrillianceHighlights = 100, BrillianceMidtones = 104, BrillianceShadows = 108;
        public const int MaskGreyFulcrum = 112, Vibrance = 116, GreyFulcrum = 120, Contrast = 124, SaturationFormula = 128;

        public static ParamsBuffer Defaults()
        {
            var p = new ParamsBuffer(Size);
            p.SetFloat(ShadowsWeight, 1f);
            p.SetFloat(HighlightsWeight, 1f);
            p.SetFloat(MaskGreyFulcrum, 0.1845f);
            p.SetFloat(GreyFulcrum, 0.1845f);
            p.SetInt(SaturationFormula, 1); // DT_COLORBALANCE_SATURATION_DTUCS
            return p;
        }
    }

    /// <summary>tone equalizer. Band offsets are in EV, band centers at -8..0 EV.</summary>
    public static class ToneEqualizer
    {
        public const string Op = "toneequal";
        public const int Version = 2, Size = 72;
        public const int FirstBand = 0, BandCount = 9;
        public const int Blending = 36, Smoothing = 40, Feathering = 44, Quantization = 48;
        public const int ContrastBoost = 52, ExposureBoost = 56, Details = 60, Method = 64, Iterations = 68;

        /// <summary>
        /// Mask settings of darktable's "compress shadows/highlights | EIGF | medium" preset:
        /// the mask exposure boost puts scene middle-grey at the -4 EV band.
        /// </summary>
        public static ParamsBuffer Defaults()
        {
            var p = new ParamsBuffer(Size);
            p.SetFloat(Blending, 3f);
            p.SetFloat(Smoothing, MathF.Sqrt(2f));
            p.SetFloat(Feathering, 7f);
            p.SetFloat(Quantization, 0f);
            p.SetFloat(ContrastBoost, 0f);
            p.SetFloat(ExposureBoost, -1.57f);
            p.SetInt(Details, 4); // DT_TONEEQ_EIGF
            p.SetInt(Method, 4);  // DT_TONEEQ_NORM_2
            p.SetInt(Iterations, 3);
            return p;
        }
    }

    /// <summary>local contrast (bilat) in local laplacian mode.</summary>
    public static class LocalContrast
    {
        public const string Op = "bilat";
        public const int Version = 3, Size = 20;
        public const int Mode = 0, SigmaR = 4, SigmaS = 8, Detail = 12, Midtone = 16;

        public static ParamsBuffer Defaults()
        {
            var p = new ParamsBuffer(Size);
            p.SetInt(Mode, 1); // local laplacian
            p.SetFloat(SigmaR, 0.5f);
            p.SetFloat(SigmaS, 0.5f);
            p.SetFloat(Detail, 0.25f);
            p.SetFloat(Midtone, 0.5f);
            return p;
        }
    }

    public static class Sharpen
    {
        public const string Op = "sharpen";
        public const int Version = 1, Size = 12;
        public const int Radius = 0, Amount = 4, Threshold = 8;
    }

    /// <summary>denoise (profiled), wavelets with the camera noise profile auto-detected.</summary>
    public static class DenoiseProfile
    {
        public const string Op = "denoiseprofile";
        public const int Version = 12, Size = 416;
        private const int Bands = 7, Channels = 6;
        public const int Radius = 0, Nbhood = 4, Strength = 8, Shadows = 12, Bias = 16;
        public const int CentralPixelWeight = 24, Overshooting = 28, A0 = 32, Mode = 56, X = 60, Y = 228;
        public const int WbAdaptiveAnscombe = 396, FixAnscombe = 400, UseNewVst = 404, WaveletColorMode = 408;

        /// <summary>Mirrors the generic wavelet settings from denoiseprofile's init_presets.</summary>
        public static ParamsBuffer Defaults()
        {
            var p = new ParamsBuffer(Size);
            p.SetFloat(Radius, 1f);
            p.SetFloat(Nbhood, 7f);
            p.SetFloat(Strength, 1.2f);
            p.SetFloat(Shadows, 0f);
            p.SetFloat(Bias, 0f);
            p.SetFloat(CentralPixelWeight, 0.1f);
            p.SetFloat(Overshooting, 1f);
            p.SetFloat(A0, -1f); // autodetect profile from camera + ISO
            p.SetInt(Mode, 1);   // MODE_WAVELETS
            for (var c = 0; c < Channels; c++)
            for (var b = 0; b < Bands; b++)
            {
                p.SetFloat(X + (c * Bands + b) * 4, b / (Bands - 1f));
                p.SetFloat(Y + (c * Bands + b) * 4, 0.5f);
            }

            p.SetBool(WbAdaptiveAnscombe, true);
            p.SetBool(FixAnscombe, true);
            p.SetBool(UseNewVst, true);
            p.SetInt(WaveletColorMode, 1); // MODE_Y0U0V0
            return p;
        }
    }

    /// <summary>rotate and perspective; only rotation is used. Crop values must be computed by us (the GUI normally does it).</summary>
    public static class RotatePerspective
    {
        public const string Op = "ashift";
        public const int Version = 5, Size = 892;
        public const int Rotation = 0, FocalLength = 16, CropFactor = 20, OrthoCorr = 24, Aspect = 28;
        public const int Mode = 32, CropMode = 36, Cl = 40, Cr = 44, Ct = 48, Cb = 52;

        public static ParamsBuffer Create(float rotationDeg, int width, int height)
        {
            var p = new ParamsBuffer(Size);
            p.SetFloat(Rotation, rotationDeg);
            p.SetFloat(FocalLength, 28f);
            p.SetFloat(CropFactor, 1f);
            p.SetFloat(OrthoCorr, 100f);
            p.SetFloat(Aspect, 1f);
            p.SetInt(Mode, 0);     // generic
            p.SetInt(CropMode, 2); // original format

            // Largest centered rectangle with the original aspect inside the rotated image,
            // as fractions of the rotated bounding box (what the GUI's auto-crop computes).
            var theta = Math.Abs(rotationDeg) * Math.PI / 180.0;
            var (cos, sin) = (Math.Cos(theta), Math.Sin(theta));
            double w = width, h = height;
            var boundW = w * cos + h * sin;
            var boundH = w * sin + h * cos;
            var scale = Math.Min(w / boundW, h / boundH);
            var cl = (boundW - scale * w) / (2 * boundW);
            var ct = (boundH - scale * h) / (2 * boundH);
            p.SetFloat(Cl, (float)cl);
            p.SetFloat(Cr, (float)(1 - cl));
            p.SetFloat(Ct, (float)ct);
            p.SetFloat(Cb, (float)(1 - ct));
            return p;
        }
    }

    public static class Crop
    {
        public const string Op = "crop";
        public const int Version = 3, Size = 24;
        public const int Left = 0, Top = 4, Right = 8, Bottom = 12, RatioN = 16, RatioD = 20;

        public static ParamsBuffer Create(float left, float top, float right, float bottom)
        {
            var p = new ParamsBuffer(Size);
            p.SetFloat(Left, left);
            p.SetFloat(Top, top);
            p.SetFloat(Right, right);
            p.SetFloat(Bottom, bottom);
            p.SetInt(RatioN, 0); // 0/0 = freehand, no aspect snapping
            p.SetInt(RatioD, 0);
            return p;
        }
    }
}
