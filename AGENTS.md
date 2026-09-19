# AGENTS.md

Instructions for AI coding agents working in this repository. Human contributors: see
[`CONTRIBUTING.md`](CONTRIBUTING.md) (this file is more prescriptive about exact commands and
architecture rules, but the two shouldn't contradict each other — if they do, that's a bug in one
of them, please fix it).

## Project overview

Adyen Out Loud turns a successful Adyen terminal payment into a spoken "Payment successful"
confirmation on a nearby device. A Cloudflare Worker receives Adyen's Display webhook, routes it by
terminal serial in a stateless Durable Object (dropping it if no app is connected), and pushes a
message over WebSocket to a .NET MAUI app (Android/iOS/macOS/Windows), which plays a pre-recorded
clip in one of four languages (English, Chinese, Malay, Tamil). There is no backend account system
and one shared relay URL (`https://adyenoutloud.adam-eea.workers.dev`) compiled into the app; each
app instance is configured only with its terminal serial number. Read
[`docs/architecture.md`](docs/architecture.md) before making any non-trivial change; read
[`docs/threat-model.md`](docs/threat-model.md) before touching anything related to routing or
webhook validation.

## Repository map

```text
app/                          .NET MAUI solution
  AdyenOutLoud.Core/           Platform-independent logic — Models/, Abstractions/, Services/, Resources/*.resx
  AdyenOutLoud/                MAUI head project (Android/iOS/MacCatalyst/Windows) — ViewModels/, Services/ (platform adapters), MainPage.xaml
  AdyenOutLoud.Tests/           xUnit tests for Core
  AdyenOutLoud.ArchitectureTests/  Enforces the architecture rules below, in CI on every PR
worker/                       Cloudflare Worker (TypeScript)
  src/index.ts                 HTTP entrypoint: validate, route, delegate — no business logic
  src/relay-object.ts           Durable Object: ingest, terminal-tagged WebSocket fan-out, no storage
  src/adyen/                    Pure Display-parsing/model logic — no Cloudflare imports
  test/worker.test.ts            Integration tests (real Workers runtime via @cloudflare/vitest-plugin)
  test/parsers.test.ts           Fast unit tests for the pure logic above
docs/                          Everything described below — read before assuming behavior
scripts/                       quality.sh / quality.ps1 / dotnet-coverage.sh
.github/workflows/             CI — see docs/quality.md for what each job enforces
```

## Architecture rules

These are enforced by `AdyenOutLoud.ArchitectureTests` and `dependency-cruiser`
(`npm run architecture` in `worker/`) — a violation fails CI, not just a style nit:

- `AdyenOutLoud.Core` must never reference `Microsoft.Maui`, Android, iOS/UIKit, or Windows
  assemblies, and must declare no type in a platform namespace.
- The MAUI head project's `ViewModels/*.cs` must import no platform-specific namespace; put
  platform calls behind a Core abstraction implemented under `Services/`.
- `*.xaml.cs` code-behind must never construct a service directly (`new SomeService()`) —
  dependencies come through constructor injection registered in `MauiProgram.cs`.
- No production project may reference a test project; the project-reference graph must have no
  cycles.
- `worker/src/adyen/**` must never import Cloudflare infrastructure (`cloudflare:*` modules,
  `src/index.ts`, `src/relay-object.ts`) — it must stay runnable in a plain test host.
