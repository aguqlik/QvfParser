using System.Text.Json;
using QVF.Parser.Core.Model;

namespace QVF.Parser.Core.Extraction;

internal sealed class ExtractedObjects
{
    public AppMetadata App { get; set; } = new();
    public string? Script { get; set; }
    public JsonElement? DataConnections { get; set; }
    public List<Sheet> Sheets { get; } = [];
    public List<Story> Stories { get; } = [];
    public List<Bookmark> Bookmarks { get; } = [];
    public List<Variable> Variables { get; } = [];
    public List<MasterDimension> Dimensions { get; } = [];
    public List<MasterMeasure> Measures { get; } = [];
    public List<SheetObject> Visualizations { get; } = [];
    public DataModel DataModel { get; set; } = new();
    public List<OtherObject> OtherObjects { get; } = [];
}

internal static class ObjectExtractor
{
    public static ExtractedObjects Extract(IReadOnlyList<JsonElement> documents, IReadOnlyList<BinaryObject> binaries)
    {
        var result = new ExtractedObjects();
        foreach (var document in documents)
            Classify(document, result);
        foreach (var binary in binaries)
            ClassifyBinary(binary, result);
        return result;
    }

    private static void Classify(JsonElement document, ExtractedObjects result)
    {
        var qType = document.Path("qMetaData", "qType").String();
        var root = document.Path("qRoot") ?? default;
        var property = root.ValueKind == JsonValueKind.Object ? root.Path("qProperty") : null;

        if (document.TryGetProperty("qTitle", out _))
        {
            result.App = AppMetadata(document);
        }
        else if (document.Path("qScript").String() is { } script)
        {
            result.Script = script;
        }
        else if (document.Path("qreload_meta") is not null)
        {
            result.DataModel = DataModel(document);
        }
        else if (document.String("qId") is "qvapp_variablelist" or "user_variablelist")
        {
            var scriptList = document.String("qId") == "qvapp_variablelist";
            foreach (var entry in document.Path("qEntryList").Items())
            {
                var variable = entry.Path("qProperties");
                result.Variables.Add(new Variable
                {
                    Id = variable.Path("qInfo", "qId").String(),
                    Name = variable.Path("qName").String(),
                    Definition = variable.Path("qDefinition").String(),
                    ScriptCreated = scriptList && (entry.Path("qIsScriptCreated").Bool() ?? false),
                });
            }
        }
        else if (qType is not null && property is not null)
        {
            ClassifyGeneric(qType, root, property.Value, "json", result);
        }
        else if (qType is null && document.Path("qInfo", "qType").String() is { } bareType)
        {
            // Master items (and similar) are stored without qMetaData/qRoot wrappers.
            ClassifyGeneric(bareType, document, document, "json", result);
        }
    }

    private static void ClassifyGeneric(string qType, JsonElement node, JsonElement property, string storedAs, ExtractedObjects result)
    {
        switch (qType.ToLowerInvariant())
        {
            case "loadmodel":
                result.DataConnections = property.Path("connectionMetaDataModels");
                break;
            case "sheet":
                result.Sheets.Add(new Sheet
                {
                    Id = property.Path("qInfo", "qId").String(),
                    Title = property.Path("title").TextOf() ?? property.Path("qMetaDef", "title").TextOf(),
                    Description = property.Path("description").TextOf() ?? property.Path("qMetaDef", "description").TextOf(),
                    Rank = property.Path("rank").Double(),
                    Objects = Children(node),
                });
                break;
            case "story":
                result.Stories.Add(new Story
                {
                    Id = property.Path("qInfo", "qId").String(),
                    Title = property.Path("title").TextOf() ?? property.Path("qMetaDef", "title").TextOf(),
                    Description = property.Path("description").TextOf(),
                    Slides = Children(node),
                });
                break;
            case "bookmark":
                result.Bookmarks.Add(new Bookmark
                {
                    Id = property.Path("qInfo", "qId").String(),
                    Title = property.Path("qMetaDef", "title").TextOf() ?? property.Path("title").TextOf(),
                    Description = property.Path("qMetaDef", "description").TextOf(),
                    SheetId = property.Path("sheetId").String(),
                    SelectionFields = StringOrList(property.Path("selectionFields")),
                    CreatedDate = property.Path("creationDate").String(),
                    StoredAs = storedAs,
                });
                break;
            case "dimension":
                result.Dimensions.Add(new MasterDimension
                {
                    Id = property.Path("qInfo", "qId").String(),
                    Title = property.Path("qMetaDef", "title").TextOf(),
                    Fields = property.Path("qDim", "qFieldDefs").Strings(),
                    Labels = property.Path("qDim", "qFieldLabels").Strings(),
                });
                break;
            case "measure":
                result.Measures.Add(new MasterMeasure
                {
                    Id = property.Path("qInfo", "qId").String(),
                    Title = property.Path("qMetaDef", "title").TextOf(),
                    Label = property.Path("qMeasure", "qLabel").String(),
                    Definition = property.Path("qMeasure", "qDef").String(),
                    FieldDefs = property.Path("qMeasure", "qFieldDefs").Strings(),
                });
                break;
            case "masterobject":
                result.Visualizations.Add(Describe(node));
                break;
            default:
                var described = Describe(node);
                result.OtherObjects.Add(new OtherObject
                {
                    Id = described.Id,
                    Type = described.Type ?? qType,
                    Title = described.Title,
                    Fields = described.Fields,
                    StoredAs = storedAs,
                    Raw = node,
                });
                break;
        }
    }

