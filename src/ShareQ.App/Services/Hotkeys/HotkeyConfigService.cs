using ShareQ.Core.Pipeline;
using ShareQ.Hotkeys;
using ShareQ.Pipeline.Profiles;

namespace ShareQ.App.Services.Hotkeys;

/// <summary>
/// Catalog of user-rebindable hotkeys. After the workflow refactor, the source of truth is the
/// <see cref="PipelineProfile.Hotkey"/> field on each profile; this service is a thin adapter that
/// lets the existing Settings → Hotkeys UI keep working until that tab is folded into the
/// Workflows view in the next sprint.
///
/// "Unbound" is represented as <see cref="HotkeyModifiers.None"/> + <c>VirtualKey == 0</c> so
/// callers don't have to deal with a nullable binding type. Use <see cref="ClearAsync"/> to remove
/// a binding the user no longer wants.
/// </summary>
public sealed class HotkeyConfigService
{
    public sealed record HotkeyEntry(string Id, string DisplayName, HotkeyModifiers DefaultModifiers, uint DefaultVirtualKey);

    private readonly IPipelineProfileStore _profiles;

    public HotkeyConfigService(IPipelineProfileStore profiles)
    {
        _profiles = profiles;
    }

    public event EventHandler<HotkeyDefinition>? Changed;

    /// <summary>Workflow ids that the user can bind to a hotkey. Built from every default profile
    /// whose <see cref="PipelineProfile.Trigger"/> starts with <c>"hotkey:"</c> — that includes
    /// profiles shipped without a default binding (e.g. <c>open-screenshot-folder</c>).</summary>
    public static readonly IReadOnlyList<HotkeyEntry> Catalog = BuildCatalog();

    private static IReadOnlyList<HotkeyEntry> BuildCatalog()
    {
        var list = new List<HotkeyEntry>();
        foreach (var profile in DefaultPipelineProfiles.All)
        {
            if (!profile.Trigger.StartsWith("hotkey:", StringComparison.Ordinal)) continue;
            var mods = profile.Hotkey is { } hk ? (HotkeyModifiers)hk.Modifiers : HotkeyModifiers.None;
            var vk = profile.Hotkey?.VirtualKey ?? 0u;
            list.Add(new HotkeyEntry(profile.Id, profile.DisplayName, mods, vk));
        }
        return list;
    }

    /// <summary>Returns the user's current binding for a workflow, or the unbound sentinel
    /// (<see cref="HotkeyModifiers.None"/>, VK 0) when the user has cleared it. The seeder ensures
    /// the profile exists by start-up; the fallback covers race conditions where the seed runs
    /// after a settings tab has already opened.</summary>
    public async Task<HotkeyDefinition> GetEffectiveAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var profile = await _profiles.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (profile is not null)
        {
            if (profile.Hotkey is { } binding)
                return new HotkeyDefinition(id, (HotkeyModifiers)binding.Modifiers, binding.VirtualKey);
            return new HotkeyDefinition(id, HotkeyModifiers.None, 0);
        }
        var entry = Catalog.FirstOrDefault(e => e.Id == id)
            ?? throw new ArgumentException($"unknown workflow id '{id}'", nameof(id));
        return new HotkeyDefinition(id, entry.DefaultModifiers, entry.DefaultVirtualKey);
    }

    public async Task UpdateAsync(string id, HotkeyModifiers modifiers, uint virtualKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var profile = await _profiles.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (profile is null) throw new ArgumentException($"workflow '{id}' not found", nameof(id));
        var newBinding = virtualKey == 0 ? null : new HotkeyBinding((int)modifiers, virtualKey);
        var updated = profile with { Hotkey = newBinding };
        await _profiles.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, new HotkeyDefinition(id, modifiers, virtualKey));
    }

    /// <summary>Removes the workflow's hotkey binding. The runtime hook unregisters; the workflow
    /// remains and can still be invoked from the tray / popup / future menu entries.</summary>
    public Task ClearAsync(string id, CancellationToken cancellationToken)
        => UpdateAsync(id, HotkeyModifiers.None, 0, cancellationToken);

    public async Task ResetAsync(string id, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        var entry = Catalog.FirstOrDefault(e => e.Id == id)
            ?? throw new ArgumentException($"unknown workflow id '{id}'", nameof(id));
        await UpdateAsync(id, entry.DefaultModifiers, entry.DefaultVirtualKey, cancellationToken).ConfigureAwait(false);
    }
}
