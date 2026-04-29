namespace ShareQ.Uploaders.OneDrive;

/// <summary>
/// Placeholder OAuth client_id for OneDrive. Committed as a build-time fallback so CI / fresh
/// clones compile without needing the real value.
///
/// To get the plugin to actually upload locally:
///   1. https://entra.microsoft.com/ → Applications → App registrations → + New registration
///   2. Supported account types: "Personal Microsoft accounts only" (matches the consumers
///      authority in <c>OneDriveUploader.cs</c>)
///   3. Authentication → Allow public client flows: Yes
///   4. API permissions → Microsoft Graph → Delegated → Files.ReadWrite, offline_access
///   5. Create <c>Secrets.Local.cs</c> next to this file with the real Application (client) ID.
///      The .csproj excludes this placeholder when <c>Secrets.Local.cs</c> exists.
///      <c>Secrets.Local.cs</c> is git-ignored.
/// </summary>
internal static class Secrets
{
    public const string MicrosoftClientId = "";
}
