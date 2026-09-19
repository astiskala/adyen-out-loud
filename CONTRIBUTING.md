# Contributing

Branch from `main` and open a pull request. AI coding agents should read [`AGENTS.md`](AGENTS.md) too.

## Before opening a PR

- Set up and run the checks from [`docs/development.md`](docs/development.md); `scripts/quality.sh`
  (or `.ps1`) runs what CI requires on your OS.
- Add tests: a bug fix needs a regression test in the layer the bug lived in, and a change at the
  webhook or protocol boundary needs a malformed-input test as well as a valid one.
- Update the docs and README for any behavior, configuration, or protocol change, and add an ADR under
  [`docs/adr/`](docs/adr/) for a decision with real trade-offs.

## Rules that CI enforces

- `AdyenOutLoud.Core` never references MAUI or platform assemblies; view models stay platform-agnostic;
  code-behind never constructs services (use DI); the Worker's `src/adyen/**` never imports
  Cloudflare modules. See [`docs/architecture.md`](docs/architecture.md).
- Formatting is `dotnet format` and Prettier; don't hand-format.
- **Fix the problem, not the gate.** Don't add blanket lint or warning suppressions, exclude files
  from coverage, lower thresholds, or skip failing tests. A narrow suppression needs a comment saying why.

## Security

- Never log raw payment payloads, weaken TLS validation, or bypass input validation to make a test pass.
- Never commit credentials or signing material.
- Report vulnerabilities privately ([`SECURITY.md`](SECURITY.md)), not in a public issue.
