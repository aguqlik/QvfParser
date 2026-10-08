# Code Review: `qvf_extract.py`

**Reviewed:** 2026-09-30
**Inputs tested:** `ExportReduceData.qvf` (245,760 B), `ExportReducedPersonal.qvf` (229,376 B)
**Runtime:** Python 3.13.5 on WSL2. Each file takes about 0.1–0.2 s.

---

## 1. What the script does, step by step

QVF is Qlik Sense's own binary app container, and its layout isn't publicly documented. The script doesn't parse that layout. It carves out anything that looks like a compressed stream and dumps what it finds.

| # | Step | Code | Detail |
|---|------|------|--------|
| 1 | **Parse CLI args** | `parse_args()` L117–132 | Positional `qvf`, then `-o/--output-dir` (default `<stem>_extracted` next to the input), `--redact-secrets`, and `--no-binary`. |
| 2 | **Validate input and create the output dir** | `main()` L137–144 | Returns exit code 2 if the file is missing. Runs `mkdir(parents=True, exist_ok=True)`. |
| 3 | **Read the whole file into memory** | L146 | `args.qvf.read_bytes()` |
| 4 | **Scan for candidate stream headers** | `candidate_offsets()` L43–51 | Walks every byte offset. It yields a **zlib** candidate when the 2 bytes are one of `78 01 / 78 5E / 78 9C / 78 DA` and the header passes the `% 31 == 0` check. It yields a **gzip** candidate on `1F 8B 08`. |
| 5 | **Try to decompress each candidate** | `decompress_streams()` L54–73 | Runs `zlib.decompressobj(wbits)` over `blob[offset:]` and keeps the result only if the stream reaches **EOF** and the output is at least 8 bytes. `compressed_size` is taken from `unused_data`. |
| 6 | **Deduplicate** | L68–71 | Drops any payload whose SHA-256 has already been seen, even if it came from a different offset. |
| 7 | **Convert each payload to "readable text"** | `readable_text()` L76–95 | Decodes as UTF-8 with `errors="replace"`, strips NULs, and keeps runs of printable characters (plus `\r\n\t`) that are at least 4 characters long. Each run goes on its own line. |
| 8 | **Write per-stream artifacts** | L151–171 | Writes `stream_NNN_offset_X.txt` and, unless `--no-binary` is set, the raw `.bin`. Adds one entry per stream to the manifest. |
| 9 | **Write combined output** | L173–174 | `readable.txt` holds every stream's text, each under a `===== stream_... (zlib) =====` header. |
| 10 | **Search for credentials** | `find_sensitive_values()` L98–114 | Applies the `ASSIGNMENT` regex to each line of the combined text (`[SET\|LET] name = 'value';`). A match counts when the name also matches `SENSITIVE_NAME` (api_key, token, secret, password, and so on). Each finding records the line, name, value (or redacted), length and SHA-256. |
| 11 | **Write JSON reports** | L176–181 | `findings.json` and `manifest.json` |
| 12 | **Print a summary** | L183–192 | Stream count, output path and findings. Values are printed in plaintext unless `--redact-secrets` is set. |

### What the carved streams turned out to be (from this run)

The streams fall into four groups:

| Streams | Content |
|---|---|
| 1–16 | **Symbol tables.** Field names, table names, `AUTOGENERATE(n)` sources, then the distinct values of each field. They use a tagged encoding: `04 <len> <str>` is a string, `05 <len> <str> <int32>` is a dual value, and `02 <float64>` is a number. |
| 17–33 | **Bit-packed index/data tables.** These are pure binary, so their `.txt` files are noise. |
| 34–46 | **JSON objects.** `appprops`, `sheet`, `LoadModel`, app metadata (`qTitle`), `qScript`, `qreload_meta`, `qvapp_variablelist`, `singlepublic` and `user_variablelist`. |
| 38 (Data) / 37 (Personal) | **Binary blob.** Holds a second copy of the load script with real line breaks. |

---

## 2. Findings

### High

1. **Secrets are shown by default (`--redact-secrets` is opt-in).** Plaintext values go to the terminal, to `findings.json` and, in any case, to `readable.txt` and every `.txt`/`.bin` file. A tool built to find credentials should redact by default.
   *Fix:* flip the default and replace the flag with `--show-secrets`. Also warn that `readable.txt` itself contains the secrets.

