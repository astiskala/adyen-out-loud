# Development setup

## Prerequisites

| Tool | Version | Where it's pinned |
| --- | --- | --- |
| .NET SDK | 10.0.401 (feature band pinned, patch rolls forward) | [`global.json`](../global.json) |
| .NET MAUI workload | matching the SDK's workload set (`10.0.401`) | [`global.json`](../global.json) `sdk.workloadVersion` |
| Node.js | 24.15.0 | [`worker/.node-version`](../worker/.node-version), `worker/package.json` `engines.node` |
| npm | 12.0.2 | `worker/package.json` `packageManager` |
| Wrangler | 4.134.0 | `worker/package.json` (installed via `npm ci`, not globally) |
| Xcode (iOS/Mac Catalyst only) | **26.6** exactly | see [Xcode version](#xcode-version-ios--mac-catalyst) below |
| Android SDK/emulator (Android only) | installed by the `android` MAUI workload | — |
| Windows (Windows target only) | a real Windows machine or CI runner — the Windows head build requires `mt.exe`, which is a native Windows binary and cannot cross-compile from macOS/Linux | — |

### Getting the pinned .NET SDK and workload set

```bash
# Installs whatever global.json pins, if you don't already have it.
# See https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script
dotnet --version   # should print 10.0.401 once installed, matching global.json

dotnet workload install maui
dotnet workload --info   # confirm the workload set matches global.json's sdk.workloadVersion
```

`global.json`'s `sdk.workloadVersion` pins the **workload set**, not just the SDK — this is what
keeps every developer and CI machine resolving the same MAUI/Android/iOS/etc. workload manifest
versions instead of silently drifting. To move to a newer workload set deliberately:

```bash
dotnet workload update          # updates to the latest workload set for your installed SDK band
dotnet workload --info          # note the new workload set version
# then edit global.json's sdk.workloadVersion to match, commit both together
```

### Xcode version (iOS / Mac Catalyst)

At the time of this hardening pass, `net10.0-ios`/`net10.0-maccatalyst` (workload manifest
`10.0.20`) require **exactly Xcode 26.6** — a newer or older Xcode fails the build with an explicit
`This version of .NET for iOS/MacCatalyst requires Xcode 26.6` error rather than a confusing one.
This was discovered directly in this hardening pass: the development machine used had Xcode 27.0
installed, which is *ahead* of what the currently-installed MAUI workload supports, so iOS and Mac
Catalyst builds could not be locally verified here (Android and the plain `net10.0`
projects built and tested cleanly on the same machine). If your Xcode is ahead of what the error
message asks for, install the matching version from
[Apple's developer downloads](https://developer.apple.com/download/all/) alongside your newer one
(`xcode-select -s` switches between them) rather than downgrading your primary Xcode install.

### Node / Worker

```bash
cd worker
npm ci                # exact versions from package-lock.json — never `npm install` in CI
npm run quality        # format check, lint, typecheck, wrangler types check, architecture, dead-code, coverage
```

## Building and testing

```bash
# .NET — Core, Tests, and ArchitectureTests build and test on any OS with the SDK installed:
cd app
dotnet restore AdyenOutLoud.slnx
dotnet build AdyenOutLoud.Core/AdyenOutLoud.Core.csproj
dotnet test AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj
dotnet test AdyenOutLoud.ArchitectureTests/AdyenOutLoud.ArchitectureTests.csproj

# The MAUI head project needs a workload per target framework:
dotnet workload install android
dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android

# iOS / Mac Catalyst — macOS + Xcode 26.6 only (see above):
dotnet workload install ios maccatalyst
dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-ios
dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-maccatalyst

# Windows — Windows only:
dotnet workload install windows
dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-windows10.0.19041.0
```

```bash
# Worker
cd worker
npm run test            # vitest, inside the Workers runtime
npm run test:coverage   # same, with coverage thresholds enforced
npm run dev              # local Worker dev server (wrangler dev)
```

### Relay configuration

The relay URL is compiled in (`RelayEndpointFactory.DefaultBaseUrl`); only the terminal serial
number is entered in the app and stored in app preferences (see
[ADR 0008](adr/0008-single-shared-relay-and-prerecorded-audio.md)). Debug builds of the app honour an
`ADYEN_OUT_LOUD_RELAY_URL` environment variable to point at another (HTTPS) relay; release builds
ignore it. `RelayEndpointFactory` rejects anything that isn't an HTTPS address with no query or
fragment.

## One-command quality checks

| Command | What it runs | Closest to CI? |
| --- | --- | --- |
| `scripts/quality.sh` / `scripts/quality.ps1` | Everything the `quality.yml` and `platform-builds.yml` fast-required jobs run *that's runnable on your current OS* (Worker `npm run quality`, .NET format + Core/Tests/ArchitectureTests build+test+coverage, and the Android head build) | Yes — this is the practical full local approximation of the required PR gates |
| `worker && npm run quality` | Worker-only: format, lint, typecheck, `wrangler types --check`, architecture, dead-code, coverage | Yes, for the Worker half |
| `scripts/dotnet-coverage.sh` | .NET unit tests + coverage threshold check only | Partial |
| `dotnet format app/AdyenOutLoud.slnx --verify-no-changes` | Formatting only | Partial |

**Fast development checks** — what you run while iterating: `npm test` / `dotnet test` on just the
project you're touching.

**Full local quality checks** — `scripts/quality.sh` (or `.ps1` on Windows) before opening a PR.

**Platform-specific checks** — the `dotnet build -f <tfm>` commands above, run only for the
platform(s) your change actually touches; you don't need all four on every machine.

**Scheduled/deep checks** — not part of local iteration at all: mutation testing
(`docs/testing.md#mutation-testing`) and (once built) the Appium UI smoke suite run on a schedule
in CI, not locally before every commit.

## Adyen configuration

See [`docs/index.md`](index.md) for configuring the Display webhook against a deployed
Worker.

## Cloudflare deployment (local/manual)

```bash
cd worker
npx wrangler login     # one-time, opens a browser
npm run deploy          # wrangler deploy
```

See [`docs/releasing.md`](releasing.md) for the full release process this project intends to use
once it has one, and what's actually automated today (currently: nothing — `npm run deploy` is a
manual step).
