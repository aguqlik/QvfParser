#!/usr/bin/env python3
"""Best-effort extractor for readable content embedded in Qlik QVF files.

QVF is a proprietary container format. This tool does not fully parse every QVF
structure; it locates and decompresses embedded zlib/gzip streams, extracts
readable strings, and searches them for credential-like assignments.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import zlib
from dataclasses import dataclass
from pathlib import Path


ZLIB_HEADERS = {b"\x78\x01", b"\x78\x5e", b"\x78\x9c", b"\x78\xda"}
GZIP_HEADER = b"\x1f\x8b\x08"

SENSITIVE_NAME = re.compile(
    r"(?i)(?:api[_-]?key|x-api-key|access[_-]?key|auth(?:orization)?|"
    r"bearer|token|secret|password|passwd|client[_-]?secret)"
)
ASSIGNMENT = re.compile(
    r"(?im)^\s*(?:SET|LET)?\s*"
    r"(?P<name>[A-Za-z_][\w.-]*)\s*[:=]\s*"
    r"(?P<quote>['\"]?)(?P<value>[^\r\n;'\"]+)(?P=quote)\s*;?"
)


@dataclass(frozen=True)
class Stream:
    offset: int
    kind: str
    compressed_size: int
    data: bytes


def candidate_offsets(blob: bytes):
    """Yield plausible compressed-stream offsets in ascending order."""
    for offset in range(max(0, len(blob) - 2)):
        if blob[offset : offset + 2] in ZLIB_HEADERS:
            # A valid zlib CMF/FLG header is divisible by 31.
            if int.from_bytes(blob[offset : offset + 2], "big") % 31 == 0:
                yield offset, "zlib", zlib.MAX_WBITS
        elif blob[offset : offset + 3] == GZIP_HEADER:
            yield offset, "gzip", zlib.MAX_WBITS | 16


def decompress_streams(blob: bytes, minimum_size: int = 8) -> list[Stream]:
    streams: list[Stream] = []
    seen_digests: set[bytes] = set()
    for offset, kind, window_bits in candidate_offsets(blob):
        try:
            decoder = zlib.decompressobj(window_bits)
            data = decoder.decompress(blob[offset:])
            data += decoder.flush()
            if not decoder.eof or len(data) < minimum_size:
                continue
            consumed = len(blob[offset:]) - len(decoder.unused_data)
        except zlib.error:
            continue

        digest = hashlib.sha256(data).digest()
        if digest in seen_digests:
            continue
        seen_digests.add(digest)
        streams.append(Stream(offset, kind, consumed, data))
    return streams


def readable_text(data: bytes, minimum_run: int = 4) -> str:
    """Preserve UTF-8 text while removing binary control-byte noise."""
    decoded = data.decode("utf-8", errors="replace")
    decoded = decoded.replace("\x00", "")
    output: list[str] = []
    run: list[str] = []

    def flush() -> None:
        text = "".join(run).strip()
        if len(text) >= minimum_run:
            output.append(text)
        run.clear()

    for char in decoded:
        if char in "\r\n\t" or (char.isprintable() and char != "\ufffd"):
            run.append(char)
        else:
            flush()
    flush()
    return "\n".join(output)


def find_sensitive_values(text: str, show_secrets: bool) -> list[dict[str, object]]:
    findings: list[dict[str, object]] = []
    for line_number, line in enumerate(text.splitlines(), 1):
        match = ASSIGNMENT.search(line)
        if not match or not SENSITIVE_NAME.search(match.group("name")):
            continue
        value = match.group("value").strip()
        findings.append(
            {
                "line": line_number,
                "name": match.group("name"),
                "value": value if show_secrets else "***REDACTED***",
                "length": len(value),
                "sha256": hashlib.sha256(value.encode()).hexdigest(),
            }
        )
    return findings


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("qvf", type=Path, help="QVF file to inspect")
    parser.add_argument(
        "-o", "--output-dir", type=Path,
        help="Output directory (default: <qvf-name>_extracted)",
    )
    parser.add_argument(
        "--redact-secrets", action="store_true",
        help="Redact discovered secret values in findings.json and terminal output",
    )
    parser.add_argument(
        "--no-binary", action="store_true",
        help="Do not save raw decompressed .bin payloads",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    if not args.qvf.is_file():
        print(f"error: file not found: {args.qvf}", file=sys.stderr)
        return 2

    output_dir = args.output_dir or args.qvf.with_name(
        f"{args.qvf.stem}_extracted"
    )
    output_dir.mkdir(parents=True, exist_ok=True)

    blob = args.qvf.read_bytes()
    streams = decompress_streams(blob)
    combined_parts: list[str] = []
    manifest: list[dict[str, object]] = []

    for index, stream in enumerate(streams, 1):
        stem = f"stream_{index:03d}_offset_{stream.offset}"
        text = readable_text(stream.data)
        text_path = output_dir / f"{stem}.txt"
        text_path.write_text(text, encoding="utf-8")
        if not args.no_binary:
            (output_dir / f"{stem}.bin").write_bytes(stream.data)

        combined_parts.append(
            f"===== {stem} ({stream.kind}) =====\n{text}\n"
        )
        manifest.append(
            {
                "stream": index,
                "offset": stream.offset,
                "kind": stream.kind,
                "compressed_size": stream.compressed_size,
                "decompressed_size": len(stream.data),
                "text_file": text_path.name,
            }
        )

    combined = "\n".join(combined_parts)
    (output_dir / "readable.txt").write_text(combined, encoding="utf-8")
    findings = find_sensitive_values(combined, not args.redact_secrets)
    (output_dir / "findings.json").write_text(
        json.dumps(findings, indent=2) + "\n", encoding="utf-8"
    )
    (output_dir / "manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
    )

    print(f"Found {len(streams)} compressed streams")
    print(f"Wrote readable output to: {output_dir / 'readable.txt'}")
    print(f"Credential-like findings: {len(findings)}")
    for finding in findings:
        print(
            f"  {finding['name']}={finding['value']} "
            f"(length={finding['length']}, line={finding['line']})"
        )
    if findings and args.redact_secrets:
        print("Values were redacted because --redact-secrets was supplied.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
