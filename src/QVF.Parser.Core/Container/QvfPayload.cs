using System.Text.Json;

namespace QVF.Parser.Core.Container;

public enum PayloadKind
{
    Binary,
    Json,
}

/// <summary>One decompressed zlib stream found in the QVF container.</summary>
public sealed class QvfPayload
{
    internal QvfPayload(int index, long offset, int compressedSize, byte[] data, string sha256)
    {
        Index = index;
        Offset = offset;
        CompressedSize = compressedSize;
        Data = data;
        Sha256 = sha256;
        Json = TryParseJson(data);
        Kind = Json is null ? PayloadKind.Binary : PayloadKind.Json;
    }

    /// <summary>1-based position among the distinct streams, in file order.</summary>
    public int Index { get; }
    public long Offset { get; }
    public int CompressedSize { get; }
    public ReadOnlyMemory<byte> Data { get; }
    public string Sha256 { get; }
    public PayloadKind Kind { get; }

    /// <summary>Root element when the payload is a JSON object; otherwise null.</summary>
    public JsonElement? Json { get; }

    private static JsonElement? TryParseJson(ReadOnlySpan<byte> data)
    {
        data = data.TrimEnd((byte)0).Trim(" \t\r\n"u8);
        if (data.IsEmpty || data[0] != (byte)'{')
            return null;
        try
        {
            using var document = JsonDocument.Parse(data.ToArray());
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
