using System.Globalization;
using System.Text;
using SkiaSharp;

namespace PhotoProcessing.Core.Imaging;

/// <summary>
/// Objective numbers to back up what Claude sees in a preview: tonal distribution in CIE L*,
/// clipping, and the average color of near-neutral midtones (a gray-world cast hint).
/// </summary>
public sealed record ImageStats(
    int Width,
    int Height,
    double MeanL,
    double P1L,
    double P5L,
    double MedianL,
    double P95L,
    double P99L,
    double ClippedAnyPct,
    double BlownPct,
    double CrushedPct,
    double NeutralA,
    double NeutralB,
    double MeanChroma,
    double P95Chroma,
    double VividPct,
    int[] Histogram)
{
    private const int Bins = 50;

    public static ImageStats FromFile(string path)
    {
        using var bitmap = SKBitmap.Decode(path) ?? throw new InvalidDataException($"cannot decode {path}");
        return From(bitmap);
    }

    public static ImageStats From(SKBitmap bitmap)
    {
        var lut = new double[256];
        for (var i = 0; i < 256; i++)
        {
            var c = i / 255.0;
            lut[i] = c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var pixels = bitmap.Pixels;
        var n = pixels.Length;
        var lValues = new float[n];
        var chromas = new float[n];
        var histogram = new int[Bins];
        long clippedAny = 0, blown = 0, crushed = 0, vivid = 0;
        double sumL = 0, sumChroma = 0, neutralA = 0, neutralB = 0;
        long neutralCount = 0;

        for (var i = 0; i < n; i++)
        {
            var p = pixels[i];
            byte r = p.Red, g = p.Green, b = p.Blue;
            if (r >= 254 || g >= 254 || b >= 254) clippedAny++;
            if (r >= 254 && g >= 254 && b >= 254) blown++;
            if (r <= 2 && g <= 2 && b <= 2) crushed++;

            var (l, a, bb) = SrgbToLab(lut[r], lut[g], lut[b]);
            var chroma = Math.Sqrt(a * a + bb * bb);
            lValues[i] = (float)l;
            chromas[i] = (float)chroma;
            sumL += l;
            sumChroma += chroma;
            if (chroma > 60) vivid++;
            histogram[Math.Clamp((int)(l / 100 * Bins), 0, Bins - 1)]++;

            if (l is > 20 and < 85 && chroma < 25)
            {
                neutralA += a;
                neutralB += bb;
                neutralCount++;
            }
        }

        Array.Sort(lValues);
        Array.Sort(chromas);
        double Pct(long count) => 100.0 * count / n;
        double Quantile(float[] sorted, double q) => sorted[Math.Clamp((int)(q * (sorted.Length - 1)), 0, sorted.Length - 1)];

        return new ImageStats(
            bitmap.Width, bitmap.Height,
            sumL / n, Quantile(lValues, 0.01), Quantile(lValues, 0.05), Quantile(lValues, 0.5),
            Quantile(lValues, 0.95), Quantile(lValues, 0.99),
            Pct(clippedAny), Pct(blown), Pct(crushed),
            neutralCount > 0 ? neutralA / neutralCount : 0,
            neutralCount > 0 ? neutralB / neutralCount : 0,
            sumChroma / n, Quantile(chromas, 0.95), Pct(vivid),
            histogram);
    }

    public string Describe()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(inv, $"size: {Width}x{Height}");
        sb.AppendLine(inv, $"L* (0=black, 100=white): mean {MeanL:F1} | p1 {P1L:F1} | p5 {P5L:F1} | median {MedianL:F1} | p95 {P95L:F1} | p99 {P99L:F1}");
        sb.AppendLine($"histogram L* 0→100 (sqrt-scaled): |{Sparkline()}|");
        sb.AppendLine(inv, $"clipping: {ClippedAnyPct:F2}% have a channel at 255, {BlownPct:F2}% fully blown white, {CrushedPct:F2}% crushed to black");
        sb.AppendLine(inv, $"neutral-midtone cast: a* {NeutralA:+0.0;-0.0} ({(NeutralA >= 0 ? "magenta" : "green")}), b* {NeutralB:+0.0;-0.0} ({(NeutralB >= 0 ? "yellow/warm" : "blue/cool")})  [gray-world hint; |value| < 2 is neutral]");
        sb.Append(inv, $"chroma C*: mean {MeanChroma:F1}, p95 {P95Chroma:F1}, {VividPct:F2}% above 60");
        return sb.ToString();
    }

    private string Sparkline()
    {
        const string blocks = " ▁▂▃▄▅▆▇█";
        var max = Math.Sqrt(Histogram.Max());
        return string.Concat(Histogram.Select(c =>
            blocks[max <= 0 ? 0 : (int)Math.Round(Math.Sqrt(c) / max * (blocks.Length - 1))]));
    }

    /// <summary>Linear sRGB (D65) → CIE L*a*b* (D65 white).</summary>
    private static (double L, double A, double B) SrgbToLab(double r, double g, double b)
    {
        var x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
        var y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        var z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;

        static double F(double t) => t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;
        double fx = F(x), fy = F(y), fz = F(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }
}
