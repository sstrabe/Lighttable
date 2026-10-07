using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PhotoProcessing.Core.Darktable;
using PhotoProcessing.Core.Imaging;
using SkiaSharp;
using M = PhotoProcessing.Core.Darktable.Modules;

namespace PhotoProcessing.Core.Editing;

public sealed class JobInfo
{
    public required string Id { get; set; }
    public required string RawFile { get; set; }
    public string? RemotePath { get; set; }
    public string? Instructions { get; set; }
    public DateTime CreatedUtc { get; set; }
    public BaselineInfo? Baseline { get; set; }
    public ExifSummary? Exif { get; set; }
    public int Renders { get; set; }
    public DateTime? FinalizedUtc { get; set; }
}

/// <summary>
/// One photo being edited. Layout of a job folder:
/// <code>
/// input/&lt;raw&gt;          the raw file
/// job.json               metadata (as-shot WB, EXIF, render count)
/// baseline.xmp           darktable's default history for this raw
/// recipe.json            the edit; Claude changes this
/// current.xmp            last compiled history
/// previews/vNN.jpg       every render (v00 = darktable defaults), with vNN.recipe.json
/// output/                final JPEG + darktable sidecar (.xmp) for the raw
/// notes.md               Claude's summary of the edit
/// </code>
/// </summary>
public sealed partial class EditJob
{
    private EditJob(string dir, JobInfo info)
    {
        Dir = dir;
        Info = info;
    }

    public string Dir { get; }
    public JobInfo Info { get; }

    public string Id => Info.Id;
    public string RawPath => Path.Combine(Dir, "input", Info.RawFile);
    public string RecipePath => Path.Combine(Dir, "recipe.json");
    public string BaselineXmpPath => Path.Combine(Dir, "baseline.xmp");
    public string CurrentXmpPath => Path.Combine(Dir, "current.xmp");
    public string PreviewsDir => Path.Combine(Dir, "previews");
    public string OutputDir => Path.Combine(Dir, "output");
    public string NotesPath => Path.Combine(Dir, "notes.md");
    public string OutputJpegPath => Path.Combine(OutputDir, Path.GetFileNameWithoutExtension(Info.RawFile) + ".jpg");
    public string OutputSidecarPath => Path.Combine(OutputDir, Info.RawFile + ".xmp");
    private string InfoPath => Path.Combine(Dir, "job.json");

    private static readonly JsonSerializerOptions InfoJson = new(Recipe.JsonOptions)
    {
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip,
    };

    /// <summary>Creates an empty job; the caller writes the raw to <see cref="RawPath"/>.</summary>
    public static EditJob Create(string jobsRoot, string rawFileName, string? remotePath = null, string? instructions = null)
    {
        var stem = SafeName().Replace(Path.GetFileNameWithoutExtension(rawFileName), "_");
        var id = $"{DateTime.Now:yyyyMMdd-HHmmss}-{stem}";
        var dir = Path.Combine(jobsRoot, id);
        for (var n = 2; Directory.Exists(dir); n++)
            dir = Path.Combine(jobsRoot, $"{id}-{n}");

        Directory.CreateDirectory(Path.Combine(dir, "input"));
        var job = new EditJob(dir, new JobInfo
        {
            Id = Path.GetFileName(dir),
            RawFile = Path.GetFileName(rawFileName),
            RemotePath = remotePath,
            Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim(),
            CreatedUtc = DateTime.UtcNow,
        });
        job.SaveInfo();
        return job;
    }

    public static EditJob CreateFromFile(string jobsRoot, string rawPath, string? instructions = null)
    {
        var job = Create(jobsRoot, Path.GetFileName(rawPath), instructions: instructions);
        File.Copy(rawPath, job.RawPath);
        return job;
    }

