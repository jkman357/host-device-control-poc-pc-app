#!/usr/bin/env python3
# Copyright © 2026 Ray Yang. All rights reserved.
# No license is granted. See LICENSE and NOTICE.md.

"""Cross-check the pinned Project Protocol against all derived C# identities."""

from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROTOCOL_PATH = ROOT / "protocol" / "protocol.yaml"
AUTHORITY_LOCK_PATH = ROOT / "protocol" / "authority-lock.yaml"
TRANSPORT_PROPOSAL_PATH = (
    ROOT / "protocol" / "transport-profile-proposal.yaml"
)
VECTORS_PATH = (
    ROOT / "protocol" / "test-vectors" / "protocol-v0.1.0-vectors.json"
)
MESSAGE_ENUM_PATH = (
    ROOT / "src" / "HostDeviceControl.Core" / "Protocol" / "MessageType.cs"
)
RESULT_ENUM_PATH = (
    ROOT / "src" / "HostDeviceControl.Core" / "Protocol" / "ResultCode.cs"
)
STATE_ENUM_PATH = (
    ROOT
    / "src"
    / "HostDeviceControl.Core"
    / "Protocol"
    / "DeviceOperatingState.cs"
)
STATUS_FLAGS_PATH = (
    ROOT / "src" / "HostDeviceControl.Core" / "Protocol" / "DeviceStatusBits.cs"
)
CONSTANTS_PATH = (
    ROOT / "src" / "HostDeviceControl.Core" / "Protocol" / "ProtocolConstants.cs"
)
SERIAL_OPTIONS_PATH = (
    ROOT
    / "src"
    / "HostDeviceControl.Transport.Serial"
    / "SerialTransportOptions.cs"
)
SERIAL_CAPACITY_PATH = (
    ROOT
    / "src"
    / "HostDeviceControl.Transport.Serial"
    / "SerialStreamCapacity.cs"
)
EXPECTED_PROTOCOL_SHA256 = (
    "7ff8db3a1ed669407e0d4cada2a78b212ea3c7bccdf371f232a2689a02e7c56e"
)
EXPECTED_AUTHORITY_COMMIT = "e4aa40b4d5dfc3e7f878f82f5a89115de9fe3679"
EXPECTED_TRANSPORT_PROPOSAL_SHA256 = (
    "6d7d62f88f0b7b62e6a1468dba0ae9797447a1e295848f4bce8d5eda21645310"
)
EXPECTED_IMPLEMENTATION_BASE = "446827e9103872bd7d809005999fb8eab065a0b6"


def pascal_case(name: str) -> str:
    return "".join(part.lower().capitalize() for part in name.split("_"))


def parse_csharp_enum(path: Path) -> dict[str, int]:
    text = path.read_text(encoding="utf-8")
    pairs = re.findall(
        r"^\s*([A-Za-z][A-Za-z0-9]*)\s*=\s*"
        r"(0x[0-9A-Fa-f]+|\d+)",
        text,
        re.MULTILINE,
    )
    return {name: int(value, 0) for name, value in pairs}


def parse_messages(protocol_text: str) -> dict[str, int]:
    block = protocol_text.split("\nresult_codes:", 1)[0].split("\nmessages:", 1)[1]
    pairs = re.findall(
        r"^\s*-\s+name:\s+([A-Z0-9_]+)\s*\n"
        r"\s+id:\s+(0x[0-9A-Fa-f]+)",
        block,
        re.MULTILINE,
    )
    return {pascal_case(name): int(value, 16) for name, value in pairs}


def parse_result_codes(protocol_text: str) -> dict[str, int]:
    block = protocol_text.split("\nresult_codes:", 1)[1].split(
        "\nstatus_flags:", 1
    )[0]
    pairs = re.findall(
        r"name:\s*([A-Z0-9_]+),\s*value:\s*(0x[0-9A-Fa-f]+)",
        block,
    )
    return {pascal_case(name): int(value, 16) for name, value in pairs}


def parse_states(protocol_text: str) -> dict[str, int]:
    block = protocol_text.split("\nstate_model:", 1)[1].split("\nmessages:", 1)[0]
    pairs = re.findall(
        r"name:\s*([a-z0-9_]+),\s*value:\s*(0x[0-9A-Fa-f]+)",
        block,
    )
    return {pascal_case(name): int(value, 16) for name, value in pairs}