2. **The credential regex misses common Qlik patterns.** I confirmed these by calling `find_sensitive_values` directly:

   | Input | Result |
   |---|---|
   | `SET vApiKey='abc;def';` | **missed**. The value class `[^\r\n;'"]` rejects `;` inside quotes. |
   | `LIB CONNECT TO 'REST (user=me;password=hunter2)';` | **missed** |
   | `CUSTOM CONNECT TO "Provider=x;Password=hunter2;";` | **missed** |
   | The script inside the `qScript` JSON stream (`\r\n` escaped, so the whole script is one line) | **missed**. The regex is anchored with `^` and only sees the first "line". |
   | `LET vToken = 'x' & 'y';` | reports a truncated value, `x` |
   | Section Access inline tables (`USERID, PASSWORD` columns) | not detected at all |

   Today, detection only works because stream 38 happens to hold a second copy of the script with real newlines.
   *Fix:*
   - JSON-decode the `qScript` stream and scan the decoded script.
   - Add patterns for `CONNECT TO` strings, `key=value` pairs inside connection strings, and `Section Access` blocks.
   - Allow quoted values to contain `;`, for example `'(?P<value>[^']*)'`.

3. **Quadratic memory copying.** `decoder.decompress(blob[offset:])` and `len(blob[offset:])` each copy the rest of the file for every candidate. Random binary data yields about one zlib-looking header per 16 KB. On a real multi-hundred-MB QVF, that means thousands of candidates, each copying hundreds of MB.
   *Fix:*
   - Use `mv = memoryview(blob)` and pass `mv[offset:]`.
   - Compute consumed bytes as `len(blob) - offset - len(decoder.unused_data)`.
   - Once a stream decodes, continue scanning from `offset + consumed` so the scan doesn't re-enter the stream (this also reduces false positives).

### Medium

4. **Stale output from earlier runs.** File names include the offset, and the output dir is never cleaned. Re-running on a modified QVF therefore leaves old `stream_*_offset_*` files mixed in with new ones, and `manifest.json` no longer describes the directory. *Fix:* clear `stream_*` files first, or refuse a non-empty directory unless `--force` is given.

5. **Line endings depend on the platform.** `write_text` uses the OS newline. The `*_extracted` folders already in the repo were made on Windows (CRLF). My Linux run produced LF, so **every text and JSON file "differs" even though all `.bin` payloads are byte-identical.** *Fix:* pass `newline="\n"` to `write_text` (Python 3.10+) or write bytes.

6. **`readable_text()` gives little signal on binary streams.** Float64 and bit-packed tables decode to garbage "words" such as `}a2U0*` or `H8-x`. That garbage makes up most of `readable.txt` and swamps any diff (842 diff lines between the two files, almost all noise).
   *Fix:*
   - (a) Detect JSON (`startswith(b"{")`), then `json.loads` and pretty-print it to `stream_N.json`.
   - (b) Decode symbol tables with the `04`/`05`/`02` tag grammar above.
   - (c) Treat the rest as binary and skip the text dump, or raise `minimum_run` to about 6 and require mostly-ASCII runs.

7. **No PII detection.** The commented-out Section Access block contains a real user ID (`aravinda.gajjarapu`), which appears twice in the output. The tool reports `0 findings`, which could give false assurance before a file is shared. Consider a separate "identities" pass for `USERID`/`NTNAME`, emails and `domain\user` values.

### Low / nits

8. The `% 31` check is redundant: the four whitelisted headers already pass it. Valid zlib streams with other CMF bytes (a smaller window) are ignored. That's fine for Qlik, but it's worth a comment.
9. Deduplicating by digest loses information. When the same payload appears at two offsets, the manifest records only the first. Consider adding `duplicate_offsets: [...]`.
10. The `line` field in findings points into `readable.txt`, not into the stream's own file. Also store `stream`/`text_file` so a finding can be traced.
11. An unsalted SHA-256 of a short secret can be brute-forced. If the purpose is correlation, use HMAC with a per-run key, or just drop the hash.
12. The per-byte Python loop in `candidate_offsets` is slow on large files. `re.finditer(rb"\x78[\x01\x5e\x9c\xda]|\x1f\x8b\x08", blob)` is roughly 100× faster.
13. There are no tests. A tiny synthetic QVF-like blob with known zlib/gzip payloads would lock the behaviour down.

---

