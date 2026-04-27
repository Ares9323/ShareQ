namespace ShareQ.Core.Pipeline;

public sealed record PipelineProfile(
    string Id,
    string DisplayName,
    string Trigger,
    IReadOnlyList<PipelineStep> Steps);
