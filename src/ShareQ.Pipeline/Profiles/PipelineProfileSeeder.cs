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
        // Now that the user can reorder / toggle pipeline steps from Settings, the profile in DB
        // is the source of truth. We only seed defaults when the profile is genuinely missing —
        // user customisations survive across restarts.
        foreach (var profile in DefaultPipelineProfiles.All)
        {
            var existing = await _store.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                _logger.LogDebug("Pipeline profile {Id} already present; preserving user changes.", profile.Id);
                continue;
            }
            await _store.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Pipeline profile {Id} seeded with defaults.", profile.Id);
        }
    }

    /// <summary>Force-overwrite a profile with its default definition. Used by the "Reset to
    /// defaults" button in Settings for users who have made the pipeline unusable.</summary>
    public async Task ResetToDefaultsAsync(string profileId, CancellationToken cancellationToken)
    {
        var profile = DefaultPipelineProfiles.All.FirstOrDefault(p => p.Id == profileId)
            ?? throw new ArgumentException($"unknown profile id '{profileId}'", nameof(profileId));
        await _store.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Pipeline profile {Id} reset to defaults.", profileId);
    }
}
