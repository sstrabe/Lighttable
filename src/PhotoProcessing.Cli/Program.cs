using System.Globalization;
using Microsoft.Extensions.Configuration;
using PhotoProcessing.Cli;
using PhotoProcessing.Core;
using PhotoProcessing.Core.Darktable;
using PhotoProcessing.Core.Editing;

const string Usage = """
    photoedit — Claude-driven raw editing with darktable

    Editing (used by Claude inside a job):
      photoedit new <raw> [--instructions TEXT]   create a job, render darktable's defaults (v00)
      photoedit info <job>                        EXIF, as-shot white balance, render count
      photoedit render <job> [--size PX]          render recipe.json to the next previews/vNN.jpg
      photoedit zoom <job> L,T,R,B                1:1 crop of a region (fractions 0..1) at full resolution
      photoedit finalize <job>                    full-res JPEG + darktable sidecar into output/

    Automation:
      photoedit edit <raw|job> [--instructions TEXT]  run the whole Claude edit for one photo
      photoedit login [--switch-account]           sign in with Heimdall in the browser (once; the watcher reuses it)
      photoedit watch                              poll the Nextcloud inbox and process new raws
      photoedit check                              verify darktable, claude, Heimdall and Nextcloud access

    <job> is a job folder path, or a job id (prefix) under the jobs folder.
    --switch-account asks Heimdall for a password even if the browser is already signed in as someone else.
    """;

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Console.WriteLine(Usage);
    return 0;
}

Console.OutputEncoding = System.Text.Encoding.UTF8;
var configuration = AppConfig.Build();
var settings = AppConfig.Bind(configuration);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    var command = args[0];
    var rest = args[1..];
    string? Option(string name)
    {
        var i = Array.IndexOf(rest, name);
        return i >= 0 && i + 1 < rest.Length ? rest[i + 1] : null;
    }

    string Positional(int index, string what)
    {
        var positional = new List<string>();
        for (var i = 0; i < rest.Length; i++)
        {
            if (rest[i].StartsWith("--", StringComparison.Ordinal)) { i++; continue; }
            positional.Add(rest[i]);
        }

        return index < positional.Count ? positional[index] : throw new UsageException($"missing {what}");
    }

    DarktableCli Darktable() => new(settings.ResolveDarktableCli(), settings.DarktableConfigDir);
    EditJob OpenJob() => EditJob.Open(settings.JobsDir, Positional(0, "<job>"));

    switch (command)
    {
        case "new":
        {
            var raw = Positional(0, "<raw>");
            if (!File.Exists(raw)) throw new UsageException($"{raw} does not exist");
            var job = EditJob.CreateFromFile(settings.JobsDir, raw, Option("--instructions"));
            var rendered = await job.InitAsync(Darktable(), settings.PreviewSize, cts.Token);
            Console.WriteLine(job.DescribeInfo());
            Console.WriteLine(rendered);
            Console.WriteLine($"job folder: {job.Dir}");
            break;
        }

        case "info":
            Console.WriteLine(OpenJob().DescribeInfo());
            break;

        case "render":
        {
            var size = Option("--size") is { } s ? int.Parse(s, CultureInfo.InvariantCulture) : settings.PreviewSize;
            Console.WriteLine(await OpenJob().RenderAsync(Darktable(), size, cts.Token));
            break;
        }

        case "zoom":
        {
            var job = OpenJob();
            var parts = Positional(1, "region L,T,R,B").Split(',').Select(p => double.Parse(p, CultureInfo.InvariantCulture)).ToArray();
            if (parts.Length != 4 || parts[0] >= parts[2] || parts[1] >= parts[3] || parts.Any(p => p is < 0 or > 1))
                throw new UsageException("region must be L,T,R,B fractions with L<R and T<B, e.g. 0.4,0.4,0.6,0.6");
            Console.WriteLine(await job.ZoomAsync(Darktable(), parts[0], parts[1], parts[2], parts[3], cts.Token));
            break;
        }

        case "finalize":
            Console.WriteLine(await OpenJob().FinalizeAsync(Darktable(), cts.Token));
            break;

        case "edit":
            return await Commands.EditAsync(settings, Positional(0, "<raw|job>"), Option("--instructions"), cts.Token);

        case "login":
            return await Commands.LoginAsync(settings, rest.Contains("--switch-account"), cts.Token);

        case "watch":
            return await Commands.WatchAsync(settings, configuration, args, cts.Token);

        case "check":
            return await Commands.CheckAsync(settings, cts.Token);

        default:
            throw new UsageException($"unknown command '{command}'");
    }

    return 0;
}
catch (UsageException e)
{
    Console.Error.WriteLine($"error: {e.Message}\n\n{Usage}");
    return 2;
}
catch (Exception e) when (e is not OperationCanceledException)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

internal sealed class UsageException(string message) : Exception(message);
