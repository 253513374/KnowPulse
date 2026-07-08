using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace KnowPulse.AiModelManagement;

internal static class AiModelJsonHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? SerializeFallbackAliases(IEnumerable<string>? aliases)
    {
        var values = NormalizeAliases(aliases).ToList();
        return values.Count == 0 ? null : JsonSerializer.Serialize(values, JsonOptions);
    }

    public static IReadOnlyList<string> DeserializeFallbackAliases(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        try
        {
            return NormalizeAliases(JsonSerializer.Deserialize<List<string>>(value, JsonOptions)).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    public static IEnumerable<string> NormalizeAliases(IEnumerable<string>? aliases)
    {
        return (aliases ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }
}
