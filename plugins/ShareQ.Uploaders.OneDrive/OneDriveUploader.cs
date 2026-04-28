using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ShareQ.PluginContracts;

namespace ShareQ.Uploaders.OneDrive;

/// <summary>
/// Uploads files to the user's OneDrive (consumer / personal account) via Microsoft Graph.
/// Authentication is OAuth2 + PKCE — the first upload triggers a browser sign-in via the host's
/// <see cref="IOAuthHelper"/>; tokens are persisted (encrypted) in <see cref="IPluginConfigStore"/>
/// and refreshed automatically when expired.
/// </summary>
public sealed class OneDriveUploader : IUploader
{
    public const string UploaderId = "onedrive";

    // Public client_id from our Azure App Registration. Public clients use PKCE in lieu of a
    // client_secret, so this id can ship in source — it identifies the app, it doesn't
    // authenticate it.
    private const string ClientId = "31cfd8ee-79e9-4ec6-a523-8b2514552719";
    private const string AuthorizeUrl = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
    private const string TokenUrl    = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
    private static readonly string[] Scopes = ["Files.ReadWrite", "offline_access"];

    private const string AccessTokenKey  = "access_token";
    private const string RefreshTokenKey = "refresh_token";
    private const string ExpiresAtKey    = "expires_at_utc";
    private const string FolderKey       = "folder_path";
    private const string DefaultFolder   = "ShareQ";

    private readonly IPluginConfigStore _config;
    private readonly IOAuthHelper _oauth;
    private readonly IHttpClientFactory _httpFactory;

    public OneDriveUploader(
        IPluginConfigStoreFactory configFactory,
        IOAuthHelper oauth,
        IHttpClientFactory httpFactory)
    {
        ArgumentNullException.ThrowIfNull(configFactory);
        _config = configFactory.Create(UploaderId);
        _oauth = oauth;
        _httpFactory = httpFactory;
    }

    public string Id => UploaderId;
    public string DisplayName => "OneDrive";

    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Bytes.Length == 0) return UploadResult.Failure("empty payload");

        var token = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null) return UploadResult.Failure("OneDrive sign-in required (or refused)");

        var folder = (await _config.GetAsync(FolderKey, cancellationToken).ConfigureAwait(false)) ?? DefaultFolder;
        var fileName = SanitizeFileName(request.FileName);
        // Relative path without leading '/': appended to BaseAddress so the "/v1.0/" segment is
        // preserved. A leading '/' would make the URI root-relative and drop the version prefix
        // (Graph then complains "Invalid version: me").
        var graphPath = $"me/drive/root:/{Uri.EscapeDataString(folder)}/{Uri.EscapeDataString(fileName)}:/content";

        using var http = _httpFactory.CreateClient(nameof(OneDriveUploader));
        http.BaseAddress = new Uri("https://graph.microsoft.com/v1.0/");

        var (uploadOk, itemId, errorMessage) = await PutFileAsync(http, graphPath, request, token, cancellationToken).ConfigureAwait(false);
        if (!uploadOk && string.Equals(errorMessage, "401", StringComparison.Ordinal))
        {
            // Access token rejected — try a single refresh, then retry.
            token = await ForceRefreshAsync(cancellationToken).ConfigureAwait(false);
            if (token is null) return UploadResult.Failure("re-auth failed");
            (uploadOk, itemId, errorMessage) = await PutFileAsync(http, graphPath, request, token, cancellationToken).ConfigureAwait(false);
        }
        if (!uploadOk || itemId is null) return UploadResult.Failure(errorMessage ?? "upload failed");

        var shareUrl = await CreateShareLinkAsync(http, itemId, token, cancellationToken).ConfigureAwait(false);
        return shareUrl is null
            ? UploadResult.Failure("upload succeeded but failed to create sharing link")
            : UploadResult.Success(shareUrl);
    }

    private async Task<string?> EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        var stored = await _config.GetAsync(AccessTokenKey, cancellationToken).ConfigureAwait(false);
        var expiresAtRaw = await _config.GetAsync(ExpiresAtKey, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(stored)
            && DateTimeOffset.TryParse(expiresAtRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expiresAt)
            && expiresAt > DateTimeOffset.UtcNow)
        {
            return stored;
        }

        // Try refresh first (silent), fall back to interactive sign-in.
        var refreshed = await TryRefreshAsync(cancellationToken).ConfigureAwait(false);
        if (refreshed is not null) return refreshed;

        return await InteractiveSignInAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TryRefreshAsync(CancellationToken cancellationToken)
    {
        var refreshToken = await _config.GetAsync(RefreshTokenKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(refreshToken)) return null;

        var result = await _oauth.RefreshAsync(
            new OAuthRefreshRequest(TokenUrl, ClientId, refreshToken, Scopes),
            cancellationToken).ConfigureAwait(false);
        if (!result.Ok || string.IsNullOrEmpty(result.AccessToken)) return null;

        await PersistTokensAsync(result, cancellationToken).ConfigureAwait(false);
        return result.AccessToken;
    }

    private async Task<string?> InteractiveSignInAsync(CancellationToken cancellationToken)
    {
        var result = await _oauth.AuthorizeAsync(
            new OAuthRequest(
                AuthorizeUrl, TokenUrl, ClientId, Scopes,
                ExtraAuthorizeParams: new Dictionary<string, string> { ["prompt"] = "select_account" }),
            cancellationToken).ConfigureAwait(false);
        if (!result.Ok || string.IsNullOrEmpty(result.AccessToken)) return null;
        await PersistTokensAsync(result, cancellationToken).ConfigureAwait(false);
        return result.AccessToken;
    }

    private async Task<string?> ForceRefreshAsync(CancellationToken cancellationToken)
        => await TryRefreshAsync(cancellationToken).ConfigureAwait(false)
           ?? await InteractiveSignInAsync(cancellationToken).ConfigureAwait(false);

    private async Task PersistTokensAsync(OAuthResult result, CancellationToken cancellationToken)
    {
        await _config.SetAsync(AccessTokenKey, result.AccessToken!, sensitive: true, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(result.RefreshToken))
            await _config.SetAsync(RefreshTokenKey, result.RefreshToken!, sensitive: true, cancellationToken).ConfigureAwait(false);
        if (result.ExpiresAt is { } exp)
            await _config.SetAsync(ExpiresAtKey, exp.ToString("O", CultureInfo.InvariantCulture), sensitive: false, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(bool Ok, string? ItemId, string? Error)> PutFileAsync(
        HttpClient http, string graphPath, UploadRequest request, string accessToken, CancellationToken cancellationToken)
    {
        using var content = new ByteArrayContent(request.Bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
        using var message = new HttpRequestMessage(HttpMethod.Put, graphPath) { Content = content };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) return (false, null, "401");
        if (!response.IsSuccessStatusCode) return (false, null, $"HTTP {(int)response.StatusCode}: {body}");

        try
        {
            using var doc = JsonDocument.Parse(body);
            var id = doc.RootElement.GetProperty("id").GetString();
            return (true, id, null);
        }
        catch (Exception ex)
        {
            return (false, null, $"unexpected response: {ex.Message}");
        }
    }

    private static async Task<string?> CreateShareLinkAsync(
        HttpClient http, string itemId, string accessToken, CancellationToken cancellationToken)
    {
        var path = $"me/drive/items/{Uri.EscapeDataString(itemId)}/createLink";
        using var content = new StringContent("{\"type\":\"view\",\"scope\":\"anonymous\"}", Encoding.UTF8, "application/json");
        using var message = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.GetProperty("link").GetProperty("webUrl").GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
