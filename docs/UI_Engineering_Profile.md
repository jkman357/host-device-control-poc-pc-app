# UI Engineering Profile

Copyright © 2026 Ray Yang. All rights reserved. No license is granted.

The WPF UI presents, but does not own, device authority. Button availability is derived from `DeviceSessionState`. Transport configuration is disabled while a session is active. The baud-rate control is a non-editable selector backed by the Serial transport's single supported-rate list, with 115200 selected by default. Display-only bindings are explicitly `OneWay`; editable fields are explicitly `TwoWay`.

Telemetry arrives independently of UI rendering. A bounded drop-oldest buffer protects acquisition from UI stalls, and the UI drains a bounded batch every 50 ms. CRC, format, unknown-ID, lost-sample, UI-drop, queue-depth, and recorder-drop indicators remain visible.

Operational errors are converted to user-facing status plus bounded logs. Framework-required `async void` event boundaries catch exceptions. Shutdown is cancellable, bounded, and retryable rather than silently abandoning work.

The main controls include automation names, the operational log uses recycling virtualization, and UI colors are not the sole carrier of connection or error state. This PoC is English-only and has not completed formal localization or automated accessibility verification.
