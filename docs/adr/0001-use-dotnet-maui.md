# 0001: Use .NET MAUI for the client

## Status
Accepted

## Context
The client needs to run on Android, iOS, macOS, and Windows — the realistic spread of devices a
merchant might place near a till — with one localized UI, on-device text-to-speech, secure
storage, and a background-aware WebSocket connection. A single C# codebase targeting all four
avoids maintaining four separate native clients (or three, if web/PWA speech and background
limitations ruled that out) for a project with no dedicated platform teams.

## Decision
Build the client as a single .NET MAUI project (`app/AdyenOutLoud`) targeting
`net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, and `net10.0-windows10.0.19041.0`, with
the platform-independent logic factored into a separate `AdyenOutLoud.Core` class library (see
[`docs/architecture.md`](../architecture.md#client-architecture-maui)).

## Consequences
- One codebase, one set of localized resources, one relay protocol implementation — changes to
  announcement logic, parsing, or localization apply to every platform at once.
- MAUI's cross-platform abstractions (`TextToSpeech`, `SecureStorage`, `Preferences`) cover every
  platform-specific need this app actually has, so no platform-specific forks were necessary.
- MAUI tooling is genuinely platform-gated: iOS and Mac Catalyst builds require macOS with a
  specific Xcode version installed; Windows builds require Windows. CI therefore needs three
  different runner OSes (see [`.github/workflows/platform-builds.yml`](../../.github/workflows/platform-builds.yml)
  and [`docs/development.md`](../development.md)), and no single developer machine can build and
  test all four targets locally.
- A shared runtime means a MAUI-wide regression (a workload update, a binding-compilation change)
  can affect all platforms simultaneously — mitigated by pinning the SDK and workload set in
  [`global.json`](../../global.json) and requiring all four platform builds in CI.
