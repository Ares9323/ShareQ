using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ShareQ.PluginContracts;

namespace ShareQ.Uploaders.GoogleDrive;

/// <summary>
/// Uploads files to the user's Google Drive. Auth is OAuth2 + PKCE — sign-in goes through the
/// host's <see cref="IOAuthHelper"/>; tokens persist (encrypted) in <see cref="IPluginConfigStore"/>
/// and refresh automatically.
///
/// Settings (via <see cref="IConfigurableUploader"/>): target folder picker (loaded from the
/// user's Drive at runtime, defaults to "My Drive root"), public/private toggle. Scope is
/// <c>drive.file</c> — the app can only see files / folders it created, not the user's full
/// Drive — so the folder dropdown only shows folders ShareQ has access to.
/// </summary>
public sealed class GoogleDriveUploader : IUploader, IAuthenticatedUploader, IConfigurableUploader
{
    public const string UploaderId = "google-drive";

    // OAuth client credentials live in Secrets.cs (git-ignored). They ship in release binaries
    // but stay out of the public repo so Google's automated secret-scanner doesn't revoke the
    // pair the moment GitHub indexes a commit. See Secrets.cs.template for the file shape and
    // setup instructions for fork contributors.
    private const string ClientId = Secrets.GoogleClientId;
    private const string ClientSecret = Secrets.GoogleClientSecret;
    private const string AuthorizeUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    private const string TokenUrl     = "https://oauth2.googleapis.com/token";
    private static readonly string[] Scopes = ["https://www.googleapis.com/auth/drive.file"];

    private const string AccessTokenKey  = "access_token";
    private const string RefreshTokenKey = "refresh_token";
    private const string ExpiresAtKey    = "expires_at_utc";

    // Settings keys exposed via IConfigurableUploader.
    private const string FolderIdKey     = "folder_id";   // empty = upload to My Drive root
    private const string MakePublicKey   = "make_public"; // "true"/"false"

    private readonly IPluginConfigStore _config;
    private readonly IOAuthHelper _oauth;
    private readonly IHttpClientFactory _httpFactory;

    public GoogleDriveUploader(
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
    public string DisplayName => "Google Drive";

    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Bytes.Length == 0) return UploadResult.Failure("empty payload");

        var token = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (token is null)
        {
            var detail = string.IsNullOrEmpty(_lastSignInError) ? "sign-in required (or refused)" : _lastSignInError;
            return UploadResult.Failure($"Google Drive: {detail}");
        }

        using var http = _httpFactory.CreateClient(nameof(GoogleDriveUploader));
        http.BaseAddress = new Uri("https://www.googleapis.com/");

        // Settings: empty folder_id means "upload to My Drive root" (ShareX-equivalent default).
        // make_public defaults to true to preserve current behaviour for existing users.
        var folderId = await _config.GetAsync(FolderIdKey, cancellationToken).ConfigureAwait(false);
        var makePublicRaw = await _config.GetAsync(MakePublicKey, cancellationToken).ConfigureAwait(false);
        var makePublic = makePublicRaw is null || string.Equals(makePublicRaw, "true", StringComparison.OrdinalIgnoreCase);

        var fileName = SanitizeFileName(request.FileName);

        var (uploadOk, fileId, errorMessage) = await UploadMultipartAsync(http, fileName, folderId, request, token, cancellationToken).ConfigureAwait(false);
        if (!uploadOk && string.Equals(errorMessage, "401", StringComparison.Ordinal))
        {
            // Token rejected mid-flight — refresh once then retry.
            token = await ForceRefreshAsync(cancellationToken).ConfigureAwait(false);
            if (token is null) return UploadResult.Failure("re-auth failed");
            (uploadOk, fileId, errorMessage) = await UploadMultipartAsync(http, fileName, folderId, request, token, cancellationToken).ConfigureAwait(false);
        }
        if (!uploadOk || fileId is null) return UploadResult.Failure(errorMessage ?? "upload failed");

        if (makePublic)
        {
            var shareLinkOk = await MakeFilePublicAsync(http, fileId, token, cancellationToken).ConfigureAwait(false);
            if (!shareLinkOk) return UploadResult.Failure("upload succeeded but failed to set public sharing");
        }

        // webViewLink format is stable for Drive: opens the in-Drive viewer the same way ShareX does.
        // Private files at this URL prompt the viewer to request access — that's the user's intent
        // when they've turned off make_public.
        return UploadResult.Success($"https://drive.google.com/file/d/{fileId}/view?usp=sharing");
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

