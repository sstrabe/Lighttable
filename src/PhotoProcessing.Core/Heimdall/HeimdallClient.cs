using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhotoProcessing.Core.Net;

namespace PhotoProcessing.Core.Heimdall;

/// <summary>A Heimdall problem that retrying won't fix. <see cref="SignInRequired"/>: only `photoedit login` will.</summary>
public sealed class HeimdallException(string message, bool signInRequired = false) : Exception(message)
{
    public bool SignInRequired { get; } = signInRequired;
}

public sealed record TokenResponse(string AccessToken, int ExpiresIn, string? RefreshToken, string? Scope);

public sealed record UserInfo(string Sub, string? PreferredUsername);

/// <summary>
/// The OpenID Connect calls to Heimdall (Keycloak): authorization URL, code exchange, refresh and userinfo.
/// The endpoints come from the issuer's discovery document, fetched once.
/// </summary>
public sealed class HeimdallClient(HeimdallSettings settings, HttpMessageHandler? handler = null) : IDisposable
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly HttpClient _http = new(handler ?? HttpHandlers.Create()) { Timeout = TimeSpan.FromSeconds(30) };
    private Endpoints? _endpoints;

    private sealed record Endpoints(string Issuer, string AuthorizationEndpoint, string TokenEndpoint, string UserinfoEndpoint);

    public HeimdallSettings Settings => settings;

    /// <summary>A random state or PKCE code_verifier: 32 random bytes, base64url.</summary>
    public static string NewSecret() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    /// <summary>The S256 PKCE code_challenge for a code_verifier.</summary>
    public static string CodeChallenge(string codeVerifier) =>
        Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    /// <param name="forceLogin">prompt=login: ask for credentials even if the browser is already signed in.</param>
    public async Task<Uri> AuthorizationUrlAsync(string state, string codeChallenge, bool forceLogin, CancellationToken ct)
    {
        var endpoints = await GetEndpointsAsync(ct);
        var query = new Dictionary<string, string>
        {
            ["client_id"] = settings.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = settings.RedirectUri,
            ["scope"] = settings.Scope,
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        if (forceLogin) query["prompt"] = "login";
        return new Uri(endpoints.AuthorizationEndpoint + "?" +
            string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}")));
    }

    public Task<TokenResponse> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct) =>
        RequestTokensAsync(new()
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = settings.RedirectUri,
            ["code_verifier"] = codeVerifier,
        }, ct);

    public Task<TokenResponse> RefreshAsync(string refreshToken, CancellationToken ct) =>
        RequestTokensAsync(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refreshToken }, ct);

    public async Task<UserInfo> GetUserInfoAsync(string accessToken, CancellationToken ct)
    {
        var endpoints = await GetEndpointsAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoints.UserinfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Heimdall userinfo failed: {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<UserInfo>(Json, ct)
            ?? throw new HeimdallException("Heimdall returned an empty userinfo response");
    }

    public void Dispose() => _http.Dispose();

    private async Task<TokenResponse> RequestTokensAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        var endpoints = await GetEndpointsAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoints.TokenEndpoint);
        if (string.IsNullOrEmpty(settings.ClientSecret))
        {
            form["client_id"] = settings.ClientId; // public app
        }
        else
        {
            // RFC 6749 §2.3.1: both halves are form-encoded before base64.
            var pair = $"{Uri.EscapeDataString(settings.ClientId)}:{Uri.EscapeDataString(settings.ClientSecret)}";
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(pair)));
        }

        request.Content = new FormUrlEncodedContent(form);
        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (response.IsSuccessStatusCode)
            return JsonSerializer.Deserialize<TokenResponse>(body, Json) is { AccessToken.Length: > 0 } tokens
                ? tokens
                : throw new HeimdallException("Heimdall's token response had no access_token");

        var (error, description) = ParseError(body);
        throw error switch
        {
            // Expired, revoked or signed out (refresh), or a stale or mismatched code (exchange).
            "invalid_grant" => (Exception)new HeimdallException(description ?? error, signInRequired: true),
            "invalid_client" or "unauthorized_client" => new HeimdallException(
                $"Heimdall rejected the app ({description ?? error}); check PhotoProcessing:Heimdall:ClientId and ClientSecret"),
            _ => new HttpRequestException(
                $"Heimdall token request failed: {(int)response.StatusCode} {error ?? response.ReasonPhrase} {description}".TrimEnd(),
                null, response.StatusCode),
        };
    }

    private async Task<Endpoints> GetEndpointsAsync(CancellationToken ct)
    {
        if (_endpoints is not null) return _endpoints;
        if (string.IsNullOrWhiteSpace(settings.ClientId))
            throw new HeimdallException("Heimdall client ID missing: set PhotoProcessing:Heimdall:ClientId (see README → Setup)");

        var issuer = settings.Issuer.TrimEnd('/');
        using var response = await _http.GetAsync(issuer + "/.well-known/openid-configuration", ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Heimdall discovery failed: {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);

        var endpoints = await response.Content.ReadFromJsonAsync<Endpoints>(Json, ct);
        if (endpoints?.Issuer != issuer)
            throw new HeimdallException($"Heimdall discovery at {issuer} names a different issuer: {endpoints?.Issuer}");
        return _endpoints = endpoints;
    }

    private static (string? Error, string? Description) ParseError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return (doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null,
                    doc.RootElement.TryGetProperty("error_description", out var d) ? d.GetString() : null);
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }
}
