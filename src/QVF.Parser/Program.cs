using QVF.Parser.Commands;
using Spectre.Console;

namespace QVF.Parser;

internal static class Program
{
    public const int Success = 0;
    public const int NoQlikContent = 1;
    public const int UsageError = 2;

    /// <summary>Console for errors, so stdout stays clean for json/script output.</summary>
    public static readonly IAnsiConsole Stderr = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error),
    });

    public static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            WriteHelp(args.Length == 0 ? Stderr : AnsiConsole.Console);
            return args.Length == 0 ? UsageError : Success;
        }

        try
        {
            var arguments = Arguments.Parse(args.AsSpan(1));
            return args[0] switch
            {
                "inspect" => InspectCommand.Run(arguments),
                "json" => JsonCommand.Run(arguments),
                "script" => ScriptCommand.Run(arguments),
                "streams" => StreamsCommand.Run(arguments),
                _ => throw new UsageException($"unknown command '{args[0]}'"),
            };
        }
        catch (UsageException e)
        {
            Error(e.Message);
            Stderr.WriteLine();
            WriteHelp(Stderr);
            return UsageError;
        }
        catch (FileNotFoundException e)
        {
            Error($"file not found: {e.FileName}");
            return UsageError;
        }
        catch (Core.QvfFormatException e)
        {
            Error(e.Message);
            return NoQlikContent;
        }
    }

    public static void Error(string message) =>
        Stderr.MarkupLine($"[red bold]error:[/] {Markup.Escape(message)}");

    private static void WriteHelp(IAnsiConsole console)
    {
        console.MarkupLine("[bold]qvf-parser[/] [grey]· inspect Qlik Sense .qvf files[/]");
        console.WriteLine();
        console.MarkupLine("[grey]Usage:[/] qvf-parser [cyan]<command>[/] [green]<file.qvf>[/] [yellow][[options]][/]");
        console.WriteLine();

        var commands = new Table().Border(TableBorder.Rounded).AddColumn("[grey]Command[/]").AddColumn("[grey]Description[/]");
        commands.AddRow("[cyan]inspect[/] [green]<file>[/] [yellow][[-o <dir>]][/]",
            "Write script.qvs, model.json, objects.json and security.json to [yellow]<dir>[/] (default [grey]<stem>_inspection[/]) and print a summary");
        commands.AddRow("[cyan]json[/] [green]<file>[/] [yellow][[--section <name>]][/]",
            "Print JSON to stdout. Sections: [yellow]all[/] (default), app, script, sheets, objects, security, datamodel, streams");
        commands.AddRow("[cyan]script[/] [green]<file>[/]", "Print the load script to stdout, byte-for-byte");
        commands.AddRow("[cyan]streams[/] [green]<file>[/]", "List the compressed streams in the file");
        console.Write(commands);

        console.MarkupLine("[grey]Exit codes:[/] [green]0[/] success · [yellow]1[/] no Qlik content found · [red]2[/] usage error or file not found");
    }
}
