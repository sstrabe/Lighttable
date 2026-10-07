using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using PhotoProcessing.Core.Net;

namespace PhotoProcessing.Core.Nextcloud;

public sealed record RemoteItem(string Path, long Size, string ETag, DateTimeOffset LastModified, bool IsFolder)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// Minimal WebDAV client for a Nextcloud user's files (remote.php/dav/files/&lt;user&gt;), authenticated
/// with Heimdall access tokens (<see cref="Heimdall.HeimdallSession.GetAccessTokenAsync"/>).
/// </summary>
public sealed class NextcloudClient : IDisposable
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly HttpMethod Propfind = new("PROPFIND");
    private static readonly HttpMethod Mkcol = new("MKCOL");
    private static readonly HttpMethod Move = new("MOVE");

    private const string PropfindBody = """
        <?xml version="1.0" encoding="utf-8"?>
        <d:propfind xmlns:d="DAV:">
          <d:prop><d:getlastmodified/><d:getcontentlength/><d:resourcetype/><d:getetag/></d:prop>
        </d:propfind>
        """;

    private readonly HttpClient _http;
    private readonly string _filesRootPath; // e.g. /remote.php/dav/files/sam/

    /// <param name="username">The signed-in person's preferred_username; WebDAV paths are under it.</param>
    /// <param name="accessToken">Returns a current bearer token; called before every request.</param>
    public NextcloudClient(string baseUrl, string username, Func<CancellationToken, ValueTask<string>> accessToken,
        HttpMessageHandler? handler = null)
    {
        Username = username;
        var baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
        _filesRootPath = $"{baseUri.AbsolutePath}remote.php/dav/files/{Uri.EscapeDataString(username)}/";
        _http = new HttpClient(new BearerHandler(accessToken, handler ?? HttpHandlers.Create()));
        _http.BaseAddress = new Uri(baseUri, _filesRootPath);
        _http.Timeout = TimeSpan.FromMinutes(10);
    }

    public string Username { get; }

    /// <summary>Lists a folder's direct children; an absent folder lists as empty.</summary>
    public async Task<IReadOnlyList<RemoteItem>> ListAsync(string folder, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(Propfind, Encode(folder.TrimEnd('/') + "/"))
        {
            Content = new StringContent(PropfindBody, Encoding.UTF8, "application/xml"),
        };
        request.Headers.Add("Depth", "1");
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return [];
        await EnsureSuccess(response, $"PROPFIND {folder}");

        var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var self = folder.Trim('/');
        return xml.Descendants(Dav + "response")
            .Select(Parse)
            .Where(item => item.Path.Trim('/') != self)
            .ToList();
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(Propfind, Encode(path));
        request.Headers.Add("Depth", "0");
        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await EnsureSuccess(response, $"PROPFIND {path}");
        return true;
    }

    public async Task DownloadAsync(string path, string localPath, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(Encode(path), HttpCompletionOption.ResponseHeadersRead, ct);
        await EnsureSuccess(response, $"GET {path}");
        var temp = localPath + ".part";
        await using (var file = File.Create(temp))
            await response.Content.CopyToAsync(file, ct);
        File.Move(temp, localPath, overwrite: true);
    }

    public async Task<string> DownloadStringAsync(string path, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync(Encode(path), ct);
        await EnsureSuccess(response, $"GET {path}");
        return await response.Content.ReadAsStringAsync(ct);
    }

    public async Task UploadAsync(string localPath, string path, CancellationToken ct = default)
    {
        await using var file = File.OpenRead(localPath);
        using var content = new StreamContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await _http.PutAsync(Encode(path), content, ct);
        await EnsureSuccess(response, $"PUT {path}");
    }

    public async Task UploadStringAsync(string text, string path, CancellationToken ct = default)
    {
        using var content = new StringContent(text, Encoding.UTF8, "text/plain");
        using var response = await _http.PutAsync(Encode(path), content, ct);
        await EnsureSuccess(response, $"PUT {path}");
    }

    /// <summary>Moves without overwriting; fails if the destination exists (use <see cref="UniquePathAsync"/>).</summary>
    public async Task MoveAsync(string from, string to, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(Move, Encode(from));
        request.Headers.Add("Destination", new Uri(_http.BaseAddress!, Encode(to)).AbsoluteUri);
        request.Headers.Add("Overwrite", "F");
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccess(response, $"MOVE {from} -> {to}");
    }

    /// <summary>Creates the folder and any missing parents.</summary>
    public async Task EnsureFolderAsync(string folder, CancellationToken ct = default)
    {
        var current = "";
        foreach (var segment in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += segment + "/";
            using var request = new HttpRequestMessage(Mkcol, Encode(current));
            using var response = await _http.SendAsync(request, ct);
            if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.MethodNotAllowed))
                await EnsureSuccess(response, $"MKCOL {current}");
        }
    }

    /// <summary>Returns <paramref name="path"/>, or "name-2.ext", "name-3.ext", … if it is taken.</summary>
    public async Task<string> UniquePathAsync(string path, CancellationToken ct = default)
    {
        if (!await ExistsAsync(path, ct)) return path;
        var dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        // Treat "IMG_1.CR2.xmp"-style double extensions as one suffix.
        var dot = name.IndexOf('.');
        var (stem, ext) = dot > 0 ? (name[..dot], name[dot..]) : (name, "");
        for (var n = 2; ; n++)
        {
            var candidate = $"{(string.IsNullOrEmpty(dir) ? "" : dir + "/")}{stem}-{n}{ext}";
            if (!await ExistsAsync(candidate, ct)) return candidate;
        }
    }

    public void Dispose() => _http.Dispose();

    private RemoteItem Parse(XElement response)
    {
        var href = Uri.UnescapeDataString(response.Element(Dav + "href")!.Value);
        var path = href.StartsWith(_filesRootPath, StringComparison.Ordinal) ? href[_filesRootPath.Length..] : href;
        var prop = response.Elements(Dav + "propstat")
            .Where(ps => ps.Element(Dav + "status")?.Value.Contains(" 200 ") == true)
            .Select(ps => ps.Element(Dav + "prop"))
            .FirstOrDefault();

        var isFolder = prop?.Element(Dav + "resourcetype")?.Element(Dav + "collection") is not null;
        var size = long.TryParse(prop?.Element(Dav + "getcontentlength")?.Value, CultureInfo.InvariantCulture, out var s) ? s : 0;
        var etag = prop?.Element(Dav + "getetag")?.Value.Trim('"') ?? "";
        var modified = DateTimeOffset.TryParse(prop?.Element(Dav + "getlastmodified")?.Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var m) ? m : DateTimeOffset.MinValue;
        return new RemoteItem(path.TrimEnd('/'), size, etag, modified, isFolder);
    }

    private static string Encode(string path) =>
        string.Join('/', path.TrimStart('/').Split('/').Select(Uri.EscapeDataString));

    private async Task EnsureSuccess(HttpResponseMessage response, string what)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync();
        var hint = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => $" (Nextcloud rejected the Heimdall token; {Username} must have signed in to Nextcloud once)",
            HttpStatusCode.Forbidden => $" (only {Username}'s own files are allowed, and changes need the nextcloud.files.write scope)",
            _ => "",
        };
        throw new HttpRequestException(
            $"{what} failed: {(int)response.StatusCode} {response.ReasonPhrase}{hint} {Truncate(body)}", null, response.StatusCode);
    }

    private static string Truncate(string s) => s.Length > 300 ? s[..300] + "…" : s;

    private sealed class BearerHandler(Func<CancellationToken, ValueTask<string>> accessToken, HttpMessageHandler inner)
        : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await accessToken(ct));
            return await base.SendAsync(request, ct);
        }
    }
}
