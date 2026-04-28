namespace ShareQ.PluginContracts;

/// <summary>
/// Optional extension to <see cref="IUploader"/> for uploaders that need user-supplied settings
/// (API keys, target folders, ...). The host renders a generic form from <see cref="ConfigSchema"/>
/// and persists values via <see cref="IPluginConfigStore"/> so plugins stay UI-framework agnostic.
/// </summary>
public interface IConfigurableUploader : IUploader
{
    IReadOnlyList<ConfigField> ConfigSchema { get; }
}

public sealed record ConfigField(
    string Key,
    string DisplayName,
    ConfigFieldType Type,
    bool Required = false,
    string? Description = null,
    string? DefaultValue = null);

public enum ConfigFieldType
{
    Text,
    /// <summary>Stored encrypted via DPAPI; rendered as a password box.</summary>
    Secret,
    Number,
    Boolean,
    /// <summary>Folder picker (rendered as a text box + browse button).</summary>
    Folder,
    /// <summary>Triggers an OAuth flow when clicked. Plugin invokes <c>IOAuthHelper</c> to drive it.</summary>
    OAuthButton,
}
