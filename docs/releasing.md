# Releasing

**Nothing described below is automated yet.** This project has no tagged releases and no release
workflow in `.github/workflows/` — this document describes the process to follow when cutting one
by hand, and doubles as the spec for release automation if/when it's built. Don't read any of this
as "CI already does this."

## Process

1. **Update the version.**
   - Worker: bump `version` in `worker/package.json` (informational — Cloudflare Workers don't use
     semver for deployment, but keep it in sync with the app for release notes).
   - App: bump `ApplicationDisplayVersion` and `ApplicationVersion` in
     [`app/AdyenOutLoud/AdyenOutLoud.csproj`](../app/AdyenOutLoud/AdyenOutLoud.csproj).
2. **Run the full quality suite** — `scripts/quality.sh` (or `.ps1`), plus the platform-specific
   `dotnet build -f <tfm>` commands for every platform you can build locally. Every check in
   [`docs/quality.md`](quality.md)'s baseline table must pass; don't cut a release with a known-red
   check.
3. **Platform builds** — verify (locally or by checking the latest `platform-builds.yml` run on
   the release commit) that Android, iOS, Mac Catalyst, and Windows all build in `Release`
   configuration.
4. **Worker release**
   ```bash
   cd worker
   npm ci
   npx wrangler deploy
   ```
   Confirm the deployed `compatibility_date` matches `wrangler.jsonc` (see
   [Cloudflare compatibility-date updates](#cloudflare-compatibility-date-updates) below — a
   compat-date change should already have been tested and merged well before a release, not bundled
   into one).
5. **App release artifacts** — build signed packages per platform. **Signing is not configured in
   this repository** (see [Signing requirements](#signing-requirements) below) — CI's
   `platform-builds.yml` deliberately proves the app *compiles* without needing any signing
   secrets; producing an installable, signed artifact is a separate, currently-manual step.
6. **Tag and release notes** — create a Git tag (`vX.Y.Z`) and a GitHub Release. This project has
   no `CHANGELOG.md` — write the release notes directly from the commits/PRs included (e.g.
   `gh release create vX.Y.Z --generate-notes`).
7. **Verify** — install the released app build and confirm it connects to the deployed Worker
   end-to-end (see [`docs/index.md`](index.md) for the manual verification steps).

## Signing requirements

Not configured. Each platform needs its own signing identity, kept **out of this repository**:

- **Android** — a keystore + credentials, supplied to a build pipeline as secrets, never committed
  (see [`.gitignore`](../.gitignore) — `*.keystore`, `*.jks`).
- **iOS / Mac Catalyst** — an Apple Developer signing certificate + provisioning profile.
- **Windows** — a code-signing certificate, if you intend to distribute outside the Microsoft
  Store.

If you wire up signing later, supply the certificates/keys via your CI provider's encrypted
secrets store (GitHub Actions secrets, scoped to a protected environment) — never as a committed
file, and never exposed to a fork pull request's workflow run (see
[`docs/repository-settings.md`](repository-settings.md) on why `pull_request` workflows from forks
don't get secret access by default).

## Cloudflare compatibility-date updates

`wrangler.jsonc`'s `compatibility_date` is a deliberate, tested change — never bumped
automatically or as a drive-by part of an unrelated PR:

1. Read Cloudflare's [compatibility date changelog](https://developers.cloudflare.com/workers/configuration/compatibility-dates/)
   for what changed between the current and target date.
2. Update `compatibility_date` in `worker/wrangler.jsonc`.
3. Regenerate types: `npm run types:generate` (commit the resulting `worker-configuration.d.ts`
   diff, if any — `npm run types:check` in CI will fail the PR if you forget).
4. Run the full Worker test suite (`npm run quality`) — a compatibility-date bump can change
   runtime behavior in ways only integration tests will catch.
5. Open this as its own PR, separate from feature work, so a regression is easy to bisect to.

## Rollback

- **Worker:** `npx wrangler deployments list` then `npx wrangler rollback <deployment-id>` —
  Cloudflare keeps prior deployments; this is fast and requires no code change.
- **App:** there is no forced-upgrade or remote kill-switch mechanism — a bad app release can only
  be walked back by publishing a new release through each platform's store (or, for Windows/direct
  distribution, redistributing the previous build). Plan release verification accordingly; the
  Worker is far cheaper to roll back than the app.
