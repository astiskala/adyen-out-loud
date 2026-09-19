# Development

## Prerequisites

| Tool | Version | Pinned in |
| --- | --- | --- |
| .NET SDK and MAUI workload set | 10.0.401 | [`global.json`](../global.json) |
| Node.js / npm | 24.x / 12.x | `worker/.node-version`, `worker/package.json` |
| Xcode (iOS / Mac Catalyst) | 26.6 exactly (.NET 10 fails the build on any other) | — |

Install the workloads for the platforms you build: `dotnet workload install maui-android` (and
`maui-ios maui-maccatalyst` on macOS, `maui-windows` on Windows). The MAUI project only lists the
platforms its OS can build: Android everywhere, iOS and Mac Catalyst on macOS, Windows on Windows.

## Build, test, run

```bash
# Worker
cd worker && npm ci
npm test                 # runs inside the Workers runtime
npm run quality          # format, lint, types, architecture, dead code, coverage
npm run dev              # local relay

# App
cd app
dotnet test AdyenOutLoud.Tests
dotnet test AdyenOutLoud.ArchitectureTests
dotnet test AdyenOutLoud.E2ETests          # real Worker (wrangler dev) + Core logic
dotnet build AdyenOutLoud -f net10.0-android   # or -ios / -maccatalyst / -windows10.0.19041.0
dotnet build AdyenOutLoud -f net10.0-maccatalyst && dotnet build AdyenOutLoud -f net10.0-maccatalyst -t:Run
scripts/ui-tests.sh                        # iOS Simulator UI tests (macOS, Xcode 26.6)
scripts/quality.sh                         # what CI's required jobs run, on your OS
```

The app talks to the hosted relay. To test against another HTTPS relay, set `ADYEN_OUT_LOUD_RELAY_URL`
when launching a Debug build.

## Tests

- **Worker** (`worker/test`): parser unit tests plus routing and WebSocket tests in `workerd`.
- **Core** (`AdyenOutLoud.Tests`): parsing, configuration, playback, reconnect logic. `scripts/dotnet-coverage.sh`
  enforces 90% line / 85% branch coverage on `AdyenOutLoud.Core` only; the MAUI project is thin platform glue.
- **Architecture** (`AdyenOutLoud.ArchitectureTests`): Core stays free of MAUI, code-behind doesn't
  construct services, and every language has both recordings.
- **E2E / UI**: `AdyenOutLoud.E2ETests` and `AdyenOutLoud.UITests` drive a real local Worker.
- A weekly job runs mutation testing on Core (`scheduled-deep-quality.yml`).

For a bug fix, add a test that fails before the fix. For a change at the webhook boundary, add a
malformed-input test as well as a valid one.

## CI/CD

- `quality.yml`: Worker quality gate and .NET format, analyzers, tests, and coverage.
- `platform-builds.yml`: builds Android (Linux), iOS + Mac Catalyst (`macos-26`, Xcode 26.6), Windows.
- `codeql.yml`, `dependency-review.yml`, and `sonarcloud.yml` (inert until you configure a SonarCloud
  project and `SONAR_TOKEN`).
- `deploy-worker.yml`: on a push to `main` that touches `worker/`, runs the quality gate and
  `wrangler deploy`, then checks `/health`. It needs the `CLOUDFLARE_API_TOKEN` and
  `CLOUDFLARE_ACCOUNT_ID` repository secrets.
- `release.yml`: on a `v1.2.3` tag, builds an Android APK and a Windows installer `.exe` plus a
  portable `.zip` (unpackaged, self-contained, via `app/Installer/AdyenOutLoud.iss`) and attaches them
  to a GitHub release. The tag sets the display version and the run number the build number. Run it
  by hand to get the same files as workflow artifacts without a release.
- This set-up guide is served by GitHub Pages from `main` / `docs`.

Deploy the Worker by hand with `cd worker && npx wrangler login && npm run deploy`. To release the app,
push a tag: `git tag v1.2.3 && git push origin v1.2.3`.

Android updates only install over an APK signed with the same key. Create a keystore once
(`keytool -genkeypair -v -keystore release.keystore -alias adyenoutloud -keyalg RSA -keysize 2048 -validity 10000`)
and set the `ANDROID_KEYSTORE_BASE64` (`base64 -i release.keystore`), `ANDROID_KEYSTORE_PASSWORD`,
`ANDROID_KEY_ALIAS` and `ANDROID_KEY_PASSWORD` repository secrets; without them each release is signed
with a new debug key. Windows builds aren't code-signed, and iOS and Mac Catalyst aren't released.

## Dependencies

Add one only when the platform can't do the job. Versions are pinned centrally (`Directory.Packages.props`,
lockfiles) and updated by Dependabot. TypeScript is pinned to 6.0.3 because the tooling doesn't
support the 7 line yet.
