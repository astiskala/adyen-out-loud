#!/usr/bin/env bash
# Runs the UI tests (app/AdyenOutLoud.UITests) against the real app on a throwaway iOS Simulator.
#
# What it does: builds the app for the simulator, creates and boots a dedicated simulator, starts Appium,
# runs the tests (each drives the real app and a real local Worker), then tears everything down.
# Needs macOS with Xcode 26.6 (see docs/development.md), Node 24, and the iOS simulator runtime that ships
# with that Xcode (Xcode > Settings > Components, or `xcodebuild -downloadPlatform iOS`).
#
# Usage: scripts/ui-tests.sh [extra `dotnet test` arguments, e.g. --filter PaymentUiTests]
#        UITEST_KEEP_SIMULATOR=1 scripts/ui-tests.sh   # leave the simulator behind for debugging
set -euo pipefail
cd "$(dirname "$0")/.."
ROOT="$PWD"
UI="$ROOT/app/AdyenOutLoud.UITests"
APP_PROJECT="$ROOT/app/AdyenOutLoud/AdyenOutLoud.csproj"
APP_BUNDLE="$ROOT/app/AdyenOutLoud/bin/Debug/net10.0-ios/iossimulator-arm64/AdyenOutLoud.app"
DRIVER="xcuitest@12.12.5"

[[ "$(uname)" == "Darwin" ]] || { echo "The UI tests need macOS (iOS Simulator)." >&2; exit 1; }

SDK_VERSION="$(xcrun --sdk iphonesimulator --show-sdk-version)"
RUNTIME="com.apple.CoreSimulator.SimRuntime.iOS-${SDK_VERSION//./-}"
if ! xcrun simctl list runtimes | grep -q "$RUNTIME"; then
  echo "No iOS $SDK_VERSION simulator runtime is installed. Run: xcodebuild -downloadPlatform iOS" >&2
  exit 1
fi

UDID=""
APPIUM_PID=""
cleanup() {
  [[ -n "$APPIUM_PID" ]] && kill "$APPIUM_PID" 2>/dev/null || true
  if [[ -n "$UDID" && -z "${UITEST_KEEP_SIMULATOR:-}" ]]; then
    xcrun simctl shutdown "$UDID" 2>/dev/null || true
    xcrun simctl delete "$UDID" 2>/dev/null || true
  fi
}
trap cleanup EXIT

echo "==> Building the app for the iOS Simulator (ad-hoc signed; no Apple certificate needed)"
dotnet build "$APP_PROJECT" -f net10.0-ios -p:RuntimeIdentifier=iossimulator-arm64 \
  -p:EnableCodeSigning=true -p:CodesignKey=- -p:CodesignRequireProvisioningProfile=false --verbosity quiet

echo "==> Appium"
(cd "$UI/appium" && npm ci --ignore-scripts --no-audit --no-fund)
export APPIUM_HOME="$UI/appium/.appium"
APPIUM="$UI/appium/node_modules/.bin/appium"
"$APPIUM" driver list --installed 2>&1 | grep -q "xcuitest" || "$APPIUM" driver install "$DRIVER"
PORT="$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1])')"
"$APPIUM" --address 127.0.0.1 --port "$PORT" --log-no-colors > "$UI/appium/appium.log" 2>&1 &
APPIUM_PID=$!
for _ in $(seq 1 60); do curl -fs "http://127.0.0.1:$PORT/status" >/dev/null 2>&1 && break; sleep 1; done
curl -fs "http://127.0.0.1:$PORT/status" >/dev/null || { echo "Appium did not start; see $UI/appium/appium.log" >&2; exit 1; }

echo "==> Simulator (iOS $SDK_VERSION)"
UDID="$(xcrun simctl create "AdyenOutLoud-UITests-$$" "com.apple.CoreSimulator.SimDeviceType.iPhone-17-Pro" "$RUNTIME")"
xcrun simctl boot "$UDID"
xcrun simctl bootstatus "$UDID" -b >/dev/null

echo "==> Tests (the first run also builds WebDriverAgent, which takes a few minutes)"
export ADYEN_UITEST_APP="$APP_BUNDLE"
export ADYEN_UITEST_UDID="$UDID"
export ADYEN_UITEST_APPIUM="http://127.0.0.1:$PORT"
export ADYEN_UITEST_ARTIFACTS="$UI/artifacts"
rm -rf "$ADYEN_UITEST_ARTIFACTS"
dotnet test "$UI/AdyenOutLoud.UITests.csproj" "$@"
