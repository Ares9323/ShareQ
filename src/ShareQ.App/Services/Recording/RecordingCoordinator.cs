using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.App.Windows;
using ShareQ.Capture.Recording;
using ShareQ.Core.Domain;
using ShareQ.Storage.Items;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ShareQ.App.Services.Recording;

/// <summary>Top-level orchestrator: pick region, start ffmpeg, show overlay, stop, save to history,
/// notify toast (click → open in folder). Mirrors the capture-region flow but for video.</summary>
public sealed class RecordingCoordinator
{
    private const int FpsDefault = 30;
    private static readonly string OutputFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ShareQ");

    private readonly ScreenRecordingService _recorder;
    private readonly FfmpegLocator _locator;
    private readonly FfmpegDownloader _downloader;
    private readonly IItemStore _items;
    private readonly IToastNotifier _notifier;
    private readonly ILogger<RecordingCoordinator> _logger;
    private RecordingOverlayWindow? _overlay;
    private RecordingFormat _activeFormat;
    private bool _downloadInProgress;

    public RecordingCoordinator(
        ScreenRecordingService recorder,
        FfmpegLocator locator,
        FfmpegDownloader downloader,
        IItemStore items,
        IToastNotifier notifier,
        ILogger<RecordingCoordinator> logger)
    {
        _recorder = recorder;
        _locator = locator;
        _downloader = downloader;
        _items = items;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>Single hotkey toggle: if not recording, prompt for region + start; if recording, stop.</summary>
    public async Task ToggleAsync(RecordingFormat format, CancellationToken cancellationToken)
    {
        if (_recorder.IsRecording)
        {
            await StopAndPersistAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await StartAsync(format, cancellationToken).ConfigureAwait(false);
    }

    private async Task StartAsync(RecordingFormat format, CancellationToken cancellationToken)
    {
        // Ensure ffmpeg is available before we even ask the user to pick a region.
        if (_locator.Find() is null)
        {
            if (!await EnsureFfmpegInstalledAsync(cancellationToken).ConfigureAwait(false)) return;
        }

        // Re-use the region overlay we already use for screenshots.
        var region = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var overlay = new RegionOverlayWindow();
            return overlay.PickRegion();
        }).Task.ConfigureAwait(false);
        if (region is null) return;

        Directory.CreateDirectory(OutputFolder);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var ext = format == RecordingFormat.Mp4 ? "mp4" : "gif";
        var outPath = Path.Combine(OutputFolder, $"shareq-rec-{stamp}.{ext}");

        var options = new RecordingOptions(
            X: region.X, Y: region.Y, Width: region.Width, Height: region.Height,
            Fps: FpsDefault, DrawCursor: true, OutputPath: outPath, Format: format);

        if (!_recorder.TryStart(options))
        {
            _notifier.Show("Recording", "FFmpeg not found. Drop ffmpeg.exe in %APPDATA%/ShareQ/Tools.");
            return;
        }
        _activeFormat = format;

        // Show the overlay (red border + timer + Stop/Pause/Abort) on the captured area.
        Application.Current.Dispatcher.Invoke(() =>
        {
            _overlay = new RecordingOverlayWindow(region.X, region.Y, region.Width, region.Height);
            _overlay.StopRequested += (_, _) => _ = StopAndPersistAsync(CancellationToken.None);
            _overlay.PauseRequested += (_, _) => { _recorder.Pause(); _overlay?.SetPausedVisual(true); };
            _overlay.ResumeRequested += (_, _) => { _recorder.Resume(); _overlay?.SetPausedVisual(false); };
            _overlay.AbortRequested += (_, _) =>
            {
                _recorder.Abort();
                _overlay?.Close();
                _overlay = null;
                _notifier.Show("Recording", "Aborted");
            };
            _overlay.Show();
        });
    }

    /// <summary>Returns true once ffmpeg.exe is available (existing or newly downloaded).</summary>
    private async Task<bool> EnsureFfmpegInstalledAsync(CancellationToken cancellationToken)
    {
        if (_downloadInProgress)
        {
            _notifier.Show("FFmpeg", "Download already in progress…");
            return false;
        }
        var consent = MessageBox.Show(
            "FFmpeg is required for screen recording but isn't installed yet.\n\n" +
            "Download the official build from github.com/ShareX/FFmpeg now?\n\n" +
            $"It will be installed at:\n{FfmpegLocator.ToolsFolder}",
            "Install FFmpeg",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Question,
            MessageBoxResult.OK);
        if (consent != MessageBoxResult.OK) return false;

        _downloadInProgress = true;
        try
        {
            var progress = new Progress<string>(msg => _notifier.Show("FFmpeg", msg));
            _notifier.Show("FFmpeg", "Starting download…");
            var path = await _downloader.DownloadAsync(progress, cancellationToken).ConfigureAwait(false);
            if (path is null)
            {
                _notifier.Show("FFmpeg", "Download failed. Check internet connection and try again.");
                return false;
            }
            _notifier.Show("FFmpeg", "Installed. Recording is ready.");
            return true;
        }
        finally { _downloadInProgress = false; }
    }

    private async Task StopAndPersistAsync(CancellationToken cancellationToken)
    {
        var path = _recorder.CurrentOutputPath;
        await _recorder.StopAsync().ConfigureAwait(true);

        Application.Current.Dispatcher.Invoke(() =>
        {
            _overlay?.Close();
            _overlay = null;
        });

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            _notifier.Show("Recording", "Stopped. Output file not found.");
            return;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var newItem = new NewItem(
            Kind: ItemKind.Image, // closest existing kind for now — we don't have a Video kind yet.
            Source: ItemSource.CaptureRegion,
            CreatedAt: DateTimeOffset.UtcNow,
            Payload: bytes,
            PayloadSize: bytes.LongLength,
            BlobRef: path,
            SearchText: $"Recording {Path.GetFileName(path)}");

        var id = await _items.AddAsync(newItem, cancellationToken).ConfigureAwait(false);

        _notifier.Show($"Recording saved",
            Path.GetFileName(path),
            onClick: () =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            });
        _logger.LogInformation("Recording stored as item {Id} ({Format})", id, _activeFormat);
    }
}
