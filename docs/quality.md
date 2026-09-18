# Quality system

What's enforced, by what tool, and why. If you're wondering "why does CI reject this," the answer
is here. If you're wondering "why *doesn't* CI catch X," that's probably a deliberate scope
decision — also here.

## The rule this whole system follows

**When a tool finds a real issue, we fix the code.** We do not silence a warning, exclude a file
from a check, or weaken a threshold just to make a build green. Every suppression that does exist
in this repository is narrow, commented with why, and was a judgment call about a genuinely
incorrect or unavoidable warning — not a way to avoid fixing something. See
[`AGENTS.md`](../AGENTS.md#quality-gate-policy) for the policy agents (and contributors) are held
to.

## Tooling, by concern

| Concern | .NET | Worker (TypeScript) |
| --- | --- | --- |
| Formatting | `dotnet format` (`dotnet format --verify-no-changes`) | Prettier (`npm run format:check`) |
| Static analysis / lint | Built-in Roslyn analyzers, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true`, all warnings are build errors ([`Directory.Build.props`](../Directory.Build.props)) | ESLint 9 flat config + `typescript-eslint` `strictTypeChecked`/`stylisticTypeChecked` (type-aware), `eslint-config-prettier` to keep lint and formatting non-overlapping (`npm run lint -- --max-warnings 0`) |
| Type checking | The C# compiler itself, with `Nullable=enable` and `TreatWarningsAsErrors=true` everywhere | `tsc --noEmit` under `strict: true` plus `noUncheckedIndexedAccess`, `exactOptionalPropertyTypes`, and the rest of `tsconfig.json` (`npm run typecheck`) |
| XAML validation | `MauiStrictXamlCompilation` + `MauiEnableXamlCBindingWithSourceCompilation`, both `true` ([`AdyenOutLoud.csproj`](../app/AdyenOutLoud/AdyenOutLoud.csproj)) — binding errors fail the build, not just show a runtime warning | — |
| Architecture boundaries | `AdyenOutLoud.ArchitectureTests` (reflection over the compiled `Core` assembly + source scanning — see [`docs/testing.md`](testing.md)) | `dependency-cruiser` (`npm run architecture`) |
| Dead code | Compiler/analyzer diagnostics + architecture review (see [§ .NET dead-code policy](#net-dead-code-policy)) | `knip` (`npm run deadcode`) |
| Cloudflare binding types | `wrangler types` generates `worker-configuration.d.ts` from `wrangler.jsonc`; `wrangler types --check` fails CI if it's stale (`npm run types:check`) | (same) |
| Unit + integration tests | xUnit + `Microsoft.NET.Test.Sdk` (VSTest) — see [why not xUnit v3/Microsoft.Testing.Platform yet](#why-not-xunit-v3microsofttestingplatform-yet) | Vitest, via `@cloudflare/vitest-plugin` for anything touching the Workers runtime |
| Coverage | coverlet → Cobertura → `dotnet reportgenerator` (`scripts/dotnet-coverage.sh`) | `@vitest/coverage-istanbul` (native V8 coverage doesn't work inside `workerd`) |
| Security static analysis | CodeQL (`csharp`) | CodeQL (`javascript-typescript`) |
| Dependency vulnerabilities | NuGet audit at restore time, `NuGetAuditLevel=high` (fails the build) | `npm audit --audit-level=high` in CI |
| Supply-chain review (PRs) | GitHub's `dependency-review-action`, `fail-on-severity: high` | (same workflow, both ecosystems) |
| Dependency updates | Dependabot: NuGet, npm, GitHub Actions — weekly, grouped (see [`.github/dependabot.yml`](../.github/dependabot.yml)) | (same) |
| Static analysis (second opinion) | SonarCloud (`.github/workflows/sonarcloud.yml`, `sonar-project.properties`) — not yet configured with a real project/org/token, see [`docs/repository-settings.md`](repository-settings.md) | (same workflow, both ecosystems) |

## Why this and not something else

- **`dotnet format` over a third-party formatter** — it's the canonical, SDK-included C#
  formatter; no reason to add a second one.
- **Built-in Roslyn analyzers before third-party ones** — `latest-recommended` plus CodeQL already
  covers the high-signal cases (nullability, disposal, culture-sensitive formatting, security
  rules) without the noise of stacking multiple overlapping analyzer packages. No third-party
  analyzer package was added; if one is added later, it needs to earn its place the same way
  everything in this table did — see [`docs/dependencies.md`](dependencies.md).
- **Reflection/source-scanning architecture tests over ArchUnitNET** — this repository has three
  small, stable rule sets (Core's platform independence, the project-reference graph, ViewModel
  purity). ArchUnitNET was evaluated; for a rule set this size, hand-written tests against
  `Assembly.GetReferencedAssemblies()`/`GetTypes()` and `.csproj` source parsing are equally
  effective, add zero third-party dependency, and are easier for a contributor to read and extend
  without learning ArchUnitNET's fluent API. Revisit if the rule set grows significantly more
  complex.
- **`dependency-cruiser` over a custom ESLint rule** — dependency-cruiser expresses "module A can't
  import module B" rules directly and produces a readable violation report; encoding the same rules
  as `no-restricted-imports` patterns per-file would be more code for less clarity.
- **`knip` for the Worker, "compiler diagnostics + review" for .NET** — `knip` handles unused
  exports/dependencies/dead modules well for a TypeScript project this size and integrates cleanly
  with Vitest and Cloudflare Workers. .NET doesn't have an equivalently mature, low-noise
  equivalent that was worth adding as a new dependency on top of the existing analyzer set — see
  [§ .NET dead-code policy](#net-dead-code-policy).
- **Istanbul over V8 for Worker coverage** — V8 native coverage cannot observe code executing
  inside the Workers runtime (`workerd`); this is a documented Cloudflare limitation, not a choice
  — see [`docs/testing.md`](testing.md).

## .NET dead-code policy

No dead-code scanner is used for the .NET projects. `EnableNETAnalyzers` + `AnalysisLevel=latest-recommended`
already flags genuinely unused private members (CA1823/IDE0051-class rules) as build errors, and
the codebase is small enough that unused public surface is caught in code review and by the
architecture tests' project-reference graph check. Revisit if the .NET codebase grows large enough
that this stops being reliable.

## Why not xUnit v3/Microsoft.Testing.Platform yet

Evaluated and deliberately deferred: on .NET 10, xUnit v3 with Microsoft.Testing.Platform v2
currently requires `xunit.v3.mtp-v2` plus `Microsoft.Testing.Extensions.CodeCoverage` in place of
`coverlet.collector`, since VSTest-bridge coverage support was removed. That's two coordinated
changes (test runner *and* coverage tooling) to a stack that already works, tests, and gates
coverage correctly today. The current xUnit 2.9 + `Microsoft.NET.Test.Sdk` (VSTest) +
`coverlet.collector` stack is stable, well-documented, and every command in this document works
against it as verified in this hardening pass. Revisit once the MTP v2 + coverage-extension
combination has had time to stabilize in the wider ecosystem.

## Baseline (last verified 2026-09-19)

Every check below was run against this repository's actual state as part of the display-only/
stateless/company-scoped relay change (see
[ADR 0007](adr/0007-display-only-stateless-company-scoped-relay.md)) — not asserted from memory:

- **Worker:** `npm run quality` (format check → lint → typecheck → `wrangler types --check` →
  architecture → dead-code → coverage) passes with 0 findings. `npm audit`: 0 vulnerabilities.
  36 tests passing.
- **.NET:** `AdyenOutLoud.Core`, `AdyenOutLoud.Tests`, `AdyenOutLoud.ArchitectureTests`, and the
  Android head build all build with 0 warnings/0 errors under `TreatWarningsAsErrors=true`.
  `dotnet format --verify-no-changes` passes. 58 unit tests + 8 architecture tests passing.
  Core coverage: 91.6% line / 87.2% branch (floor: 90%/85%).
- **iOS, Mac Catalyst, Windows builds:** not locally verifiable in this environment (this
  development machine's installed Xcode (27.0) is ahead of what the .NET 10 iOS/macCatalyst
  workload currently requires (26.6); Windows-only build tooling isn't available on macOS/Linux) —
  see [`docs/development.md`](development.md) for the exact versions and
  [`.github/workflows/platform-builds.yml`](../.github/workflows/platform-builds.yml) for how CI
  builds them on the correct OS per platform.

## Local quality commands

See [`docs/development.md`](development.md#one-command-quality-checks) for `scripts/quality.sh` /
`scripts/quality.ps1` and how closely each approximates CI.

## Material decisions made during this hardening pass

- Central Package Management (`Directory.Packages.props`) adopted for the four .NET projects —
  see [`docs/dependencies.md`](dependencies.md).
- The app's four supported languages were corrected from an unrelated `en/nl/de/fr` set to the
  actually-intended `en/zh/ms/ta` (English, Chinese, Malay, Tamil), matching Singapore-market
  locales (`en-SG`/`zh-SG`/`ms-SG`/`ta-SG`) — this was a genuine functional gap found during the
  audit, not a hardening-only change. The `zh`/`ms`/`ta` translations added should be reviewed by
  a native speaker before a live deployment — see [`docs/testing.md`](testing.md#localization) and
  [ADR 0006](adr/0006-on-device-text-to-speech.md).
- The TTS voice-selection algorithm was extracted from `MauiSpeechService` into a pure,
  independently-tested `SpeechLocaleSelector` in `AdyenOutLoud.Core` — it was previously untestable
  without a device.
- `Env` for the Worker is now entirely generated by `wrangler types` (`worker-configuration.d.ts`,
  committed, checked in CI) instead of hand-maintained in `src/adyen/models.ts` — see
  [`docs/dependencies.md`](dependencies.md).
- `typescript` is pinned to `6.0.3` (exact) rather than the newer TypeScript 7 line — see
  [`docs/dependencies.md`](dependencies.md#typescript-7) for why.
