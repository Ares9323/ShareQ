namespace ShareQ.PluginContracts;

/// <summary>
/// Optional interface implemented by uploaders that expose user-tunable settings (target folder,
/// public/private toggle, region, …). The host renders a settings form auto-generated from
/// <see cref="GetSettings"/> descriptors, persisting values via <see cref="IPluginConfigStore"/>
/// using the same keys the upload code reads.
/// </summary>
public interface IConfigurableUploader : IUploader
{
    /// <summary>Declarative shape of the settings form. Static — doesn't depend on auth state.
    /// Dropdown values that need to be loaded from the API at runtime use
    /// <see cref="DropdownSetting.IsAsyncLoaded"/> and are fetched via
    /// <see cref="LoadDropdownOptionsAsync"/> when the dropdown is opened.</summary>
    IReadOnlyList<PluginSettingDescriptor> GetSettings();

    /// <summary>Resolve dynamic dropdown options for an async-loaded setting (e.g. the user's
    /// folders, regions, drives). Called when the dropdown is opened in the host UI; results
    /// are cached per-open. Plugins without async-loaded dropdowns can return an empty list.</summary>
    Task<IReadOnlyList<DropdownOption>> LoadDropdownOptionsAsync(string settingKey, CancellationToken cancellationToken);
}

/// <summary>Base record for settings descriptors. Plugins return a list of these from
/// <see cref="IConfigurableUploader.GetSettings"/>; the host pattern-matches on the concrete
/// subtype to render the right control.</summary>
public abstract record PluginSettingDescriptor(string Key, string Label, string? Description = null);

public sealed record BoolSetting(
    string Key,
    string Label,
    string? Description = null,
    bool DefaultValue = false)
    : PluginSettingDescriptor(Key, Label, Description);

public sealed record DropdownSetting(
    string Key,
    string Label,
    string? Description = null,
    /// <summary>Static set of options known at compile time. Ignored when <see cref="IsAsyncLoaded"/>.</summary>
    IReadOnlyList<DropdownOption>? StaticOptions = null,
    /// <summary>Set true for dropdowns whose options come from the plugin's API (e.g. folders, drives).
    /// The host calls <see cref="IConfigurableUploader.LoadDropdownOptionsAsync"/> on open.</summary>
    bool IsAsyncLoaded = false,
    string? DefaultValue = null)
    : PluginSettingDescriptor(Key, Label, Description);

public sealed record DropdownOption(string Value, string Display);