    private static void ClassifyBinary(BinaryObject binary, ExtractedObjects result)
    {
        string? Text(IReadOnlyDictionary<string, JsonElement> values, string key) =>
            values.TryGetValue(key, out var value) ? ((JsonElement?)value).TextOf() : null;

        if (binary.Type.Equals("bookmark", StringComparison.OrdinalIgnoreCase))
        {
            result.Bookmarks.Add(new Bookmark
            {
                Id = binary.Id,
                Title = Text(binary.Meta, "title"),
                Description = Text(binary.Meta, "description"),
                SheetId = Text(binary.Properties, "sheetId"),
                SelectionFields = binary.Properties.TryGetValue("selectionFields", out var fields) ? StringOrList(fields) : [],
                CreatedDate = Text(binary.Properties, "creationDate"),
                StoredAs = "binary",
            });
            return;
        }
        result.OtherObjects.Add(new OtherObject
        {
            Id = binary.Id,
            Type = binary.Type,
            Title = Text(binary.Meta, "title"),
            StoredAs = "binary",
            Raw = JsonSerializer.SerializeToElement(binary.Properties, Serialization.QvfJsonContext.Default.IReadOnlyDictionaryStringJsonElement),
        });
    }

    /// <summary>Summarises a {qProperty, qChildren} generic-object node.</summary>
    internal static SheetObject Describe(JsonElement node)
    {
        var property = node.Path("qProperty") ?? node;
        var fields = new List<string>();
        var measures = new List<string>();
        var libraryIds = new List<string>();
        CollectReferences(property, parent: null, fields, measures, libraryIds);
        return new SheetObject
        {
            Id = property.Path("qInfo", "qId").String(),
            Type = property.Path("qInfo", "qType").String(),
            Visualization = property.Path("visualization").String(),
            Title = property.Path("title").TextOf() ?? property.Path("qMetaDef", "title").TextOf() ?? "",
            Fields = fields,
            Measures = measures,
            LibraryIds = libraryIds.Distinct().ToList(),
            Children = Children(node),
        };
    }

    private static List<SheetObject> Children(JsonElement node) =>
        node.Path("qChildren").Items().Select(Describe).ToList();

    private static void CollectReferences(JsonElement element, string? parent, List<string> fields, List<string> measures, List<string> libraryIds)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "qChildListDef")
                        continue;
                    if (property.Name == "qFieldDefs")
                        fields.AddRange(((JsonElement?)property.Value).Strings());
                    else if (parent == "qMeasures" && property.Name == "qDef" && property.Value.Path("qDef").String() is { Length: > 0 } expression)
                        measures.Add(expression);
                    else if (property.Name == "qLibraryId" && property.Value.GetString() is { Length: > 0 } libraryId)
                        libraryIds.Add(libraryId);
                    CollectReferences(property.Value, property.Name, fields, measures, libraryIds);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    CollectReferences(item, parent, fields, measures, libraryIds);
                break;
        }
    }

    private static List<string> StringOrList(JsonElement? element) =>
        element?.ValueKind == JsonValueKind.String ? [element.Value.GetString()!] : element.Strings();

    private static AppMetadata AppMetadata(JsonElement document) => new()
    {
        Title = document.Path("qTitle").String(),
        Description = document.Path("description").String(),
        Owner = document.Path("owner").String(),
        OwnerId = document.Path("ownerId").String(),
        CreatedDate = document.Path("createdDate").String(),
        ModifiedDate = document.Path("modifiedDate").String(),
        LastReloadTime = document.Path("qLastReloadTime").String(),
        SavedInProductVersion = document.Path("qSavedInProductVersion").String(),
        Usage = document.Path("qUsage").String(),
        Encrypted = document.Path("encrypted").Bool(),
        Published = document.Path("published").Bool(),
        HasSectionAccess = document.Path("hassectionaccess").Bool(),
        MigrationHash = document.Path("qMigrationHash").String(),
        ResourceType = document.Path("_resourcetype").String(),
    };

    private static DataModel DataModel(JsonElement document) => new()
    {
        Tables = document.Path("qtables").Items()
            .Where(t => !(t.String("qname") ?? "").StartsWith("$$", StringComparison.Ordinal))
            .Select(t => new DataTable(
                t.String("qname"),
                t.Path("qno_of_rows").Long(),
                (int?)t.Path("qno_of_fields").Long(),
                (int?)t.Path("qno_of_key_fields").Long()))
            .ToList(),
        Fields = document.Path("qfields").Items()
            .Where(f => !(f.String("qname") ?? "").StartsWith('$'))
            .Select(f => new DataField
            {
                Name = f.String("qname"),
                Tables = f.Path("qsrc_tables").Strings(),
                Cardinal = f.Path("qcardinal").Long(),
                TotalCount = f.Path("qtotal_count").Long(),
                Tags = f.Path("qtags").Strings(),
                Comment = f.String("qcomment") is { Length: > 0 } comment ? comment : null,
            })
            .ToList(),
        Reload = document.Path("qreload_meta"),
    };
}
