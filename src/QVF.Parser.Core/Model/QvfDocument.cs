using System.Text.Json.Serialization;
using QVF.Parser.Core.Container;

namespace QVF.Parser.Core.Model;

/// <summary>Everything extracted from one QVF file.</summary>
public sealed record QvfDocument
{
    public required SourceInfo Source { get; init; }
    public required AppMetadata App { get; init; }
    public required ScriptInfo Script { get; init; }
    public required IReadOnlyList<Sheet> Sheets { get; init; }
    public required IReadOnlyList<Story> Stories { get; init; }
    public required IReadOnlyList<Bookmark> Bookmarks { get; init; }
    public required IReadOnlyList<Variable> Variables { get; init; }
    public required MasterItems MasterItems { get; init; }
    public required DataModel DataModel { get; init; }
    public required SecurityInfo Security { get; init; }
    public required IReadOnlyList<OtherObject> OtherObjects { get; init; }

    /// <summary>
    /// Decompressed streams, kept only when <see cref="QvfReadOptions.IncludePayloadData"/> is set.
    /// Used by the text-search helpers.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<QvfPayload>? Payloads { get; init; }
}

public sealed record SourceInfo(
    string? Path,
    long SizeBytes,
    string Sha256,
    IReadOnlyList<StreamInfo> Streams);

public sealed record StreamInfo(
    int Index,
    long Offset,
    int CompressedSize,
    int DecompressedSize,
    PayloadKind Kind,
    string? QType,
    string Sha256);
