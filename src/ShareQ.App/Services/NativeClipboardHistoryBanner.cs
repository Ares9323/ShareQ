using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Services;

public sealed class NativeClipboardHistoryBanner
{
    private const string SettingsKey = "ui.banner.native-clipboard-history.choice";
    private const string ChoiceDontAskAgain = "dont-ask-again";
    private const string ChoiceDisabled = "disabled";
    private const string ChoiceLater = "later";

    private readonly NativeClipboardHistoryProbe _probe;
    private readonly ISettingsStore _settings;
    private readonly ILogger<NativeClipboardHistoryBanner> _logger;

    public NativeClipboardHistoryBanner(
        NativeClipboardHistoryProbe probe,
        ISettingsStore settings,
        ILogger<NativeClipboardHistoryBanner> logger)
    {
        _probe = probe;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Show the banner if appropriate. Safe to call on app startup.</summary>
    public async Task EvaluateAsync(CancellationToken cancellationToken)
    {
        var previous = await _settings.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (previous == ChoiceDontAskAgain || previous == ChoiceDisabled) return;

        var enabled = _probe.IsEnabled() ?? false;
        if (!enabled) return;

        _logger.LogInformation("Native Windows clipboard history is enabled; offering to disable.");
        var choice = await PromptAsync(cancellationToken).ConfigureAwait(false);

        switch (choice)
        {
            case BannerChoice.Disable:
                _probe.Disable();
                await _settings.SetAsync(SettingsKey, ChoiceDisabled, sensitive: false, cancellationToken).ConfigureAwait(false);
                break;
            case BannerChoice.DontAskAgain:
                await _settings.SetAsync(SettingsKey, ChoiceDontAskAgain, sensitive: false, cancellationToken).ConfigureAwait(false);
                break;
            case BannerChoice.Later:
            default:
                await _settings.SetAsync(SettingsKey, ChoiceLater, sensitive: false, cancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private static Task<BannerChoice> PromptAsync(CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<BannerChoice>();
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            const string Body = "ShareQ replaces the native Windows clipboard history (Win+V).\n\n" +
                                "Disable it now so ShareQ can take over Win+V?";
            var result = MessageBox.Show(
                Body,
                "ShareQ — Use Win+V?",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            tcs.TrySetResult(result switch
            {
                MessageBoxResult.Yes => BannerChoice.Disable,
                MessageBoxResult.No => BannerChoice.DontAskAgain,
                _ => BannerChoice.Later
            });
        });
        cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        return tcs.Task;
    }

    private enum BannerChoice { Disable, Later, DontAskAgain }
}
