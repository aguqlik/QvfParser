using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using QVF.Parser.Core.Model;

namespace QVF.Parser.Core.Serialization;

/// <summary>The object-related parts of a document, as written to objects.json.</summary>
public sealed record ObjectsView(
    AppMetadata App,
    IReadOnlyList<Sheet> Sheets,
    IReadOnlyList<Story> Stories,
    IReadOnlyList<Bookmark> Bookmarks,
    IReadOnlyList<Variable> Variables,
    MasterItems MasterItems,
    DataModel DataModel,
    IReadOnlyList<OtherObject> OtherObjects)
{
    public static ObjectsView From(QvfDocument document) => new(
        document.App, document.Sheets, document.Stories, document.Bookmarks,
        document.Variables, document.MasterItems, document.DataModel, document.OtherObjects);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(QvfDocument))]
[JsonSerializable(typeof(ObjectsView))]
[JsonSerializable(typeof(AppMetadata))]
[JsonSerializable(typeof(ScriptInfo))]
[JsonSerializable(typeof(IReadOnlyList<Sheet>))]
[JsonSerializable(typeof(SecurityInfo))]
[JsonSerializable(typeof(DataModel))]
[JsonSerializable(typeof(SourceInfo))]
[JsonSerializable(typeof(IReadOnlyDictionary<string, JsonElement>))]
internal sealed partial class QvfJsonContext : JsonSerializerContext;

/// <summary>JSON output for the model: camelCase, nulls omitted, readable non-ASCII text.</summary>
public static class QvfJson
{
    private static readonly QvfJsonContext Context = new(new JsonSerializerOptions(QvfJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        NewLine = "\n", // identical output on Windows and Linux
    });

    public static string Serialize(QvfDocument value) => JsonSerializer.Serialize(value, Context.QvfDocument);
    public static string Serialize(ObjectsView value) => JsonSerializer.Serialize(value, Context.ObjectsView);
    public static string Serialize(AppMetadata value) => JsonSerializer.Serialize(value, Context.AppMetadata);
    public static string Serialize(ScriptInfo value) => JsonSerializer.Serialize(value, Context.ScriptInfo);
    public static string Serialize(IReadOnlyList<Sheet> value) => JsonSerializer.Serialize(value, Context.IReadOnlyListSheet);
    public static string Serialize(SecurityInfo value) => JsonSerializer.Serialize(value, Context.SecurityInfo);
    public static string Serialize(DataModel value) => JsonSerializer.Serialize(value, Context.DataModel);
    public static string Serialize(SourceInfo value) => JsonSerializer.Serialize(value, Context.SourceInfo);
}
