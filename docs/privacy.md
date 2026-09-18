# Privacy and data minimization

This document explains what data this project's Worker touches, what it keeps, for how long, and
why. See [`docs/threat-model.md`](threat-model.md) for the security assumptions behind these
choices and [`docs/architecture.md`](architecture.md) for how the pieces fit together.

## What reaches Cloudflare

Only what Adyen sends to the webhook URL: the Display (`TENDER_FINAL`) and Standard
(`AUTHORISATION`) webhook bodies, as Adyen constructs them. Neither the app nor the Worker adds
any additional data — no device identifiers beyond the instance token, no location, no analytics
payload, no user account information (there is no user account).

The full parsed set of fields the Worker extracts and stores: `pspReference`, `terminalId`,
`transactionId`, `occurredAt`, the Display result (`APPROVED`/etc.), success/failure flags,
`paymentMethod` (a scheme name like `"visa"`, not cardholder data), and `amount` (`currency` +
minor-unit integer value). See [`worker/src/adyen/models.ts`](../worker/src/adyen/models.ts) for
the exact shape.

## What is persisted, and why

Everything above is persisted in the instance's Durable Object SQLite storage
(`worker/src/relay-object.ts`) for a bounded time, because the Worker needs to:

- **Deduplicate** retried/duplicate webhook deliveries (`raw_ingress`).
- **Correlate** the Display and Standard webhooks for the same payment, which can arrive seconds
  apart (`display_states`, `authorisation_states`) — see
  [ADR 0005](adr/0005-correlate-display-and-standard-webhooks.md).
- **Guarantee at-most-once publication** per payment (`published`).
- **Replay** undelivered announcements to a client that reconnects after being offline
  (`outbound_messages`) — see [ADR 0004](adr/0004-websocket-delivery-and-acknowledgements.md).

Nothing is persisted anywhere else. There is no analytics pipeline, no logging service that
receives payment payloads, and no data warehouse.

## Retention

Every table is pruned by the Durable Object's alarm-driven sweep
(`RelayObject.prune`/`pruneOutbound` in `worker/src/relay-object.ts`) using a single constant,
`RETENTION_MS = 24 hours`. `outbound_messages` additionally never exceeds `MAX_OUTBOUND_MESSAGES`
(100) regardless of age. This retention window exists purely to make correlation and replay work
across normal delays (a slow Authorisation webhook, a device that was briefly offline) — it is not
a data-retention policy chosen for any business or analytics purpose, and there is no way to
opt into longer retention. Cleanup is exercised directly by
`worker/test/worker.test.ts`'s retention test (18. applies both 24-hour retention and the outbound
count bound) — see [`docs/testing.md`](testing.md).

## What is spoken locally

The full localized announcement sentence (amount, currency, payment method) is composed and spoken
entirely on the device via on-device text-to-speech — see
[ADR 0006](adr/0006-on-device-text-to-speech.md). It is never sent anywhere else. The device also
keeps a small bounded cache of recently-seen event IDs (`ISettingsService`, backed by MAUI
`Preferences`, capped at 40 entries — see
[`PreferencesSettingsService`](../app/AdyenOutLoud/Services/PreferencesSettingsService.cs)) purely
to avoid re-announcing a duplicate; this cache holds event IDs, not payment content.

## Analytics

None. There is no analytics SDK, crash reporter, or telemetry pipeline in either the Worker or the
app.

## Is any data sold or shared?

No. The Worker's only outbound network activity is serving the app's own WebSocket connection and
receiving Adyen's webhooks — it makes no calls to any third-party service.

## What is deliberately never retained

This project never receives, stores, or transmits:

- PAN (card number), CVV, or track data
- PIN data
- Full raw cardholder payment data beyond the scheme-name/amount fields Adyen's webhooks
  themselves already summarize (a Display/Standard webhook does not carry cardholder data; this
  project persists no more than what those webhooks already contain, for no longer than 24 hours)
- Any account/identity data — there is no user account
