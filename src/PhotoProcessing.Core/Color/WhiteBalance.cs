namespace PhotoProcessing.Core.Color;

/// <summary>
/// Converts illuminant chromaticity (CIE 1931 xy, what darktable's color calibration stores)
/// to and from correlated color temperature + tint.
/// Tint follows the usual raw-editor slider convention (image effect, not illuminant): positive makes
/// the picture more magenta, negative greener. Internally tint = 1000 × Duv of the assumed illuminant
/// (an illuminant above the Planckian locus is greenish, so compensating for it pushes the image magenta).
/// </summary>
public static class WhiteBalance
{
    public const double MinKelvin = 1500, MaxKelvin = 15000;

    public readonly record struct TempTint(double Kelvin, double Tint);

    public static TempTint FromXy(double x, double y)
    {
        var (u, v) = XyToUv(x, y);

        // Coarse scan in mired space, then golden-section refine around the best sample.
        double Distance(double mired)
        {
            var (lu, lv) = PlanckUv(1e6 / mired);
            return (u - lu) * (u - lu) + (v - lv) * (v - lv);
        }

        double lo = 1e6 / MaxKelvin, hi = 1e6 / MinKelvin;
        var best = lo;
        const int steps = 400;
        for (var i = 0; i <= steps; i++)
        {
            var m = lo + (hi - lo) * i / steps;
            if (Distance(m) < Distance(best)) best = m;
        }

        var step = (hi - lo) / steps;
        double a = Math.Max(lo, best - step), b = Math.Min(hi, best + step);
        var phi = (Math.Sqrt(5) - 1) / 2;
        for (var i = 0; i < 80; i++)
        {
            var c = b - phi * (b - a);
            var d = a + phi * (b - a);
            if (Distance(c) < Distance(d)) b = d; else a = c;
        }

        var kelvin = 1e6 / ((a + b) / 2);
        var (pu, pv) = PlanckUv(kelvin);
        var (nu, nv) = Normal(kelvin);
        var duv = (u - pu) * nu + (v - pv) * nv;
        return new TempTint(kelvin, 1000 * duv);
    }

    public static (double X, double Y) ToXy(double kelvin, double tint)
    {
        kelvin = Math.Clamp(kelvin, MinKelvin, MaxKelvin);
        var duv = tint / 1000;
        var (pu, pv) = PlanckUv(kelvin);
        var (nu, nv) = Normal(kelvin);
        return UvToXy(pu + duv * nu, pv + duv * nv);
    }

    /// <summary>Krystek (1985) rational approximation of the Planckian locus in CIE 1960 uv.</summary>
    private static (double U, double V) PlanckUv(double t)
    {
        var u = (0.860117757 + 1.54118254e-4 * t + 1.28641212e-7 * t * t)
              / (1 + 8.42420235e-4 * t + 7.08145163e-7 * t * t);
        var v = (0.317398726 + 4.22806245e-5 * t + 4.20481691e-8 * t * t)
              / (1 - 2.89741816e-5 * t + 1.61456053e-7 * t * t);
        return (u, v);
    }

    /// <summary>Unit normal to the locus at t, oriented towards +v (Duv &gt; 0 = above the locus = greenish).</summary>
    private static (double U, double V) Normal(double t)
    {
        const double h = 1.0;
        var (u1, v1) = PlanckUv(t - h);
        var (u2, v2) = PlanckUv(t + h);
        double du = u2 - u1, dv = v2 - v1;
        var len = Math.Sqrt(du * du + dv * dv);
        var (nu, nv) = (-dv / len, du / len);
        return nv >= 0 ? (nu, nv) : (-nu, -nv);
    }

    private static (double U, double V) XyToUv(double x, double y)
    {
        var d = -2 * x + 12 * y + 3;
        return (4 * x / d, 6 * y / d);
    }

    private static (double X, double Y) UvToXy(double u, double v)
    {
        var d = 2 * u - 8 * v + 4;
        return (3 * u / d, 2 * v / d);
    }
}
