# 0001: Use .NET MAUI for the client

## Status
Accepted

## Context
The client must run on Android, iOS, macOS, and Windows with one UI, audio playback, storage, and a
background-aware WebSocket. Four native clients would be too much for a project this small.

## Decision
One .NET MAUI project (`app/AdyenOutLoud`) targeting Android, iOS, Mac Catalyst, and Windows, with the
platform-independent logic in `AdyenOutLoud.Core`.

## Consequences
- One codebase and one protocol implementation for every platform.
- Builds are OS-gated: iOS and Mac Catalyst need macOS with a specific Xcode, Windows needs Windows,
  so CI uses three runner OSes and no single machine builds all four (see
  [`docs/development.md`](../development.md)).
- The SDK and workload set are pinned in [`global.json`](../../global.json) to limit MAUI-wide regressions.
