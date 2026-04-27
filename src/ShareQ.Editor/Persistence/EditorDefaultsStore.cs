using System.Text.Json;
using ShareQ.Editor.Model;
using ShareQ.Editor.Tools;
using ShareQ.Storage.Settings;

namespace ShareQ.Editor.Persistence;

public sealed record EditorDefaults(
    ShapeColor Outline,
    ShapeColor Fill,
    double StrokeWidth,
    EditorTool Tool);

public sealed class EditorDefaultsStore
{
    private const string SettingsKey = "editor.defaults";

    public static readonly EditorDefaults Initial =
        new(ShapeColor.Red, ShapeColor.Transparent, 2, EditorTool.Rectangle);

    private readonly ISettingsStore _settings;

    public EditorDefaultsStore(ISettingsStore settings)
    {
        _settings = settings;
    }

    public async Task<EditorDefaults> LoadAsync(CancellationToken cancellationToken)
    {
        var raw = await _settings.GetAsync(SettingsKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(raw)) return Initial;

        try
        {
            var dto = JsonSerializer.Deserialize<Dto>(raw);
            if (dto is null) return Initial;
            return new EditorDefaults(
                new ShapeColor(dto.OutlineA, dto.OutlineR, dto.OutlineG, dto.OutlineB),
                new ShapeColor(dto.FillA, dto.FillR, dto.FillG, dto.FillB),
                dto.StrokeWidth,
                Enum.IsDefined(typeof(EditorTool), dto.Tool) ? (EditorTool)dto.Tool : Initial.Tool);
        }
        catch (JsonException)
        {
            return Initial;
        }
    }

    public async Task SaveAsync(EditorDefaults defaults, CancellationToken cancellationToken)
    {
        var dto = new Dto(
            defaults.Outline.A, defaults.Outline.R, defaults.Outline.G, defaults.Outline.B,
            defaults.Fill.A, defaults.Fill.R, defaults.Fill.G, defaults.Fill.B,
            defaults.StrokeWidth,
            (int)defaults.Tool);
        var json = JsonSerializer.Serialize(dto);
        await _settings.SetAsync(SettingsKey, json, sensitive: false, cancellationToken).ConfigureAwait(false);
    }

    private sealed record Dto(
        byte OutlineA, byte OutlineR, byte OutlineG, byte OutlineB,
        byte FillA, byte FillR, byte FillG, byte FillB,
        double StrokeWidth,
        int Tool);
}
