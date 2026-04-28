using Microsoft.Extensions.Logging;

namespace ShareQ.Pipeline.Profiles;

public sealed class PipelineProfileSeeder
{
    private readonly IPipelineProfileStore _store;
    private readonly ILogger<PipelineProfileSeeder> _logger;

    public PipelineProfileSeeder(IPipelineProfileStore store, ILogger<PipelineProfileSeeder> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        // Until we have a profile editor, always re-seed defaults so changes to the built-in
        // pipelines (e.g. adding upload steps) reach existing installs without manual DB surgery.
        foreach (var profile in DefaultPipelineProfiles.All)
        {
            await _store.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Pipeline profile {Id} (re)seeded", profile.Id);
        }
    }
}
