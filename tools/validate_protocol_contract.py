#!/usr/bin/env python3
# Copyright © 2026 Ray Yang. All rights reserved.
# No license is granted. See LICENSE and NOTICE.md.

"""Cross-check the authoritative Project Protocol against derived C# values."""

from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROTOCOL_PATH = ROOT / "protocol" / "protocol.yaml"
MESSAGE_ENUM_PATH = ROOT / "src" / "HostDeviceControl.Core" / "Protocol" / "MessageType.cs"
RESULT_ENUM_PATH = ROOT / "src" / "HostDeviceControl.Core" / "Protocol" / "ResultCode.cs"
CONSTANTS_PATH = ROOT / "src" / "HostDeviceControl.Core" / "Protocol" / "ProtocolConstants.cs"
VECTORS_PATH = ROOT / "protocol" / "test-vectors" / "protocol-v0.1.0-vectors.json"


def pascal_case(name: str) -> str:
    return "".join(part.lower().capitalize() for part in name.split("_"))


def parse_csharp_enum(path: Path) -> dict[str, int]:
    text = path.read_text(encoding="utf-8")
    return {
        name: int(value, 16)
        for name, value in re.findall(r"^\s*([A-Za-z][A-Za-z0-9]*)\s*=\s*(0x[0-9A-Fa-f]+)", text, re.MULTILINE)
    }


def parse_messages(protocol_text: str) -> dict[str, int]:
    messages_text = protocol_text.split("\nresult_codes:", 1)[0].split("\nmessages:", 1)[1]
    pairs = re.findall(
        r"^\s*-\s+name:\s+([A-Z0-9_]+)\s*\n\s+id:\s+(0x[0-9A-Fa-f]+)",
        messages_text,
        re.MULTILINE,
    )
    return {pascal_case(name): int(value, 16) for name, value in pairs}


def parse_result_codes(protocol_text: str) -> dict[str, int]:
    result_text = protocol_text.split("\nresult_codes:", 1)[1].split("\nhost_timeouts_ms:", 1)[0]
    pairs = re.findall(
        r"name:\s*([A-Z0-9_]+),\s*value:\s*(0x[0-9A-Fa-f]+)", result_text
    )
    return {pascal_case(name): int(value, 16) for name, value in pairs}


def parse_hex_scalar(protocol_text: str, key: str) -> int:
    match = re.search(rf"^\s*{re.escape(key)}:\s*(0x[0-9A-Fa-f]+)\s*$", protocol_text, re.MULTILINE)
    if match is None:
        raise ValueError(f"Missing protocol scalar: {key}")
    return int(match.group(1), 16)


def parse_int_scalar(protocol_text: str, key: str) -> int:
    match = re.search(rf"^\s*{re.escape(key)}:\s*(\d+)\s*$", protocol_text, re.MULTILINE)
    if match is None:
        raise ValueError(f"Missing protocol scalar: {key}")
    return int(match.group(1))


def parse_stream_range(protocol_text: str) -> tuple[int, int, int]:
    block = re.search(
        r"-\s+name:\s+interval_us(?P<body>.*?)(?=\n\s+valid_responses:)",
        protocol_text,
        re.DOTALL,
    )
    if block is None:
        raise ValueError("Missing interval_us protocol block")
    body = block.group("body")
    range_match = re.search(r"valid_range:\s*\[(\d+),\s*(\d+)\]", body)
    default_match = re.search(r"poc_default:\s*(\d+)", body)
    if range_match is None or default_match is None:
        raise ValueError("Missing interval range/default")
    return int(range_match.group(1)), int(range_match.group(2)), int(default_match.group(1))


def parse_csharp_constant(text: str, name: str) -> int:
    match = re.search(
        rf"public\s+const\s+(?:byte|ushort|int)\s+{re.escape(name)}\s*=\s*(0x[0-9A-Fa-f]+|\d+)",
        text,
    )
    if match is None:
        raise ValueError(f"Missing C# constant: {name}")
    return int(match.group(1), 0)



def crc16_ccitt_false(data: bytes) -> int:
    crc = 0xFFFF
    for value in data:
        crc ^= value << 8
        for _ in range(8):
            if crc & 0x8000:
                crc = ((crc << 1) ^ 0x1021) & 0xFFFF
            else:
                crc = (crc << 1) & 0xFFFF
    return crc


