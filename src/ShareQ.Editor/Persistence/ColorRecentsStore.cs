using System.Text.Json;
using ShareQ.Editor.Model;
using ShareQ.Storage.Settings;

namespace ShareQ.Editor.Persistence;

public sealed class ColorRecentsStore
{
    public const int MaxEntries = 8;
    private const string SettingsKey = "editor.color.recents";

    private readonly ISettingsStore _settings;

    public ColorRecentsStore(ISettingsStore settings)
    {
        _settings = settings;
    }

    public async Task<IReadOnlyList<ShapeColor>> LoadAsync(CancellationToken cancellationToken)
    {
        var raw = await _settings.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw)) return [];

        try
        {
            var entries = JsonSerializer.Deserialize<List<Dto>>(raw);
            if (entries is null) return [];
            return entries.Select(d => new ShapeColor(d.A, d.R, d.G, d.B)).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async Task PushAsync(ShapeColor color, CancellationToken cancellationToken)
    {
        var current = (await LoadAsync(cancellationToken).ConfigureAwait(false)).ToList();
        current.RemoveAll(c => c == color);
        current.Insert(0, color);
        if (current.Count > MaxEntries) current = current.Take(MaxEntries).ToList();

        var dtos = current.Select(c => new Dto(c.A, c.R, c.G, c.B)).ToList();
        var json = JsonSerializer.Serialize(dtos);
        await _settings.SetAsync(SettingsKey, json, sensitive: false, cancellationToken).ConfigureAwait(false);
    }

    private sealed record Dto(byte A, byte R, byte G, byte B);
}