- `worker/src/adyen/*-parser.ts` must never import `src/index.ts`.
- External JSON always starts as `unknown` (TypeScript) / is walked field-by-field via
  `JsonElement`/`JsonDocument` (C#) and validated before any typed value is trusted — never cast
  or reflection-deserialize untrusted input directly into a domain type. See
  [`docs/security.md#json-assumptions`](docs/security.md#json-assumptions).

Full picture: [`docs/architecture.md`](docs/architecture.md).

## Required commands

```bash
# Worker (from worker/)
npm ci                    # restore — always this in CI, `npm install` locally only when adding a dependency
npm run format             # fix formatting
npm run format:check       # verify formatting (what CI runs)
npm run lint                # ESLint, --max-warnings 0
npm run typecheck           # tsc --noEmit
npm run types:check         # wrangler types --check — fails if wrangler.jsonc changed without regenerating types
npm run types:generate      # regenerate worker-configuration.d.ts after a wrangler.jsonc binding change
npm run architecture        # dependency-cruiser boundary rules
npm run deadcode             # knip
npm run test                 # vitest, inside the real Workers runtime
npm run test:coverage        # vitest with coverage thresholds enforced
npm run quality               # everything above, in the order CI runs it

# .NET (from app/)
dotnet restore AdyenOutLoud.slnx
dotnet format AdyenOutLoud.slnx --verify-no-changes      # what CI runs; drop --verify-no-changes to fix
dotnet build AdyenOutLoud.Core/AdyenOutLoud.Core.csproj
dotnet test AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj
dotnet test AdyenOutLoud.ArchitectureTests/AdyenOutLoud.ArchitectureTests.csproj
../scripts/dotnet-coverage.sh      # unit tests + coverage threshold check (from app/, or scripts/dotnet-coverage.sh from repo root)
dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android      # needs: dotnet workload install android
dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-ios           # macOS + Xcode 26.6 only, see docs/development.md
dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-maccatalyst   # macOS + Xcode 26.6 only
dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-windows10.0.19041.0  # Windows only

# Everything runnable on the current OS (from repo root)
scripts/quality.sh    # or scripts/quality.ps1 on Windows
```

## Before making changes

- Read the adjacent implementation and its existing tests before editing — don't assume behavior
  from the function name alone; the parsers in particular (`worker/src/adyen/*`,
  `RelayProtocol.cs`) have deliberate, non-obvious rejection rules (see
  [`docs/security.md`](docs/security.md) and [`docs/threat-model.md`](docs/threat-model.md)).
- Preserve the architecture rules above — if a change seems to require violating one, that's a
  signal to reconsider the design, not to add a suppression.
- Don't add a dependency without justifying it against [`docs/dependencies.md`](docs/dependencies.md)'s
  checklist first (BCL/framework sufficiency, maintenance, license, known vulnerabilities, stable
  release).
- When behavior is version-sensitive (a .NET/MAUI API, a Cloudflare Workers API, an npm package's
  current major version), check current official documentation rather than relying on training
  data — this project has already hit real version-skew issues during its hardening pass (see
  [`docs/dependencies.md#typescript-7`](docs/dependencies.md#typescript-7)) that only showed up by
  actually checking.

## Before completing changes

Run, in this order, whatever subset applies to what you changed:

1. Format (`dotnet format` / `npm run format`)
2. Lint (ESLint) / analyzers (implicit in `dotnet build` — warnings are errors)
3. Type-check (`tsc --noEmit` / implicit in `dotnet build`)
4. Relevant tests (unit + integration for what you touched)
5. Architecture tests (`AdyenOutLoud.ArchitectureTests`, `npm run architecture`) if you touched any
   project/module boundary
6. The complete quality suite (`scripts/quality.sh`/`.ps1`) when practical — see
   [`docs/development.md`](docs/development.md#one-command-quality-checks) for what it covers
7. Update documentation for any behavior, configuration, or protocol change — README, the relevant
   `docs/*.md`, or a new ADR under `docs/adr/` for a decision with real trade-offs
8. **Report, explicitly, any check you could not run** (e.g., "iOS build not verified — no macOS
   available") — never claim a check passed without having run it. This is not optional: this
   project's own hardening pass hit real environment limitations (Xcode version mismatch, no
   Windows machine) and documented them in [`docs/quality.md`](docs/quality.md) and
   [`docs/development.md`](docs/development.md) rather than asserting success.

## Coding standards

- **C#**: nullable reference types enabled everywhere, file-scoped namespaces, `var` when the type
  is apparent, expression-bodied members where they fit on one line, primary constructors where
  they read cleanly — see [`.editorconfig`](.editorconfig) for the enforced specifics.
  `ConfigureAwait(false)` is required outside test projects (`CA2007`); avoid `async void` except
  genuine UI event handlers; never block on async code with `.Wait()`/`.Result`/`.GetAwaiter().GetResult()`.
- **TypeScript**: `strict` plus `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`, and the
  rest of `tsconfig.json`'s stricter options. No `any` without a narrowly-scoped, commented reason
  — use `unknown` plus validation instead. No double-cast (`as unknown as T`) to bypass the type
  checker.
- **XAML**: every binding needs a correct `x:DataType` — never `x:DataType="x:Object"` to silence a
  warning. `MauiStrictXamlCompilation` and `MauiEnableXamlCBindingWithSourceCompilation` are both
  `true`; a binding error must fail the build, not surface at runtime.

## Testing requirements

- Every bug fix needs a regression test that fails before the fix and passes after, in the layer
  the bug actually lived in.
- New business logic needs unit tests — in `AdyenOutLoud.Core` or `worker/src/adyen/**`, never in
  the MAUI head project or `worker/src/index.ts` (see the architecture rules above).
- A change to an external boundary (a new/changed webhook field, a relay-protocol field) needs both
  a positive test and a negative/adversarial test (missing, wrong-typed, and where relevant,
  malicious values) — see [`docs/testing.md`](docs/testing.md) for the existing pattern to follow.

## Security

Never:

- Log raw payment payloads at any level — see
  [`docs/security.md#logging-and-redaction`](docs/security.md#logging-and-redaction).
- Commit credentials, signing material, or Cloudflare/Apple/Android secrets.
- Weaken or disable TLS certificate validation.
- Bypass payload/input validation "to make a test pass" — the validation is the thing under test.
- Use non-cryptographic randomness (`System.Random`, `Math.random()`) for anything security-relevant.

## Dependency policy

Before adding a dependency: justify why platform/BCL/framework functionality isn't sufficient,
confirm it's actively maintained, check its license is MIT-compatible, check for known
vulnerabilities, and use a stable release (not a pre-release/canary) unless you document why — see
[`docs/dependencies.md`](docs/dependencies.md) for the full checklist and current examples.

## Quality-gate policy

**Fix failures, don't disable the gate that caught them.** This is a hard rule, not a preference:

- No blanket `eslint-disable`, `#pragma warning disable`, or equivalent to make a file pass.
- No excluding a file/directory from coverage or analysis to raise a number.
- No weakening a TypeScript compiler option or disabling nullable reference types.
- No skipping/deleting a failing test instead of fixing what it caught.
- No lowering a coverage threshold to match wherever the number currently sits.

A narrow suppression is acceptable only when **all** of: the warning is genuinely incorrect or
truly unavoidable; the suppression is scoped as narrowly as the tool allows (one line/one rule, not
a file or directory); it has a comment explaining why; ideally there's a test demonstrating the
suppressed case is actually safe; and it stays visible in code review, not hidden in a config file
that silently applies everywhere. If you think a gate itself is wrong, say so explicitly and open
that as its own reviewable change — don't quietly work around it.

## Documentation policy

An architectural decision with real trade-offs gets an ADR (`docs/adr/`, see
[`docs/adr/README.md`](docs/adr/README.md) for the format and when one is/isn't warranted). Any
user-facing behavior change, configuration change, or relay-protocol change updates the relevant
doc in the same PR — not as a follow-up. Don't create a new top-level doc for something that
already has a natural home in an existing one.

## Subsystem-specific guidance

No `worker/AGENTS.md` or `app/AGENTS.md` exists — the rules above apply uniformly to both, and
splitting them out didn't add enough subsystem-specific detail to be worth the duplication risk.
If that changes (a subsystem accumulates enough genuinely distinct guidance), add one rather than
overloading this file.
