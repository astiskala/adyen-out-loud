# Adyen Out Loud

[![Quality](https://github.com/astiskala/adyen-out-loud/actions/workflows/quality.yml/badge.svg)](https://github.com/astiskala/adyen-out-loud/actions/workflows/quality.yml)
[![CodeQL](https://github.com/astiskala/adyen-out-loud/actions/workflows/codeql.yml/badge.svg)](https://github.com/astiskala/adyen-out-loud/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Turn a successful Adyen terminal payment into an instant spoken "Payment successful" confirmation
on a phone, tablet, or PC near the till — in English, Chinese, Malay, or Tamil. No account to
create, no backend to run, no URL to enter in the app: whoever manages the Adyen account adds one
webhook URL in the Customer Area once (`https://adyenoutloud.adam-eea.workers.dev/webhook`), and
each terminal's app only needs its terminal serial number.

> **Unofficial project.** "Adyen" is a trademark of Adyen N.V. This is an independent, open-source
> project, not affiliated with, endorsed by, or supported by Adyen. See
> [Trademark and non-affiliation](#trademark-and-non-affiliation).

## How it works

```text
Adyen terminal ──▶ Cloudflare Worker ──▶ Durable Object ──▶ WebSocket ──▶ MAUI app ──▶ bundled recording
```

Adyen's terminal sends a Display webhook straight to the Worker; the Worker routes it, by terminal
serial number, to the right device's live WebSocket connection (dropping it if no app is
connected) — no persisted state anywhere. See [`docs/architecture.md`](docs/architecture.md) for the full picture, including
a diagram, [`docs/protocol.md`](docs/protocol.md) for the wire protocol, and
[ADR 0007](docs/adr/0007-display-only-stateless-company-scoped-relay.md) and
[ADR 0008](docs/adr/0008-single-shared-relay-and-prerecorded-audio.md) for why this design is
display-only, stateless, and uses one shared relay.

There is no signup step and no backend database: Adyen's Display webhook is pointed at the one shared
relay URL, and every terminal's app instance only needs its own terminal serial number — no
Customer Area access is needed to install the app. See the [set-up guide](docs/index.md) — and,
importantly, [`docs/threat-model.md`](docs/threat-model.md) for what that design trades away.

## Supported platforms

Android, iOS, macOS (Mac Catalyst), and Windows — one .NET MAUI codebase
([ADR 0001](docs/adr/0001-use-dotnet-maui.md)).

## Supported languages

English, Chinese (Simplified), Malay, and Tamil — played from pre-recorded clips bundled with the
app (`app/AdyenOutLoud/Resources/Raw`), not synthesized on the device — see
[ADR 0008](docs/adr/0008-single-shared-relay-and-prerecorded-audio.md).

## Current limitations

- **No authentication or HMAC webhook signature verification** — the terminal serial number is the
  only routing key. Read [`docs/threat-model.md`](docs/threat-model.md) before relying on this for
  anything where a fabricated announcement would be costly.
- **No announcement richer than "payment successful"** — Adyen's Display API doesn't carry payment
  method or amount, and there's no correlated second webhook anymore — see
  [ADR 0007](docs/adr/0007-display-only-stateless-company-scoped-relay.md).
- **No true background listening on iOS** — Android, Windows, and Mac Catalyst keep listening
  while backgrounded/minimized; iOS stays foreground-only by deliberate choice — see
  [`docs/architecture.md`](docs/architecture.md#client-architecture-maui).
- **No UI test automation yet** — the manual UI smoke checklist in
  [`docs/testing.md`](docs/testing.md#ui-smoke-scenarios-manual-today) hasn't been automated with
  Appium yet.
- **No release automation yet** — [`docs/releasing.md`](docs/releasing.md) documents the intended
  process; today it's entirely manual.

## Security model

Read [`docs/threat-model.md`](docs/threat-model.md) in full before deploying this for anyone other
than yourself. The short version: the relay is one shared public URL with **no
authentication** — the terminal serial number (which is not a secret) is the only routing key, so
anyone who knows a serial can send a fabricated announcement to it or listen to its feed. This is a
deliberate design choice, documented in detail (including what it does and doesn't protect against)
rather than glossed over.

## Adyen configuration

See the [set-up guide](https://astiskala.github.io/adyen-out-loud/) ([`docs/index.md`](docs/index.md)) — adding the Display webhook
once, and entering a terminal serial number in each app instance. The app links to the same guide.

## Cloudflare architecture

A single Worker (`worker/src/index.ts`) fronting one Durable Object class (`RelayObject`) — one
object for all terminals, holding no persisted state, using hibernatable WebSockets tagged by
terminal serial to route an ingested notification to the right device. See
[`docs/architecture.md`](docs/architecture.md) and
[ADR 0002](docs/adr/0002-use-cloudflare-durable-objects.md).

## Quick start (development)

```bash
git clone <this-repo>
cd adyen-terminal-payment-announcer

# Worker
cd worker
npm ci
npm run dev            # local Worker dev server
npm run quality         # format, lint, typecheck, wrangler-types check, architecture, dead-code, coverage

# App (from repo root)
cd app
dotnet workload install android   # or ios/maccatalyst (macOS + Xcode 26.6) / windows
dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android
```

Only the terminal serial number is entered in the app; the relay URL is compiled in (Debug builds
honour an `ADYEN_OUT_LOUD_RELAY_URL` override) — see the [set-up guide](docs/index.md). Full prerequisites and exact versions:
[`docs/development.md`](docs/development.md).

## Build commands

See [`docs/development.md`](docs/development.md#building-and-testing) for the per-platform `dotnet
build` commands.

## Test commands

```bash
cd worker && npm test            # or: npm run test:coverage
cd app && dotnet test AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj
cd app && dotnet test AdyenOutLoud.ArchitectureTests/AdyenOutLoud.ArchitectureTests.csproj
```

Full test-layer breakdown, coverage thresholds and rationale, and mutation-testing baseline:
[`docs/testing.md`](docs/testing.md).

## Deployment

```bash
cd worker && npx wrangler login && npm run deploy
```

App releases are currently a manual, unsigned-in-CI process — see
[`docs/releasing.md`](docs/releasing.md) for the full checklist and what's not yet automated.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) for the human contributor workflow, or
[`AGENTS.md`](AGENTS.md) if you're an AI coding agent.

## Reporting a security vulnerability

See [`SECURITY.md`](SECURITY.md) — please report privately, not as a public issue.

## License

[MIT](LICENSE).

## Trademark and non-affiliation

"Adyen" and any associated logos are trademarks of Adyen N.V. This project is an independent,
open-source effort and is not affiliated with, sponsored by, or endorsed by Adyen. No Adyen
branding or logo assets are used in this repository. Use of the Adyen name here is solely to
describe interoperability with Adyen's terminal and webhook APIs.
