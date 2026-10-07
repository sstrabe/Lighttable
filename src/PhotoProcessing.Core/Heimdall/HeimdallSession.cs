using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoProcessing.Core.Heimdall;

/// <summary>What `photoedit login` stores: whose files, and the refresh token that keeps the session alive.</summary>
public sealed record SignIn(string Username, string Subject, string RefreshToken, string Scope, DateTime SignedInUtc)
{
    /// <summary>An offline session survives signing out of Heimdall and lasts until 30 days unused.</summary>
    [JsonIgnore]
    public bool Offline => Scope.Split(' ').Contains("offline_access");
}

/// <summary>
/// The Heimdall sign-in in &lt;Home&gt;/state/heimdall.json, and the 5-minute access tokens minted from it.
/// The file is a plain file in the profile (like Claude's own login) so the S4U watcher can read it.
/// It is the source of truth: Keycloak rotates the refresh token on every refresh, so each refresh
/// re-reads it and writes the new token back, under a lock file shared by every photoedit process
/// (the watcher, `check`, `login`).
/// </summary>
public sealed class HeimdallSession(HeimdallSettings settings, string path, HttpMessageHandler? handler = null) : IDisposable
{
    /// <summary>Headroom for an upload: the server may only check the token once the whole body has arrived.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly HeimdallClient _client = new(settings, handler);
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The current access token, and the sign-in file's write time when it was minted.</summary>
    private (string Token, DateTime ExpiresUtc, DateTime SignInWrittenUtc)? _access;

    public SignIn? Load()
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<SignIn>(stream, Json);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The signed-in person's Nextcloud username (preferred_username).</summary>
    public string Username => Load()?.Username ?? throw NotSignedIn();

    public async ValueTask<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (Cached() is { } cached) return cached;

        await _gate.WaitAsync(ct);
        try
        {
            if (Cached() is { } fresh) return fresh;

            await using var _ = await AcquireLockAsync(ct);
            var signIn = Load() ?? throw NotSignedIn();
            TokenResponse tokens;
            try
            {
                tokens = await _client.RefreshAsync(signIn.RefreshToken, ct);
            }
            catch (HeimdallException e) when (e.SignInRequired)
            {
                throw new HeimdallException(
                    $"the Heimdall sign-in for {signIn.Username} has ended ({e.Message}); run `photoedit login` again", signInRequired: true);
            }

            if (tokens.RefreshToken is { } rotated && rotated != signIn.RefreshToken)
                Write(signIn with { RefreshToken = rotated });
            Remember(tokens);
            return tokens.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The cached access token, unless it is about to expire or the sign-in file changed since:
    /// a new `photoedit login`, possibly as someone else, must take effect at once.
    /// </summary>
    private string? Cached() =>
        _access is { } a && DateTime.UtcNow < a.ExpiresUtc - RefreshMargin && File.GetLastWriteTimeUtc(path) == a.SignInWrittenUtc
            ? a.Token
            : null;

    private void Remember(TokenResponse tokens) =>
        _access = (tokens.AccessToken, DateTime.UtcNow + TimeSpan.FromSeconds(tokens.ExpiresIn), File.GetLastWriteTimeUtc(path));

    /// <summary>
    /// Interactive sign-in: the authorization code flow with PKCE, with Heimdall redirecting back to
    /// <see cref="HeimdallSettings.RedirectUri"/> on this machine. Stores and returns the sign-in.
    /// </summary>
    /// <param name="openBrowser">Shows the person the authorization URL.</param>
    /// <param name="forceLogin">Ask for credentials even if the browser is already signed in to Heimdall.</param>
    public async Task<SignIn> SignInAsync(Action<Uri> openBrowser, bool forceLogin, CancellationToken ct)
    {
        var redirect = new Uri(settings.RedirectUri);
        if (redirect.Scheme != Uri.UriSchemeHttp || !redirect.IsLoopback || redirect.IsDefaultPort)
            throw new HeimdallException(
                $"PhotoProcessing:Heimdall:RedirectUri must be a loopback address with a port, like http://127.0.0.1:38517/callback (is {redirect})");

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect.GetLeftPart(UriPartial.Authority) + "/");
        try
        {
            listener.Start();
        }
        catch (HttpListenerException e)
        {
            throw new HeimdallException($"can't listen on {redirect.GetLeftPart(UriPartial.Authority)} for Heimdall's redirect: {e.Message}");
        }

        var state = HeimdallClient.NewSecret();
        var verifier = HeimdallClient.NewSecret();
        openBrowser(await _client.AuthorizationUrlAsync(state, HeimdallClient.CodeChallenge(verifier), forceLogin, ct));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(LoginTimeout);
        while (true)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new HeimdallException($"no sign-in arrived from Heimdall within {LoginTimeout.TotalMinutes:0} minutes");
            }

