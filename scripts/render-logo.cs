#:package Svg.Skia@5.2.3

// Renders the PNGs and .ico files in assets/logo from its SVG masters. Run from anywhere:
//   dotnet run scripts/render-logo.cs
// Re-run it after editing an SVG and commit the outputs; the builds use them as they are.

using System.Runtime.CompilerServices;
using SkiaSharp;
using Svg.Skia;

var logo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ScriptPath())!, "..", "assets", "logo"));
using var mark = Load("mark.svg");
using var small = Load("mark-small.svg"); // pixel-fitted; the master turns to mush below 24 px
SKPicture MarkFor(int size) => size <= 24 ? small.Picture! : mark.Picture!;

// The app icon (photoedit.exe, photoedit-tray.exe): every size Windows asks for at 100-200% scaling.
WriteIco("lighttable.ico", [16, 20, 24, 32, 40, 48, 64, 256], MarkFor);
WriteIco("favicon.ico", [16, 32, 48], MarkFor);
WritePng("mark-180.png", Render(mark.Picture!, 180, 180)); // apple-touch-icon
WritePng("mark-512.png", Render(mark.Picture!, 512, 512)); // Heimdall's app logo, anywhere else a raster is needed
foreach (var name in new[] { "lockup", "lockup-dark" })
{
    using var lockup = Load(name + ".svg");
    var bounds = lockup.Picture!.CullRect;
    WritePng(name + ".png", Render(lockup.Picture, (int)(bounds.Width * 2), (int)(bounds.Height * 2)));
}

SKSvg Load(string name)
{
    var svg = new SKSvg();
    if (svg.Load(Path.Combine(logo, name)) is null)
        throw new InvalidOperationException($"could not parse {name}");
    return svg;
}

void WritePng(string name, SKBitmap bitmap)
{
    using (bitmap)
        File.WriteAllBytes(Path.Combine(logo, name), Png(bitmap));
    Console.WriteLine($"wrote {name}");
}

// .ico frames below 256 px are classic 32-bit DIBs, which every reader takes. PNG frames there
// trip up some (System.Drawing.Icon among them); the 256 px frame is PNG, as Windows expects.
void WriteIco(string name, int[] sizes, Func<int, SKPicture> pictureFor)
{
    var frames = sizes.Select(size =>
    {
        using var bitmap = Render(pictureFor(size), size, size);
        return size >= 256 ? Png(bitmap) : Dib(bitmap);
    }).ToArray();

    using var file = File.Create(Path.Combine(logo, name));
    using var w = new BinaryWriter(file);
    w.Write((ushort)0); // reserved
    w.Write((ushort)1); // type: icon
    w.Write((ushort)frames.Length);
    var offset = 6 + 16 * frames.Length;
    for (var i = 0; i < frames.Length; i++)
    {
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i])); // 0 means 256
        w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        w.Write((byte)0); // palette size
        w.Write((byte)0); // reserved
        w.Write((ushort)1); // planes
        w.Write((ushort)32); // bits per pixel
        w.Write(frames[i].Length);
        w.Write(offset);
        offset += frames[i].Length;
    }
    foreach (var frame in frames)
        w.Write(frame);
    Console.WriteLine($"wrote {name} ({string.Join(", ", sizes)} px)");
}

static SKBitmap Render(SKPicture picture, int width, int height)
{
    var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent);
    canvas.Scale(width / picture.CullRect.Width, height / picture.CullRect.Height);
    canvas.DrawPicture(picture);
    return bitmap;
}

static byte[] Png(SKBitmap bitmap)
{
    using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// BITMAPINFOHEADER, then bottom-up BGRA rows with straight alpha (GetPixel un-premultiplies),
// then the 1-bit AND mask (set where fully transparent), whose rows are padded to 4 bytes.
static byte[] Dib(SKBitmap bitmap)
{
    int size = bitmap.Width, maskStride = (size + 31) / 32 * 4;
    using var stream = new MemoryStream();
    using var w = new BinaryWriter(stream);
    w.Write(40); // header size
    w.Write(size);
    w.Write(size * 2); // colour rows plus mask rows
    w.Write((ushort)1); // planes
    w.Write((ushort)32);
    w.Write(0); // BI_RGB
    w.Write(size * size * 4 + maskStride * size);
    w.Write(0); w.Write(0); w.Write(0); w.Write(0); // resolution, palette
    for (var y = size - 1; y >= 0; y--)
        for (var x = 0; x < size; x++)
        {
            var c = bitmap.GetPixel(x, y);
            w.Write(c.Blue); w.Write(c.Green); w.Write(c.Red); w.Write(c.Alpha);
        }
    for (var y = size - 1; y >= 0; y--)
    {
        var row = new byte[maskStride];
        for (var x = 0; x < size; x++)
            if (bitmap.GetPixel(x, y).Alpha == 0)
                row[x / 8] |= (byte)(0x80 >> (x % 8));
        w.Write(row);
    }
    return stream.ToArray();
}

static string ScriptPath([CallerFilePath] string path = "") => path;
