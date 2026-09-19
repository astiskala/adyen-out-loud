## What changed and why

<!-- One or two sentences. Link an issue if there is one. -->

## Checklist

- [ ] Tests added or updated for this change (bug fixes need a regression test; new business logic needs unit tests)
- [ ] `npm run quality` passes locally in `worker/` (if the Worker changed)
- [ ] `dotnet format --verify-no-changes` and `dotnet test` pass locally in `app/` (if the app changed)
- [ ] Architecture rules preserved — see [AGENTS.md](../AGENTS.md#rules-enforced-by-ci) (Core stays platform-independent, Worker domain code stays Cloudflare-free, no new circular references)
- [ ] Documentation updated for any behavior, configuration, or protocol change (README, docs/, or AGENTS.md)
- [ ] No secrets, tokens, real webhook URLs, or sensitive payment data included in this PR or its description

## Checks that could not be run locally (if any)

<!-- e.g. "iOS build not verified — no macOS available" — CI will confirm the rest. -->
