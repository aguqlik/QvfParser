using System.Text.Json;

namespace QVF.Parser.Core.Model;

public enum SectionAccessStatus
{
    Absent,
    CommentedOut,
    Active,
}

public sealed record SectionAccessInfo
{
    public SectionAccessStatus Status { get; init; }
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Rows { get; init; } = [];
    public IReadOnlyList<string> Columns { get; init; } = [];
    public IReadOnlyList<string> Users { get; init; } = [];
    public bool LoadsFromExternalSource { get; init; }
}

public sealed record DataSource(string Kind, string Target);

/// <summary>A credential-like value. The value itself is never exposed.</summary>
public sealed record CredentialFinding(string Location, string Name, int Length);

public sealed record SecurityInfo
{
    public required SectionAccessInfo SectionAccess { get; init; }

    /// <summary>The app's own hassectionaccess flag (null when not stored).</summary>
    public bool? HasSectionAccessFlag { get; init; }

    /// <summary>True when the flag disagrees with the script (Section Access active or not).</summary>
    public bool SectionAccessMismatch { get; init; }

    public JsonElement? DataConnections { get; init; }
    public IReadOnlyList<DataSource> DataSources { get; init; } = [];
    public IReadOnlyList<string> Identities { get; init; } = [];
    public IReadOnlyList<CredentialFinding> CredentialFindings { get; init; } = [];
}
