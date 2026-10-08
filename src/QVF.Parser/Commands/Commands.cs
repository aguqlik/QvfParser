using System.Text;
using QVF.Parser.Core.Container;
using QVF.Parser.Core.Serialization;
using QVF.Parser.Output;
using Spectre.Console;

namespace QVF.Parser.Commands;

internal static class Stdout
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes text as UTF-8 bytes, unchanged (no newline translation).</summary>
    public static void WriteRaw(string text)
    {
        using var stream = Console.OpenStandardOutput();
        stream.Write(Utf8.GetBytes(text));
    }
}

internal static class InspectCommand
{
    public static int Run(Arguments arguments)
    {
        var output = arguments.Option("-o", "--output");
        var document = arguments.Read();
        var directory = output ?? Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(arguments.File))!,
            Path.GetFileNameWithoutExtension(arguments.File) + "_inspection");
        Directory.CreateDirectory(directory);

        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(directory, "script.qvs"), document.Script.Text ?? "", utf8);
        File.WriteAllText(Path.Combine(directory, "model.json"), QvfJson.Serialize(document) + "\n", utf8);
        File.WriteAllText(Path.Combine(directory, "objects.json"), QvfJson.Serialize(ObjectsView.From(document)) + "\n", utf8);
        File.WriteAllText(Path.Combine(directory, "security.json"), QvfJson.Serialize(document.Security) + "\n", utf8);

        SummaryPrinter.Print(document, Path.GetFullPath(directory));
        return Program.Success;
    }
}

internal static class JsonCommand
{
    public static int Run(Arguments arguments)
    {
        var section = arguments.Option("-s", "--section") ?? "all";
        var document = arguments.Read();
        var json = section switch
        {
            "all" => QvfJson.Serialize(document),
            "app" => QvfJson.Serialize(document.App),
            "script" => QvfJson.Serialize(document.Script),
            "sheets" => QvfJson.Serialize(document.Sheets),
            "objects" => QvfJson.Serialize(ObjectsView.From(document)),
            "security" => QvfJson.Serialize(document.Security),
            "datamodel" => QvfJson.Serialize(document.DataModel),
            "streams" => QvfJson.Serialize(document.Source),
            _ => throw new UsageException($"unknown section '{section}'"),
        };
        Stdout.WriteRaw(json + "\n");
        return Program.Success;
    }
}

internal static class ScriptCommand
{
    public static int Run(Arguments arguments)
    {
        var document = arguments.Read();
        if (!document.Script.Present)
        {
            Program.Error($"no load script found in {arguments.File}");
            return Program.NoQlikContent;
        }
        Stdout.WriteRaw(document.Script.Text!);
        return Program.Success;
    }
}

internal static class StreamsCommand
{
    public static int Run(Arguments arguments)
    {
        var document = arguments.Read();
        var table = new Table().Border(TableBorder.Rounded)
            .Title($"[bold]{Markup.Escape(Path.GetFileName(arguments.File))}[/]")
            .Caption($"[grey]{document.Source.Streams.Count} streams · {document.Source.SizeBytes:N0} bytes · sha256 {document.Source.Sha256}[/]");
        table.AddColumn(new TableColumn("[grey]#[/]").RightAligned());
        table.AddColumn(new TableColumn("[grey]Offset[/]").RightAligned());
        table.AddColumn(new TableColumn("[grey]Compressed[/]").RightAligned());
        table.AddColumn(new TableColumn("[grey]Size[/]").RightAligned());
        table.AddColumn("[grey]Kind[/]");
        table.AddColumn("[grey]SHA-256[/]");
        table.AddColumn("[grey]qType[/]");
        foreach (var stream in document.Source.Streams)
        {
            table.AddRow(
                stream.Index.ToString(),
                $"{stream.Offset:N0}",
                $"{stream.CompressedSize:N0}",
                $"{stream.DecompressedSize:N0}",
                stream.Kind == PayloadKind.Json ? "[green]json[/]" : "[grey]binary[/]",
                $"[grey]{stream.Sha256[..12]}[/]",
                $"[cyan]{Markup.Escape(stream.QType ?? "")}[/]");
        }
        AnsiConsole.Write(table);
        return Program.Success;
    }
}
