using QVF.Parser.Core;
using QVF.Parser.Core.Model;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace QVF.Parser.Output;

/// <summary>Renders the inspect summary: app facts, sheet tree, contents and security.</summary>
internal static class SummaryPrinter
{
    public static void Print(QvfDocument document, string? outputDirectory = null)
    {
        var app = document.App;
        AnsiConsole.Write(new Rule($"[bold]{Esc(app.Title ?? Path.GetFileName(document.Source.Path) ?? "QVF")}[/]").LeftJustified());
        AnsiConsole.Write(AppGrid(document));
        AnsiConsole.WriteLine();
        AnsiConsole.Write(SheetTree(document));
        AnsiConsole.WriteLine();
        AnsiConsole.Write(ContentsTable(document));
        AnsiConsole.WriteLine();
        AnsiConsole.Write(SecurityPanel(document.Security));
        if (outputDirectory is not null)
            AnsiConsole.MarkupLine($"[green]✓[/] Wrote [link]{Esc(outputDirectory)}[/] [grey](script.qvs, model.json, objects.json, security.json)[/]");
    }

    private static Grid AppGrid(QvfDocument document)
    {
        var app = document.App;
        var grid = new Grid().AddColumn(new GridColumn().PadRight(3)).AddColumn();
        void Row(string label, string? value) =>
            grid.AddRow($"[grey]{label}[/]", value is null ? "[grey](not stored)[/]" : Esc(value));

        Row("File", document.Source.Path);
        Row("Size", $"{document.Source.SizeBytes:N0} bytes, {document.Source.Streams.Count} streams");
        Row("Saved in", app.SavedInProductVersion);
        Row("Created", app.CreatedDate);
        Row("Last reload", app.LastReloadTime);
        Row("Owner", app.Owner);
        grid.AddRow("[grey]Encrypted[/]", Flag(app.Encrypted));
        grid.AddRow("[grey]Published[/]", Flag(app.Published));
        return grid;
    }

    private static Tree SheetTree(QvfDocument document)
    {
        var tree = new Tree($"[bold]Sheets[/] [grey]({document.Sheets.Count})[/]").Guide(TreeGuide.Line);
        foreach (var sheet in document.Sheets)
        {
            var node = tree.AddNode($"[bold]{Esc(Title(sheet.Title))}[/] [grey]{Esc(sheet.Id)}[/]");
            AddObjects(node, sheet.Objects);
            if (sheet.Objects.Count == 0)
                node.AddNode("[grey](empty)[/]");
        }
        foreach (var story in document.Stories)
        {
            var node = tree.AddNode($"[bold]{Esc(Title(story.Title))}[/] [grey]story {Esc(story.Id)}[/]");
            AddObjects(node, story.Slides);
        }
        if (document.Sheets.Count == 0 && document.Stories.Count == 0)
            tree.AddNode("[grey](no sheets)[/]");
        return tree;
    }

    private static void AddObjects(IHasTreeNodes parent, IReadOnlyList<SheetObject> objects)
    {
        foreach (var item in objects)
        {
            var label = $"[cyan]{Esc(item.Type ?? "?")}[/] [grey]{Esc(item.Id)}[/]";
            if (!string.IsNullOrEmpty(item.Title))
                label += $" {Esc(item.Title)}";
            if (item.Fields.Count > 0)
                label += $"  [green]{Esc(string.Join(", ", item.Fields))}[/]";
            if (item.Measures.Count > 0)
                label += $"  [yellow]{Esc(string.Join(", ", item.Measures))}[/]";
            if (item.LibraryIds.Count > 0)
                label += $"  [blue]⇢ {Esc(string.Join(", ", item.LibraryIds))}[/]";
            AddObjects(parent.AddNode(label), item.Children);
        }
    }

    private static Table ContentsTable(QvfDocument document)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Contents[/]")
            .AddColumn("[grey]Item[/]").AddColumn(new TableColumn("[grey]Count[/]").RightAligned()).AddColumn("[grey]Details[/]");

