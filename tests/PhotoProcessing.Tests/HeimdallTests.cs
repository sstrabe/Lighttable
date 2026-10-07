using System.Net;
using System.Net.Sockets;
using System.Text;
using PhotoProcessing.Core;
using PhotoProcessing.Core.Heimdall;

namespace PhotoProcessing.Tests;

public sealed class HeimdallTests : IDisposable
{
    private const string Issuer = "https://sso.example.com/realms/heimdall";
    private readonly string _home = Path.Combine(Path.GetTempPath(), "photoedit-heimdall-" + Guid.NewGuid().ToString("N"));
    private string SignInPath => Path.Combine(_home, "state", "heimdall.json");

    /// <summary>Plays Heimdall: discovery, then token and userinfo answers from <paramref name="respond"/>.</summary>
    private sealed class FakeHeimdall(Func<string, Dictionary<string, string>, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, Dictionary<string, string> Form)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
                return Json($$"""
                    {"issuer":"{{Issuer}}","authorization_endpoint":"{{Issuer}}/protocol/openid-connect/auth",
                     "token_endpoint":"{{Issuer}}/protocol/openid-connect/token","userinfo_endpoint":"{{Issuer}}/protocol/openid-connect/userinfo"}
                    """);

            var form = request.Content is null ? [] : (await request.Content.ReadAsStringAsync(ct)).Split('&')
                .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));
            Calls.Add((request, form));
            return respond(path[(path.LastIndexOf('/') + 1)..], form);
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Tokens(string access, string refresh, int expiresIn = 300) =>
        Json($$"""{"access_token":"{{access}}","expires_in":{{expiresIn}},"refresh_token":"{{refresh}}","token_type":"Bearer","scope":"openid profile nextcloud.files.write offline_access"}""");

    private static HeimdallSettings Settings(string secret = "", string redirect = "http://127.0.0.1:38517/callback") =>
        new() { Issuer = Issuer, ClientId = "photoedit", ClientSecret = secret, RedirectUri = redirect };

    /// <summary>What `photoedit login` would have stored.</summary>
    private void Store(string refreshToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SignInPath)!);
        File.WriteAllText(SignInPath, $$"""
            {"username":"lighttable","subject":"0a1b","refreshToken":"{{refreshToken}}","scope":"openid offline_access","signedInUtc":"2026-10-07T10:00:00Z"}
            """);
    }

    [Fact]
    public void Code_challenge_is_rfc7636_s256()
    {
        // RFC 7636 appendix B.
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", HeimdallClient.CodeChallenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        Assert.Equal(43, HeimdallClient.NewSecret().Length);
    }

    [Fact]
    public async Task Refresh_writes_back_the_rotated_token_and_caches_the_access_token()
    {
        Store("refresh-1");
        var heimdall = new FakeHeimdall((_, _) => Tokens("access-1", "refresh-2"));
        using var session = new HeimdallSession(Settings(), SignInPath, heimdall);

        Assert.Equal("lighttable", session.Username);
        Assert.Equal("access-1", await session.GetAccessTokenAsync());
        Assert.Equal("access-1", await session.GetAccessTokenAsync());

        var (request, form) = Assert.Single(heimdall.Calls);
        Assert.Equal($"{Issuer}/protocol/openid-connect/token", request.RequestUri!.AbsoluteUri);
        Assert.Equal("refresh_token", form["grant_type"]);
        Assert.Equal("refresh-1", form["refresh_token"]);
        Assert.Equal("photoedit", form["client_id"]); // a public app identifies itself in the body
        Assert.Null(request.Headers.Authorization);

        var stored = session.Load()!;
        Assert.Equal("refresh-2", stored.RefreshToken);
        Assert.Equal("lighttable", stored.Username);
        Assert.True(stored.Offline);
    }

    [Fact]
    public async Task Refresh_uses_whatever_token_is_on_disk_now()
    {
        Store("refresh-1");
        var heimdall = new FakeHeimdall((_, form) => Tokens("access-for-" + form["refresh_token"], "next", expiresIn: 30));
        using var session = new HeimdallSession(Settings(), SignInPath, heimdall);

        Assert.Equal("access-for-refresh-1", await session.GetAccessTokenAsync());
        Store("from-another-login"); // e.g. `photoedit login` while the watcher runs
        Assert.Equal("access-for-from-another-login", await session.GetAccessTokenAsync()); // 30 s tokens are never cached
    }

    [Fact]
    public async Task A_new_login_replaces_the_cached_access_token()
    {
        Store("refresh-1");
        var heimdall = new FakeHeimdall((_, form) => Tokens("access-for-" + form["refresh_token"], "next"));
        using var session = new HeimdallSession(Settings(), SignInPath, heimdall);

        Assert.Equal("access-for-refresh-1", await session.GetAccessTokenAsync());
        Store("someone-else");
        File.SetLastWriteTimeUtc(SignInPath, DateTime.UtcNow.AddMinutes(1)); // visible however coarse the clock
        Assert.Equal("access-for-someone-else", await session.GetAccessTokenAsync());
    }

    [Fact]
    public async Task A_confidential_app_authenticates_with_basic()
    {
        Store("refresh-1");
        var heimdall = new FakeHeimdall((_, _) => Tokens("access-1", "refresh-2"));
        using var session = new HeimdallSession(Settings(secret: "s3cret"), SignInPath, heimdall);

        await session.GetAccessTokenAsync();

        var (request, form) = Assert.Single(heimdall.Calls);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal("photoedit:s3cret", Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!)));
        Assert.False(form.ContainsKey("client_id"));
    }

    [Fact]
    public async Task An_ended_session_requires_a_new_login()
    {
        Store("refresh-1");
        var heimdall = new FakeHeimdall((_, _) =>
            Json("""{"error":"invalid_grant","error_description":"Offline session not active"}""", HttpStatusCode.BadRequest));
        using var session = new HeimdallSession(Settings(), SignInPath, heimdall);

        var e = await Assert.ThrowsAsync<HeimdallException>(() => session.GetAccessTokenAsync().AsTask());
        Assert.True(e.SignInRequired);
        Assert.Contains("Offline session not active", e.Message);
        Assert.Contains("photoedit login", e.Message);
    }

    [Fact]
    public async Task Without_a_sign_in_nothing_is_sent()
    {
        var heimdall = new FakeHeimdall((_, _) => throw new InvalidOperationException("no request expected"));
        using var session = new HeimdallSession(Settings(), SignInPath, heimdall);

        Assert.True(Assert.Throws<HeimdallException>(() => session.Username).SignInRequired);
        Assert.True((await Assert.ThrowsAsync<HeimdallException>(() => session.GetAccessTokenAsync().AsTask())).SignInRequired);
        Assert.Empty(heimdall.Calls);
    }

    [Fact]
    public async Task Login_runs_the_code_flow_with_pkce_through_the_loopback_redirect()
    {
        var redirect = $"http://127.0.0.1:{FreePort()}/callback";
        var heimdall = new FakeHeimdall((endpoint, _) => endpoint switch
        {
            "token" => Tokens("access-1", "refresh-1"),
            "userinfo" => Json("""{"sub":"0a1b","preferred_username":"lighttable","name":"Light Table"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        using var session = new HeimdallSession(Settings(redirect: redirect), SignInPath, heimdall);

        using var browser = new HttpClient();
        Uri? authorize = null;
        Task<HttpResponseMessage>? landing = null;
        var signIn = await session.SignInAsync(url =>
        {
            // Heimdall signs the person in and redirects back with the code and the same state.
            authorize = url;
            var state = Query(url)["state"];
            landing = Task.Run(async () =>
            {
                (await browser.GetAsync(redirect.Replace("/callback", "/favicon.ico"))).Dispose();
                return await browser.GetAsync($"{redirect}?state={Uri.EscapeDataString(state)}&session_state=x&code=the-code");
            });
        }, forceLogin: true, CancellationToken.None);

        var query = Query(authorize!);
        Assert.StartsWith($"{Issuer}/protocol/openid-connect/auth?", authorize!.AbsoluteUri);
        Assert.Equal("photoedit", query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(redirect, query["redirect_uri"]);
        Assert.Equal("openid profile nextcloud.files.write offline_access", query["scope"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("login", query["prompt"]);

        var (tokenRequest, form) = heimdall.Calls[0];
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal(redirect, form["redirect_uri"]);
        Assert.Equal(query["code_challenge"], HeimdallClient.CodeChallenge(form["code_verifier"]));
        Assert.Equal("Bearer access-1", heimdall.Calls[1].Request.Headers.Authorization!.ToString());

        Assert.Equal("lighttable", signIn.Username);
        Assert.Equal("refresh-1", session.Load()!.RefreshToken);
        Assert.Equal("access-1", await session.GetAccessTokenAsync()); // the login's access token is reused
        Assert.Equal(2, heimdall.Calls.Count);

        using var page = await landing!;
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Signed in as lighttable", html);
        Assert.Contains("<img src=\"data:image/svg+xml;base64,", html); // the embedded logo
    }

    [Fact]
    public async Task Login_rejects_a_redirect_from_another_attempt()
    {
        var redirect = $"http://127.0.0.1:{FreePort()}/callback";
        var heimdall = new FakeHeimdall((_, _) => throw new InvalidOperationException("no token request expected"));
        using var session = new HeimdallSession(Settings(redirect: redirect), SignInPath, heimdall);
        using var browser = new HttpClient();
        Task<HttpResponseMessage>? landing = null;

        var e = await Assert.ThrowsAsync<HeimdallException>(() => session.SignInAsync(
            _ => landing = browser.GetAsync($"{redirect}?state=stale&code=the-code"), forceLogin: false, CancellationToken.None));

        Assert.Contains("different sign-in attempt", e.Message);
        using var page = await landing!;
        Assert.Equal(HttpStatusCode.BadRequest, page.StatusCode);
        Assert.Null(session.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private static Dictionary<string, string> Query(Uri url) => url.Query.TrimStart('?').Split('&')
        .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
