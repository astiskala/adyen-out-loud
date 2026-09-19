# Privacy and data minimization

This document explains what data this project's Worker touches and why. See
[`docs/threat-model.md`](threat-model.md) for the security assumptions behind these choices and
[`docs/architecture.md`](architecture.md) for how the pieces fit together.

## What reaches Cloudflare

Only what Adyen's terminal sends to the relay URL: the Display (`TENDER_FINAL`) webhook body, as
Adyen constructs it. Neither the app nor the Worker adds any additional data — no device
identifiers beyond the terminal serial (itself not secret — see
[`docs/threat-model.md`](threat-model.md)), no location, no analytics payload, no user account
information (there is no user account).

The fields the Worker reads out of that notification: `pspReference`, `terminalId` (and the
terminal serial derived from it), `transactionId`, `occurredAt`, and the Display result
(`APPROVED`/etc.). See [`worker/src/adyen/models.ts`](../worker/src/adyen/models.ts) for the exact
shape.

## What is persisted

**Nothing, anywhere, on the Worker.** `RelayObject` holds no storage at all — a Display
notification is parsed and, if successful, pushed directly to whatever WebSocket connections are
open for that terminal at that instant. There is no queue, no replay log,
and no retention window, because there is nothing to retain. A payment that happens while its
terminal's app instance is disconnected is not announced later — see the ADR's Consequences section
for that trade-off.

There is no analytics pipeline, no logging service that receives payment payloads, and no data
warehouse.

## What is spoken locally

The announcement — always the fixed phrase "Payment successful" (recorded in four languages, see
[`docs/testing.md`](testing.md#localization)) — is a pre-recorded clip bundled in the app and played entirely on
the device — see [ADR 0008](adr/0008-single-shared-relay-and-prerecorded-audio.md). It is never sent
anywhere else. The device also keeps a small bounded cache of recently-seen event IDs
(`ISettingsService`, backed by MAUI `Preferences`, capped at 40 entries — see
[`PreferencesSettingsService`](../app/AdyenOutLoud/Services/PreferencesSettingsService.cs)) purely
to avoid re-announcing a duplicate; this cache holds event IDs, not payment content. The app also
stores its configured terminal serial number in platform secure storage — see
[`docs/security.md`](security.md#terminal-serial-handling).

## Analytics

None. There is no analytics SDK, crash reporter, or telemetry pipeline in either the Worker or the
app.

## Is any data sold or shared?

No. The Worker's only outbound network activity is serving the app's own WebSocket connection and
receiving Adyen's webhook — it makes no calls to any third-party service.

## What is deliberately never retained

This project never receives, stores, or transmits:

- PAN (card number), CVV, or track data
- PIN data
- Payment method or amount — the Display notification this project relies on never carries either
  (see [ADR 0007](adr/0007-display-only-stateless-company-scoped-relay.md))
- Any account/identity data — there is no user account
