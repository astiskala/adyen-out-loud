# Recommended GitHub repository settings

These are **recommendations for whoever administers this repository on GitHub** — none of this can
be committed as a file, and none of it has been verified as actually enabled (this hardening pass
had no GitHub repository to configure against; the repository doesn't have a remote yet). Treat
this as a setup checklist, not a claim that these protections are live.

## Branch protection (`main`)

- **Require a pull request before merging** — no direct pushes to `main`, including by
  administrators if your plan allows enforcing that.
- **Require status checks to pass before merging**, and **require branches to be up to date**
  before merging. At minimum, require:
  - `Quality / Worker (lint, types, architecture, tests, coverage)`
  - `Quality / .NET (format, analyzers, architecture tests, unit tests, coverage)`
  - `CodeQL / Analyze (csharp)`
  - `CodeQL / Analyze (javascript-typescript)`
  - `Dependency review / review`
  - The `platform-builds.yml` jobs (`Android`, `iOS + Mac Catalyst`, `Windows`) for any PR that
    touches `app/**`
- **Require conversation resolution before merging.**
- **Prevent force pushes to `main`.**
- **Prevent branch deletion** for `main`.
- Consider requiring signed commits once contributors are set up for it — not required to start.

## Code scanning and dependencies

- **Code scanning (CodeQL)** — already wired up via
  [`.github/workflows/codeql.yml`](../.github/workflows/codeql.yml); enable **required** status on
  the branch protection rule above once the workflow has run at least once.
- **Dependabot alerts** — enable in **Settings → Code security**.
- **Dependabot security updates** — enable alongside alerts; these are separate from the version
  updates already configured in [`.github/dependabot.yml`](../.github/dependabot.yml) and cover
  urgent security-only bumps.
- **Dependency graph** — enable (usually on by default for public repositories); this is what
  powers both Dependabot and the dependency-review action.
- **Secret scanning** — enable for public repositories (GitHub provides this by default; confirm
  it's on in **Settings → Code security**).
- **Push protection** (secret scanning) — enable so an accidental commit containing something that
  looks like a credential is blocked *before* it reaches the remote, not just flagged after.
- **Private vulnerability reporting** — enable in **Settings → Code security**; this is what makes
  the link in [`SECURITY.md`](../SECURITY.md) actually work.

## Actions

- **Actions permissions** — restrict to the actions this project actually uses (or "Allow
  actions/reusable workflows created by GitHub" plus an explicit allow-list) rather than "Allow all
  actions."
- **Workflow permissions default** — "Read repository contents" (not "Read and write"); every
  workflow in this repository already sets its own least-privilege `permissions:` block, but the
  org/repo default should not be write-by-default regardless.
- Fork pull requests never get secret access under GitHub's default `pull_request` trigger
  behavior — this repository does not use `pull_request_target` anywhere, deliberately, so that
  stays true. Do not add a `pull_request_target` workflow without a clear, reviewed reason (see
  the security guidance already enforced in each workflow file).

## Repository metadata

- **Default branch:** `main`.
- **Topics/description:** should make clear this is an independent/unofficial project — see the
  trademark note in [`README.md`](../README.md#trademark-and-non-affiliation).
- **License:** MIT (see [`LICENSE`](../LICENSE)); confirm the repository's detected license badge
  matches once pushed.

## What's already enforced by files in this repository (not a settings-page item)

For contrast — these don't need a GitHub setting, they're already code:
[`.github/dependabot.yml`](../.github/dependabot.yml) (dependency updates),
[`.github/workflows/*.yml`](../.github/workflows/) (all required/scheduled checks, least-privilege
`permissions:`, SHA-pinned third-party actions, `concurrency` cancellation, job `timeout-minutes`),
and a `.github/CODEOWNERS` file, not present today, if you add one once there's a team to route
reviews to.
