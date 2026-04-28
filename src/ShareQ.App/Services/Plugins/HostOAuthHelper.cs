using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ShareQ.PluginContracts;

namespace ShareQ.App.Services.Plugins;

/// <summary>
/// Host implementation of <see cref="IOAuthHelper"/>: drives an OAuth2 authorization-code+PKCE
/// flow against any provider. Spins up an <see cref="HttpListener"/> on a free localhost port to
/// catch the redirect, opens the user's default browser, then exchanges the code for tokens via
/// the configured token endpoint.
/// </summary>
public sealed class HostOAuthHelper : IOAuthHelper
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HostOAuthHelper> _logger;

    public HostOAuthHelper(IHttpClientFactory httpFactory, ILogger<HostOAuthHelper> logger)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<OAuthResult> AuthorizeAsync(OAuthRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var port = FindFreePort();
            var redirectUri = $"http://localhost:{port}/";
            var (codeVerifier, codeChallenge) = request.UsePkce ? GeneratePkcePair() : (null, null);
            var state = RandomBase64Url(16);

            var authorizeUrl = BuildAuthorizeUrl(request, redirectUri, codeChallenge, state);

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri);
            listener.Start();

            _logger.LogInformation("OAuth: opening browser → {Url}", authorizeUrl);
            OpenBrowser(authorizeUrl);

            var contextTask = listener.GetContextAsync();
            using var ctsTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            ctsTimeout.CancelAfter(TimeSpan.FromMinutes(5));

            HttpListenerContext? ctx;
            try
            {
                ctx = await contextTask.WaitAsync(ctsTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                listener.Stop();
                return OAuthResult.Failure("authorization timed out (waited 5 minutes for browser callback)");
            }

            var query = ctx.Request.QueryString;
            await WriteCallbackResponseAsync(ctx).ConfigureAwait(false);
            listener.Stop();

            var error = query["error"];
            if (!string.IsNullOrEmpty(error))
                return OAuthResult.Failure($"{error}: {query["error_description"]}");

            var code = query["code"];
            if (string.IsNullOrEmpty(code)) return OAuthResult.Failure("authorization response missing 'code'");
            if (!string.Equals(query["state"], state, StringComparison.Ordinal))
                return OAuthResult.Failure("OAuth state mismatch (possible CSRF)");

            return await ExchangeCodeAsync(request, code, redirectUri, codeVerifier, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth: authorize flow threw");
            return OAuthResult.Failure(ex.Message);
        }
    }

    public async Task<OAuthResult> RefreshAsync(OAuthRefreshRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = request.ClientId,
                ["refresh_token"] = request.RefreshToken,
                ["scope"] = string.Join(' ', request.Scopes),
            };
            if (!string.IsNullOrEmpty(request.ClientSecret))
                form["client_secret"] = request.ClientSecret!;

            return await PostTokenEndpointAsync(request.TokenUrl, form, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OAuth: refresh flow threw");
            return OAuthResult.Failure(ex.Message);
        }
    }

    private async Task<OAuthResult> ExchangeCodeAsync(
        OAuthRequest request, string code, string redirectUri, string? codeVerifier, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = request.ClientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', request.Scopes),
        };
        if (!string.IsNullOrEmpty(codeVerifier)) form["code_verifier"] = codeVerifier!;
        if (!string.IsNullOrEmpty(request.ClientSecret)) form["client_secret"] = request.ClientSecret!;

        return await PostTokenEndpointAsync(request.TokenUrl, form, cancellationToken).ConfigureAwait(false);
    }

    private async Task<OAuthResult> PostTokenEndpointAsync(
        string tokenUrl, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var http = _httpFactory.CreateClient(nameof(HostOAuthHelper));
        using var content = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync(tokenUrl, content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            return OAuthResult.Failure($"token endpoint returned HTTP {(int)response.StatusCode}: {body}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var accessToken = root.TryGetProperty("access_token", out var at) ? at.GetString() : null;
            var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            DateTimeOffset? expiresAt = null;
            if (root.TryGetProperty("expires_in", out var ex) && ex.TryGetInt32(out var seconds))
                expiresAt = DateTimeOffset.UtcNow.AddSeconds(seconds - 60);
            if (string.IsNullOrEmpty(accessToken)) return OAuthResult.Failure("token endpoint response missing 'access_token'");
            return OAuthResult.Success(accessToken, refreshToken, expiresAt);
        }
        catch (JsonException ex)
        {
            return OAuthResult.Failure($"invalid token endpoint JSON: {ex.Message}");
        }
    }

    private static string BuildAuthorizeUrl(OAuthRequest request, string redirectUri, string? codeChallenge, string state)
    {
        var sb = new StringBuilder(request.AuthorizeUrl);
        sb.Append(request.AuthorizeUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?');
        sb.Append("response_type=code");
        sb.Append("&client_id=").Append(Uri.EscapeDataString(request.ClientId));
        sb.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
        sb.Append("&scope=").Append(Uri.EscapeDataString(string.Join(' ', request.Scopes)));
        sb.Append("&state=").Append(Uri.EscapeDataString(state));
        if (!string.IsNullOrEmpty(codeChallenge))
        {
            sb.Append("&code_challenge=").Append(Uri.EscapeDataString(codeChallenge));
            sb.Append("&code_challenge_method=S256");
        }
        if (request.ExtraAuthorizeParams is { Count: > 0 })
        {
            foreach (var kv in request.ExtraAuthorizeParams)
                sb.Append('&').Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
        }
        return sb.ToString();
    }

    private static (string Verifier, string Challenge) GeneratePkcePair()
    {
        var verifier = RandomBase64Url(64);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64UrlEncode(hash);
        return (verifier, challenge);
    }

    private static string RandomBase64Url(int byteLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteLength);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void OpenBrowser(string url)
    {
        // ProcessStartInfo with UseShellExecute=true defers to the OS default browser.
        Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private static async Task WriteCallbackResponseAsync(HttpListenerContext ctx)
    {
        const string html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>ShareQ</title>" +
                            "<style>body{background:#1E1E1E;color:#DDD;font-family:Segoe UI,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;}" +
                            "div{text-align:center;}h1{font-weight:600;}p{color:#888;}</style></head>" +
                            "<body><div><h1>Sign-in complete</h1><p>You can close this tab and return to ShareQ.</p></div></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.StatusCode = 200;
        await ctx.Response.OutputStream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
        ctx.Response.OutputStream.Close();
    }
}
