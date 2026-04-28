using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ShareQ.Storage.Settings;

namespace ShareQ.App.ViewModels;

public sealed partial class CaptureDefaultsViewModel : ObservableObject
{
    private const string FolderKey = "capture.folder";
    private const string DelayKey  = "capture.delay_seconds";
    private const string DefaultFolder = "%USERPROFILE%\\Pictures\\ShareQ";

    private readonly ISettingsStore _settings;

    public CaptureDefaultsViewModel(ISettingsStore settings, AfterCaptureViewModel afterCapture)
    {
        _settings = settings;
        AfterCapture = afterCapture;
        _ = LoadAsync();
    }

    public AfterCaptureViewModel AfterCapture { get; }

    [ObservableProperty]
    private string _folder = DefaultFolder;

    [ObservableProperty]
    private int _delaySeconds;

    private bool _suppressPersist;

    public async Task LoadAsync()
    {
        _suppressPersist = true;
        Folder = (await _settings.GetAsync(FolderKey, CancellationToken.None).ConfigureAwait(true)) ?? DefaultFolder;
        var rawDelay = await _settings.GetAsync(DelayKey, CancellationToken.None).ConfigureAwait(true);
        DelaySeconds = int.TryParse(rawDelay, out var d) ? Math.Clamp(d, 0, 30) : 0;
        _suppressPersist = false;
    }

    partial void OnFolderChanged(string value)
    {
        if (_suppressPersist) return;
        _ = _settings.SetAsync(FolderKey, value, sensitive: false, CancellationToken.None);
    }

    partial void OnDelaySecondsChanged(int value)
    {
        if (_suppressPersist) return;
        _ = _settings.SetAsync(DelayKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sensitive: false, CancellationToken.None);
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        // OpenFolderDialog is the modern WPF folder picker (Win32 IFileOpenDialog + FOS_PICKFOLDERS).
        var dialog = new OpenFolderDialog
        {
            Title = "Choose default capture folder",
            Multiselect = false,
            InitialDirectory = ResolvePath(Folder),
        };
        if (dialog.ShowDialog() == true) Folder = dialog.FolderName;
    }

    private static string ResolvePath(string folder)
    {
        var expanded = Environment.ExpandEnvironmentVariables(folder);
        return Directory.Exists(expanded) ? expanded : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    }
}
