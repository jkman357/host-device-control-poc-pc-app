# Contributing

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

No public contribution license is granted. Proposed changes may be reviewed at the repository owner's discretion.

Any accepted change must:

1. preserve the authority and precedence recorded in `authority-registry.yaml`;
2. update the system repository before changing wire behavior; keep the local protocol mirror exact, and record unapproved transport changes only in a controlled proposal;
3. keep continuous work bounded and document overflow, cancellation, timeout, and ownership;
4. avoid blocking or device/protocol work on the WPF UI thread;
5. update tests and fault injection for changed behavior;
6. record justified deviations in `docs/Project_Profile.md`;
7. run `./scripts/validate.ps1`, `./scripts/build.ps1`, and `./scripts/test.ps1`;
8. retain human review as the final approval boundary.
