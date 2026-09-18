# Testing

## Test layers and who owns them

| Layer | Project | What it covers | Needs a device/emulator? |
| --- | --- | --- | --- |
| Unit — pure logic | `worker/test/parsers.test.ts` | Adyen parsers, `canonicalJson`, `asObject`/`asString` — no Workers runtime globals needed | No |
| Integration — Worker runtime | `worker/test/worker.test.ts` | HTTP ingress, Durable Object routing/SQLite, alarms, WebSocket + ACK + replay, dedup, retention, sensitive-data logging | No (runs inside `workerd` via `@cloudflare/vitest-plugin`, not a real device) |
| Unit — Core domain | `app/AdyenOutLoud.Tests` | Announcement composition, currency formatting, payment-method normalization, localization, relay protocol parsing, instance identity, speech-locale selection, connection retry/backoff | No |
| Architecture | `app/AdyenOutLoud.ArchitectureTests` | Dependency-direction rules (see [`docs/quality.md`](quality.md)) | No |
| UI smoke (documented, not yet automated) | — | See [UI smoke scenarios](#ui-smoke-scenarios-manual-today) below | Yes |

Each pure-logic layer is fast (sub-second) and has no external dependencies (no network, no
production Adyen, no production Cloudflare, no local secrets) — see
[Determinism](#determinism) below.

## Coverage

Both sides collect and enforce coverage; see the rationale in each config for *why* a given
threshold applies to a given file, not just the number.

**.NET (`AdyenOutLoud.Core` only):** `scripts/dotnet-coverage.sh` — 90% line / 85% branch floor.
Only `AdyenOutLoud.Core` is coverage-gated: it holds the project's actual business logic (parsing,
correlation-adjacent state, formatting, localization, retry/backoff). The MAUI head project
(`AdyenOutLoud`) is thin platform-adapter code around MAUI/OS APIs (`TextToSpeech`,
`SecureStorage`, `Preferences`, `ClientWebSocket`) that isn't meaningfully unit-testable without
either a device/emulator or so much mocking of platform statics that the test would verify the
mock, not the code — see [UI smoke scenarios](#ui-smoke-scenarios-manual-today) for how that layer
is actually exercised instead. Architecture rules (Core stays platform-independent, no service
locator in code-behind, no circular references) are enforced separately and unconditionally by
`AdyenOutLoud.ArchitectureTests`, regardless of line coverage.

**Worker:** `vitest.config.ts` → `coverage.thresholds`, provider `istanbul` (native V8 coverage is
not supported for code running inside the Workers runtime — see
[Cloudflare's known issues](https://developers.cloudflare.com/workers/testing/vitest-integration/known-issues/)).
`src/adyen/**` and `src/identity.ts` (critical pure logic): 90% statements/lines, 85% branches,
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

On the Worker side, the equivalent is `.dependency-cruiser.cjs` (`npm run architecture`): domain
modules (`src/adyen/**`, `src/identity.ts`) cannot import Cloudflare infrastructure or the
entrypoint; parsers cannot import the entrypoint; production code cannot import `test/`; no
circular imports.

## Worker runtime tests

`worker/test/worker.test.ts` runs inside the actual Workers runtime (`workerd`) via
`@cloudflare/vitest-plugin`, using `runInDurableObject`/`runDurableObjectAlarm`/`evictDurableObject`
from `cloudflare:test` to drive the Durable Object directly — SQLite storage, alarms, and
hibernatable WebSockets are exercised against their real implementation, not a mock.

## Adversarial / parser tests

Webhook parsing is a trust boundary (see [`docs/threat-model.md`](threat-model.md)). Both
`worker/test/parsers.test.ts` and `app/AdyenOutLoud.Tests/ProductContractTests.cs`
(`RejectedMutations`) cover: invalid JSON, missing/empty bodies, wrong-typed fields, missing
required fields, malformed timestamps, non-string/negative/fractional numeric fields, unknown
event types (forward compatibility), extra unknown fields, unusual Unicode, and very long values.
The rule throughout: a malformed notification is rejected or ignored, never an unhandled exception
that reaches the HTTP response, and unknown fields never break forward compatibility.

## Concurrency and ordering tests

`worker/test/worker.test.ts`'s "parsing and correlation" and "Hibernation WebSocket protocol"
describe blocks directly cover the scenarios that matter for "at most one logical announcement per
successful payment":

- Display-then-Authorisation and Authorisation-then-Display both correlate to the same rich result.
- A declined Display is never published, even after its fallback deadline passes.
- An unmatched/failed Authorisation never publishes on its own, even when its alarm runs.
- A late Authorisation arriving after the generic fallback already published never republishes or
  upgrades that announcement.
- Duplicate Display/Authorisation deliveries after publication don't create a second publication.
- Two connections to the same instance both receive live messages; an ACK from either deletes the
  message for both (see [ADR 0004](adr/0004-websocket-delivery-and-acknowledgements.md)).
- Reconnecting after eviction (`evictDurableObject`) correctly replays only what's still
  unacknowledged.

## Determinism

Tests don't depend on wall-clock timing where avoidable (the .NET reconnect/backoff tests inject a
fake `IRetryDelay`/`IClock`), don't reach the internet, don't touch production Adyen or Cloudflare,
and don't read local developer secrets. The Worker's Durable Object tests use `cloudflare:test`'s
in-process simulation, not a deployed Worker.

## Localization

`AnnouncementContractTests.FourLanguagesHaveIndependentResxAnnouncementsAndLocalizedFallbackMethod`
(and its sibling `EveryLanguageHasAnIndependentTestAnnouncement`) assert that all four languages
(`en`, `zh`, `ms`, `ta`) have independent, non-empty `.resx` entries and that
`ResxLocalizationService` correctly falls back to each culture's template. The TTS
voice-*selection* algorithm — given a requested locale like `zh-SG`, pick the best installed voice
— is tested separately and independently of any device or emulator in `SpeechLocaleSelectorTests`
(`app/AdyenOutLoud.Tests/SpeechLocaleSelectorTests.cs`), covering exact-locale match, same-base-language
fallback (e.g. `zh-SG` requested, only `zh-CN` installed), fallback to the platform's first reported
voice when no language match exists, case/separator-insensitive matching, and the "no voices
reported at all" case. This is what section 40 of the original hardening brief asked for:
preferred/fallback ordering for `en-SG`/`zh-SG`/`ms-SG`/`ta-SG` and fallback locales, tested apart
from the native TTS invocation itself (which `MauiSpeechService` still owns, and which can only be
exercised on an actual device/emulator — see [UI smoke scenarios](#ui-smoke-scenarios-manual-today)).

What is **not** tested, and can't be by an automated suite: whether the `zh`/`ms`/`ta` sentence
*translations themselves* are natural, correct, and appropriately formal for a retail context. They
were written for this project, not by a certified translator — have a native speaker review
`app/AdyenOutLoud.Core/Resources/Strings.{zh,ms,ta}.resx` before a live deployment. See
[ADR 0006](adr/0006-on-device-text-to-speech.md).

## UI smoke scenarios (manual today)

Section 20 of the original engineering brief for this hardening pass calls for Appium-based UI
smoke tests covering: app starts; generated URL is displayed; copy action is available; language
can be changed; language persists; test-speech command can be triggered; connection state is
visible. **These are not automated in this repository yet** — building and stabilizing an Appium
harness across four platforms is a substantial, environment-dependent undertaking (device
farms/emulators per platform) that was out of scope for this hardening pass to fabricate as
"passing." Treat the list above as the manual pre-release checklist until an Appium suite exists,
and only make it a required PR gate once it's demonstrably stable — a flaky UI suite blocking every
PR is worse than no UI suite (see [`docs/quality.md`](quality.md) on not chasing gates for their
own sake).

## Mutation testing

Evaluates whether the test suite actually kills injected faults in the critical pure logic, not
just whether it executes the lines.

- **.NET (`AdyenOutLoud.Core`):** [Stryker.NET](https://stryker-mutator.io/docs/stryker-net/) —
  configured (`app/AdyenOutLoud.Core/stryker-config.json`, `.config/dotnet-tools.json`) and
  **verified working**: `dotnet tool restore && dotnet tool run dotnet-stryker --test-project ../AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj`
  from `app/AdyenOutLoud.Core`. Last measured score: **56.32%** (2026-09-18, 264 tested mutants).
  That's a real, current baseline from this hardening pass, not a target — the coverage-floor work
  above prioritized closing branch-coverage gaps over chasing mutation score, so there is
  meaningful room to raise it (the report highlights exactly which surviving mutants to target
  first). Runs in [`.github/workflows/scheduled-deep-quality.yml`](../.github/workflows/scheduled-deep-quality.yml)
  (weekly + manual dispatch), not on every PR — it takes ~2 minutes even for this small a codebase.
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
  lived in (a parser bug → `parsers.test.ts` or the relevant `.NET` test file; a correlation/ordering
  bug → `worker.test.ts`).
- **New business logic:** unit test it in `AdyenOutLoud.Core` or `worker/src/adyen/**`/`identity.ts`
  — not in the MAUI head project or `worker/src/index.ts`, per the architecture rules above.
- **External-boundary change** (a new webhook field, a protocol field): add both a positive test
  and a negative/adversarial test (missing, wrong-typed, and — where relevant — malicious values).
