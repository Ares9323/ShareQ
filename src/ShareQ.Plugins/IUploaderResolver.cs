using ShareQ.PluginContracts;

namespace ShareQ.Plugins;

/// <summary>
/// Host-side abstraction over the active uploader catalog. Implementation lives in <c>ShareQ.App</c>
/// (<c>PluginRegistry</c>) and applies the user's enabled/disabled toggle before returning an
/// uploader, so disabled plugins are invisible to the pipeline.
/// </summary>
public interface IUploaderResolver
{
    Task<IUploader?> ResolveAsync(string uploaderId, CancellationToken cancellationToken);
}