        var refreshed = await TryRefreshAsync(cancellationToken).ConfigureAwait(false);
        if (refreshed is not null) return refreshed;

        return await InteractiveSignInAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TryRefreshAsync(CancellationToken cancellationToken)
    {
        var refreshToken = await _config.GetAsync(RefreshTokenKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(refreshToken)) return null;

        var result = await _oauth.RefreshAsync(
            new OAuthRefreshRequest(TokenUrl, ClientId, refreshToken, Scopes, ClientSecret: ClientSecret),
            cancellationToken).ConfigureAwait(false);
        if (!result.Ok || string.IsNullOrEmpty(result.AccessToken))
        {
            // Surface the underlying error so token-endpoint 400s aren't silently swallowed —
            // typical causes (wrong client type, expired refresh_token, revoked grant) are only
            // visible in this string.
            Debug.WriteLine($"[GoogleDriveUploader] refresh failed: {result.Error}");
            return null;
        }

        await PersistTokensAsync(result, cancellationToken).ConfigureAwait(false);
        return result.AccessToken;
    }

    /// <summary>Last error from the OAuth flow — surfaced into UploadResult.ErrorMessage so the
    /// user sees the actual Google response (e.g. "invalid_client", "redirect_uri_mismatch")
    /// rather than a generic "sign-in refused".</summary>
    private string? _lastSignInError;

