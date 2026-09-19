# Contributing

Thanks for considering a contribution. This document covers the human contributor workflow; if
you're an AI coding agent, read [`AGENTS.md`](AGENTS.md) instead (or in addition — it's more
prescriptive about commands and architecture rules).

## Development setup

See [`docs/development.md`](docs/development.md) for exact prerequisites, versions, and commands.
Short version:

```bash
# Worker
cd worker && npm ci && npm run quality

# .NET
cd app && dotnet restore AdyenOutLoud.slnx && dotnet test AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj
```

## Branching

Branch from `main`, open a PR against `main`. No particular branch-naming convention is enforced,
but a short descriptive name (`fix-alarm-race`, `add-ms-locale-fallback`) helps.

## Before opening a PR

- Run `scripts/quality.sh` (or `.ps1` on Windows) — see [`docs/development.md`](docs/development.md#one-command-quality-checks)
  for what it covers and what it doesn't (platform-specific builds).
- If you touched `app/**`, also run the `dotnet build -f <tfm>` command for any platform you can
  build locally (see [`docs/development.md`](docs/development.md)).
- Add tests: a bug fix needs a regression test in the layer the bug lived in; new business logic
  needs unit tests; a change to an external boundary (a webhook field, the relay protocol) needs
  both positive and negative/adversarial tests. See [`docs/testing.md`](docs/testing.md).
- Update documentation for any behavior, configuration, or protocol change — README, the relevant
  `docs/*.md`, or an ADR under `docs/adr/` if it's a decision with real trade-offs (see
  [`docs/adr/README.md`](docs/adr/README.md) for when an ADR is warranted vs. overkill).

## Architectural rules (enforced, not just convention)

- `AdyenOutLoud.Core` must never reference `Microsoft.Maui` or any platform assembly.
- The MAUI head project's `ViewModels/` must stay platform-agnostic; put platform calls behind a
  Core abstraction implemented under `Services/`.
- No `.xaml.cs` code-behind may construct a service directly (`new SomeService()`) — dependencies
  come through DI (see `MauiProgram.cs`).
- The Worker's `src/adyen/**` and `src/identity.ts` must never import Cloudflare infrastructure
  (`cloudflare:*`, `src/index.ts`, `src/relay-object.ts`).
- No project may reference a test project; the project-reference graph must have no cycles.

These are enforced by `AdyenOutLoud.ArchitectureTests` and `dependency-cruiser`
(`npm run architecture`) respectively — a PR that violates one will fail CI, not just review. See
[`docs/architecture.md`](docs/architecture.md) for the full picture.

## Formatting and linting

- C#: `dotnet format` — CI runs `--verify-no-changes`; run it locally the same way before pushing.
- TypeScript: Prettier for formatting (`npm run format` to fix, `format:check` to verify), ESLint
  for correctness (`npm run lint`). Don't hand-format C# or TypeScript to "match style" — run the
  tool.

## Quality-gate policy

**Fix the underlying issue, not the gate.** When a linter, analyzer, or test catches something
real, fix the code. Do not:

- Add a blanket `eslint-disable` or `#pragma warning disable` to make a file pass
- Exclude a file/directory from coverage or analysis to raise a number
- Weaken a TypeScript compiler option or turn off nullable reference types
- Skip or delete a failing test instead of fixing what it caught
- Lower a coverage threshold to match wherever the number currently sits

A narrow, well-justified suppression is occasionally the right call — see
[`AGENTS.md`](AGENTS.md#quality-gate-policy) for the exact bar it has to clear (genuinely incorrect
warning, scoped as narrowly as possible, commented with why, visible in review). If you think a
gate itself is wrong (a threshold too strict for what it's actually measuring, a rule that doesn't
fit this codebase), open that as its own PR with the reasoning — don't quietly work around it in an
unrelated change.

## Dependency policy

Before adding a dependency, see [`docs/dependencies.md`](docs/dependencies.md) — in short: justify
why the BCL/framework/platform APIs aren't enough, confirm active maintenance and license
compatibility, check for known vulnerabilities, and prefer a stable release over a pre-release
unless there's a documented reason.

## Documentation expectations

If your change alters observable behavior, configuration, or the relay protocol, the PR isn't done
until the relevant doc reflects it. See the [PR template](.github/PULL_REQUEST_TEMPLATE.md)
checklist.

## Security

- Never log raw payment payloads — see [`docs/security.md`](docs/security.md#logging-and-redaction).
- Never commit credentials, real webhook URLs, signing material, or Cloudflare/Apple/Android
  credentials — see [`.gitignore`](.gitignore).
- Never weaken TLS certificate validation.
- Never bypass payload validation to "just make a test pass" — validation is the point of the code
  being tested.
- Report vulnerabilities privately — see [`SECURITY.md`](SECURITY.md), not a public issue.

## Getting help

See [`SUPPORT.md`](SUPPORT.md).
