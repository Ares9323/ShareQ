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
        // Built-ins that have disappeared from DefaultPipelineProfiles.All (e.g. a feature like
        // OCR was tried, persisted, then dropped from the codebase) get DEMOTED to custom rather
        // than deleted: a user might have customised the steps and we don't want to silently
        // throw their work away. After the demotion the orphan shows up under the "Custom" tab,
        // where the user can either keep it or remove it manually with the trash icon.
        // Real customs (IsBuiltIn=false) are never touched — they're already under user control.
        var defaultIds = new HashSet<string>(DefaultPipelineProfiles.All.Select(p => p.Id), StringComparer.Ordinal);
        var existingProfiles = await _store.ListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var existing in existingProfiles)
        {
            if (!existing.IsBuiltIn) continue;
            if (defaultIds.Contains(existing.Id)) continue;
            await _store.UpsertAsync(existing with { IsBuiltIn = false }, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Pipeline profile {Id} demoted to custom (no longer a built-in default).", existing.Id);
        }

        // The profile in DB is the source of truth (user-edited steps must survive restarts), so
        // we only insert defaults when the profile is missing entirely. Older installs may have
        // a profile row predating the Hotkey / IsBuiltIn fields — for those we run a non-
        // destructive upgrade that fills only the missing metadata without touching steps.
        foreach (var profile in DefaultPipelineProfiles.All)
        {
            var existing = await _store.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                await _store.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Pipeline profile {Id} seeded with defaults.", profile.Id);
                continue;
            }

            // Don't auto-fill Hotkey: a missing binding can mean "user explicitly cleared it" and
            // we must respect that across restarts. IsBuiltIn is metadata the user can't change,
            // so it's safe to upgrade in-place.
            var upgraded = existing;
            if (!upgraded.IsBuiltIn && profile.IsBuiltIn)
                upgraded = upgraded with { IsBuiltIn = true };

            if (!ReferenceEquals(upgraded, existing))
            {
                await _store.UpsertAsync(upgraded, cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Pipeline profile {Id} upgraded with default hotkey / built-in flag.", profile.Id);
            }
            else
            {
                _logger.LogDebug("Pipeline profile {Id} already present; preserving user changes.", profile.Id);
            }
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