def parse_status_flags(protocol_text: str) -> dict[str, int]:
    block = protocol_text.split("\nstatus_flags:", 1)[1].split(
        "\nhost_timeouts_ms:", 1
    )[0]
    pairs = re.findall(
        r"name:\s*([A-Z0-9_]+),\s*mask:\s*(0x[0-9A-Fa-f]+)",
        block,
    )
    return {pascal_case(name): int(value, 16) for name, value in pairs}


def parse_hex_scalar(protocol_text: str, key: str) -> int:
    match = re.search(
        rf"^\s*{re.escape(key)}:\s*(0x[0-9A-Fa-f]+)\s*$",
        protocol_text,
        re.MULTILINE,
    )
    if match is None:
        raise ValueError(f"Missing protocol hexadecimal scalar: {key}")
    return int(match.group(1), 16)


def parse_int_scalar(protocol_text: str, key: str) -> int:
    match = re.search(
        rf"^\s*{re.escape(key)}:\s*(\d+)\s*$",
        protocol_text,
        re.MULTILINE,
    )
    if match is None:
        raise ValueError(f"Missing protocol integer scalar: {key}")
    return int(match.group(1))


def parse_string_scalar(text: str, key: str) -> str:
    match = re.search(
        rf"^\s*{re.escape(key)}:\s*([A-Za-z0-9_.-]+)\s*$",
        text,
        re.MULTILINE,
    )
    if match is None:
        raise ValueError(f"Missing YAML string scalar: {key}")
    return match.group(1)



def parse_yaml_int_list(text: str, key: str) -> list[int]:
    match = re.search(
        rf"^\s*{re.escape(key)}:\s*\n(?P<body>(?:\s+-\s+\d+\s*\n)+)",
        text,
        re.MULTILINE,
    )
    if match is None:
        raise ValueError(f"Missing YAML integer list: {key}")
    return [int(value) for value in re.findall(r"-\s+(\d+)", match.group("body"))]


def parse_csharp_int_array(text: str, name: str) -> list[int]:
    match = re.search(
        rf"{re.escape(name)}\s*=\s*\[(?P<body>.*?)\];",
        text,
        re.DOTALL,
    )
    if match is None:
        raise ValueError(f"Missing C# integer array: {name}")
    return [int(value) for value in re.findall(r"\b(\d+)\b", match.group("body"))]


def parse_any_csharp_int_constant(text: str, name: str) -> int:
    match = re.search(
        rf"(?:public|private|internal)\s+const\s+(?:byte|ushort|int|long)\s+"
        rf"{re.escape(name)}\s*=\s*(\d+)",
        text,
    )
    if match is None:
        raise ValueError(f"Missing C# integer constant: {name}")
    return int(match.group(1))


def parse_csharp_constant(constants_text: str, name: str) -> int:
    match = re.search(
        rf"public\s+const\s+(?:byte|ushort|int)\s+{re.escape(name)}\s*=\s*"
        r"(0x[0-9A-Fa-f]+|\d+)",
        constants_text,
    )
    if match is None:
        raise ValueError(f"Missing numeric C# protocol constant: {name}")
    return int(match.group(1), 0)


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
        raise ValueError("Missing stream interval range or default")
    return (
        int(range_match.group(1)),
        int(range_match.group(2)),
        int(default_match.group(1)),
    )


def parse_frame_offsets(protocol_text: str) -> dict[str, int]:
    framing_block = protocol_text.split("\nframing:", 1)[1].split(
        "\nsequence_rules:", 1
    )[0]
    pairs = re.findall(
        r"^\s*-\s+name:\s+([a-z0-9_]+)\s*\n"
        r"\s+offset_bytes:\s+(\d+)\s*$",
        framing_block,
        re.MULTILINE,
    )
    return {name: int(value) for name, value in pairs}


def require_equal(
    label: str,
    expected: object,
    actual: object,
    errors: list[str],
) -> None:
    if expected != actual:
        errors.append(f"{label}: expected {expected!r}, actual {actual!r}")


def require_unique(label: str, values: dict[str, int], errors: list[str]) -> None:
    reverse: dict[int, list[str]] = {}
    for name, value in values.items():
        reverse.setdefault(value, []).append(name)
    duplicates = {
        value: names for value, names in reverse.items() if len(names) > 1
    }
    if duplicates:
        errors.append(f"{label} contains duplicate numeric values: {duplicates}")


