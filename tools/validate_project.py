#!/usr/bin/env python3
# Copyright © 2026 Ray Yang. All rights reserved.
# No license is granted. See LICENSE and NOTICE.md.
"""Run repository-level static validators without third-party packages."""
from __future__ import annotations
import subprocess, sys
import xml.etree.ElementTree as ET
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
TEXT_SUFFIXES = {'.cs','.csx','.xaml','.xml','.csproj','.props','.targets','.md','.yaml','.yml','.json','.ps1','.py','.sln','.slnlaunch','.txt'}
TEXT_FILENAMES = {'.editorconfig','.gitattributes','.gitignore','LICENSE'}
EXCLUDED_PARTS = {'.git','bin','obj'}

def run(name: str) -> int:
    return subprocess.run([sys.executable, str(ROOT/'tools'/name)], cwd=ROOT, check=False).returncode

def validate_line_endings() -> list[str]:
    errors=[]
    for path in ROOT.rglob('*'):
        if not path.is_file() or any(part in EXCLUDED_PARTS for part in path.parts): continue
        if path.name not in TEXT_FILENAMES and path.suffix.lower() not in TEXT_SUFFIXES: continue
        if b'\r' in path.read_bytes(): errors.append(f'non-LF line ending: {path.relative_to(ROOT).as_posix()}')
    return errors

def validate_xml() -> list[str]:
    errors=[]
    for suffix in ('*.xaml','*.csproj','*.props','*.targets'):
        for path in ROOT.rglob(suffix):
            try: ET.parse(path)
            except ET.ParseError as exc: errors.append(f'{path.relative_to(ROOT)}: {exc}')
    return errors

def validate_required_files() -> list[str]:
    required=['.gitattributes','authority-registry.yaml','docs/Engineering_Rules_Adoption.md','docs/Project_Profile.md','docs/Concurrency_Model.md','docs/UI_Engineering_Profile.md','docs/Testing_Strategy.md','docs/Fake_Device_Simulator_Profile.md','docs/Conformance_Assessment.md','protocol/authority-lock.yaml','protocol/protocol.yaml','protocol/transport-profile-proposal.yaml','protocol/test-vectors/protocol-v0.1.0-vectors.json']
    return [f'missing required file: {name}' for name in required if not (ROOT/name).is_file()]

def validate_authority_guidance() -> list[str]:
    errors=[]
    checks={
        'CONTRIBUTING.md':[
            'update the system repository before changing wire behavior;',
            'keep the local protocol mirror exact',
            'record unapproved transport changes only in a controlled proposal',
        ],
        'README.md':[
            'The selectable-baud transport profile remains a pending proposal.',
            'protocol/transport-profile-proposal.yaml',
            'docs/System_Transport_Profile_Change_Proposal.md',
            'baud-aware acquisition: 200 Hz at 115200 baud or faster',
        ],
        'NOTICE.md':[
            'The selectable-baud transport profile remains a pending proposal and is not upstream',
            'The exact local protocol mirror must remain unchanged',
        ],
    }
    for name, fragments in checks.items():
        text=(ROOT/name).read_text(encoding='utf-8')
        for fragment in fragments:
            if fragment not in text:
                errors.append(f'{name} is missing authority guidance: {fragment}')
    contributing=(ROOT/'CONTRIBUTING.md').read_text(encoding='utf-8')
    if 'update `protocol/protocol.yaml` before changing wire behavior' in contributing:
        errors.append('CONTRIBUTING.md incorrectly treats the local mirror as editable authority')
    return errors


def validate_handshake_retry_policy() -> list[str]:
    errors=[]
    checks={
        'src/HostDeviceControl.Core/Device/DeviceSessionOptions.cs':[
            'public int GetDeviceInfoAttemptCount { get; init; } = 2;',
            'public TimeSpan GetDeviceInfoRetryDelay { get; init; } =',
            'TimeSpan.FromMilliseconds(250);',
            'GET_DEVICE_INFO attempt count must be between 1 and 3.',
        ],
        'src/HostDeviceControl.Core/Device/DeviceSession.cs':[
            'GetDeviceInfoWithRetryAsync(',
            'catch (TimeoutException)',
            'attempt < _options.GetDeviceInfoAttemptCount',
            '_options.GetDeviceInfoRetryDelay',
            'retrying initial handshake.',
        ],
        'tests/HostDeviceControl.Protocol.Tests/Program.cs':[
            'GET_DEVICE_INFO startup retry',
            'suppressResponseOnceFor: MessageType.GetDeviceInfo',
            'AssertEqual(2, transport.GetDeviceInfoRequestCount);',
            'GetDeviceInfoAttemptCount = 0',
            'GetDeviceInfoAttemptCount = 4',
        ],
    }
    for name, fragments in checks.items():
        text=(ROOT/name).read_text(encoding='utf-8')
        for fragment in fragments:
            if fragment not in text:
                errors.append(f'{name} is missing handshake retry control: {fragment}')
    return errors

def main() -> int:
    errors=(
        validate_required_files()
        +validate_xml()
        +validate_line_endings()
        +validate_authority_guidance()
        +validate_handshake_retry_policy()
    )
    if run('validate_protocol_contract.py') != 0: errors.append('protocol contract validator failed')
    if run('validate_source_headers.py') != 0: errors.append('source-policy validator failed')
    if errors:
        print('Project validation failed:', file=sys.stderr)
        for error in errors: print(f'- {error}', file=sys.stderr)
        return 1
    print('Project validation passed.')
    return 0
if __name__ == '__main__': raise SystemExit(main())
