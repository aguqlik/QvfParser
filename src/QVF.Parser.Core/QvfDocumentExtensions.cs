using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using QVF.Parser.Core.Model;

namespace QVF.Parser.Core;

/// <summary>Query helpers for assertions over a parsed QVF.</summary>
public static class QvfDocumentExtensions
{
    /// <summary>Every object on every sheet, story and master visualization, flattened.</summary>
    public static IEnumerable<SheetObject> AllObjects(this QvfDocument document) =>
        document.Sheets.SelectMany(s => s.Objects)
            .Concat(document.Stories.SelectMany(s => s.Slides))
            .Concat(document.MasterItems.Visualizations)
            .SelectMany(Flatten);

    public static IEnumerable<SheetObject> Flatten(this SheetObject root) =>
        root.Children.SelectMany(Flatten).Prepend(root);

    /// <summary>Finds a sheet, object, bookmark, master item or other object by id.</summary>
    public static bool ContainsObjectId(this QvfDocument document, string id) =>
        document.Sheets.Any(s => s.Id == id)
        || document.Stories.Any(s => s.Id == id)
        || document.AllObjects().Any(o => o.Id == id)
        || document.Bookmarks.Any(b => b.Id == id)
        || document.MasterItems.Dimensions.Any(d => d.Id == id)
        || document.MasterItems.Measures.Any(m => m.Id == id)
        || document.OtherObjects.Any(o => o.Id == id);

    public static SheetObject? FindObject(this QvfDocument document, string id) =>
        document.AllObjects().FirstOrDefault(o => o.Id == id);

    /// <summary>Every field referenced by an object, master item or bookmark.</summary>
    public static IReadOnlySet<string> FieldsReferenced(this QvfDocument document)
    {
        var fields = new SortedSet<string>(StringComparer.Ordinal);
        fields.UnionWith(document.AllObjects().SelectMany(o => o.Fields));
        fields.UnionWith(document.MasterItems.Dimensions.SelectMany(d => d.Fields));
        fields.UnionWith(document.OtherObjects.SelectMany(o => o.Fields));
        fields.UnionWith(document.Bookmarks.SelectMany(b => b.SelectionFields));
        return fields;
    }

    /// <summary>
    /// True when <paramref name="text"/> occurs in any decompressed stream, either
    /// as raw UTF-8 or JSON-escaped. Searches the whole file, not just the model.
    /// </summary>
    public static bool ContainsText(this QvfDocument document, string text)
    {
        var payloads = RequirePayloads(document);
        var raw = Encoding.UTF8.GetBytes(text);
        var escaped = JsonEncodedText.Encode(text).EncodedUtf8Bytes.ToArray();
        return payloads.Any(p => p.Data.Span.IndexOf(raw) >= 0
            || (!escaped.AsSpan().SequenceEqual(raw) && p.Data.Span.IndexOf(escaped) >= 0));
    }

    /// <summary>True when <paramref name="pattern"/> matches any stream decoded as UTF-8.</summary>
    public static bool ContainsText(this QvfDocument document, Regex pattern) =>
        RequirePayloads(document).Any(p => pattern.IsMatch(Encoding.UTF8.GetString(p.Data.Span)));

    private static IReadOnlyList<Container.QvfPayload> RequirePayloads(QvfDocument document) =>
        document.Payloads ?? throw new InvalidOperationException(
            "Stream data was not kept. Read the file with new QvfReadOptions { IncludePayloadData = true }.");
}
