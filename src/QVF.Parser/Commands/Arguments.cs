using QVF.Parser.Core;
using QVF.Parser.Core.Model;

namespace QVF.Parser.Commands;

internal sealed class UsageException(string message) : Exception(message);

/// <summary>Minimal argument parser: one positional file plus --name value options.</summary>
internal sealed class Arguments
{
    private readonly Dictionary<string, string> _options;

    private Arguments(string file, Dictionary<string, string> options)
    {
        File = file;
        _options = options;
    }

    public string File { get; }

    public static Arguments Parse(ReadOnlySpan<string> args)
    {
        string? file = null;
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith('-'))
            {
                if (i + 1 >= args.Length)
                    throw new UsageException($"option '{args[i]}' needs a value");
                options[args[i]] = args[++i];
            }
            else if (file is null)
            {
                file = args[i];
            }
            else
            {
                throw new UsageException($"unexpected argument '{args[i]}'");
            }
        }
        return new Arguments(file ?? throw new UsageException("missing <file.qvf>"), options);
    }

    public string? Option(params string[] names)
    {
        foreach (var name in names)
        {
            if (_options.Remove(name, out var value))
                return value;
        }
        return null;
    }

    /// <summary>Fails on options the command did not consume.</summary>
    public void EnsureNoUnknownOptions()
    {
        if (_options.Count > 0)
            throw new UsageException($"unknown option '{_options.Keys.First()}'");
    }

    public QvfDocument Read(QvfReadOptions? options = null)
    {
        EnsureNoUnknownOptions();
        if (!System.IO.File.Exists(File))
            throw new FileNotFoundException(null, File);
        return QvfReader.Read(File, options);
    }
}
