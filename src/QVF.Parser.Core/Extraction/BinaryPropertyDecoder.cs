using System.Text;
using System.Text.Json;

namespace QVF.Parser.Core.Extraction;

/// <summary>An object stored in the binary property encoding (e.g. bookmarks).</summary>
internal sealed record BinaryObject(
    string Id,
    string Type,
    IReadOnlyDictionary<string, JsonElement> Properties,
    IReadOnlyDictionary<string, JsonElement> Meta);

/// <summary>
/// Best-effort decoder for objects serialized as <c>JsonProperty</c> records
/// plus a <c>qInfo</c> record, with varint-encoded lengths.
/// Layout: root properties precede qInfo; qMetaDef properties follow it.
/// </summary>
internal static class BinaryPropertyDecoder
{
    private static readonly byte[] JsonPropertyMarker = "JsonProperty"u8.ToArray();
    private static readonly byte[] QInfoMarker = "\u0005qInfo\0"u8.ToArray();
    private static readonly HashSet<string> MetaKeys = ["title", "description", "isExtended", "tags"];

    public static List<BinaryObject> Decode(ReadOnlySpan<byte> payload)
    {
        var events = new List<(int Position, string Name, object Value)>();

        foreach (var start in Occurrences(payload, JsonPropertyMarker))
        {
            if (TryReadProperty(payload, start + JsonPropertyMarker.Length, out var name, out var value))
                events.Add((start, name, value));
        }
        foreach (var start in Occurrences(payload, QInfoMarker))
        {
            if (TryReadQInfo(payload, start + QInfoMarker.Length, out var id, out var type))
                events.Add((start, "qInfo", (id, type)));
        }
        events.Sort((a, b) => a.Position.CompareTo(b.Position));

        var objects = new List<BinaryObject>();
        var pending = new Dictionary<string, JsonElement>();
        Dictionary<string, JsonElement>? lastMeta = null;
        foreach (var (_, name, value) in events)
        {
            if (value is (string id, string type))
            {
                lastMeta = new Dictionary<string, JsonElement>();
                objects.Add(new BinaryObject(id, type, pending, lastMeta));
                pending = new Dictionary<string, JsonElement>();
            }
            else if (lastMeta is not null && MetaKeys.Contains(name) && pending.Count == 0)
            {
                lastMeta[name] = (JsonElement)value;
            }
            else
            {
                pending[name] = (JsonElement)value;
            }
        }
        return objects;
    }

    private static bool TryReadProperty(ReadOnlySpan<byte> data, int index, out string name, out object value)
    {
        name = "";
        value = default(JsonElement);
        if (!TryReadVarint(data, ref index, out _)
            || !data[index..].StartsWith("\0\0\u0001"u8))
            return false;
        index += 3;
        if (!TryReadVarint(data, ref index, out _)
            || !TryReadString(data, ref index, out name)
            || index >= data.Length || data[index] != 0)
            return false;
        index++;
        if (!TryReadVarint(data, ref index, out var length) || index + length > data.Length)
            return false;
        try
        {
            using var document = JsonDocument.Parse(data.Slice(index, length).ToArray());
            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadQInfo(ReadOnlySpan<byte> data, int index, out string id, out string type)
    {
        type = "";
        return TryReadString(data, ref index, out id) && TryReadString(data, ref index, out type);
    }

    private static bool TryReadString(ReadOnlySpan<byte> data, ref int index, out string text)
    {
        text = "";
        if (!TryReadVarint(data, ref index, out var length) || index + length > data.Length)
            return false;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data.Slice(index, length));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
        index += length;
        return true;
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int index, out int value)
    {
        value = 0;
        for (var shift = 0; index < data.Length && shift < 32; shift += 7)
        {
            var current = data[index++];
            value |= (current & 0x7F) << shift;
            if ((current & 0x80) == 0)
                return value >= 0;
        }
        return false;
    }

    private static IEnumerable<int> Occurrences(ReadOnlySpan<byte> data, byte[] marker)
    {
        var positions = new List<int>();
        for (var offset = 0; ;)
        {
            var found = data[offset..].IndexOf(marker);
            if (found < 0)
                return positions;
            positions.Add(offset + found);
            offset += found + marker.Length;
        }
    }
}
