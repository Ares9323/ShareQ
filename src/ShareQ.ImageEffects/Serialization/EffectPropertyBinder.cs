using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShareQ.ImageEffects.Serialization;

/// <summary>Reflective copy of JSON object properties into a live <see cref="ImageEffect"/>.
/// Both our own preset reader and the ShareX <c>.sxie</c> importer go through this — they
/// just feed it different <see cref="JsonElement"/> shapes (camelCase vs PascalCase). A
/// missing property is fine (effect keeps its default); an incompatible value is logged-and-
/// skipped rather than aborting the load, so one bad slider doesn't poison a whole preset.</summary>
internal static class EffectPropertyBinder
{
    public static void Apply(ImageEffect effect, JsonElement source, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        if (source.ValueKind != JsonValueKind.Object) return;
        options ??= _defaults;

        var props = EffectPresetSerializer.PropertyCache.For(effect.GetType());
        foreach (var prop in props)
        {
            var attr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            var camel = attr?.Name ?? options.PropertyNamingPolicy?.ConvertName(prop.Name) ?? prop.Name;

            // Try camelCase first (our format), then raw PropertyName (ShareX format).
            if (!source.TryGetProperty(camel, out var value)
                && !source.TryGetProperty(prop.Name, out value)) continue;

            try
            {
                var typed = value.Deserialize(prop.PropertyType, options);
                prop.SetValue(effect, typed);
            }
            catch (JsonException)
            {
                // Tolerate per-property mismatches — a typo'd numeric, a renamed enum value,
                // a removed parameter from an older ShareX build. Rest of the chain still loads.
            }
        }

        // ShareX-legacy "Point" strings: a JSON entry like {"Offset": "0, 32"} corresponds to
        // OffsetX/OffsetY ints on our side. We don't expose Point as a property type (too rare
        // to justify a dedicated kind), so we recognise the pattern at bind-time: any pair of
        // sibling <Foo>X / <Foo>Y int properties picks up "<Foo>" string when present.
        foreach (var prop in props)
        {
            if (prop.PropertyType != typeof(int) || !prop.Name.EndsWith('X')) continue;
            var baseName = prop.Name[..^1];
            var matchY = Array.Find(props, p => p.Name == baseName + "Y" && p.PropertyType == typeof(int));
            if (matchY is null) continue;

            var camelBase = options.PropertyNamingPolicy?.ConvertName(baseName) ?? baseName;
            if (!source.TryGetProperty(camelBase, out var pointVal)
                && !source.TryGetProperty(baseName, out pointVal)) continue;
            if (pointVal.ValueKind != JsonValueKind.String) continue;

            var raw = pointVal.GetString();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var parts = raw.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length == 2
                && int.TryParse(parts[0], out var x)
                && int.TryParse(parts[1], out var y))
            {
                prop.SetValue(effect, x);
                matchY.SetValue(effect, y);
            }
        }
    }

    private static readonly JsonSerializerOptions _defaults = BuildDefaults();

    /// <summary>Effect-property options. The custom converters cover the types that don't
    /// round-trip through default STJ — colours, paddings — so an effect can declare a
    /// <c>SKColor Background</c> or a <c>Padding Margin</c> and the binder Just Works.</summary>
    private static JsonSerializerOptions BuildDefaults()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };
        opts.Converters.Add(new SkColorJsonConverter());
        opts.Converters.Add(new PaddingJsonConverter());
        opts.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: true));
        return opts;
    }
}
