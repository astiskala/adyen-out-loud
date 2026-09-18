# Adyen Out Loud

[![Quality](https://github.com/OWNER/REPO/actions/workflows/quality.yml/badge.svg)](https://github.com/OWNER/REPO/actions/workflows/quality.yml)
[![CodeQL](https://github.com/OWNER/REPO/actions/workflows/codeql.yml/badge.svg)](https://github.com/OWNER/REPO/actions/workflows/codeql.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> Badges above point to `OWNER/REPO` placeholders — update them once this repository has a GitHub
> remote (see [`docs/repository-settings.md`](docs/repository-settings.md)).

Turn a successful Adyen terminal payment into an instant spoken "Payment successful" confirmation
on a phone, tablet, or PC near the till — in English, Chinese, Malay, or Tamil. No account to
create, no backend to run: whoever manages the Adyen account pastes one URL into Adyen once, and
each terminal's app is configured with that URL plus its own terminal serial number.

> **Unofficial project.** "Adyen" is a trademark of Adyen N.V. This is an independent, open-source
> project, not affiliated with, endorsed by, or supported by Adyen. See
> [Trademark and non-affiliation](#trademark-and-non-affiliation).

## How it works

```text
Adyen terminal ──▶ Cloudflare Worker ──▶ per-company Durable Object ──▶ WebSocket ──▶ MAUI app ──▶ on-device TTS
```

Adyen's terminal sends a Display webhook straight to the Worker; the Worker routes it, by company
and then by terminal serial number, to the right device's live WebSocket connection — no persisted
state anywhere. See [`docs/architecture.md`](docs/architecture.md) for the full picture, including
a diagram, [`docs/protocol.md`](docs/protocol.md) for the wire protocol, and
[ADR 0007](docs/adr/0007-display-only-stateless-company-scoped-relay.md) for why this design is
display-only and stateless.

There is no signup step and no shared backend database: one relay URL is generated per Adyen
company account and configured as that company's Display webhook; every terminal's app instance is
configured, at runtime, with that URL plus its own terminal serial number — no Customer Area access
is needed to install and configure the app itself. See
[`docs/adyen-setup.md`](docs/adyen-setup.md) for the setup flow — and, importantly,
[`docs/threat-model.md`](docs/threat-model.md) for what that design trades away.

## Supported platforms

Android, iOS, macOS (Mac Catalyst), and Windows — one .NET MAUI codebase
([ADR 0001](docs/adr/0001-use-dotnet-maui.md)).

## Supported languages

English, Chinese (Simplified), Malay, and Tamil — announcement text and TTS locale selection, both
tested independently of any device (see [`docs/testing.md`](docs/testing.md)). The `zh`/`ms`/`ta`
translations should be reviewed by a native speaker before a live retail deployment — see
[ADR 0006](docs/adr/0006-on-device-text-to-speech.md).

## Current limitations

- **No HMAC webhook signature verification yet** — the relay URL's high entropy is the only
  authorization mechanism in v1. Read [`docs/threat-model.md`](docs/threat-model.md) before
  relying on this for anything where a fabricated announcement would be costly.
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
- The `zh`/`ms`/`ta` translations were written for this project, not by a certified translator —
  see [Supported languages](#supported-languages).

## Security model

Read [`docs/threat-model.md`](docs/threat-model.md) in full before deploying this for anyone other
than yourself. The short version: the relay URL is a **bearer secret**, shared across every
terminal in an Adyen company account — treat it exactly like a password. Possession of it is
sufficient to send that company's terminals fabricated payment announcements. This is a deliberate
v1 design choice, documented in detail (including what it does and doesn't protect against) rather
than glossed over.

## Adyen configuration

See [`docs/adyen-setup.md`](docs/adyen-setup.md) — generating a company token, configuring it as
the Display webhook once, and entering it plus a terminal serial number in each app instance.

## Cloudflare architecture

A single Worker (`worker/src/index.ts`) fronting one Durable Object class (`RelayObject`) — one
object per Adyen company, holding no persisted state, using hibernatable WebSockets tagged by
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

The relay URL and terminal serial number are entered in the app's own UI at runtime, not compiled
in — see [`docs/adyen-setup.md`](docs/adyen-setup.md). Full prerequisites and exact versions:
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
