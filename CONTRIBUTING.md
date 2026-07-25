# Contributing

This repository is not offered under an open-source license. Contributions may
be accepted only by explicit arrangement with the copyright holder.

For internal engineering changes:

1. update `protocol/protocol.yaml` before changing an externally visible frame;
2. update cross-language test vectors;
3. update both PC and MCU implementations;
4. run the protocol self-test;
5. record the change in `CHANGELOG.md`;
6. preserve transport, protocol, session, and UI responsibility boundaries.

A protocol change is incomplete until both directions, error behavior, version
compatibility, and test evidence are defined.
