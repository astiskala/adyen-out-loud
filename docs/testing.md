# Testing

## Test layers and who owns them

| Layer | Project | What it covers | Needs a device/emulator? |
| --- | --- | --- | --- |
| Unit — pure logic | `worker/test/parsers.test.ts` | Display parser, terminal-serial derivation, `asObject`/`asString` — no Workers runtime globals needed | No |
| Integration — Worker runtime | `worker/test/worker.test.ts` | HTTP ingress, terminal Durable Object routing, hibernatable-WebSocket tag fan-out, statelessness (no queue/replay), sensitive-data logging | No (runs inside `workerd` via `@cloudflare/vitest-plugin`, not a real device) |
| Unit — Core domain | `app/AdyenOutLoud.Tests` | Announcement playback, relay protocol parsing, relay configuration/endpoint derivation, connection retry/backoff | No |
| Architecture | `app/AdyenOutLoud.ArchitectureTests` | Dependency-direction rules (see [`docs/quality.md`](quality.md)) | No |
| UI smoke (documented, not yet automated) | — | See [UI smoke scenarios](#ui-smoke-scenarios-manual-today) below | Yes |

Each pure-logic layer is fast (sub-second) and has no external dependencies (no network, no
production Adyen, no production Cloudflare, no local secrets) — see
[Determinism](#determinism) below.

## Coverage

Both sides collect and enforce coverage; see the rationale in each config for *why* a given
threshold applies to a given file, not just the number.

**.NET (`AdyenOutLoud.Core` only):** `scripts/dotnet-coverage.sh` — 90% line / 85% branch floor.
Only `AdyenOutLoud.Core` is coverage-gated: it holds the project's actual business logic
(announcement composition, localization, relay configuration, retry/backoff). The MAUI head project
(`AdyenOutLoud`) is thin platform-adapter code around MAUI/OS APIs (`Plugin.Maui.Audio`,
`SecureStorage`, `Preferences`, `ClientWebSocket`) that isn't meaningfully unit-testable without
either a device/emulator or so much mocking of platform statics that the test would verify the
mock, not the code — see [UI smoke scenarios](#ui-smoke-scenarios-manual-today) for how that layer
is actually exercised instead. Architecture rules (Core stays platform-independent, no service
locator in code-behind, no circular references) are enforced separately and unconditionally by
`AdyenOutLoud.ArchitectureTests`, regardless of line coverage.

**Worker:** `vitest.config.ts` → `coverage.thresholds`, provider `istanbul` (native V8 coverage is
not supported for code running inside the Workers runtime — see
[Cloudflare's known issues](https://developers.cloudflare.com/workers/testing/vitest-integration/known-issues/)).
`src/adyen/**` (the Display parser, critical pure logic): 90% statements/lines, 85% branches,
90% functions. `src/relay-object.ts` (Durable Object transport wiring around that logic): 80%/75%/80%.
`src/index.ts` is excluded from the gate — it's thin HTTP routing already exercised end-to-end by
`worker.test.ts`, and gating it would mostly measure branches that are trivially covered by the
same handful of integration tests rather than adding signal.

Neither threshold was hit by writing implementation-detail tests to inflate a number — every added
test in this hardening pass asserts an actual behavior (a rejected malformed field, a specific
reconnect/backoff transition, a specific retention cutoff), and the current coverage percentages
are simply what covering that real behavior worked out to. Coverage must never regress below these
floors; if you touch covered code, run the coverage command before opening a PR (see
[`docs/quality.md`](quality.md) for the exact commands).

## Architecture tests

`AdyenOutLoud.ArchitectureTests` enforces, by inspecting the compiled `AdyenOutLoud.Core` assembly
and by scanning `.csproj`/`.cs` source (see the file-level comments for why each rule uses
reflection vs. source scanning):

- `AdyenOutLoud.Core` references no MAUI/Android/iOS/Windows assembly, and declares no type in a
  platform namespace.
- No production project references a test project; the project-reference graph has no cycles.
- `AdyenOutLoud`'s `ViewModels/*.cs` import no platform-specific namespace.
- `AdyenOutLoud`'s `*.xaml.cs` code-behind never constructs a service directly (`new SomeService()`)
  — dependencies must come through DI (`MauiProgram.cs`).

On the Worker side, the equivalent is `.dependency-cruiser.cjs` (`npm run architecture`): the
domain module (`src/adyen/**`) cannot import Cloudflare infrastructure or the entrypoint; the
parser cannot import the entrypoint; production code cannot import `test/`; no circular imports.

## Worker runtime tests

`worker/test/worker.test.ts` runs inside the actual Workers runtime (`workerd`) via
`@cloudflare/vitest-plugin`, driving the Worker's `fetch()` directly against `cloudflare:test`'s
`env` — hibernatable WebSockets and the Durable Object's tag-based routing are exercised against
their real implementation, not a mock. There is no storage to drive (see
[ADR 0007](adr/0007-display-only-stateless-company-scoped-relay.md)).

## Adversarial / parser tests

Webhook parsing is a trust boundary (see [`docs/threat-model.md`](threat-model.md)). Both
`worker/test/parsers.test.ts` and `app/AdyenOutLoud.Tests/ProductContractTests.cs`
(`RejectedMutations`) cover: invalid JSON, missing/empty bodies, wrong-typed fields, missing
required fields, malformed timestamps, unknown event types (forward compatibility), extra unknown
fields, unusual Unicode, and very long values. The rule throughout: a malformed notification is
rejected or ignored, never an unhandled exception that reaches the HTTP response, and unknown
fields never break forward compatibility.

## Routing and statelessness tests

`worker/test/worker.test.ts`'s "WebSocket relay" and "Durable Object internal contract" describe
blocks directly cover the scenarios that matter now that there's no persistence to correlate
against (see [ADR 0007](adr/0007-display-only-stateless-company-scoped-relay.md)):

- A successful Display notification is delivered only to the socket(s) tagged with the matching
  terminal serial — a different terminal receives nothing.
- A declined Display notification is never delivered.
- A notification ingested with no connected socket for its terminal is silently dropped — a socket
  that connects *after* ingest does not receive it (no queue, no replay).
- An arbitrary message sent by a client is ignored rather than closing the connection or erroring
  (there is no client → server protocol to violate).
- A recognized-but-incomplete Display notification is logged and swallowed rather than crashing the
  ingest path.

## Determinism

Tests don't depend on wall-clock timing where avoidable (the .NET reconnect/backoff tests inject a
fake `IRetryDelay`/`IClock`), don't reach the internet, don't touch production Adyen or Cloudflare,
and don't read local developer secrets. The Worker's Durable Object tests use `cloudflare:test`'s
in-process simulation, not a deployed Worker.

## Localization

Announcements are pre-recorded MP3s (`app/AdyenOutLoud/Resources/Raw/{PaymentReceived,TestAnnouncement}-{EN,ZH,MS,TA}.mp3`),
so there is no text or voice-selection logic to test.
`MauiSourceBoundaryTests.EveryLanguageHasBothRecordingsBundledWithTheApp` asserts every language has
both recordings, and `AnnouncementContractTests.TheRecordingForTheSelectedLanguageIsPlayed` asserts
the app asks for the recording matching the selected language. What no automated suite can check is
whether the recordings themselves sound natural and are appropriate for a retail context — have a
native speaker of each language listen to them before a live deployment. See
[ADR 0008](adr/0008-single-shared-relay-and-prerecorded-audio.md).

## UI smoke scenarios (manual today)

**Not automated in this repository yet** — building and stabilizing an Appium harness across four
platforms is a substantial, environment-dependent undertaking (device farms/emulators per
platform). Treat the list below as the manual pre-release checklist until an Appium suite exists,
and only make it a required PR gate once it's demonstrably stable — a flaky UI suite blocking every
PR is worse than no UI suite (see [`docs/quality.md`](quality.md) on not chasing gates for their
own sake):

- App starts and shows the relay-configuration form (empty on first launch).
- A terminal serial number can be entered and saved; they're pre-filled from storage
  on the next launch.
- Language can be changed; the selection persists.
- The test-speech command can be triggered and plays the selected language's test recording.
- Connection state (CONNECTING / LISTENING / NEEDS ATTENTION) is visible and updates correctly.
- **Background execution** (see [`docs/architecture.md`](architecture.md#client-architecture-maui)):
  on Android, backgrounding the app shows a persistent "listening" notification and a payment is
  still announced while backgrounded; on Windows/Mac Catalyst, minimizing the app doesn't interrupt
  announcements; on iOS, the status shows disconnected shortly after backgrounding and reconnects
  automatically on foreground.

## Mutation testing

Evaluates whether the test suite actually kills injected faults in the critical pure logic, not
just whether it executes the lines.

- **.NET (`AdyenOutLoud.Core`):** [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/) —
  configured (`app/AdyenOutLoud.Core/stryker-config.json`, `.config/dotnet-tools.json`), run with
  `dotnet tool restore && dotnet tool run dotnet-stryker --test-project ../AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj`
  from `app/AdyenOutLoud.Core`. The last measured score (56.32%, 2026-09-18) predates the
  display-only/stateless/company-scoped relay change and no longer reflects the current codebase —
  re-run it and record a fresh baseline before relying on this number. Runs in
  [`.github/workflows/scheduled-deep-quality.yml`](../.github/workflows/scheduled-deep-quality.yml)
  (weekly + manual dispatch), not on every PR — it takes a couple of minutes even for this small a
  codebase.
- **Worker (StrykerJS):** **Not wired up.** As of 2026-09-18, `@stryker-mutator/core`'s instrumenter
  pulls in a `@babel/*` 8.0.6 release line with an internal version mismatch
  (`@babel/helper-validator-identifier@^8.0.6` isn't resolvable against the rest of that line), so
  `npm install` fails outright — an upstream publishing problem, not a configuration issue. Retry
  once the Babel 8.0.x line settles; there's nothing project-specific to fix first.

## CI-required vs. scheduled checks

See [`docs/quality.md`](quality.md) for the full list and the reasoning behind what's required on
every PR versus what runs on a schedule.

## Adding a regression test

- **Bug fix:** add a test that fails before your fix and passes after, in the same layer the bug
  lived in (a parser bug → `parsers.test.ts` or the relevant `.NET` test file; a routing/fan-out
  bug → `worker.test.ts`).
- **New business logic:** unit test it in `AdyenOutLoud.Core` or `worker/src/adyen/**` — not in the
  MAUI head project or `worker/src/index.ts`, per the architecture rules above.
- **External-boundary change** (a new webhook field, a protocol field): add both a positive test
  and a negative/adversarial test (missing, wrong-typed, and — where relevant — malicious values).
