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
        foreach (var profile in DefaultPipelineProfiles.All)
        {
            var existing = await _store.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                _logger.LogDebug("Pipeline profile {Id} already present; leaving untouched", profile.Id);
                continue;
            }
            await _store.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Pipeline profile {Id} seeded", profile.Id);
        }
    }
}
