using System.Text.Json;
using System.Text.RegularExpressions;
using QVF.Parser.Core.Model;

namespace QVF.Parser.Core.Extraction;

internal static partial class SecurityExtractor
{
    [GeneratedRegex(@"api[_-]?key|access[_-]?key|auth(?:orization)?|bearer|token|secret|password|passwd|pwd|client[_-]?secret", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveName();

    // SET/LET vName = 'value' (quoted value may contain ';') or unquoted up to ';'.
    [GeneratedRegex(@"^\s*(?:SET|LET)\s+(?<name>[\w.$-]+)\s*=\s*(?:'(?<sq>[^']*)'|""(?<dq>[^""]*)""|(?<bare>[^;\r\n]+))", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex Assignment();

    // key=value pairs inside connection strings: Password=...; pwd=...
    [GeneratedRegex(@"\b(?<name>password|pwd|passwd|secret|token|api[_-]?key|client[_-]?secret|access[_-]?key)\s*=\s*(?<value>[^;'"")\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ConnectionPair();

    [GeneratedRegex(@"\bbearer\s+(?<value>[A-Za-z0-9._~+/=-]{8,})", RegexOptions.IgnoreCase)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+")]
    private static partial Regex Email();

    public const string Redacted = "***REDACTED***";

    public static string RedactConnectionPairs(string text) =>
        ConnectionPair().Replace(text, m => $"{m.Groups["name"].Value}={Redacted}");

    public static SecurityInfo Build(string script, AppMetadata app, JsonElement? dataConnections, IReadOnlyList<JsonElement> documents)
    {
        var activeScript = ScriptAnalyzer.StripComments(script);
        var access = ScriptAnalyzer.SectionAccess(script, activeScript);

        var identities = new SortedSet<string>(access.Users, StringComparer.Ordinal);
        foreach (Match match in Email().Matches(script))
            identities.Add(match.Value);
        if (app.Owner is { Length: > 0 } owner)
            identities.Add(owner);

        return new SecurityInfo
        {
            SectionAccess = access,
            HasSectionAccessFlag = app.HasSectionAccess,
            SectionAccessMismatch = (app.HasSectionAccess ?? false) != (access.Status == SectionAccessStatus.Active),
            DataConnections = dataConnections,
            DataSources = ScriptAnalyzer.DataSources(activeScript, RedactConnectionPairs),
            Identities = identities.ToList(),
            CredentialFindings = CredentialFindings(script, documents),
        };
    }

    private static List<CredentialFinding> CredentialFindings(string script, IReadOnlyList<JsonElement> documents)
    {
        var findings = new List<CredentialFinding>();
        void Add(string location, string name, string value) => findings.Add(new CredentialFinding(location, name, value.Length));

        var lines = script.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var commented = line.TrimStart().StartsWith("//", StringComparison.Ordinal);
            var location = $"script line {i + 1}" + (commented ? " (commented out)" : "");
            var body = ScriptAnalyzer.UncommentLines(line);

            var assignment = Assignment().Match(body);
            if (assignment.Success && SensitiveName().IsMatch(assignment.Groups["name"].Value))
            {
                var value = new[] { "sq", "dq", "bare" }.Select(g => assignment.Groups[g]).First(g => g.Success).Value;
                Add(location, assignment.Groups["name"].Value, value.Trim());
            }
            foreach (Match pair in ConnectionPair().Matches(body))
                Add(location, pair.Groups["name"].Value, pair.Groups["value"].Value);
            foreach (Match bearer in Bearer().Matches(body))
                Add(location, "bearer", bearer.Groups["value"].Value);
        }

        // Values stored outside the script (variable definitions, object properties).
        void Walk(JsonElement element, string path)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Name == "qScript")
                            continue;
                        if (property.Value.ValueKind == JsonValueKind.String
                            && SensitiveName().IsMatch(property.Name)
                            && property.Value.GetString() is { Length: > 0 } text)
                            Add($"{path}.{property.Name}", property.Name, text);
                        Walk(property.Value, $"{path}.{property.Name}");
                    }
                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                        Walk(item, $"{path}[{index++}]");
                    break;
                case JsonValueKind.String:
                    foreach (Match pair in ConnectionPair().Matches(element.GetString()!))
                        Add(path, pair.Groups["name"].Value, pair.Groups["value"].Value);
                    break;
            }
        }

        foreach (var document in documents)
        {
            var kind = document.Path("qMetaData", "qType").String() ?? document.String("qId") ?? "object";
            Walk(document, kind);
            foreach (var entry in document.Path("qEntryList").Items())
            {
                var name = entry.Path("qProperties", "qName").String() ?? "";
                var definition = entry.Path("qProperties", "qDefinition").String() ?? "";
                if (SensitiveName().IsMatch(name) && definition.Length > 0)
                    Add($"variable {name}", name, definition);
            }
        }
        return findings;
    }
}
