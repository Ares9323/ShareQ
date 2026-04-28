namespace ShareQ.PluginContracts;

/// <summary>
/// Host-driven OAuth2 authorization-code flow with PKCE for desktop apps. Plugins call
/// <see cref="AuthorizeAsync"/> to trigger an interactive sign-in (opens a browser, waits on a
/// local HTTP listener for the redirect, exchanges code → tokens) and <see cref="RefreshAsync"/>
/// to swap a refresh token for a new access token.
/// </summary>
public interface IOAuthHelper
{
    Task<OAuthResult> AuthorizeAsync(OAuthRequest request, CancellationToken cancellationToken);
    Task<OAuthResult> RefreshAsync(OAuthRefreshRequest request, CancellationToken cancellationToken);
}

public sealed record OAuthRequest(
    string AuthorizeUrl,
    string TokenUrl,
    string ClientId,
    IReadOnlyList<string> Scopes,
    /// <summary>Optional. Public clients should leave null and rely on PKCE.</summary>
    string? ClientSecret = null,
    bool UsePkce = true,
    /// <summary>Extra query params appended to the authorize URL (e.g. <c>prompt=consent</c>).</summary>
    IReadOnlyDictionary<string, string>? ExtraAuthorizeParams = null);

public sealed record OAuthRefreshRequest(
    string TokenUrl,
    string ClientId,
    string RefreshToken,
    IReadOnlyList<string> Scopes,
    string? ClientSecret = null);

public sealed record OAuthResult(
    bool Ok,
    string? AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string? Error)
{
    public static OAuthResult Success(string accessToken, string? refreshToken, DateTimeOffset? expiresAt)
        => new(true, accessToken, refreshToken, expiresAt, null);
    public static OAuthResult Failure(string error) => new(false, null, null, null, error);
}
