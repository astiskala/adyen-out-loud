# Instructions for AI coding agents

Human contributors: see [`CONTRIBUTING.md`](CONTRIBUTING.md).

## Project

Adyen Out Loud plays a pre-recorded "Payment successful" clip on a nearby device when an Adyen
terminal payment is approved. A Cloudflare Worker receives Adyen's Display webhook (`POST /webhook`)
and a single Durable Object pushes it over WebSocket (`/ws/<terminalSerial>`) to a .NET MAUI app
(Android, iOS, Mac Catalyst, Windows), or drops it if no app is connected. A device must first pair
with the terminal (`POST /pair/<serial>` with codes from two recent receipts) and then presents its
token on the WebSocket. There are no accounts and one shared relay URL. Read [`docs/architecture.md`](docs/architecture.md) and
[`docs/threat-model.md`](docs/threat-model.md) before changing anything non-trivial.

## Layout

```text
app/AdyenOutLoud.Core/             platform-independent logic (models, abstractions, services)
app/AdyenOutLoud/                  MAUI head: ViewModels/, Services/ (platform adapters), MainPage.xaml, Resources/Raw (MP3s)
app/AdyenOutLoud.Tests/            unit tests for Core
app/AdyenOutLoud.ArchitectureTests/ enforces the rules below
app/AdyenOutLoud.E2ETests/, UITests/ end-to-end tests against a local Worker
worker/src/                        index.ts (routing), relay-object.ts (Durable Object), adyen/ (pure parsing)
docs/                              read before assuming behavior; docs/index.md is the user set-up guide
```

## Rules (enforced by CI)

- `AdyenOutLoud.Core` never references MAUI, Android, iOS, or Windows assemblies.
- View models import no platform namespace; platform calls go behind a Core abstraction implemented
  under `Services/`.
- `*.xaml.cs` never constructs a service (`new SomeService()`); use DI in `MauiProgram.cs`.
- No production project references a test project, and references have no cycles.
- `worker/src/adyen/**` never imports `cloudflare:*`, `index.ts`, or `relay-object.ts`.
- External JSON starts as `unknown` (TypeScript) or is walked field by field (C#) and validated before
  use; never cast or deserialize untrusted input straight into a domain type.
- XAML bindings need a correct `x:DataType`; binding errors must fail the build.
- No `any` or double casts in TypeScript; no `.Wait()`, `.Result`, or `async void` outside UI handlers in C#.

## Commands

Commands and versions are in [`docs/development.md`](docs/development.md). Before finishing, run the
relevant tests plus `npm run quality` (worker) and/or `dotnet format` and the .NET tests (app), or
`scripts/quality.sh` for both. **Say explicitly which checks you could not run**; never claim a check
passed without running it.

## Policies

- **Fix failures, don't disable the gate:** no blanket suppressions, no excluding files from
  coverage or analysis, no lowering thresholds, no skipping failing tests. A narrow suppression needs
  a comment explaining why.
- **Tests:** every bug fix gets a regression test; new logic gets unit tests in Core or
  `worker/src/adyen`; boundary changes get valid and malformed cases.
- **Security:** never log raw payloads, weaken TLS validation, bypass validation, or commit credentials.
- **Dependencies:** add one only if the platform can't do the job; use a stable, maintained,
  MIT-compatible release with no known vulnerabilities.
- **Version-sensitive APIs** (.NET/MAUI, Cloudflare, npm majors): check current documentation rather than
  relying on memory.
- **Docs:** update the relevant doc in the same change, and add an ADR for a real design decision.
  Don't add new top-level docs when an existing one fits.
