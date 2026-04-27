using System.Text.Json.Nodes;

namespace ShareQ.Core.Pipeline;

public sealed record PipelineStep(
    string TaskId,
    JsonNode? Config = null,
    bool Enabled = true,
    bool AbortOnError = false);
