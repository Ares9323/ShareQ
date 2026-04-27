using ShareQ.Core.Pipeline;

namespace ShareQ.Pipeline.Registry;

public interface IPipelineTaskRegistry
{
    IPipelineTask? Resolve(string taskId);
    IReadOnlyCollection<IPipelineTask> All { get; }
}
