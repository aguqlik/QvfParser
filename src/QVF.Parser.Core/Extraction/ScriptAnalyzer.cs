using System.Text;
using System.Text.RegularExpressions;
using QVF.Parser.Core.Container;
using QVF.Parser.Core.Model;

namespace QVF.Parser.Core.Extraction;

internal static partial class ScriptAnalyzer
{
    private static readonly byte[] TabMarker = "///$tab"u8.ToArray();

    [GeneratedRegex(@"^///\$tab[ \t]*(?<name>[^\r\n]*)", RegexOptions.Multiline)]
    private static partial Regex TabHeader();

    [GeneratedRegex(@"^[ \t]*//[ \t]?", RegexOptions.Multiline)]
    private static partial Regex LineCommentPrefix();

    [GeneratedRegex(@"\bSection\s+Access\s*;", RegexOptions.IgnoreCase)]
    private static partial Regex SectionAccessStart();

    [GeneratedRegex(@"\bSection\s+Application\s*;", RegexOptions.IgnoreCase)]
    private static partial Regex SectionApplicationStart();

    [GeneratedRegex(@"\bINLINE\s*\[(.*?)\]", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex InlineTable();

    [GeneratedRegex(@"\bFROM\b", RegexOptions.IgnoreCase)]
    private static partial Regex FromKeyword();

    private const string Target = @"(\[[^\]]*\]|'[^']*'|""[^""]*"")";
    private const string TargetOrWord = @"(\[[^\]]*\]|'[^']*'|""[^""]*""|[^\s;]+)";

    [GeneratedRegex(@"\bLIB\s+CONNECT\s+TO\s+" + Target, RegexOptions.IgnoreCase)]
    private static partial Regex LibConnect();

    [GeneratedRegex(@"(?<!\bLIB\s)\b(?:(?:ODBC|OLEDB|CUSTOM)\s+)?CONNECT\d*\s+TO\s+" + Target, RegexOptions.IgnoreCase)]
    private static partial Regex Connect();

    [GeneratedRegex(@"^\s*BINARY\s+" + TargetOrWord, RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex Binary();

    [GeneratedRegex(@"\bFROM\s+" + TargetOrWord, RegexOptions.IgnoreCase)]
    private static partial Regex From();

    [GeneratedRegex(@"\bSTORE\b.*?\bINTO\s+" + TargetOrWord, RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex StoreInto();

    [GeneratedRegex(@"\b(?:lib|https?|ftp)://[^\s'""\]\);]+", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    private static readonly (string Kind, Func<Regex> Pattern)[] SourcePatterns =
    [
        ("lib_connect", LibConnect),
        ("connect", Connect),
        ("binary", Binary),
        ("from", From),
        ("store_into", StoreInto),
        ("url", Url),
    ];

    public static ScriptInfo Build(string? jsonScript, IReadOnlyList<QvfPayload> payloads)
    {
        var binaryCopy = FindBinaryCopy(jsonScript, payloads);
        var text = jsonScript ?? binaryCopy;
        if (text is null)
            return new ScriptInfo { Present = false };

        bool? consistent = jsonScript is not null && binaryCopy is not null
            ? jsonScript.TrimEnd() == binaryCopy.TrimEnd()
            : null;
        return new ScriptInfo
        {
            Present = true,
            Text = text,
            Source = jsonScript is not null ? "json" : "binary",
            LineCount = text.Split('\n').Length - (text.EndsWith('\n') ? 1 : 0),
            Tabs = SplitTabs(text),
            BinaryCopyText = binaryCopy,
            ScriptCopiesConsistent = consistent,
        };
    }

    /// <summary>
    /// Locates the plain-text script copy inside a binary stream. It starts at a
    /// <c>///$tab</c> marker, or at the first line of the JSON script, and runs
    /// until the first control byte other than tab/CR/LF.
    /// </summary>
    private static string? FindBinaryCopy(string? jsonScript, IReadOnlyList<QvfPayload> payloads)
    {
        var firstLine = jsonScript?.Split('\n', 2)[0].TrimEnd('\r');
        byte[]? firstLineBytes = string.IsNullOrEmpty(firstLine) ? null : Encoding.UTF8.GetBytes(firstLine);

        foreach (var payload in payloads.Where(p => p.Kind == PayloadKind.Binary).OrderByDescending(p => p.Data.Length))
        {
            var data = payload.Data.Span;
            var start = data.IndexOf(TabMarker);
            if (start < 0 && firstLineBytes is not null)
            {
                start = data.IndexOf(firstLineBytes);
                // The binary copy may carry a ///$tab header that the JSON copy lacks.
                if (start >= 0)
                {
                    var lookBehind = Math.Max(0, start - 64);
                    var tab = data[lookBehind..start].LastIndexOf(TabMarker);
                    if (tab >= 0)
                        start = lookBehind + tab;
                }
            }
            if (start < 0)
                continue;

            var end = start;
            while (end < data.Length && (data[end] >= 0x20 || data[end] is 0x09 or 0x0A or 0x0D))
                end++;
            var copy = Encoding.UTF8.GetString(data[start..end]);
            if (copy.Length > 0)
                return copy;
        }
        return null;
    }

    private static List<ScriptTab> SplitTabs(string script)
    {
        var headers = TabHeader().Matches(script);
        if (headers.Count == 0)
            return [new ScriptTab("Main", 1, script)];

        var tabs = new List<ScriptTab>();
        if (headers[0].Index > 0 && script[..headers[0].Index].Trim().Length > 0)
            tabs.Add(new ScriptTab("(before first tab)", 1, script[..headers[0].Index]));
        for (var i = 0; i < headers.Count; i++)
        {
            var start = headers[i].Index;
            var end = i + 1 < headers.Count ? headers[i + 1].Index : script.Length;
            var line = 1 + script.AsSpan(0, start).Count('\n');
            tabs.Add(new ScriptTab(headers[i].Groups["name"].Value.Trim(), line, script[start..end]));
        }
        return tabs;
    }

    /// <summary>Removes // and /* */ comments, respecting '...', "..." and [...].</summary>
    public static string StripComments(string script)
    {
        var output = new StringBuilder(script.Length);
        var index = 0;
        while (index < script.Length)
        {
            var c = script[index];
            var closing = c switch { '\'' => '\'', '"' => '"', '[' => ']', _ => '\0' };
            if (closing != '\0')
            {
                var end = script.IndexOf(closing, index + 1);
                end = end < 0 ? script.Length : end + 1;
                output.Append(script, index, end - index);
                index = end;
            }
            else if (string.CompareOrdinal(script, index, "//", 0, 2) == 0)
            {
                var end = script.IndexOf('\n', index);
                index = end < 0 ? script.Length : end;
            }
            else if (string.CompareOrdinal(script, index, "/*", 0, 2) == 0)
            {
                var end = script.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = end < 0 ? script.Length : end + 2;
            }
            else
            {
                output.Append(c);
                index++;
            }
        }
        return output.ToString();
    }

    public static string UncommentLines(string script) => LineCommentPrefix().Replace(script, "");

    public static SectionAccessInfo SectionAccess(string script, string activeScript)
    {
        var active = SectionAccessBlock(activeScript);
        var block = active ?? SectionAccessBlock(UncommentLines(script));
        var status = active is not null ? SectionAccessStatus.Active
            : block is not null ? SectionAccessStatus.CommentedOut
            : SectionAccessStatus.Absent;
        if (block is null)
            return new SectionAccessInfo { Status = status };

        var rows = InlineRows(block);
        return new SectionAccessInfo
        {
            Status = status,
            Rows = rows,
            Columns = rows.SelectMany(r => r.Keys).Distinct().Order(StringComparer.Ordinal).ToList(),
            Users = rows.SelectMany(r => r)
                .Where(kv => kv.Key.ToUpperInvariant() is "USERID" or "NTNAME" or "GROUP" && kv.Value is not ("" or "*"))
                .Select(kv => kv.Value)
                .Distinct()
                .Order(StringComparer.Ordinal)
                .ToList(),
            LoadsFromExternalSource = FromKeyword().IsMatch(block),
        };
    }

    private static string? SectionAccessBlock(string text)
    {
        var start = SectionAccessStart().Match(text);
        if (!start.Success)
            return null;
        var rest = text[(start.Index + start.Length)..];
        var end = SectionApplicationStart().Match(rest);
        return text.Substring(start.Index, start.Length + (end.Success ? end.Index : rest.Length));
    }

    private static List<IReadOnlyDictionary<string, string>> InlineRows(string block)
    {
        var rows = new List<IReadOnlyDictionary<string, string>>();
        foreach (Match match in InlineTable().Matches(block))
        {
            var lines = match.Groups[1].Value.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0)
                continue;
            var header = lines[0].Split(',').Select(h => h.Trim()).ToList();
            foreach (var line in lines.Skip(1))
            {
                var values = line.Split(',').Select(v => v.Trim()).ToList();
                var row = new Dictionary<string, string>(StringComparer.Ordinal); // keeps script column order
                for (var i = 0; i < header.Count; i++)
                {
                    if (header[i].Length > 0)
                        row[header[i]] = i < values.Count ? values[i] : "";
                }
                rows.Add(row);
            }
        }
        return rows;
    }

    public static List<DataSource> DataSources(string activeScript, Func<string, string> redact)
    {
        var seen = new HashSet<(string, string)>();
        var sources = new List<DataSource>();
        foreach (var (kind, pattern) in SourcePatterns)
        {
            foreach (Match match in pattern().Matches(activeScript))
            {
                var raw = match.Groups.Count > 1 ? match.Groups[1].Value : match.Value;
                var target = redact(raw.Trim());
                if (seen.Add((kind, target)))
                    sources.Add(new DataSource(kind, target));
            }
        }
        return sources;
    }
}