            if (context.Request.Url?.AbsolutePath != redirect.AbsolutePath)
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound; // e.g. the browser asking for a favicon
                context.Response.Close();
                continue;
            }

            try
            {
                var query = context.Request.QueryString;
                if (query["error"] is { } error)
                    throw new HeimdallException($"Heimdall did not sign you in: {query["error_description"] ?? error}");
                if (query["state"] != state)
                    throw new HeimdallException("the redirect from Heimdall belongs to a different sign-in attempt; try again");
                var code = query["code"] ?? throw new HeimdallException("the redirect from Heimdall had no code");

                var tokens = await _client.ExchangeCodeAsync(code, verifier, ct);
                if (tokens.RefreshToken is null)
                    throw new HeimdallException("Heimdall issued no refresh token, so the watcher could not stay signed in");
                var user = await _client.GetUserInfoAsync(tokens.AccessToken, ct);
                if (string.IsNullOrEmpty(user.PreferredUsername))
                    throw new HeimdallException("Heimdall did not share your username; the app needs the profile scope");

                var signIn = new SignIn(user.PreferredUsername, user.Sub, tokens.RefreshToken, tokens.Scope ?? "", DateTime.UtcNow);
                await using (await AcquireLockAsync(ct))
                    Write(signIn);
                Remember(tokens);

                Respond(context, HttpStatusCode.OK, $"Signed in as {signIn.Username}", "PhotoProcessing can use your Nextcloud files now. You can close this tab.");
                return signIn;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Respond(context, HttpStatusCode.BadRequest, "Sign-in failed", e.Message);
                throw;
            }
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        _gate.Dispose();
    }

    private static HeimdallException NotSignedIn() =>
        new("not signed in to Heimdall; run `photoedit login`", signInRequired: true);

    private void Write(SignIn signIn)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(signIn, Json));
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(50); // another process may be reading it this instant
            }
        }
    }

    private async Task<FileStream> AcquireLockAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lockPath = Path.ChangeExtension(path, ".lock");
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, ct);
            }
        }
    }

    private static void Respond(HttpListenerContext context, HttpStatusCode status, string title, string detail)
    {
        try
        {
            var html = $"""
                <!doctype html><meta charset="utf-8"><title>PhotoProcessing</title>
                <body style="font-family:system-ui,sans-serif;margin:4em auto;max-width:32em">
                <h1>{WebUtility.HtmlEncode(title)}</h1><p>{WebUtility.HtmlEncode(detail)}</p>
                """;
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = (int)status;
            context.Response.ContentType = "text/html; charset=utf-8";
            // A chunked response followed by stopping the listener (or the process exiting) resets the
            // connection, and the browser shows an error instead of this page. Send a known length and
            // close the connection once it's sent.
            context.Response.ContentLength64 = bytes.Length;
            context.Response.KeepAlive = false;
            context.Response.OutputStream.Write(bytes);
            context.Response.Close();
        }
        catch (Exception e) when (e is HttpListenerException or IOException or ObjectDisposedException)
        {
            // The browser went away; the console has the outcome.
        }
    }
}
