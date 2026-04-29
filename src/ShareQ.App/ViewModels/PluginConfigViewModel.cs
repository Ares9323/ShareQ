using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.PluginContracts;

namespace ShareQ.App.ViewModels;

/// <summary>
/// VM for a single plugin's settings dialog. Combines the plugin's auth state (if it implements
/// <see cref="IAuthenticatedUploader"/>) with a list of setting rows generated from
/// <see cref="IConfigurableUploader.GetSettings"/>. Each row reads/writes via
/// <see cref="IPluginConfigStore"/> using the plugin's own keys, so the upload code path picks
/// up settings without any extra plumbing.
/// </summary>
public sealed partial class PluginConfigViewModel : ObservableObject
{
    private readonly IUploader _uploader;
    private readonly IPluginConfigStore _config;
    private readonly IAuthenticatedUploader? _auth;
    private readonly IConfigurableUploader? _configurable;

    public PluginConfigViewModel(IUploader uploader, IPluginConfigStore config)
    {
        _uploader = uploader;
        _config = config;
        _auth = uploader as IAuthenticatedUploader;
        _configurable = uploader as IConfigurableUploader;

        DisplayName = uploader.DisplayName;
        SupportsAuth = _auth is not null;
        Settings = [];
    }

    public string DisplayName { get; }
    public bool SupportsAuth { get; }

    [ObservableProperty]
    private string _authStatus = "Loading…";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isSignedIn;

    public ObservableCollection<PluginSettingRowViewModel> Settings { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await RefreshAuthAsync(cancellationToken).ConfigureAwait(true);
        await BuildSettingsAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task RefreshAuthAsync(CancellationToken cancellationToken)
    {
        if (_auth is null) { AuthStatus = "No authentication required"; IsSignedIn = false; return; }
        var state = await _auth.GetAuthStateAsync(cancellationToken).ConfigureAwait(true);
        IsSignedIn = state.IsSignedIn;
        AuthStatus = state.IsSignedIn ? (state.AccountDisplay ?? "Signed in") : "Not signed in";
    }

    private async Task BuildSettingsAsync(CancellationToken cancellationToken)
    {
        Settings.Clear();
        if (_configurable is null) return;
        foreach (var descriptor in _configurable.GetSettings())
        {
            switch (descriptor)
            {
                case BoolSetting bs:
                {
                    var stored = await _config.GetAsync(bs.Key, cancellationToken).ConfigureAwait(true);
                    var value = stored is null
                        ? bs.DefaultValue
                        : string.Equals(stored, "true", StringComparison.OrdinalIgnoreCase);
                    Settings.Add(new PluginSettingBoolRowViewModel(bs, value, _config));
                    break;
                }
                case DropdownSetting ds:
                {
                    var stored = await _config.GetAsync(ds.Key, cancellationToken).ConfigureAwait(true);
                    var initial = stored ?? ds.DefaultValue ?? "";
                    Settings.Add(new PluginSettingDropdownRowViewModel(ds, initial, _config, _configurable));
                    break;
                }
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunAuth))]
    private async Task SignIn()
    {
        if (_auth is null) return;
        IsBusy = true;
        try
        {
            var result = await _auth.SignInAsync(CancellationToken.None).ConfigureAwait(true);
            if (!result.Ok) AuthStatus = $"Sign-in failed: {result.Error}";
            await RefreshAuthAsync(CancellationToken.None).ConfigureAwait(true);
            // Refresh dropdown options now that we're signed in (folders, etc.).
            foreach (var row in Settings.OfType<PluginSettingDropdownRowViewModel>())
                if (row.IsAsyncLoaded) await row.RefreshOptionsAsync().ConfigureAwait(true);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRunAuth))]
    private async Task SignOut()
    {
        if (_auth is null) return;
        IsBusy = true;
        try
        {
            await _auth.SignOutAsync(CancellationToken.None).ConfigureAwait(true);
            await RefreshAuthAsync(CancellationToken.None).ConfigureAwait(true);
        }
        finally { IsBusy = false; }
    }

    private bool CanRunAuth() => SupportsAuth && !IsBusy;
}

/// <summary>Base for a single rendered setting row. Concrete subtypes carry the editable value
/// and persistence callback; the XAML uses <c>DataType</c>-typed templates to pick the right
/// control.</summary>
public abstract class PluginSettingRowViewModel : ObservableObject
{
    public string Key { get; }
    public string Label { get; }
    public string? Description { get; }

    protected PluginSettingRowViewModel(PluginSettingDescriptor d)
    {
        Key = d.Key;
        Label = d.Label;
        Description = d.Description;
    }
}

public sealed partial class PluginSettingBoolRowViewModel : PluginSettingRowViewModel
{
    private readonly IPluginConfigStore _config;
    private bool _suppress;

    public PluginSettingBoolRowViewModel(BoolSetting d, bool value, IPluginConfigStore config) : base(d)
    {
        _config = config;
        _suppress = true;
        Value = value;
        _suppress = false;
    }

    [ObservableProperty]
    private bool _value;

    partial void OnValueChanged(bool value)
    {
        if (_suppress) return;
        _ = _config.SetAsync(Key, value ? "true" : "false", sensitive: false, CancellationToken.None);
    }
}

public sealed partial class PluginSettingDropdownRowViewModel : PluginSettingRowViewModel
{
    private readonly IPluginConfigStore _config;
    private readonly IConfigurableUploader _provider;
    private readonly DropdownSetting _descriptor;
    private bool _suppress;

    public PluginSettingDropdownRowViewModel(DropdownSetting d, string initialValue, IPluginConfigStore config, IConfigurableUploader provider) : base(d)
    {
        _descriptor = d;
        _config = config;
        _provider = provider;
        Options = new ObservableCollection<DropdownOption>();
        IsAsyncLoaded = d.IsAsyncLoaded;
        _suppress = true;
        if (d.StaticOptions is not null)
            foreach (var o in d.StaticOptions) Options.Add(o);
        SelectedValue = initialValue;
        _suppress = false;
        // Pre-populate async-loaded dropdowns with at least the stored value so the combo isn't
        // blank before the user clicks Refresh / opens it.
        if (d.IsAsyncLoaded && !string.IsNullOrEmpty(initialValue) && Options.All(o => o.Value != initialValue))
            Options.Add(new DropdownOption(initialValue, $"(saved: {initialValue})"));
    }

    public bool IsAsyncLoaded { get; }
    public ObservableCollection<DropdownOption> Options { get; }

    [ObservableProperty]
    private string? _selectedValue;

    [ObservableProperty]
    private bool _isLoadingOptions;

    partial void OnSelectedValueChanged(string? value)
    {
        if (_suppress) return;
        _ = _config.SetAsync(Key, value ?? string.Empty, sensitive: false, CancellationToken.None);
    }

    [RelayCommand]
    public async Task RefreshOptionsAsync()
    {
        if (!IsAsyncLoaded) return;
        IsLoadingOptions = true;
        try
        {
            var fetched = await _provider.LoadDropdownOptionsAsync(Key, CancellationToken.None).ConfigureAwait(true);
            var preserved = SelectedValue;
            _suppress = true;
            Options.Clear();
            foreach (var o in fetched) Options.Add(o);
            // Re-select the preserved value if still present; otherwise leave blank so the user
            // notices and picks a new one.
            if (!string.IsNullOrEmpty(preserved) && Options.Any(o => o.Value == preserved))
                SelectedValue = preserved;
            _suppress = false;
        }
        finally { IsLoadingOptions = false; }
    }
}
