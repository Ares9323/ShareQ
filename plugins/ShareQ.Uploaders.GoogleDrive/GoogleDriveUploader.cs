using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ShareQ.PluginContracts;

namespace ShareQ.Uploaders.GoogleDrive;

/// <summary>
/// Uploads files to the user's Google Drive. Auth is OAuth2 + PKCE — first upload triggers a
/// browser sign-in via the host's <see cref="IOAuthHelper"/>; tokens persist (encrypted) in
/// <see cref="IPluginConfigStore"/> and refresh automatically.
///
/// Files land in a top-level "ShareQ" folder (created on first upload, id cached per-account)
/// and are shared as public read-only links. Scope is <c>drive.file</c> — the app can only see
/// files it created, not the user's full Drive.
/// </summary>
public sealed class GoogleDriveUploader : IUploader
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
    private const string FolderIdKey     = "folder_id";   // cached after first lookup/create
    private const string FolderNameKey   = "folder_name";
    private const string DefaultFolder   = "ShareQ";

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

        var folderName = (await _config.GetAsync(FolderNameKey, cancellationToken).ConfigureAwait(false)) ?? DefaultFolder;
        _lastApiError = null;
        var folderId = await EnsureFolderAsync(http, folderName, token, cancellationToken).ConfigureAwait(false);
        if (folderId is null)
        {
            var detail = string.IsNullOrEmpty(_lastApiError) ? "failed to resolve / create the ShareQ folder on Drive" : _lastApiError;
            return UploadResult.Failure($"Google Drive: {detail}");
        }

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

        var shareLinkOk = await MakeFilePublicAsync(http, fileId, token, cancellationToken).ConfigureAwait(false);
        if (!shareLinkOk) return UploadResult.Failure("upload succeeded but failed to set public sharing");

        // webViewLink format is stable for Drive: opens the in-Drive viewer the same way ShareX does.
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
    private string? _lastApiError;

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

    private async Task<string?> EnsureFolderAsync(HttpClient http, string folderName, string accessToken, CancellationToken cancellationToken)
    {
        var cached = await _config.GetAsync(FolderIdKey, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(cached)) return cached;

        // Look up by name first — drive.file scope only sees app-created files, so this won't
        // collide with unrelated folders the user has named "ShareQ".
        var queryEscaped = Uri.EscapeDataString(
            $"mimeType='application/vnd.google-apps.folder' and name='{folderName.Replace("'", "\\'", StringComparison.Ordinal)}' and trashed=false");
        using (var search = new HttpRequestMessage(HttpMethod.Get, $"drive/v3/files?q={queryEscaped}&fields=files(id,name)"))
        {
            search.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(search, cancellationToken).ConfigureAwait(false);
            var searchBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(searchBody);
                if (doc.RootElement.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
                {
                    var id = files[0].GetProperty("id").GetString();
                    if (!string.IsNullOrEmpty(id))
                    {
                        await _config.SetAsync(FolderIdKey, id, sensitive: false, cancellationToken).ConfigureAwait(false);
                        return id;
                    }
                }
            }
            else
            {
                Debug.WriteLine($"[GoogleDriveUploader] folder search HTTP {(int)response.StatusCode}: {searchBody}");
            }
        }

        // Not found → create.
        using var create = new HttpRequestMessage(HttpMethod.Post, "drive/v3/files");
        create.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var meta = JsonSerializer.Serialize(new
        {
            name = folderName,
            mimeType = "application/vnd.google-apps.folder",
        });
        create.Content = new StringContent(meta, Encoding.UTF8, "application/json");
        using var createResponse = await http.SendAsync(create, cancellationToken).ConfigureAwait(false);
        var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!createResponse.IsSuccessStatusCode)
        {
            // Common cause for 403 here: the Google Drive API isn't enabled on the Cloud project
            // that owns the OAuth client. Body usually contains a link to the enablement page.
            _lastApiError = $"HTTP {(int)createResponse.StatusCode}: {createBody}";
            Debug.WriteLine($"[GoogleDriveUploader] folder create {_lastApiError}");
            return null;
        }
        using var createDoc = JsonDocument.Parse(createBody);
        var newId = createDoc.RootElement.GetProperty("id").GetString();
        if (!string.IsNullOrEmpty(newId))
            await _config.SetAsync(FolderIdKey, newId, sensitive: false, cancellationToken).ConfigureAwait(false);
        return newId;
    }

    private static async Task<(bool Ok, string? FileId, string? Error)> UploadMultipartAsync(
        HttpClient http, string fileName, string folderId, UploadRequest request, string accessToken, CancellationToken cancellationToken)
    {
        // Drive multipart upload: one boundary, two parts — JSON metadata + raw bytes. Sets parent
        // so the file lands in the ShareQ folder. uploadType=multipart is the simplest path that
        // accepts both metadata and bytes in a single round-trip; resumable would only matter for
        // larger files than typical screenshot/clip uploads.
        var boundary = "shareq_" + Guid.NewGuid().ToString("N");
        using var multipart = new MultipartContent("related", boundary);

        var metadata = JsonSerializer.Serialize(new
        {
            name = fileName,
            parents = new[] { folderId },
        });
        var metadataPart = new StringContent(metadata, Encoding.UTF8, "application/json");
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
}