def validate_vectors(
    expected_message_ids: dict[str, int],
    expected_wire_version: int,
    errors: list[str],
) -> None:
    try:
        document = json.loads(VECTORS_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"test-vector file is invalid: {exc}")
        return

    if document.get("wire_version") != expected_wire_version:
        errors.append(
            "test-vector wire version does not match protocol.yaml"
        )

    known_ids = set(expected_message_ids.values())
    vectors = document.get("vectors")
    if not isinstance(vectors, list) or not vectors:
        errors.append("test-vector file does not contain a non-empty vectors list")
        return

    for index, vector in enumerate(vectors):
        label = str(vector.get("name", f"vector[{index}]"))
        try:
            message_id = int(str(vector["message_id"]), 0)
            sequence = int(vector["sequence"])
            payload = bytes.fromhex(str(vector["payload_hex"]))
            frame = bytes.fromhex(str(vector["frame_hex"]))
        except (KeyError, TypeError, ValueError) as exc:
            errors.append(f"{label}: malformed vector fields: {exc}")
            continue

        if message_id not in known_ids:
            errors.append(f"{label}: message ID 0x{message_id:02X} is undefined")
        if not 0 <= sequence <= 0xFFFF:
            errors.append(f"{label}: sequence is outside uint16 range")
        if len(frame) < 10:
            errors.append(f"{label}: frame is shorter than the minimum frame")
            continue
        if frame[0:2] != bytes((0xA5, 0x5A)):
            errors.append(f"{label}: invalid SOF")
        if frame[2] != expected_wire_version:
            errors.append(f"{label}: frame wire version mismatch")
        if frame[3] != message_id:
            errors.append(f"{label}: frame message ID mismatch")
        if int.from_bytes(frame[4:6], "little") != sequence:
            errors.append(f"{label}: frame sequence mismatch")
        payload_length = int.from_bytes(frame[6:8], "little")
        if payload_length != len(payload):
            errors.append(f"{label}: payload length field mismatch")
        expected_length = 10 + len(payload)
        if len(frame) != expected_length:
            errors.append(
                f"{label}: expected frame length {expected_length}, actual {len(frame)}"
            )
            continue
        if frame[8:-2] != payload:
            errors.append(f"{label}: payload bytes mismatch")
        expected_crc = crc16_ccitt_false(frame[2:-2])
        received_crc = int.from_bytes(frame[-2:], "little")
        if expected_crc != received_crc:
            errors.append(
                f"{label}: CRC mismatch, expected 0x{expected_crc:04X}, "
                f"actual 0x{received_crc:04X}"
            )

def require_equal(label: str, expected: object, actual: object, errors: list[str]) -> None:
    if expected != actual:
        errors.append(f"{label}: expected {expected!r}, actual {actual!r}")


def main() -> int:
    protocol_text = PROTOCOL_PATH.read_text(encoding="utf-8")
    constants_text = CONSTANTS_PATH.read_text(encoding="utf-8")
    errors: list[str] = []

    require_equal(
        "message IDs",
        parse_messages(protocol_text),
        parse_csharp_enum(MESSAGE_ENUM_PATH),
        errors,
    )
    require_equal(
        "result codes",
        parse_result_codes(protocol_text),
        parse_csharp_enum(RESULT_ENUM_PATH),
        errors,
    )

    sof_match = re.search(r"bytes:\s*\[(0x[0-9A-Fa-f]+),\s*(0x[0-9A-Fa-f]+)\]", protocol_text)
    if sof_match is None:
        errors.append("SOF bytes are missing from protocol.yaml")
    else:
        require_equal("SOF[0]", int(sof_match.group(1), 16), parse_csharp_constant(constants_text, "StartOfFrame0"), errors)
        require_equal("SOF[1]", int(sof_match.group(2), 16), parse_csharp_constant(constants_text, "StartOfFrame1"), errors)

    require_equal("wire version", parse_hex_scalar(protocol_text, "wire_version"), parse_csharp_constant(constants_text, "WireVersion"), errors)
    require_equal("maximum payload", parse_int_scalar(protocol_text, "maximum_payload_size_bytes"), parse_csharp_constant(constants_text, "MaximumPayloadSize"), errors)
    require_equal("telemetry payload", parse_int_scalar(protocol_text, "payload_size_bytes"), parse_csharp_constant(constants_text, "TelemetryPayloadSize"), errors)
    require_equal("minimum frame", parse_int_scalar(protocol_text, "minimum_frame_size_bytes"), 10, errors)

    minimum, maximum, default = parse_stream_range(protocol_text)
    require_equal("minimum stream interval", minimum, parse_csharp_constant(constants_text, "MinimumStreamIntervalUs"), errors)
    require_equal("maximum stream interval", maximum, parse_csharp_constant(constants_text, "MaximumStreamIntervalUs"), errors)
    require_equal("default stream interval", default, parse_csharp_constant(constants_text, "DefaultStreamIntervalUs"), errors)

    validate_vectors(
        parse_messages(protocol_text),
        parse_hex_scalar(protocol_text, "wire_version"),
        errors,
    )

    if errors:
        print("Protocol contract validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Protocol contract validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
