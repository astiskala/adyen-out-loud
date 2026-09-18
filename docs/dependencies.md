# Dependency policy

## Before adding a dependency

1. **Justify why platform/BCL/framework functionality isn't enough.** .NET's BCL and MAUI, and the
   Workers runtime's Web APIs, already cover most of what this project needs — the existing
   dependency lists are short on purpose.
2. **Confirm active maintenance** — recent releases, open issues actually get triaged.
3. **Check the license** — must be compatible with this project's MIT license (see
   [`LICENSE`](../LICENSE)).
4. **Check for known vulnerabilities** — `npm audit` / NuGet audit before merging, not after.
5. **Check platform support** — a Worker dependency must run inside `workerd`, not just Node; a
   .NET dependency must support every target TFM this project builds (or be scoped to one, like
   test-only packages).
6. **Use a stable release**, not a pre-release/canary, unless there's a documented reason (see
   [TypeScript 7](#typescript-7) below for what that reason looks like in practice).

Production dependencies are kept smaller than dev-only tooling wherever possible — the Worker ships
zero runtime npm dependencies today (everything in `worker/package.json` is a `devDependency`); the
app's only non-Microsoft runtime dependency is the .NET BCL itself.

## Current dependencies and why each exists

### .NET (`Directory.Packages.props`)

| Package | Why |
| --- | --- |
| `Microsoft.Maui.Controls` | The UI framework — see [ADR 0001](adr/0001-use-dotnet-maui.md). |
| `Microsoft.Extensions.Logging.Debug` | Debug-build log output during development. |
| `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio` | Test framework/runner — see [`docs/quality.md#why-not-xunit-v3microsofttestingplatform-yet`](quality.md#why-not-xunit-v3microsofttestingplatform-yet). |
| `coverlet.collector` | Coverage collection feeding `scripts/dotnet-coverage.sh`. |
| `dotnet-reportgenerator-globaltool` (local tool, `.config/dotnet-tools.json`) | Merges coverlet's Cobertura output into the enforced-threshold summary. |
| `dotnet-stryker` (local tool) | Mutation testing for `AdyenOutLoud.Core` — see [`docs/testing.md#mutation-testing`](testing.md#mutation-testing). |

No third-party analyzer package was added — see
[`docs/quality.md`](quality.md#why-this-and-not-something-else) for why the built-in Roslyn
analyzer set plus CodeQL was judged sufficient. ArchUnitNET was evaluated for architecture tests
and deliberately not added; see the same section.

### Worker (`worker/package.json`) — all dev-only

| Package | Why |
| --- | --- |
| `@cloudflare/vitest-plugin`, `wrangler` | Official Workers tooling — dev server, deploy, and the Vitest integration that runs tests inside `workerd`. |
| `typescript` | Compiler/type checker — pinned to `6.0.3`, see [below](#typescript-7). |
| `vitest`, `@vitest/coverage-istanbul` | Test runner and coverage — see [`docs/quality.md`](quality.md) for why Istanbul, not V8. |
| `eslint`, `@eslint/js`, `typescript-eslint`, `eslint-config-prettier` | Linting — see [`docs/quality.md`](quality.md) for the rule categories enabled. |
| `prettier` | Formatting, kept strictly separate from ESLint's correctness rules. |
| `dependency-cruiser` | Architecture-boundary enforcement (`.dependency-cruiser.cjs`). |
| `knip` | Dead-code/unused-dependency detection. |
| `@types/node` | Ambient Node types — `wrangler types` itself asked for this once Node compatibility typings were needed; not imported by name anywhere in source. |

`@cloudflare/workers-types` was **removed** during this hardening pass: `wrangler types` now
generates a more precise, binding-accurate `Env` (tied to the actual `RelayObject` Durable Object
class) directly from `wrangler.jsonc`, which supersedes the generic hand-installed types package —
see [`docs/quality.md`](quality.md) and the "Action required" notice `wrangler types` itself prints.

### TypeScript 7

`typescript` is pinned to the **exact** version `6.0.3`, not `latest`/`^7`, even though TypeScript
7.0 (the native Go compiler) reached general availability before this hardening pass and is
dramatically faster. As of 2026-09-18, TS 7.0 ships without the programmatic compiler API
(`ts.createProgram`, etc.) that `typescript-eslint`'s type-aware linting depends on —
`typescript-eslint@8.70.0` declares a peer dependency of `typescript@>=4.8.4 <6.1.0`, and installing
TS 7 alongside it produces an unresolved/invalid peer dependency. The documented interim options
are running two TypeScript installs side by side (a compatibility shim package for ESLint) or
staying on the last TS 6.x release until `typescript-eslint` ships support for TS 7.1 (which is
expected to restore the programmatic API). This project chose the simpler option — one pinned
compiler — because a second parallel TypeScript install for a project this size added complexity
disproportionate to the compile-speed benefit. **Revisit this pin** once `typescript-eslint`
publishes TS 7.1 support; bump `typescript` and remove this note when you do.

## Update policy

| What | How |
| --- | --- |
| .NET SDK / workload set | Edit [`global.json`](../global.json) (`sdk.version`, `sdk.workloadVersion`). Run `dotnet --version` and `dotnet workload --info` after to confirm the pin took effect, then run the full quality suite before merging. |
| NuGet packages | Dependabot opens grouped weekly PRs against `Directory.Packages.props`; each must pass the full quality suite (including architecture and coverage gates) before merging like any other PR. |
| Node | Update `worker/.node-version` and `worker/package.json`'s `engines.node`, matching the version installed in CI. |
| npm dependencies | Dependabot, grouped weekly (lint/format tooling, Cloudflare tooling, Vitest, each as one group to reduce noise). |
| Wrangler | Dependabot bumps it like any other npm dependency; after any bump, re-run `wrangler types` and commit the regenerated `worker-configuration.d.ts` if it changed. |
| Cloudflare compatibility date | Deliberate and manual, never automatic — see [`docs/releasing.md`](releasing.md#cloudflare-compatibility-date-updates). |
| GitHub Actions | Dependabot's `github-actions` ecosystem entry in [`.github/dependabot.yml`](../.github/dependabot.yml) — PRs update the pinned commit SHA comments too, since actions are pinned by SHA (see [`docs/repository-settings.md`](repository-settings.md)). |

Every Dependabot PR — including security-update PRs — must still pass the full required quality
suite before merging. Security-relevant updates should be reviewed and merged promptly; routine
version bumps can wait for a normal review cycle.
