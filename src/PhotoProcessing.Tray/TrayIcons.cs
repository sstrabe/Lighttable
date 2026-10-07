using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PhotoProcessing.Tray;

/// <summary>Draws the status icons: a coloured lens, with a spinning arc while a photo is being edited.</summary>
internal static class TrayIcons
{
    public static readonly Color Idle = Color.FromArgb(46, 160, 67);
    public static readonly Color Busy = Color.FromArgb(31, 111, 235);
    public static readonly Color Warning = Color.FromArgb(219, 143, 0);
    public static readonly Color Error = Color.FromArgb(207, 34, 46);
    public static readonly Color Unknown = Color.FromArgb(110, 118, 129);

    public const int SpinnerFrames = 12;

    public static Icon Create(Color fill, int? spinnerFrame = null)
    {
        var size = Math.Max(SystemInformation.SmallIconSize.Width, 16) * 2; // oversample; Windows scales down crisply
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            float s = size;
            var body = new RectangleF(s * 0.06f, s * 0.06f, s * 0.88f, s * 0.88f);
            using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, body);

            // lens: white ring and a small highlight
            using var ring = new Pen(Color.White, s * 0.09f);
            var lens = RectangleF.Inflate(body, -s * 0.2f, -s * 0.2f);
            g.DrawEllipse(ring, lens);
            using (var dot = new SolidBrush(Color.White))
                g.FillEllipse(dot, lens.X + lens.Width * 0.22f, lens.Y + lens.Height * 0.22f, lens.Width * 0.22f, lens.Height * 0.22f);

            if (spinnerFrame is { } frame)
            {
                using var arc = new Pen(Color.White, s * 0.08f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                var outer = RectangleF.Inflate(body, -s * 0.05f, -s * 0.05f);
                g.DrawArc(arc, outer, frame * (360f / SpinnerFrames), 110);
            }
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

#pragma warning disable SYSLIB1054 // LibraryImport would need AllowUnsafeBlocks for this one call
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
#pragma warning restore SYSLIB1054
}
