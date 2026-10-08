# QVF.Parser

Reads a Qlik Sense `.qvf` file into a typed, queryable model covering the load script, sheets and objects, and security metadata.

- .NET 10.
- A class library (`QVF.Parser.Core`, **no NuGet packages**) plus a console app (`qvf-parser`, which uses Spectre.Console for its terminal UI).

`json` and `script` write plain output to stdout, and errors go to stderr, so piping into other tools keeps working.

Replace the reference.qvf with your app

## Build

```
dotnet build QVF.Parser.slnx -c Release
```

## Console app

```
qvf-parser inspect <file.qvf> [-o <dir>]      # script.qvs, model.json, objects.json, security.json
qvf-parser json <file.qvf> [--section <name>] # all|app|script|sheets|objects|security|datamodel|streams
qvf-parser script <file.qvf>                  # load script to stdout, byte-for-byte
qvf-parser streams <file.qvf>                 # compressed streams in the file
```

**Exit codes:**

| Code | Meaning |
|---|---|
| `0` | success |
| `1` | no Qlik content found |
| `2` | usage error or file not found |

## Library

```csharp
using QVF.Parser.Core;

var doc = QvfReader.Read("app.qvf", new QvfReadOptions { IncludePayloadData = true });

doc.Script.Present;                          // false when the script was stripped
doc.Sheets.Select(s => s.Title);
doc.AllObjects().Where(o => o.Type == "table");
doc.FieldsReferenced().Contains("Salary");
doc.ContainsObjectId("EguTr");
doc.Security.SectionAccess.Status;           // Absent | CommentedOut | Active
doc.ContainsText("Confidential sheet");      // searches every decompressed stream
```

`ContainsText` searches the raw streams, not just the parsed model. That way a stray reference to a restricted sheet or field is caught even if the parser doesn't model the object that holds it. To use it, read the file with `IncludePayloadData = true`.

## What is read

| Area | Source in the QVF |
|---|---|
| App metadata | JSON document with `qTitle`: owner, dates, encrypted, published, `hassectionaccess` |
| Script | `qScript` JSON document. A second copy in the binary app stream is reported as `BinaryCopyText` / `ScriptCopiesConsistent` |
| Sheets, stories, master items, other objects | JSON documents (`qMetaData.qType`, or a top-level `qInfo`) |
| Bookmarks | JSON documents, or the binary `JsonProperty` encoding |
| Data model | `qreload_meta`: tables, fields, tags, comments |
| Security | Section Access (active or commented out, rows, users), data sources, identities, credential-like values |

**Secrets:**
- Credential-like values in `security.json` are always redacted, and only their name, location and length are kept.
- The script text itself (`script.qvs`, `script.text` in `model.json`) is kept exactly as stored, so any secrets written in the script are still in it.

## Limits

- QVF is undocumented, so parsing is best-effort. Streams are found by scanning for zlib headers and validating each stream's Adler-32 trailer.
- Table rows and symbol values (the binary data streams) are not decoded.
- Hidden script tabs are not stored readably and are not covered.