## 3. Diff: `ExportReduceData.qvf` vs `ExportReducedPersonal.qvf`

Method: I ran both files into the scratchpad, so the existing `_extracted` folders were left untouched. I then matched payloads by SHA-256 and compared the JSON objects by `qType`, not by offset.

| | Data | Personal |
|---|---|---|
| Streams carved | 46 | 44 |
| Byte-identical payloads | 21 shared | 21 shared |
| Credential findings | 0 | 0 |

### 3.1 Real differences

| Area | ExportReduceData | ExportReducedPersonal | Notes |
|---|---|---|---|
| **App title** | `ExportReduceData` | `ExportReducedPersonal` | |
| **Created / modified** | 07:27:56 / 07:45:20 | 07:48:28 / 07:50:50 | Both on 2026-09-16. Personal was **created ~20 min later as a separate app**. |
| **Last reload** | 07:40:31 | 07:48:48 | Each app was **reloaded on its own**. |
| **Transactions rows** | **2,056** | **2,009** | |
| **TransLineID cardinality** | 12 | 10 | |
| **Expression3 cardinality** | 2,020 | 1,975 | |
| **Load script** | 71 lines | 91 lines | Personal has the whole 20-line `SET ThousandSep … NumericalAbbreviation` block **pasted twice**. Otherwise the logic is identical. |
| **Section Access** | commented out | commented out, identical | Contains a user ID (see Finding 7). |
| **Sheet "My new sheet"** | 4 objects: barchart, linechart, **distributionplot**, filterpane | 3 objects: filterpane (listbox on `Dim2`), barchart, linechart | Personal has **no distribution plot**. |
| **`singlepublic` objects** | `bookmarkgroup` + `pinnedItems` | `pinnedItems` only | |
| **Object/variable IDs** (`appprops`, all 15 variables) | different GUIDs | different GUIDs | Expected when apps are created separately. |
| **Symbol-table order** (Dim1 `C,A,B` vs `B,A,C`, and so on) | differs | differs | Only the load order differs; the value set is the same. |
| **CPU / peak memory** | 581 ms / 5.65 MB | 627 ms / 5.65 MB | Noise |

Unchanged: `LoadModel`, the variable **values**, `qEntryList`, `user_variablelist`, and the symbol tables for Alpha, ASCII and the field names.

### 3.2 Interpretation

- **The row-count difference comes from `Rand()`, not from any reduction.** The script builds `Transactions` with `Autogenerate 1000 While Rand()<=0.5 or IterNo()=1`, so every reload gives a different row count, around 2,000 rows. The two apps were reloaded separately, so 2,056 vs 2,009 is sampling noise. Every numeric table (streams 14–16 and 20–28) differs for the same reason.
- **Neither file shows signs of data reduction.** Both contain the full data model (all 26 Alpha, all 191 ASCII and all 1,000 TransID values) and the whole load script. Section Access is commented out in both. So no `REDUCTION`/`OMIT` rule ever ran, and a "reduced" export would come out identical to a full one.
- **To test export-with-reduction properly:**
  1. Uncomment Section Access with a real reduction field, for example link `REDUCTION` to `Num` or `Dim1`.
  2. Replace `Rand()` with a deterministic generator, for example `Mod(RecNo()*7919, 1000)/1000`, or run `Randomize 42;` first.
  3. Reload **once** and export both variants from **the same app**.
  4. Check the extracted `qreload_meta` for `qno_of_rows`/`qcardinal` and look for missing values in the symbol-table streams.

  Only then will the diff isolate what the reduction actually does.
- **Clean up the Personal app:** remove the duplicated `SET` block. It's harmless, but it's clearly a copy-paste slip.

### 3.3 About the `_extracted` folders already in the repo

They were made by this same script, but on Windows. The payloads are identical; only the line endings differ (Finding 5). Once `newline="\n"` is fixed, repeat runs should compare cleanly.

---

## 4. Suggested priority

1. Redact by default (Finding 1).
2. Decode `qScript` from JSON and broaden the secret patterns (Finding 2).
3. Emit JSON streams as pretty-printed `.json` and suppress noise from binary streams. This makes diffs between QVFs meaningful (Finding 6).
4. Use `memoryview` and skip past consumed bytes for performance (Finding 3).
5. Deterministic output: newline fix and cleaning the output dir (Findings 4 and 5).