        var script = document.Script;
        var scriptDetails = script.Present
            ? $"{script.Tabs.Count} tab(s): {Esc(string.Join(", ", script.Tabs.Select(t => t.Name)))}" + script.ScriptCopiesConsistent switch
            {
                false => "  [yellow]⚠ binary copy differs[/]",
                true => "  [grey]binary copy matches[/]",
                null => "",
            }
            : "[red]absent[/]";
        table.AddRow("Script lines", script.LineCount.ToString(), scriptDetails);
        table.AddRow("Sheets", document.Sheets.Count.ToString(), Esc(string.Join(", ", document.Sheets.Select(s => Title(s.Title)))));
        table.AddRow("Objects on sheets", document.AllObjects().Count().ToString(), Esc(Types(document.AllObjects().Select(o => o.Type))));
        table.AddRow("Bookmarks", document.Bookmarks.Count.ToString(), Esc(string.Join(", ", document.Bookmarks.Select(b => Title(b.Title) + (b.SelectionFields.Count > 0 ? $" ({string.Join(", ", b.SelectionFields)})" : "")))));
        table.AddRow("Stories", document.Stories.Count.ToString(), Esc(string.Join(", ", document.Stories.Select(s => Title(s.Title)))));
        var master = document.MasterItems;
        table.AddRow("Master items", (master.Dimensions.Count + master.Measures.Count + master.Visualizations.Count).ToString(),
            $"{master.Dimensions.Count} dimension(s), {master.Measures.Count} measure(s), {master.Visualizations.Count} visualization(s)");
        table.AddRow("Variables", document.Variables.Count.ToString(), $"{document.Variables.Count(v => !v.ScriptCreated)} user-created");
        table.AddRow("Tables", document.DataModel.Tables.Count.ToString(),
            Esc(string.Join(", ", document.DataModel.Tables.Select(t => $"{t.Name} ({t.Rows:N0} rows)"))));
        table.AddRow("Fields", document.DataModel.Fields.Count.ToString(), Esc(string.Join(", ", document.DataModel.Fields.Select(f => f.Name))));
        table.AddRow("Other objects", document.OtherObjects.Count.ToString(), Esc(Types(document.OtherObjects.Select(o => o.Type))));
        return table;
    }

    private static Panel SecurityPanel(SecurityInfo security)
    {
        var access = security.SectionAccess;
        var rows = new List<IRenderable>
        {
            new Markup($"[grey]Section Access[/]  {Status(access.Status)}"
                + (access.Rows.Count > 0 ? $" [grey]({access.Rows.Count} rows)[/]" : "")),
        };
        if (access.Rows.Count > 0)
        {
            var table = new Table().Border(TableBorder.Simple);
            foreach (var column in access.Rows[0].Keys)
                table.AddColumn($"[grey]{Esc(column)}[/]");
            foreach (var row in access.Rows)
                table.AddRow(access.Rows[0].Keys.Select(k => Esc(row.GetValueOrDefault(k, ""))).ToArray());
            rows.Add(table);
        }
        if (security.SectionAccessMismatch)
            rows.Add(new Markup("[yellow]⚠ hassectionaccess flag does not match the script[/]"));

        var facts = new Grid().AddColumn(new GridColumn().PadRight(3).NoWrap()).AddColumn();
        facts.AddRow("[grey]Identities[/]", security.Identities.Count > 0
            ? string.Join("\n", security.Identities.Select(Esc))
            : "[grey]none[/]");
        facts.AddRow("[grey]Data sources[/]", security.DataSources.Count > 0
            ? string.Join("\n", security.DataSources.Select(s => $"[cyan]{Esc(s.Kind)}[/] {Esc(s.Target)}"))
            : "[grey]none[/]");
        facts.AddRow("[grey]Credentials[/]", security.CredentialFindings.Count == 0
            ? "[green]✓ no credential-like values[/]"
            : $"[red]✗ {security.CredentialFindings.Count} credential-like value(s), redacted[/]");
        rows.Add(facts);

        if (security.CredentialFindings.Count > 0)
        {
            var findings = new Table().Border(TableBorder.Simple)
                .AddColumn("[grey]Name[/]").AddColumn("[grey]Location[/]").AddColumn(new TableColumn("[grey]Length[/]").RightAligned());
            foreach (var finding in security.CredentialFindings)
                findings.AddRow($"[red]{Esc(finding.Name)}[/]", Esc(finding.Location), finding.Length.ToString());
            rows.Add(findings);
        }

        var border = security.CredentialFindings.Count > 0 ? Color.Red
            : access.Status == SectionAccessStatus.Active ? Color.Green
            : Color.Grey;
        return new Panel(new Rows(rows)).Header("[bold]Security[/]").Border(BoxBorder.Rounded).BorderColor(border).Expand();
    }

    private static string Status(SectionAccessStatus status) => status switch
    {
        SectionAccessStatus.Active => "[green bold]Active[/]",
        SectionAccessStatus.CommentedOut => "[yellow]Commented out[/]",
        _ => "[grey]Absent[/]",
    };

    private static string Flag(bool? value) => value switch
    {
        true => "[green]yes[/]",
        false => "no",
        null => "[grey](not stored)[/]",
    };

    private static string Types(IEnumerable<string?> types) =>
        string.Join(", ", types.GroupBy(t => t ?? "?").Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key));

    private static string Title(string? title) => string.IsNullOrEmpty(title) ? "(untitled)" : title;

    private static string Esc(string? text) => Markup.Escape(text ?? "");
}
