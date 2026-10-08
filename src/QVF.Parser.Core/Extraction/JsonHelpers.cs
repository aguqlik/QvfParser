using System.Text.Json;

namespace QVF.Parser.Core.Extraction;

internal static class JsonHelpers
{
    /// <summary>Follows a path of property names; null if any step is missing.</summary>
    public static JsonElement? Path(this JsonElement element, params ReadOnlySpan<string> names)
    {
        var current = element;
        foreach (var name in names)
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
                return null;
        }
        return current;
    }

    public static JsonElement? Path(this JsonElement? element, params ReadOnlySpan<string> names) =>
        element is { } value ? value.Path(names) : null;

    public static string? String(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    public static string? String(this JsonElement element, string name) => element.Path(name).String();

    public static bool? Bool(this JsonElement? element) => element?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    public static long? Long(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : null;

    public static double? Double(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Number } value ? value.GetDouble() : null;

    public static IEnumerable<JsonElement> Items(this JsonElement? element) =>
        element is { ValueKind: JsonValueKind.Array } value ? value.EnumerateArray() : [];

    public static List<string> Strings(this JsonElement? element) =>
        element.Items().Where(e => e.ValueKind == JsonValueKind.String)
            .Select(e => e.GetString()!)
            .Where(s => s.Length > 0)
            .ToList();

    /// <summary>Titles are either plain strings or {qStringExpression: {qExpr}}.</summary>
    public static string? TextOf(this JsonElement? element) => element?.ValueKind switch
    {
        JsonValueKind.String => element.Value.GetString(),
        JsonValueKind.Object when element.Path("qStringExpression", "qExpr").String() is { } expr => "=" + expr,
        _ => null,
    };
}
