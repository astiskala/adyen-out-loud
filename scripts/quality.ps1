# Full local quality suite — the closest local approximation of the required PR checks
# (.github/workflows/quality.yml). See docs/development.md for what each piece needs installed
# and docs/quality.md for why each check exists.
#
# Runs everything possible on the current OS: the Worker's full `npm run quality`, the .NET
# projects that don't need a platform-specific toolchain (Core, Tests, ArchitectureTests), and the
# Android head build. On Windows, also builds the Windows head target. iOS/Mac Catalyst are never
# buildable here — see docs/development.md.
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

Write-Host "==> Worker: npm ci"
Push-Location worker
npm ci
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Worker: npm run quality"
npm run quality
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Worker: npm audit --audit-level=high"
npm audit --audit-level=high
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Pop-Location

Write-Host "==> .NET: restore"
dotnet restore app/AdyenOutLoud.slnx
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> .NET: dotnet format --verify-no-changes"
dotnet format app/AdyenOutLoud.slnx --no-restore --verify-no-changes --verbosity minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> .NET: build Core, Tests, ArchitectureTests"
dotnet build app/AdyenOutLoud.Core/AdyenOutLoud.Core.csproj --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build app/AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet build app/AdyenOutLoud.ArchitectureTests/AdyenOutLoud.ArchitectureTests.csproj --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> .NET: architecture tests"
dotnet test app/AdyenOutLoud.ArchitectureTests/AdyenOutLoud.ArchitectureTests.csproj --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> .NET: unit tests + coverage thresholds"
bash scripts/dotnet-coverage.sh
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$androidInstalled = (dotnet workload list 2>$null) -match "^(android|maui)\b"
if ($androidInstalled) {
    Write-Host "==> .NET: Android head build"
    dotnet build app/AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
} else {
    Write-Host "==> .NET: skipping Android head build (workload not installed — run: dotnet workload install android)"
}

if ($IsWindows) {
    Write-Host "==> .NET: Windows head build"
    dotnet build app/AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-windows10.0.19041.0
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host ""
Write-Host "All local quality checks passed."
Write-Host "iOS and Mac Catalyst builds are not covered by this script — see docs/development.md."
