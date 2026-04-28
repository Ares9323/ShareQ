using System.Text.Json;
using System.Text.Json.Nodes;
using ShareQ.Core.Pipeline;

namespace ShareQ.Pipeline.Profiles;

public static class PipelineProfileSerializer
{
    private static readonly JsonSerializerOptions WriterOptions = new() { WriteIndented = false };

    public static string SerializeSteps(IEnumerable<PipelineStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var array = new JsonArray();
        foreach (var step in steps)
        {
            var obj = new JsonObject
            {
                ["task_id"] = step.TaskId,
                ["enabled"] = step.Enabled,
                ["abort_on_error"] = step.AbortOnError
            };
            if (!string.IsNullOrEmpty(step.Id)) obj["id"] = step.Id;
            if (step.Config is not null) obj["config"] = step.Config.DeepClone();
            array.Add(obj);
        }
        var root = new JsonObject { ["tasks"] = array };
        return root.ToJsonString(WriterOptions);
    }

    public static IReadOnlyList<PipelineStep> DeserializeSteps(string tasksJson)
    {
        ArgumentException.ThrowIfNullOrEmpty(tasksJson);
        var root = JsonNode.Parse(tasksJson) as JsonObject
            ?? throw new InvalidDataException("Expected JSON object at root.");
        var tasks = root["tasks"] as JsonArray
            ?? throw new InvalidDataException("Expected 'tasks' array.");

        var result = new List<PipelineStep>(tasks.Count);
        foreach (var node in tasks)
        {
            if (node is not JsonObject obj) continue;
            var taskId = (string?)obj["task_id"]
                ?? throw new InvalidDataException("Task entry missing 'task_id'.");
            var enabled = (bool?)obj["enabled"] ?? true;
            var abortOnError = (bool?)obj["abort_on_error"] ?? false;
            var id = (string?)obj["id"];
            var config = obj["config"]?.DeepClone();
            result.Add(new PipelineStep(taskId, config, enabled, abortOnError, id));
        }
        return result;
    }
}
