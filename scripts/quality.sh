#!/usr/bin/env bash
# Full local quality suite — the closest local approximation of the required PR checks
# (.github/workflows/quality.yml). See docs/development.md for what each piece needs installed
# and docs/development.md.
#
# Runs everything possible on the current OS: the Worker's full `npm run quality`, the .NET
# projects that don't need a platform-specific toolchain (Core, Tests, ArchitectureTests), and the
# Android head build (buildable on any OS once the workload is installed). iOS/Mac Catalyst and
# Windows builds are intentionally NOT run here — see docs/development.md for why those need
# macOS+Xcode 26.6 or Windows respectively, and use the platform-specific `dotnet build -f <tfm>`
# commands there instead.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "==> Worker: npm ci"
(cd worker && npm ci)

echo "==> Worker: npm run quality"
(cd worker && npm run quality)

echo "==> Worker: npm audit --audit-level=high"
(cd worker && npm audit --audit-level=high)

echo "==> .NET: restore"
dotnet restore app/AdyenOutLoud.slnx

echo "==> .NET: dotnet format --verify-no-changes"
dotnet format app/AdyenOutLoud.slnx --no-restore --verify-no-changes --verbosity minimal

echo "==> .NET: build Core, Tests, ArchitectureTests"
dotnet build app/AdyenOutLoud.Core/AdyenOutLoud.Core.csproj --no-restore
dotnet build app/AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj --no-restore
dotnet build app/AdyenOutLoud.ArchitectureTests/AdyenOutLoud.ArchitectureTests.csproj --no-restore

echo "==> .NET: architecture tests"
dotnet test app/AdyenOutLoud.ArchitectureTests/AdyenOutLoud.ArchitectureTests.csproj --no-build

echo "==> .NET: unit tests + coverage thresholds"
scripts/dotnet-coverage.sh

if dotnet workload list 2>/dev/null | grep -qE "^(android|maui)\b"; then
  echo "==> .NET: Android head build"
  dotnet build app/AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android
else
  echo "==> .NET: skipping Android head build (workload not installed — run: dotnet workload install android)"
fi

echo
echo "All local quality checks passed."
echo "Platform builds (iOS, Mac Catalyst, Windows) are not covered by this script — see docs/development.md."
