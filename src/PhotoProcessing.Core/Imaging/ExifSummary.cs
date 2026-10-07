using System.Globalization;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace PhotoProcessing.Core.Imaging;

/// <summary>The shooting data Claude needs to judge a photo (ISO drives denoise, focal length hints at the scene, …).</summary>
public sealed record ExifSummary(
    string? Camera,
    string? Lens,
    int? Iso,
    string? ShutterSpeed,
    double? Aperture,
    double? FocalLength,
    DateTime? Taken,
    int Orientation)
{
    /// <summary>True when the camera recorded a 90°/270° rotation (portrait).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsRotated90 => Orientation is 5 or 6 or 7 or 8;

    public static ExifSummary Read(string path)
    {
        IReadOnlyList<MetadataExtractor.Directory> dirs;
        try
        {
            dirs = ImageMetadataReader.ReadMetadata(path);
        }
        catch (Exception)
        {
            return new ExifSummary(null, null, null, null, null, null, null, 1);
        }

        var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
        var sub = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();

        string? Desc(MetadataExtractor.Directory? d, int tag) => d?.GetDescription(tag)?.Trim();

        var make = Desc(ifd0, ExifDirectoryBase.TagMake);
        var model = Desc(ifd0, ExifDirectoryBase.TagModel);
        var camera = model is null ? make
            : make is not null && !model.StartsWith(make, StringComparison.OrdinalIgnoreCase) ? $"{make} {model}" : model;

        int? iso = sub is not null && sub.TryGetInt32(ExifDirectoryBase.TagIsoEquivalent, out var i) ? i : null;
        double? aperture = sub is not null && sub.TryGetRational(ExifDirectoryBase.TagFNumber, out var f) ? f.ToDouble() : null;
        double? focal = sub is not null && sub.TryGetRational(ExifDirectoryBase.TagFocalLength, out var fl) ? fl.ToDouble() : null;
        DateTime? taken = sub is not null && sub.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dt) ? dt : null;
        var orientation = ifd0 is not null && ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var o) ? o : 1;

        return new ExifSummary(
            camera,
            Desc(sub, ExifDirectoryBase.TagLensModel),
            iso,
            Desc(sub, ExifDirectoryBase.TagExposureTime),
            aperture,
            focal,
            taken,
            orientation);
    }

    public override string ToString()
    {
        var inv = CultureInfo.InvariantCulture;
        var parts = new List<string>();
        if (Camera is not null) parts.Add(Camera);
        if (Lens is not null) parts.Add(Lens);
        if (FocalLength is { } fl) parts.Add(string.Create(inv, $"{fl:0.#}mm"));
        if (Aperture is { } ap) parts.Add(string.Create(inv, $"f/{ap:0.0#}"));
        if (ShutterSpeed is not null) parts.Add(ShutterSpeed);
        if (Iso is { } iso) parts.Add($"ISO {iso}");
        if (Taken is { } t) parts.Add(t.ToString("yyyy-MM-dd HH:mm", inv));
        if (IsRotated90) parts.Add("portrait (rotated by camera)");
        return parts.Count == 0 ? "no EXIF data" : string.Join(" | ", parts);
    }
}
