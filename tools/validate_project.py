#!/usr/bin/env python3
# Copyright © 2026 Ray Yang. All rights reserved.
# No license is granted. See LICENSE and NOTICE.md.

"""Run repository-level static validators without third-party packages."""

from __future__ import annotations

import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def run(script_name: str) -> int:
    result = subprocess.run(
        [sys.executable, str(ROOT / "tools" / script_name)],
        cwd=ROOT,
        check=False,
    )
    return result.returncode


def validate_xml() -> list[str]:
    errors: list[str] = []
    for suffix in ("*.xaml", "*.csproj", "*.props", "*.targets"):
        for path in ROOT.rglob(suffix):
            try:
                ET.parse(path)
            except ET.ParseError as exc:
                errors.append(f"{path.relative_to(ROOT)}: {exc}")
    return errors


def validate_required_files() -> list[str]:
    required = [
        "authority-registry.yaml",
        "docs/Engineering_Rules_Adoption.md",
        "docs/Project_Profile.md",
        "docs/Concurrency_Model.md",
        "docs/UI_Engineering_Profile.md",
        "docs/Testing_Strategy.md",
        "docs/Fake_Device_Simulator_Profile.md",
        "docs/Conformance_Assessment.md",
        "protocol/protocol.yaml",
    ]
    return [f"missing required file: {name}" for name in required if not (ROOT / name).is_file()]


def main() -> int:
    errors = validate_required_files() + validate_xml()
    if run("validate_protocol_contract.py") != 0:
        errors.append("protocol contract validator failed")
    if run("validate_source_headers.py") != 0:
        errors.append("source-policy validator failed")

    if errors:
        print("Project validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Project validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
