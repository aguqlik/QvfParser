using System.Security.Cryptography;
using System.Text.Json;
using QVF.Parser.Core.Container;
using QVF.Parser.Core.Extraction;
using QVF.Parser.Core.Model;

namespace QVF.Parser.Core;

public sealed record QvfReadOptions
{
    /// <summary>
    /// Keep every decompressed stream on <see cref="QvfDocument.Payloads"/>.
    /// Required by <see cref="QvfDocumentExtensions.ContainsText(QvfDocument, string)"/>.
    /// </summary>
    public bool IncludePayloadData { get; init; }
}

/// <summary>Thrown when a file contains no recognisable Qlik app content.</summary>
public sealed class QvfFormatException(string message) : Exception(message);

/// <summary>Reads a QVF into a <see cref="QvfDocument"/>.</summary>
public static class QvfReader
{
    public static QvfDocument Read(string path, QvfReadOptions? options = null) =>
        Read(File.ReadAllBytes(path), options, Path.GetFullPath(path));

    public static QvfDocument Read(byte[] data, QvfReadOptions? options = null) => Read(data, options, path: null);

    private static QvfDocument Read(byte[] data, QvfReadOptions? options, string? path)
    {
        options ??= new QvfReadOptions();
        var payloads = StreamLocator.Locate(data);
        var documents = payloads.Where(p => p.Json is not null).Select(p => p.Json!.Value).ToList();
        if (documents.Count == 0)
            throw new QvfFormatException($"No Qlik objects found in {path ?? "input"}.");

        var binaries = payloads.Where(p => p.Kind == PayloadKind.Binary)
            .SelectMany(p => BinaryPropertyDecoder.Decode(p.Data.Span))
            .ToList();
        var objects = ObjectExtractor.Extract(documents, binaries);
        var script = ScriptAnalyzer.Build(objects.Script, payloads);

        return new QvfDocument
        {
            Source = new SourceInfo(
                path,
                data.LongLength,
                Convert.ToHexStringLower(SHA256.HashData(data)),
                payloads.Select(StreamInfoOf).ToList()),
            App = objects.App,
            Script = script,
            Sheets = objects.Sheets,
            Stories = objects.Stories,
            Bookmarks = objects.Bookmarks,
            Variables = objects.Variables,
            MasterItems = new MasterItems
            {
                Dimensions = objects.Dimensions,
                Measures = objects.Measures,
                Visualizations = objects.Visualizations,
            },
            DataModel = objects.DataModel,
            Security = SecurityExtractor.Build(script.Text ?? "", objects.App, objects.DataConnections, documents),
            OtherObjects = objects.OtherObjects,
            Payloads = options.IncludePayloadData ? payloads : null,
        };
    }

    private static StreamInfo StreamInfoOf(QvfPayload payload) => new(
        payload.Index,
        payload.Offset,
        payload.CompressedSize,
        payload.Data.Length,
        payload.Kind,
        payload.Json is { } json ? QTypeOf(json) : null,
        payload.Sha256);

    private static string? QTypeOf(JsonElement json) =>
        json.Path("qMetaData", "qType").String()
        ?? json.Path("qInfo", "qType").String()
        ?? json.String("qId")
        ?? (json.EnumerateObject().FirstOrDefault() is { Name: { Length: > 0 } name } ? name : null);
}
