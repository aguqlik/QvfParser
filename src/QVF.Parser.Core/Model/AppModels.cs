using System.Text.Json;

namespace QVF.Parser.Core.Model;

/// <summary>App-level metadata. Fields the QVF does not store are null.</summary>
public sealed record AppMetadata
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Owner { get; init; }
    public string? OwnerId { get; init; }
    public string? CreatedDate { get; init; }
    public string? ModifiedDate { get; init; }
    public string? LastReloadTime { get; init; }
    public string? SavedInProductVersion { get; init; }
    public string? Usage { get; init; }
    public bool? Encrypted { get; init; }
    public bool? Published { get; init; }
    public bool? HasSectionAccess { get; init; }
    public string? MigrationHash { get; init; }
    public string? ResourceType { get; init; }
}

public sealed record ScriptTab(string Name, int StartLine, string Text);

public sealed record ScriptInfo
{
    public bool Present { get; init; }

    /// <summary>The load script exactly as stored (line endings preserved).</summary>
    public string? Text { get; init; }

    /// <summary>Where <see cref="Text"/> came from: "json" (qScript) or "binary".</summary>
    public string? Source { get; init; }

    public int LineCount { get; init; }
    public IReadOnlyList<ScriptTab> Tabs { get; init; } = [];

    /// <summary>
    /// Second copy of the script stored in the binary app stream, if found. In the
    /// samples it differs from <see cref="Text"/> when the app was edited after its
    /// last reload, which suggests it is the script as of that reload (unconfirmed).
    /// </summary>
    public string? BinaryCopyText { get; init; }

    /// <summary>
    /// True when both copies exist and match, false when they differ,
    /// null when only one copy was found.
    /// </summary>
    public bool? ScriptCopiesConsistent { get; init; }
}

/// <summary>A visualization or other generic object, possibly with children.</summary>
public sealed record SheetObject
{
    public string? Id { get; init; }
    public string? Type { get; init; }
    public string? Visualization { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> Fields { get; init; } = [];
    public IReadOnlyList<string> Measures { get; init; } = [];

    /// <summary>Master items (qLibraryId) this object references.</summary>
    public IReadOnlyList<string> LibraryIds { get; init; } = [];

    public IReadOnlyList<SheetObject> Children { get; init; } = [];
}

public sealed record Sheet
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public double? Rank { get; init; }
    public IReadOnlyList<SheetObject> Objects { get; init; } = [];
}

public sealed record Story
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<SheetObject> Slides { get; init; } = [];
}

public sealed record Bookmark
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? SheetId { get; init; }
    public IReadOnlyList<string> SelectionFields { get; init; } = [];
    public string? CreatedDate { get; init; }

    /// <summary>"json" or "binary" storage in the QVF.</summary>
    public required string StoredAs { get; init; }
}

public sealed record Variable
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Definition { get; init; }
    public bool ScriptCreated { get; init; }
}

public sealed record MasterDimension
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> Fields { get; init; } = [];
    public IReadOnlyList<string> Labels { get; init; } = [];
}

public sealed record MasterMeasure
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Label { get; init; }
    public string? Definition { get; init; }
    public IReadOnlyList<string> FieldDefs { get; init; } = [];
}

public sealed record MasterItems
{
    public IReadOnlyList<MasterDimension> Dimensions { get; init; } = [];
    public IReadOnlyList<MasterMeasure> Measures { get; init; } = [];
    public IReadOnlyList<SheetObject> Visualizations { get; init; } = [];
}

public sealed record DataTable(string? Name, long? Rows, int? Fields, int? KeyFields);

public sealed record DataField
{
    public string? Name { get; init; }
    public IReadOnlyList<string> Tables { get; init; } = [];
    public long? Cardinal { get; init; }
    public long? TotalCount { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? Comment { get; init; }
}

public sealed record DataModel
{
    public IReadOnlyList<DataTable> Tables { get; init; } = [];
    public IReadOnlyList<DataField> Fields { get; init; } = [];

    /// <summary>The raw qreload_meta object (CPU time, memory, hardware).</summary>
    public JsonElement? Reload { get; init; }
}

/// <summary>A stored object of a type the parser does not model specifically.</summary>
public sealed record OtherObject
{
    public string? Id { get; init; }
    public string? Type { get; init; }
    public string? Title { get; init; }
    public IReadOnlyList<string> Fields { get; init; } = [];
    public required string StoredAs { get; init; }
    public JsonElement? Raw { get; init; }
}
