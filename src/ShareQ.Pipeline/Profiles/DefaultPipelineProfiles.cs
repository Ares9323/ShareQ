using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Tasks;

namespace ShareQ.Pipeline.Profiles;

public static class DefaultPipelineProfiles
{
    public const string OnClipboardId = "on-clipboard";

    public static IReadOnlyList<PipelineProfile> All { get; } = BuildAll();

    private static IReadOnlyList<PipelineProfile> BuildAll() =>
    [
        new PipelineProfile(
            Id: OnClipboardId,
            DisplayName: "On clipboard",
            Trigger: "event:clipboard",
            Steps:
            [
                new PipelineStep(AddToHistoryTask.TaskId)
            ])
        // Capture-driven profiles (quick-share, quick-save, fullscreen, window, recorder)
        // are added in M2b alongside their capture tasks.
    ];
}