    /// <summary>Opens a job by folder path, or by id/id-prefix under <paramref name="jobsRoot"/>.</summary>
    public static EditJob Open(string jobsRoot, string pathOrId)
    {
        var dir = Directory.Exists(pathOrId) ? Path.GetFullPath(pathOrId)
            : Directory.Exists(Path.Combine(jobsRoot, pathOrId)) ? Path.Combine(jobsRoot, pathOrId)
            : Directory.Exists(jobsRoot)
                ? Directory.GetDirectories(jobsRoot, pathOrId + "*").OrderDescending().FirstOrDefault()
                : null;
        if (dir is null || !File.Exists(Path.Combine(dir, "job.json")))
            throw new DirectoryNotFoundException($"no job '{pathOrId}' (looked in the current directory and {jobsRoot})");

        var info = JsonSerializer.Deserialize<JobInfo>(File.ReadAllText(Path.Combine(dir, "job.json")), InfoJson)!;
        return new EditJob(dir, info);
    }

    /// <summary>Renders darktable's defaults, captures the history and as-shot WB, writes the starting recipe.</summary>
    public async Task<string> InitAsync(DarktableCli darktable, int previewSize, CancellationToken ct = default)
    {
        Directory.CreateDirectory(PreviewsDir);
        var v00 = PreviewPath(0);
        var render = await darktable.RenderAsync(RawPath, null, v00, previewSize, highQuality: false, debugParams: true, ct);

        var baseline = DarktableXmp.FromJpeg(v00);
        File.WriteAllText(BaselineXmpPath, baseline.ToXml());

        var (x, y) = DarktableCli.ParseAsShotXy(render.Log) ?? StoredIlluminantXy(baseline);
        var exif = ExifSummary.Read(RawPath);
        var stats = ImageStats.FromFile(v00);
        // ashift runs before the orientation flip, so it needs the sensor's unrotated aspect.
        var (w, h) = exif.IsRotated90 ? (stats.Height, stats.Width) : (stats.Width, stats.Height);

        Info.Baseline = new BaselineInfo(x, y, w, h);
        Info.Exif = exif;
        Info.Renders = 1;
        SaveInfo();

        var recipe = RecipeCompiler.FromBaseline(baseline, Info.Baseline);
        recipe.Save(RecipePath);
        File.Copy(RecipePath, Path.ChangeExtension(v00, ".recipe.json"), overwrite: true);

        return Describe(v00, stats, render.Duration, []);
    }

    public async Task<string> RenderAsync(DarktableCli darktable, int previewSize, CancellationToken ct = default)
    {
        var (recipe, warnings) = LoadRecipe();
        Compile(recipe);
        var index = Info.Renders;
        var path = PreviewPath(index);
        var render = await darktable.RenderAsync(RawPath, CurrentXmpPath, path, previewSize, highQuality: false, ct: ct);
        File.Copy(RecipePath, Path.ChangeExtension(path, ".recipe.json"), overwrite: true);

        Info.Renders = index + 1;
        SaveInfo();
        return Describe(path, ImageStats.FromFile(path), render.Duration, warnings);
    }

    /// <summary>Renders at full resolution and saves a 1:1 crop of the region (fractions of the final image).</summary>
    public async Task<string> ZoomAsync(DarktableCli darktable, double left, double top, double right, double bottom, CancellationToken ct = default)
    {
        var (recipe, warnings) = LoadRecipe();
        Compile(recipe);
        var index = Info.Renders;
        var full = Path.Combine(PreviewsDir, $"v{index:D2}-full.jpg");
        var render = await darktable.RenderAsync(RawPath, CurrentXmpPath, full, 0, highQuality: true, ct: ct);

        var zoomPath = Path.Combine(PreviewsDir, $"v{index:D2}-zoom.jpg");
        using (var bitmap = SKBitmap.Decode(full))
        {
            var rect = SKRectI.Create(
                (int)(left * bitmap.Width), (int)(top * bitmap.Height),
                Math.Max(1, (int)((right - left) * bitmap.Width)), Math.Max(1, (int)((bottom - top) * bitmap.Height)));
            rect.Intersect(SKRectI.Create(0, 0, bitmap.Width, bitmap.Height));
            using var cropped = new SKBitmap(rect.Width, rect.Height);
            bitmap.ExtractSubset(cropped, rect);
            using var image = SKImage.FromBitmap(cropped);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 92);
            File.WriteAllBytes(zoomPath, data.ToArray());
        }