def validate_authority_lock(errors: list[str]) -> None:
    actual_sha = hashlib.sha256(PROTOCOL_PATH.read_bytes()).hexdigest()
    require_equal(
        "protocol authority SHA-256",
        EXPECTED_PROTOCOL_SHA256,
        actual_sha,
        errors,
    )

    lock_text = AUTHORITY_LOCK_PATH.read_text(encoding="utf-8")
    required_fragments = (
        f"authority_commit: {EXPECTED_AUTHORITY_COMMIT}",
        f"sha256: {EXPECTED_PROTOCOL_SHA256}",
        f"base_commit: {EXPECTED_IMPLEMENTATION_BASE}",
        f"sha256: {EXPECTED_TRANSPORT_PROPOSAL_SHA256}",
        "protocol_version: 0.1.0",
        "wire_version: 0x01",
    )
    for fragment in required_fragments:
        if fragment not in lock_text:
            errors.append(f"authority-lock.yaml is missing: {fragment}")



def validate_transport_profile_proposal(
    protocol_text: str,
    constants_text: str,
    errors: list[str],
) -> None:
    proposal_bytes = TRANSPORT_PROPOSAL_PATH.read_bytes()
    proposal_text = proposal_bytes.decode("utf-8")
    require_equal(
        "transport proposal SHA-256",
        EXPECTED_TRANSPORT_PROPOSAL_SHA256,
        hashlib.sha256(proposal_bytes).hexdigest(),
        errors,
    )
    options_text = SERIAL_OPTIONS_PATH.read_text(encoding="utf-8")
    capacity_text = SERIAL_CAPACITY_PATH.read_text(encoding="utf-8")

    allowed_baud_rates = parse_yaml_int_list(
        proposal_text,
        "allowed_baud_rates_bps",
    )
    csharp_baud_rates = parse_csharp_int_array(
        options_text,
        "SupportedBaudRateValues",
    )
    require_equal(
        "transport proposal baud-rate set",
        allowed_baud_rates,
        csharp_baud_rates,
        errors,
    )
    require_unique(
        "transport proposal baud rates",
        {str(value): value for value in allowed_baud_rates},
        errors,
    )

    proposal_default = parse_int_scalar(
        proposal_text,
        "default_baud_rate_bps",
    )
    upstream_default = parse_int_scalar(
        proposal_text,
        "current_fixed_baud_rate_bps",
    )
    pinned_protocol_default = parse_int_scalar(
        protocol_text,
        "baud_rate_bps",
    )
    csharp_default = parse_any_csharp_int_constant(
        options_text,
        "DefaultBaudRate",
    )
    require_equal(
        "pinned protocol default baud rate",
        pinned_protocol_default,
        upstream_default,
        errors,
    )
    require_equal(
        "transport proposal default baud rate",
        proposal_default,
        csharp_default,
        errors,
    )
    if proposal_default not in allowed_baud_rates:
        errors.append("transport proposal default baud rate is not allowed")

    proposal_data_bits = parse_int_scalar(proposal_text, "data_bits")
    proposal_parity = parse_string_scalar(proposal_text, "parity")
    proposal_stop_bits = parse_int_scalar(proposal_text, "stop_bits")
    proposal_flow_control = parse_string_scalar(
        proposal_text,
        "flow_control",
    )
    require_equal("UART data bits", 8, proposal_data_bits, errors)
    require_equal("UART parity", "none", proposal_parity, errors)
    require_equal("UART stop bits", 1, proposal_stop_bits, errors)
    require_equal(
        "UART flow control",
        "none",
        proposal_flow_control,
        errors,
    )
    required_option_fragments = (
        "public const int RequiredDataBits = 8;",
        "public const Parity RequiredParity = Parity.None;",
        "public const StopBits RequiredStopBits = StopBits.One;",
        "public const Handshake RequiredHandshake = Handshake.None;",
        "public SerialTransportOptions(\n        string portName,\n        int baudRate = DefaultBaudRate)",
        "public Parity Parity => RequiredParity;",
        "public int DataBits => RequiredDataBits;",
        "public StopBits StopBits => RequiredStopBits;",
        "public Handshake Handshake => RequiredHandshake;",
    )
    for fragment in required_option_fragments:
        if fragment not in options_text:
            errors.append(
                "SerialTransportOptions does not enforce the fixed "
                f"transport profile: {fragment}"
            )

    forbidden_option_fragments = (
        "Parity parity =",
        "int dataBits =",
        "StopBits stopBits =",
        "Handshake handshake =",
    )
    for fragment in forbidden_option_fragments:
        if fragment in options_text:
            errors.append(
                "SerialTransportOptions still exposes configurable UART "
                f"framing: {fragment}"
            )

    bits_per_byte = parse_int_scalar(
        proposal_text,
        "bits_per_uart_byte",
    )
    utilization = parse_int_scalar(
        proposal_text,
        "maximum_line_utilization_percent",
    )
    telemetry_frame_size = parse_int_scalar(
        proposal_text,
        "telemetry_frame_size_bytes",
    )
    preferred_interval = parse_int_scalar(
        proposal_text,
        "preferred_stream_interval_us",
    )
    maximum_interval = parse_int_scalar(
        proposal_text,
        "maximum_stream_interval_us",
    )
    expected_bits_per_byte = 1 + proposal_data_bits + proposal_stop_bits
    if proposal_parity != "none":
        expected_bits_per_byte += 1
    require_equal(
        "proposal UART bits per byte",
        expected_bits_per_byte,
        bits_per_byte,
        errors,
    )
    require_equal(
        "UART bits per byte",
        bits_per_byte,
        parse_any_csharp_int_constant(capacity_text, "BitsPerUartByte"),
        errors,
    )
    require_equal(
        "UART maximum line utilization",
        utilization,
        parse_any_csharp_int_constant(
            capacity_text,
            "MaximumLineUtilizationPercent",
        ),
        errors,
    )
    computed_frame_size = (
        parse_int_scalar(protocol_text, "minimum_frame_size_bytes")
        + parse_csharp_constant(constants_text, "TelemetryPayloadSize")
    )
    require_equal(
        "telemetry frame size",
        telemetry_frame_size,
        computed_frame_size,
        errors,
    )
    require_equal(
        "preferred stream interval",
        preferred_interval,
        parse_csharp_constant(constants_text, "DefaultStreamIntervalUs"),
        errors,
    )
    require_equal(
        "proposal maximum stream interval",
        maximum_interval,
        parse_csharp_constant(constants_text, "MaximumStreamIntervalUs"),
        errors,
    )

    required_fragments = (
        "status: pending_system_authority_update",
        "behavior: auto_increase_interval_or_reject_streaming",
    )
    for fragment in required_fragments:
        if fragment not in proposal_text:
            errors.append(
                f"transport-profile-proposal.yaml is missing: {fragment}"
            )


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
    expected_maximum_payload: int,
    errors: list[str],
) -> None:
    try:
        document = json.loads(VECTORS_PATH.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"test-vector file is invalid: {exc}")
        return

    require_equal(
        "test-vector protocol version",
        "0.1.0",
        document.get("protocol_version"),
        errors,
    )
    require_equal(
        "test-vector contract status",
        "candidate_for_alignment",
        document.get("contract_status"),
        errors,
    )
    require_equal(
        "test-vector wire version",
        expected_wire_version,
        document.get("wire_version"),
        errors,
    )
    require_equal(
        "test-vector byte order",
        "little_endian",
        document.get("byte_order"),
        errors,
    )
    require_equal(
        "test-vector CRC storage",
        "little_endian",
        document.get("crc_storage"),
        errors,
    )

    vectors = document.get("vectors")
    if not isinstance(vectors, list) or not vectors:
        errors.append("test-vector file does not contain a non-empty vectors list")
        return

    known_ids = set(expected_message_ids.values())
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
        if len(payload) > expected_maximum_payload:
            errors.append(f"{label}: payload exceeds maximum payload size")
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
                f"{label}: expected frame length {expected_length}, "
                f"actual {len(frame)}"
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


