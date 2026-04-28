using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Profiles;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ShareQ.App.ViewModels;

/// <summary>
/// Backs the "After capture tasks" list in Settings → Capture. The list is read directly from the
/// stored <c>region-capture</c> profile; reorders / enable-toggles persist back via
/// <see cref="IPipelineProfileStore"/>, so the next pipeline run picks them up. The seeder no
/// longer overwrites user customisations on each restart, so user changes survive.
/// </summary>
public sealed partial class AfterCaptureViewModel : ObservableObject
{
    private static readonly Dictionary<string, (string Display, string Description)> StepLabels = new(StringComparer.Ordinal)
    {
        ["open-editor"]     = ("Open editor",                   "Pause the pipeline and open the annotation editor on the captured bytes. On save, subsequent steps see the edited image; on cancel, the original is kept."),
        ["save"]            = ("Save to file",                  "Write the screenshot to disk under the configured capture folder."),
        ["add-to-history"]  = ("Add to clipboard history",      "Index the capture in ShareQ's history so it shows up in Win+V."),
        ["copy-image"]      = ("Copy image to clipboard",       "Place the bitmap on the clipboard right away (overwritten by the URL on upload success)."),
        ["upload"]          = ("Upload to selected hosts",      "Run the upload pipeline against the uploaders selected in Settings → Uploaders."),
        ["copy-url"]        = ("Copy URL to clipboard",         "Replace the image on the clipboard with the URL(s) returned by the upload step."),
        ["toast"]           = ("Show toast notification",       "Display a Windows toast confirming the capture / upload."),
    };

    private readonly IPipelineProfileStore _profiles;
    private readonly PipelineProfileSeeder _seeder;
    private bool _isReloading;

    public AfterCaptureViewModel(IPipelineProfileStore profiles, PipelineProfileSeeder seeder)
    {
        _profiles = profiles;
        _seeder = seeder;
        Items = [];
        _ = ReloadAsync();
    }

    public ObservableCollection<AfterCaptureItemViewModel> Items { get; }

    public async Task ReloadAsync()
    {
        _isReloading = true;
        try
        {
            Items.Clear();
            var profile = await _profiles.GetAsync(DefaultPipelineProfiles.RegionCaptureId, CancellationToken.None).ConfigureAwait(true);
            if (profile is null) return;
            foreach (var step in profile.Steps)
            {
                if (string.IsNullOrEmpty(step.Id)) continue; // mandatory plumbing steps
                var (display, description) = StepLabels.TryGetValue(step.Id, out var l)
                    ? l
                    : (step.Id, $"Pipeline step {step.TaskId}");
                Items.Add(new AfterCaptureItemViewModel(
                    step.Id, display, description, step.Enabled,
                    onEnabledChanged: (item, value) => _ = OnEnabledChangedAsync(item, value),
                    onMove: (item, delta) => _ = OnMoveAsync(item, delta)));
            }
            UpdateMoveFlags();
        }
        finally { _isReloading = false; }
    }

    private async Task OnEnabledChangedAsync(AfterCaptureItemViewModel item, bool value)
    {
        if (_isReloading) return;
        await UpdateProfileAsync(steps =>
        {
            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i].Id == item.StepId) steps[i] = steps[i] with { Enabled = value };
            }
        }).ConfigureAwait(true);
    }

    private async Task OnMoveAsync(AfterCaptureItemViewModel item, int delta)
    {
        if (_isReloading) return;
        var index = Items.IndexOf(item);
        var newIndex = index + delta;
        if (index < 0 || newIndex < 0 || newIndex >= Items.Count) return;

        // Reorder UI immediately for snappy feedback, then persist the same change to the
        // underlying profile (only the toggleable subset moves; mandatory plumbing steps stay
        // wherever they were originally defined).
        Items.Move(index, newIndex);
        UpdateMoveFlags();
        await UpdateProfileAsync(steps => ReorderToggleable(steps, item.StepId, delta)).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        var confirm = MessageBox.Show(
            "Reset the region-capture pipeline to the default order and enabled state?\n\nThis discards any custom reordering or per-step toggles you've made here.",
            "Reset capture pipeline",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;

        await _seeder.ResetToDefaultsAsync(DefaultPipelineProfiles.RegionCaptureId, CancellationToken.None).ConfigureAwait(true);
        await ReloadAsync().ConfigureAwait(true);
    }

    private async Task UpdateProfileAsync(Action<List<PipelineStep>> mutate)
    {
        var profile = await _profiles.GetAsync(DefaultPipelineProfiles.RegionCaptureId, CancellationToken.None).ConfigureAwait(true);
        if (profile is null) return;
        var steps = profile.Steps.ToList();
        mutate(steps);
        var updated = profile with { Steps = steps };
        await _profiles.UpsertAsync(updated, CancellationToken.None).ConfigureAwait(true);
    }

    private static void ReorderToggleable(List<PipelineStep> steps, string stepId, int delta)
    {
        // Toggleable steps (non-null Id) form a virtual ordered list. We move stepId by `delta`
        // within that list; mandatory plumbing steps (null Id) keep their absolute positions.
        var toggleable = new List<int>();
        for (var i = 0; i < steps.Count; i++)
            if (!string.IsNullOrEmpty(steps[i].Id)) toggleable.Add(i);

        var pos = toggleable.FindIndex(i => steps[i].Id == stepId);
        if (pos < 0) return;
        var newPos = pos + delta;
        if (newPos < 0 || newPos >= toggleable.Count) return;

        var srcIdx = toggleable[pos];
        var dstIdx = toggleable[newPos];
        var item = steps[srcIdx];
        steps.RemoveAt(srcIdx);
        steps.Insert(dstIdx, item);
    }

    private void UpdateMoveFlags()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].CanMoveUp = i > 0;
            Items[i].CanMoveDown = i < Items.Count - 1;
        }
    }
}