    private async Task<string?> InteractiveSignInAsync(CancellationToken cancellationToken)
    {
        // Google specifics:
        //   access_type=offline → request a refresh_token alongside the access_token.
        //   prompt=consent      → force the consent screen so we actually get a refresh_token even
        //                         if the user previously authorized this client (otherwise Google
        //                         issues only an access_token on subsequent grants).
        var result = await _oauth.AuthorizeAsync(
            new OAuthRequest(
                AuthorizeUrl, TokenUrl, ClientId, Scopes,
                ClientSecret: ClientSecret,
                ExtraAuthorizeParams: new Dictionary<string, string>
                {
                    ["access_type"] = "offline",
                    ["prompt"] = "consent",
                }),
            cancellationToken).ConfigureAwait(false);
        if (!result.Ok || string.IsNullOrEmpty(result.AccessToken))
        {
            _lastSignInError = result.Error;
            Debug.WriteLine($"[GoogleDriveUploader] interactive sign-in failed: {result.Error}");
            return null;
        }
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

    /// <summary>List folders visible to ShareQ (drive.file scope sees only app-created folders).
    /// Used by the settings dropdown — auto-create is gone, the user picks an existing folder
    /// (or accepts the default "My Drive root"). Same approach ShareX takes.</summary>
    private async Task<IReadOnlyList<DropdownOption>> ListFoldersAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var http = _httpFactory.CreateClient(nameof(GoogleDriveUploader));
        http.BaseAddress = new Uri("https://www.googleapis.com/");

        var queryEscaped = Uri.EscapeDataString("mimeType='application/vnd.google-apps.folder' and trashed=false");
        var results = new List<DropdownOption>();
        // Empty value = upload to My Drive root (no parent set on the file metadata).
        results.Add(new DropdownOption("", "(My Drive root)"));

        var pageToken = "";
        do
        {
            var url = $"drive/v3/files?q={queryEscaped}&fields=nextPageToken,files(id,name)&pageSize=100";
            if (!string.IsNullOrEmpty(pageToken)) url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"[GoogleDriveUploader] folder list HTTP {(int)response.StatusCode}: {body}");
                break;
            }
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("files", out var files))
            {
                foreach (var f in files.EnumerateArray())
                {
                    var id = f.GetProperty("id").GetString();
                    var name = f.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        results.Add(new DropdownOption(id, name));
                }
            }
            pageToken = doc.RootElement.TryGetProperty("nextPageToken", out var pt) ? pt.GetString() ?? "" : "";
        }
        while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    private static async Task<(bool Ok, string? FileId, string? Error)> UploadMultipartAsync(
        HttpClient http, string fileName, string? folderId, UploadRequest request, string accessToken, CancellationToken cancellationToken)
    {
        // Drive multipart upload: one boundary, two parts — JSON metadata + raw bytes.
        // uploadType=multipart is the simplest path that accepts both metadata and bytes in a
        // single round-trip; resumable would only matter for files larger than typical
        // screenshot/clip uploads. When folderId is null the file lands in My Drive root.
        var boundary = "shareq_" + Guid.NewGuid().ToString("N");
        using var multipart = new MultipartContent("related", boundary);

        object metadata = string.IsNullOrEmpty(folderId)
            ? new { name = fileName }
            : new { name = fileName, parents = new[] { folderId } };
        var metadataPart = new StringContent(JsonSerializer.Serialize(metadata), Encoding.UTF8, "application/json");
        multipart.Add(metadataPart);

        var bytesPart = new ByteArrayContent(request.Bytes);
        bytesPart.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
        multipart.Add(bytesPart);

        using var message = new HttpRequestMessage(HttpMethod.Post,
            "upload/drive/v3/files?uploadType=multipart&fields=id")
        {
            Content = multipart,
        };
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

    private static async Task<bool> MakeFilePublicAsync(
        HttpClient http, string fileId, string accessToken, CancellationToken cancellationToken)
    {
        using var content = new StringContent("{\"role\":\"reader\",\"type\":\"anyone\"}", Encoding.UTF8, "application/json");
        using var message = new HttpRequestMessage(HttpMethod.Post, $"drive/v3/files/{Uri.EscapeDataString(fileId)}/permissions") { Content = content };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    // ── IAuthenticatedUploader ───────────────────────────────────────────────────────────────

    public async Task<AuthState> GetAuthStateAsync(CancellationToken cancellationToken)
    {
        // We deliberately don't call Drive's /about endpoint to fetch the email — drive.file scope
        // doesn't grant access to it (would need userinfo.profile / userinfo.email scopes, which
        // means a re-consent for existing users). Showing a generic "Signed in" is sufficient.
        var refresh = await _config.GetAsync(RefreshTokenKey, cancellationToken).ConfigureAwait(false);
        var access = await _config.GetAsync(AccessTokenKey, cancellationToken).ConfigureAwait(false);
        var signedIn = !string.IsNullOrEmpty(refresh) || !string.IsNullOrEmpty(access);
        return new AuthState(signedIn, signedIn ? "Signed in" : null);
    }

    public async Task<AuthResult> SignInAsync(CancellationToken cancellationToken)
    {
        // Force a fresh interactive sign-in (don't try to reuse cached tokens — the user clicked
        // Sign in deliberately, presumably because something went wrong with the existing creds
        // or they want to switch account).
        var token = await InteractiveSignInAsync(cancellationToken).ConfigureAwait(false);
        return token is null
            ? AuthResult.Failure(_lastSignInError ?? "sign-in cancelled")
            : AuthResult.Success();
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        // Wipe tokens + folder selection. Folder cache is also cleared because a new account has
        // a different drive — the previously-saved folder id likely doesn't resolve.
        await _config.SetAsync(AccessTokenKey,  string.Empty, sensitive: true,  cancellationToken).ConfigureAwait(false);
        await _config.SetAsync(RefreshTokenKey, string.Empty, sensitive: true,  cancellationToken).ConfigureAwait(false);
        await _config.SetAsync(ExpiresAtKey,    string.Empty, sensitive: false, cancellationToken).ConfigureAwait(false);
        await _config.SetAsync(FolderIdKey,     string.Empty, sensitive: false, cancellationToken).ConfigureAwait(false);
    }

    // ── IConfigurableUploader ────────────────────────────────────────────────────────────────

    public IReadOnlyList<PluginSettingDescriptor> GetSettings() =>
    [
        new DropdownSetting(
            Key: FolderIdKey,
            Label: "Target folder",
            Description: "Folders ShareQ has previously created or that the user explicitly granted via the Google picker. Empty selection = upload to My Drive root.",
            IsAsyncLoaded: true),
        new BoolSetting(
            Key: MakePublicKey,
            Label: "Make uploads public",
            Description: "When on, ShareQ adds an anyone-with-link viewer permission so the URL is shareable. When off, uploads stay private — only signed-in viewers with explicit access can open them.",
            DefaultValue: true),
    ];

    public async Task<IReadOnlyList<DropdownOption>> LoadDropdownOptionsAsync(string settingKey, CancellationToken cancellationToken)
    {
        if (string.Equals(settingKey, FolderIdKey, StringComparison.Ordinal))
        {
            var token = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            if (token is null) return [new DropdownOption("", "(sign in to load folders)")];
            return await ListFoldersAsync(token, cancellationToken).ConfigureAwait(false);
        }
        return [];
    }
}
