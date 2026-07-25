#!/usr/bin/env python3
# Copyright © 2026 Ray Yang. All rights reserved.
# No license is granted. See LICENSE and NOTICE.md.
"""Cross-check the pinned protocol mirror, C# identities, and test vectors."""
from __future__ import annotations
import hashlib, json, re, sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PROTOCOL_PATH=ROOT/'protocol/protocol.yaml'; LOCK_PATH=ROOT/'protocol/authority-lock.yaml'; VECTORS_PATH=ROOT/'protocol/test-vectors/protocol-v0.1.0-vectors.json'
EXPECTED='7ff8db3a1ed669407e0d4cada2a78b212ea3c7bccdf371f232a2689a02e7c56e'
def pascal(name:str)->str: return ''.join(part.lower().capitalize() for part in name.split('_'))
def parse_enum(path:Path)->dict[str,int]:
    return {name:int(value,16) for name,value in re.findall(r'^\s*([A-Za-z][A-Za-z0-9]*)\s*=\s*(0x[0-9A-Fa-f]+)',path.read_text(),re.M)}
def pairs(text:str,pattern:str)->dict[str,int]: return {pascal(n):int(v,16) for n,v in re.findall(pattern,text,re.M)}
def crc16(data:bytes)->int:
    crc=0xFFFF
    for value in data:
        crc ^= value<<8
        for _ in range(8): crc=((crc<<1)^0x1021)&0xFFFF if crc&0x8000 else (crc<<1)&0xFFFF
    return crc
def main()->int:
    errors=[]; protocol=PROTOCOL_PATH.read_text()
    actual=hashlib.sha256(PROTOCOL_PATH.read_bytes()).hexdigest()
    if actual!=EXPECTED: errors.append(f'protocol authority SHA-256 mismatch: expected {EXPECTED}, actual {actual}')
    lock=LOCK_PATH.read_text()
    for fragment in ('authority_commit: e4aa40b4d5dfc3e7f878f82f5a89115de9fe3679',f'sha256: {EXPECTED}','base_commit: 6a8d3f729ae7a9bea6ba819e391c6c75f8145e11'):
        if fragment not in lock: errors.append(f'authority-lock.yaml is missing: {fragment}')
    message_block=protocol.split('\nresult_codes:',1)[0].split('\nmessages:',1)[1]
    pm=pairs(message_block,r'^\s*-\s+name:\s+([A-Z0-9_]+)\s*\n\s+id:\s+(0x[0-9A-Fa-f]+)'); cm=parse_enum(ROOT/'src/HostDeviceControl.Core/Protocol/MessageType.cs')
    if pm!=cm: errors.append('MessageType.cs does not match protocol.yaml')
    result_block=protocol.split('\nresult_codes:',1)[1].split('\nhost_timeouts_ms:',1)[0]
    pr=pairs(result_block,r'name:\s*([A-Z0-9_]+),\s*value:\s*(0x[0-9A-Fa-f]+)'); cr=parse_enum(ROOT/'src/HostDeviceControl.Core/Protocol/ResultCode.cs')
    if pr!=cr: errors.append('ResultCode.cs does not match protocol.yaml')
    state_block=protocol.split('\nstate_model:',1)[1].split('\nmessages:',1)[0]
    ps=pairs(state_block,r'name:\s*([a-z0-9_]+),\s*value:\s*(0x[0-9A-Fa-f]+)'); cs=parse_enum(ROOT/'src/HostDeviceControl.Core/Protocol/DeviceOperatingState.cs')
    if ps!=cs: errors.append('DeviceOperatingState.cs does not match protocol.yaml')
    wm=re.search(r'^\s*wire_version:\s*(0x[0-9A-Fa-f]+)\s*$',protocol,re.M); wire=int(wm.group(1),16) if wm else 0
    constants=(ROOT/'src/HostDeviceControl.Core/Protocol/ProtocolConstants.cs').read_text(); cmatch=re.search(r'public\s+const\s+byte\s+WireVersion\s*=\s*(0x[0-9A-Fa-f]+|\d+)',constants)
    if not cmatch or int(cmatch.group(1),0)!=wire: errors.append('ProtocolConstants.WireVersion does not match protocol.yaml')
    try: doc=json.loads(VECTORS_PATH.read_text())
    except Exception as exc: errors.append(f'test-vector file is invalid: {exc}'); doc={}
    if doc.get('wire_version')!=wire or doc.get('protocol_version')!='0.1.0' or doc.get('contract_status')!='candidate_for_alignment': errors.append('test-vector metadata mismatch')
    known=set(cm.values())
    for vector in doc.get('vectors',[]):
        name=vector.get('name','<unnamed>')
        try: frame=bytes.fromhex(vector['frame_hex']); payload=bytes.fromhex(vector['payload_hex']); mid=int(vector['message_id'],16); seq=int(vector['sequence'])
        except Exception as exc: errors.append(f'invalid vector {name}: {exc}'); continue
        if len(frame)<10 or frame[:2]!=b'\xA5\x5A': errors.append(f'invalid frame envelope: {name}'); continue
        plen=int.from_bytes(frame[6:8],'little')
        if frame[2]!=wire or frame[3]!=mid or mid not in known: errors.append(f'frame identity mismatch: {name}')
        if int.from_bytes(frame[4:6],'little')!=seq: errors.append(f'frame sequence mismatch: {name}')
        if plen!=len(payload) or frame[8:8+plen]!=payload: errors.append(f'frame payload mismatch: {name}')
        if int.from_bytes(frame[-2:],'little')!=crc16(frame[2:-2]): errors.append(f'frame CRC mismatch: {name}')
    if errors:
        print('Protocol-contract validation failed:',file=sys.stderr)
        for error in errors: print(f'- {error}',file=sys.stderr)
        return 1
    print('Protocol-contract validation passed.'); return 0
if __name__=='__main__': raise SystemExit(main())
