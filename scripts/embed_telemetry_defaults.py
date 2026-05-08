#!/usr/bin/env python3
"""Embed telemetry credentials into Shared/TelemetryDefaults.cs at build time.

Substitutes each ``__TELEMETRY_*__`` placeholder in the file with the value
of the matching environment variable (TELEMETRY_ENDPOINT, TELEMETRY_USERNAME,
TELEMETRY_API_KEY, TELEMETRY_TENANT_ID).

If a corresponding env var is unset, the placeholder is left untouched. The
runtime's ``TelemetryDefaults.Resolve`` helper turns surviving placeholders
into empty strings at call time, so unconfigured forks / local builds simply
no-op the telemetry sender.

The shipped string values are NOT a security control — .NET assemblies
decompile trivially. Real abuse mitigation lives at the ingest side.

Exits with code 1 on filesystem / argument errors.
"""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path

PLACEHOLDERS = (
    ("__TELEMETRY_ENDPOINT__",  "TELEMETRY_ENDPOINT"),
    ("__TELEMETRY_USERNAME__",  "TELEMETRY_USERNAME"),
    ("__TELEMETRY_API_KEY__",   "TELEMETRY_API_KEY"),
    ("__TELEMETRY_TENANT_ID__", "TELEMETRY_TENANT_ID"),
)

PLACEHOLDER_PATTERN = re.compile(r"__TELEMETRY_[A-Z_]+__")


def annotate_error(message: str) -> None:
    print(f"::error::{message}", file=sys.stderr)


def fail(message: str) -> None:
    annotate_error(message)
    sys.exit(1)


def escape_for_csharp_literal(value: str) -> str:
    """Escape a value so it can be safely placed inside a C# verbatim or regular string.

    The placeholders are surrounded by double quotes in the source, e.g.
    ``"__TELEMETRY_ENDPOINT__"``. We substitute the inner text only, so we
    must escape characters that would terminate or break a regular C# string
    literal: backslash, double quote, and control characters.
    """
    return (
        value
        .replace("\\", "\\\\")
        .replace("\"", "\\\"")
        .replace("\r", "\\r")
        .replace("\n", "\\n")
    )


def main() -> int:
    if len(sys.argv) != 2:
        fail(f"usage: {sys.argv[0]} <path-to-TelemetryDefaults.cs>")

    path = Path(sys.argv[1])
    if not path.is_file():
        fail(f"file not found: {path}")

    source = path.read_text(encoding="utf-8")

    substituted: list[str] = []
    for token, env_name in PLACEHOLDERS:
        value = os.environ.get(env_name, "")
        if not value:
            # Leave the placeholder in place — Resolve() will turn it into "".
            continue
        if token not in source:
            # Placeholder is missing entirely; not fatal but worth surfacing.
            print(f"::warning::placeholder {token} not found in {path}", file=sys.stderr)
            continue
        source = source.replace(token, escape_for_csharp_literal(value))
        substituted.append(env_name)

    path.write_text(source, encoding="utf-8")

    if substituted:
        print(f"Embedded {len(substituted)} telemetry default(s): {', '.join(substituted)}")
    else:
        print("::warning::no TELEMETRY_* env vars set — shipped binary will no-op telemetry.")

    # Surface any leftover placeholders so a half-configured run is obvious in logs.
    survivors = sorted(set(PLACEHOLDER_PATTERN.findall(source)))
    if survivors:
        print(f"Unsubstituted placeholders (will resolve to empty at runtime): {', '.join(survivors)}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
