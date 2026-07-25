#!/usr/bin/env python3
# Copyright © 2026 Ray Yang. All rights reserved.
# No license is granted. See LICENSE and NOTICE.md.
"""Validate visible ownership headers and selected concurrency anti-patterns."""
from __future__ import annotations
import re, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
COPYRIGHT='Copyright © 2026 Ray Yang'
CHECK_SUFFIXES={'.cs','.xaml','.csproj','.props','.targets','.md','.yaml','.yml','.ps1','.py'}
EXCLUDED_PARTS={'bin','obj','.git'}
EXTERNAL_AUTHORITY_FILES={'protocol/protocol.yaml'}
def candidate_files() -> list[Path]:
    return sorted(path for path in ROOT.rglob('*') if path.is_file() and path.suffix.lower() in CHECK_SUFFIXES and not any(part in EXCLUDED_PARTS for part in path.parts))
def main() -> int:
    errors=[]
    for path in candidate_files():
        relative=path.relative_to(ROOT).as_posix(); text=path.read_text(encoding='utf-8-sig')
        if relative not in EXTERNAL_AUTHORITY_FILES and COPYRIGHT not in text[:800]: errors.append(f'missing visible copyright header: {relative}')
    cs_files=[p for p in ROOT.rglob('*.cs') if not any(part in EXCLUDED_PARTS for part in p.parts)]
    source_text='\n'.join(p.read_text(encoding='utf-8-sig') for p in cs_files)
    for prohibited in ('Channel.CreateUnbounded','ConcurrentQueue<','e.Handled = true'):
        if prohibited in source_text: errors.append(f'prohibited source pattern found: {prohibited}')
    for name in re.findall(r'\bconst\s+[A-Za-z0-9_<>?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*\1\s*;', source_text): errors.append(f'self-referential constant found: {name}')
    approved={'src/HostDeviceControl.App/MainWindow.xaml.cs'}
    for path in cs_files:
        if 'async void' in path.read_text(encoding='utf-8-sig'):
            relative=path.relative_to(ROOT).as_posix()
            if relative not in approved: errors.append(f'unapproved async void boundary: {relative}')
    if errors:
        print('Source-policy validation failed:', file=sys.stderr)
        for error in errors: print(f'- {error}', file=sys.stderr)
        return 1
    print('Source-policy validation passed.'); return 0
if __name__ == '__main__': raise SystemExit(main())
