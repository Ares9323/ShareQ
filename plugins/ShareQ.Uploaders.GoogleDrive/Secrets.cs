namespace ShareQ.Uploaders.GoogleDrive;

/// <summary>
/// Placeholder OAuth credentials for Google Drive. Committed as a build-time fallback so CI /
/// fresh clones compile without needing the real values.
///
/// To get the plugin to actually upload locally:
///   1. https://console.cloud.google.com/apis/credentials → + CREATE CREDENTIALS → OAuth client ID
///   2. Application type: Desktop app
///   3. Copy the resulting Client ID + Client Secret
///   4. Create <c>Secrets.Local.cs</c> next to this file with the real values (same class name,
///      same namespace, full <c>internal static class Secrets</c> block). The .csproj excludes
///      this placeholder file when <c>Secrets.Local.cs</c> exists, so your real values win at
///      compile time. <c>Secrets.Local.cs</c> is git-ignored.
/// </summary>
internal static class Secrets
{
    public const string GoogleClientId = "";
    public const string GoogleClientSecret = "";
}