        File.Delete(full);
        Info.Renders = index + 1;
        SaveInfo();
        var stats = ImageStats.FromFile(zoomPath);
        return Describe(zoomPath, stats, render.Duration, warnings, "1:1 crop of the full-resolution render");
    }

    /// <summary>Full-resolution JPEG plus a darktable sidecar so the edit can be refined in the darktable GUI.</summary>
    public async Task<string> FinalizeAsync(DarktableCli darktable, CancellationToken ct = default)
    {
        var (recipe, warnings) = LoadRecipe();
        var history = Compile(recipe);
        Directory.CreateDirectory(OutputDir);
        var render = await darktable.RenderAsync(RawPath, CurrentXmpPath, OutputJpegPath, 0, highQuality: true, ct: ct);

        var baseline = DarktableXmp.Load(BaselineXmpPath);
        File.WriteAllText(OutputSidecarPath, baseline.ToSidecar(history));

        Info.FinalizedUtc = DateTime.UtcNow;
        SaveInfo();
        return Describe(OutputJpegPath, ImageStats.FromFile(OutputJpegPath), render.Duration, warnings, "final full-resolution export");
    }

    public bool IsFinalized => Info.FinalizedUtc is not null && File.Exists(OutputJpegPath);

    public string PreviewPath(int index) => Path.Combine(PreviewsDir, $"v{index:D2}.jpg");

    public string LatestPreviewPath() => PreviewPath(Math.Max(0, Info.Renders - 1));

    public string DescribeInfo()
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine($"job: {Id}");
        sb.AppendLine($"raw: {Info.RawFile}");
        if (Info.Exif is not null) sb.AppendLine($"exif: {Info.Exif}");
        if (Info.Baseline is { } b)
        {
            var asShot = b.AsShot;
            sb.AppendLine(inv, $"as-shot white balance: {asShot.Kelvin:F0} K, tint {asShot.Tint:+0.00;-0.00} (xy {b.AsShotX:F4}, {b.AsShotY:F4})");
            sb.AppendLine(inv, $"sensor frame: {b.SensorWidth}x{b.SensorHeight} (before orientation)");
        }

        sb.AppendLine($"renders so far: {Info.Renders} (latest: {Rel(LatestPreviewPath())})");
        if (Info.Instructions is not null) sb.AppendLine($"photographer's instructions: {Info.Instructions}");
        sb.AppendLine(Info.FinalizedUtc is null ? "not finalized" : $"finalized: {Rel(OutputJpegPath)}");
        return sb.ToString().TrimEnd();
    }

    private (Recipe Recipe, List<string> Warnings) LoadRecipe()
    {
        if (Info.Baseline is null)
            throw new InvalidOperationException($"job {Id} was never initialized");
        var recipe = Recipe.Load(RecipePath);
        var warnings = recipe.Clamp();
        if (warnings.Count > 0)
            recipe.Save(RecipePath); // keep the file truthful about what was rendered
        return (recipe, warnings);
    }

    private List<HistoryEntry> Compile(Recipe recipe)
    {
        var baseline = DarktableXmp.Load(BaselineXmpPath);
        var history = RecipeCompiler.Compile(baseline, Info.Baseline!, recipe);
        File.WriteAllText(CurrentXmpPath, baseline.ToSidecar(history));
        return history;
    }

    private string Describe(string imagePath, ImageStats stats, TimeSpan duration, List<string> warnings, string? note = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"rendered {Rel(imagePath)} in {duration.TotalSeconds:F1}s{(note is null ? "" : $" ({note})")}");
        foreach (var w in warnings) sb.AppendLine($"warning: {w}");
        sb.Append(stats.Describe());
        return sb.ToString();
    }

    private string Rel(string path) => Path.GetRelativePath(Directory.GetParent(Dir)!.Parent!.FullName, path).Replace('\\', '/');

    private void SaveInfo() => File.WriteAllText(InfoPath, JsonSerializer.Serialize(Info, InfoJson) + "\n");

    private static (double X, double Y) StoredIlluminantXy(DarktableXmp baseline)
    {
        var entry = baseline.History.Last(h => h.Operation == M.ChannelMixerRgb.Op);
        var p = new ParamsBuffer(entry.Params);
        return (p.GetFloat(M.ChannelMixerRgb.X), p.GetFloat(M.ChannelMixerRgb.Y));
    }

    [GeneratedRegex(@"[^A-Za-z0-9_\-]+")]
    private static partial Regex SafeName();
}