def main() -> int:
    protocol_text = PROTOCOL_PATH.read_text(encoding="utf-8")
    constants_text = CONSTANTS_PATH.read_text(encoding="utf-8")
    errors: list[str] = []

    messages = parse_messages(protocol_text)
    result_codes = parse_result_codes(protocol_text)
    states = parse_states(protocol_text)
    status_flags = parse_status_flags(protocol_text)

    require_equal(
        "message IDs", messages, parse_csharp_enum(MESSAGE_ENUM_PATH), errors
    )
    require_equal(
        "result codes", result_codes, parse_csharp_enum(RESULT_ENUM_PATH), errors
    )
    require_equal(
        "device states", states, parse_csharp_enum(STATE_ENUM_PATH), errors
    )
    require_equal(
        "status flags", status_flags, parse_csharp_enum(STATUS_FLAGS_PATH), errors
    )
    require_unique("message IDs", messages, errors)
    require_unique("result codes", result_codes, errors)
    require_unique("device states", states, errors)

    validate_authority_lock(errors)
    validate_transport_profile_proposal(
        protocol_text,
        constants_text,
        errors,
    )

    sof_match = re.search(
        r"bytes:\s*\[(0x[0-9A-Fa-f]+),\s*(0x[0-9A-Fa-f]+)\]",
        protocol_text,
    )
    if sof_match is None:
        errors.append("SOF bytes are missing from protocol.yaml")
    else:
        require_equal(
            "SOF[0]",
            int(sof_match.group(1), 16),
            parse_csharp_constant(constants_text, "StartOfFrame0"),
            errors,
        )
        require_equal(
            "SOF[1]",
            int(sof_match.group(2), 16),
            parse_csharp_constant(constants_text, "StartOfFrame1"),
            errors,
        )

    wire_version = parse_hex_scalar(protocol_text, "wire_version")
    maximum_payload = parse_int_scalar(
        protocol_text, "maximum_payload_size_bytes"
    )
    require_equal(
        "wire version",
        wire_version,
        parse_csharp_constant(constants_text, "WireVersion"),
        errors,
    )
    require_equal(
        "maximum payload",
        maximum_payload,
        parse_csharp_constant(constants_text, "MaximumPayloadSize"),
        errors,
    )
    require_equal(
        "telemetry payload",
        parse_int_scalar(protocol_text, "payload_size_bytes"),
        parse_csharp_constant(constants_text, "TelemetryPayloadSize"),
        errors,
    )
    require_equal(
        "command response payload",
        3,
        parse_csharp_constant(constants_text, "CommandResponsePayloadSize"),
        errors,
    )

    frame_offsets = parse_frame_offsets(protocol_text)
    expected_offsets = {
        "version": parse_csharp_constant(constants_text, "VersionOffset"),
        "message_id": parse_csharp_constant(constants_text, "MessageTypeOffset"),
        "sequence": parse_csharp_constant(constants_text, "SequenceOffset"),
        "payload_length": parse_csharp_constant(
            constants_text, "PayloadLengthOffset"
        ),
        "payload": parse_csharp_constant(constants_text, "PayloadOffset"),
    }
    require_equal("frame offsets", frame_offsets, expected_offsets, errors)

    minimum_frame = parse_int_scalar(protocol_text, "minimum_frame_size_bytes")
    computed_minimum_frame = (
        parse_csharp_constant(constants_text, "StartOfFrameSize")
        + parse_csharp_constant(constants_text, "HeaderWithoutSofSize")
        + parse_csharp_constant(constants_text, "CrcSize")
    )
    require_equal(
        "minimum frame", minimum_frame, computed_minimum_frame, errors
    )

    minimum, maximum, default = parse_stream_range(protocol_text)
    require_equal(
        "minimum stream interval",
        minimum,
        parse_csharp_constant(constants_text, "MinimumStreamIntervalUs"),
        errors,
    )
    require_equal(
        "maximum stream interval",
        maximum,
        parse_csharp_constant(constants_text, "MaximumStreamIntervalUs"),
        errors,
    )
    require_equal(
        "default stream interval",
        default,
        parse_csharp_constant(constants_text, "DefaultStreamIntervalUs"),
        errors,
    )

    timeout_mappings = (
        ("get device info timeout", "get_device_info", "GetDeviceInfoTimeoutMs"),
        ("command timeout", "command_default", "CommandDefaultTimeoutMs"),
        ("stop stream timeout", "stop_stream", "StopStreamTimeoutMs"),
        ("partial frame timeout", "partial_frame", "PartialFrameTimeoutMs"),
    )
    for label, protocol_key, constant_name in timeout_mappings:
        require_equal(
            label,
            parse_int_scalar(protocol_text, protocol_key),
            parse_csharp_constant(constants_text, constant_name),
            errors,
        )

    validate_vectors(messages, wire_version, maximum_payload, errors)

    if errors:
        print("Protocol-contract validation failed:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Protocol-contract validation passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
