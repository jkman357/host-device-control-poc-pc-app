#!/usr/bin/env python3
# Copyright © 2026 Ray Yang. All rights reserved.
# No license is granted. See LICENSE and NOTICE.md.

"""Validate visible ownership headers and selected concurrency anti-patterns."""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
COPYRIGHT = "Copyright © 2026 Ray Yang"
CHECK_SUFFIXES = {".cs", ".xaml", ".csproj", ".props", ".targets", ".md", ".yaml", ".yml", ".ps1", ".py"}
EXCLUDED_PARTS = {"bin", "obj", ".git"}


def candidate_files() -> list[Path]:
    files: list[Path] = []
    for path in ROOT.rglob("*"):
        if not path.is_file() or path.suffix.lower() not in CHECK_SUFFIXES:
            continue
        if any(part in EXCLUDED_PARTS for part in path.parts):
            continue
        files.append(path)
    return sorted(files)


def main() -> int:
    errors: list[str] = []
    for path in candidate_files():
        text = path.read_text(encoding="utf-8-sig")
        if COPYRIGHT not in text[:800]:
            errors.append(f"missing visible copyright header: {path.relative_to(ROOT)}")

    source_text = "\n".join(
        path.read_text(encoding="utf-8-sig")
        for path in ROOT.rglob("*.cs")
        if not any(part in EXCLUDED_PARTS for part in path.parts)
    )
    for prohibited in (
        "Channel.CreateUnbounded",
        "ConcurrentQueue<",
        "e.Handled = true",
    ):
        if prohibited in source_text:
            errors.append(f"prohibited source pattern found: {prohibited}")

    self_referential_constants = re.findall(
        r"\bconst\s+[A-Za-z0-9_<>?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*\1\s*;",
        source_text,
    )
    errors.extend(
        f"self-referential constant found: {name}"
        for name in self_referential_constants
    )

    approved_async_void_files = {
        "src/HostDeviceControl.App/MainWindow.xaml.cs",
    }
    async_void_files: list[str] = []
    for path in ROOT.rglob("*.cs"):
        if any(part in EXCLUDED_PARTS for part in path.parts):
            continue
        text = path.read_text(encoding="utf-8-sig")
        if "async void" in text:
            # Use repository-style separators so the allowlist behaves the same
            # on Windows and POSIX runners.
            relative = path.relative_to(ROOT).as_posix()
            if relative not in approved_async_void_files:
                async_void_files.append(relative)
    errors.extend(f"unapproved async void boundary: {name}" for name in async_void_files)

    if errors:
        print("Source-policy validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Source-policy validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
