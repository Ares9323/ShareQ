namespace ShareQ.PluginContracts;

/// <summary>
/// Optional interface implemented by uploaders that need an interactive sign-in (OAuth, API key
/// dialog, …). Plugins without auth (Catbox, Litterbox, …) skip this and the host UI doesn't
/// render the auth section. Splitting it out of <see cref="IUploader"/> keeps the simple file-host
/// case ergonomic.
/// </summary>
public interface IAuthenticatedUploader : IUploader
{
    /// <summary>Returns the current sign-in state — used by the host UI to render
    /// "Connected as X" / "Not signed in" without forcing a network round-trip on every refresh.</summary>
    Task<AuthState> GetAuthStateAsync(CancellationToken cancellationToken);

    /// <summary>Triggers an interactive sign-in flow. Idempotent: calling when already signed in
    /// either no-ops or re-authorizes; either way leaves the state signed-in on success.</summary>
    Task<AuthResult> SignInAsync(CancellationToken cancellationToken);

    /// <summary>Clears persisted credentials. Subsequent uploads will trigger a fresh sign-in.</summary>
    Task SignOutAsync(CancellationToken cancellationToken);
}

public sealed record AuthState(bool IsSignedIn, string? AccountDisplay = null);

public sealed record AuthResult(bool Ok, string? Error = null)
{
    public static AuthResult Success() => new(true);
    public static AuthResult Failure(string error) => new(false, error);
}
