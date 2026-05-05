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
    private const string SubFolderPatternKey = "capture.subfolder_pattern";
    private const string ImageFormatKey = "capture.image_format";
    private const string JpegQualityKey = "capture.jpeg_quality";
    private const string AutoJpegKey = "capture.auto_jpeg";
    private const string AutoJpegThresholdKbKey = "capture.auto_jpeg_threshold_kb";
    private const string ExternalEditorKey = "editor.external_command";
    private const string DefaultFolder = "%USERPROFILE%\\Pictures\\ShareQ";

    public static IReadOnlyList<string> ImageFormats { get; } = new[] { "PNG", "JPEG", "BMP", "GIF" };

    private readonly ISettingsStore _settings;

    public CaptureDefaultsViewModel(ISettingsStore settings)
    {
        _settings = settings;
        _ = LoadAsync();
    }

    [ObservableProperty]
    private string _folder = DefaultFolder;

    [ObservableProperty]
    private int _delaySeconds;

    /// <summary>Optional sub-folder pattern appended under <see cref="Folder"/> at save time.
    /// Supports ShareX-style tokens (<c>%y</c>, <c>%mo</c>, <c>%d</c>, <c>%h</c>, <c>%mi</c>, …).
    /// Empty = no sub-folder. Persisted to <c>capture.subfolder_pattern</c>.</summary>
    [ObservableProperty]
    private string _subFolderPattern = string.Empty;

    /// <summary>Output format for captures + editor saves. PascalCase string ("PNG"/"JPEG"/
    /// "BMP"/"GIF") for the dropdown binding; the persisted setting (<c>capture.image_format</c>)
    /// stores the same string and is parsed back through <see cref="ShareQ.Core.Imaging.ImageFormatExtensions.TryParse"/>
    /// at consume time.</summary>
    [ObservableProperty]
    private string _imageFormat = "PNG";

    /// <summary>JPEG quality 1..100. Visible for all formats but only applied when the chosen
    /// format encodes JPEG (either <see cref="ImageFormat"/>=JPEG or auto-fallback kicked in).</summary>
    [ObservableProperty]
    private int _jpegQuality = 90;

    /// <summary>When true and <see cref="ImageFormat"/>=PNG, captures whose PNG-encoded payload
    /// would exceed <see cref="AutoJpegThresholdKb"/> get re-encoded as JPEG instead. Mirrors
    /// ShareX's <c>ImageAutoUseJPEG</c> + <c>ImageAutoUseJPEGSize</c> pair so screenshots of
    /// large photographic content (gradients, video stills) don't blow up uploads.</summary>
    [ObservableProperty]
    private bool _autoJpeg = true;

    [ObservableProperty]
    private int _autoJpegThresholdKb = 2048;

    /// <summary>Path / command to launch when the user picks "Edit text in external editor"
    /// from the clipboard popup. Empty = use Windows default for <c>.txt</c> (associated app
    /// via ShellExecute). Examples: <c>code.cmd</c>, <c>"C:\Program Files\Notepad++\notepad++.exe"</c>,
    /// <c>notepad</c>. Persisted to <c>editor.external_command</c>.</summary>
    [ObservableProperty]
    private string _externalEditorCommand = string.Empty;

    private bool _suppressPersist;

    public async Task LoadAsync()
    {
        _suppressPersist = true;
        Folder = (await _settings.GetAsync(FolderKey, CancellationToken.None).ConfigureAwait(true)) ?? DefaultFolder;
        var rawDelay = await _settings.GetAsync(DelayKey, CancellationToken.None).ConfigureAwait(true);
        DelaySeconds = int.TryParse(rawDelay, out var d) ? Math.Clamp(d, 0, 30) : 0;
        SubFolderPattern = (await _settings.GetAsync(SubFolderPatternKey, CancellationToken.None).ConfigureAwait(true)) ?? string.Empty;

        var rawFormat = await _settings.GetAsync(ImageFormatKey, CancellationToken.None).ConfigureAwait(true);
        ImageFormat = NormaliseFormat(rawFormat);
        var rawQuality = await _settings.GetAsync(JpegQualityKey, CancellationToken.None).ConfigureAwait(true);
        JpegQuality = int.TryParse(rawQuality, out var q) ? Math.Clamp(q, 1, 100) : 90;
        var rawAuto = await _settings.GetAsync(AutoJpegKey, CancellationToken.None).ConfigureAwait(true);
        AutoJpeg = rawAuto is null || bool.TryParse(rawAuto, out var a) && a; // default true
        ExternalEditorCommand = (await _settings.GetAsync(ExternalEditorKey, CancellationToken.None).ConfigureAwait(true)) ?? string.Empty;
        var rawThreshold = await _settings.GetAsync(AutoJpegThresholdKbKey, CancellationToken.None).ConfigureAwait(true);
        AutoJpegThresholdKb = int.TryParse(rawThreshold, out var t) ? Math.Max(64, t) : 2048;

        _suppressPersist = false;
    }

    /// <summary>Coerce a stored format string back into the canonical PascalCase the dropdown
    /// expects. Falls back to "PNG" for unknown / null values so a corrupted setting can't
    /// leave the UI with a blank selection.</summary>
    private static string NormaliseFormat(string? raw)
    {
        var parsed = ShareQ.Core.Imaging.ImageFormatExtensions.TryParse(raw);
        return parsed switch
        {
            ShareQ.Core.Imaging.ImageFormat.Png  => "PNG",
            ShareQ.Core.Imaging.ImageFormat.Jpeg => "JPEG",
            ShareQ.Core.Imaging.ImageFormat.Bmp  => "BMP",
            ShareQ.Core.Imaging.ImageFormat.Gif  => "GIF",
            _ => "PNG",
        };
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

    partial void OnSubFolderPatternChanged(string value)
    {
        if (_suppressPersist) return;
        _ = _settings.SetAsync(SubFolderPatternKey, value, sensitive: false, CancellationToken.None);
    }

    partial void OnImageFormatChanged(string value)
    {
        if (_suppressPersist) return;
        _ = _settings.SetAsync(ImageFormatKey, value, sensitive: false, CancellationToken.None);
    }

    partial void OnJpegQualityChanged(int value)
    {
        if (_suppressPersist) return;
        _ = _settings.SetAsync(JpegQualityKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sensitive: false, CancellationToken.None);
    }

    partial void OnAutoJpegChanged(bool value)
    {
        if (_suppressPersist) return;
        _ = _settings.SetAsync(AutoJpegKey, value.ToString(),
            sensitive: false, CancellationToken.None);
    }

    partial void OnAutoJpegThresholdKbChanged(int value)
    {
        if (_suppressPersist) return;
        _ = _settings.SetAsync(AutoJpegThresholdKbKey, value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            sensitive: false, CancellationToken.None);
    }

    partial void OnExternalEditorCommandChanged(string value)
    {
        if (_suppressPersist) return;
        _ = _settings.SetAsync(ExternalEditorKey, value, sensitive: false, CancellationToken.None);
    }

    [RelayCommand]
    private void BrowseExternalEditor()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose external text editor",
            Filter = "Programs (*.exe;*.cmd;*.bat)|*.exe;*.cmd;*.bat|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() == true) ExternalEditorCommand = dialog.FileName;
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
