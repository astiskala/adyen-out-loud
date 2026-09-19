# Threat model

This document describes what this project protects, from whom, and — just as importantly — what
it deliberately does not protect against. If you're evaluating this project for a real deployment,
read this before the feature list.

## Assets

| Asset | Why it matters |
| --- | --- |
| The terminal serial number | **Not a secret.** It is the value Adyen already sends in every Display webhook's `POIID`, and it is the *only* thing that routes a message to an app. |
| Payment metadata in transit | `terminalId`, `transactionId`, `pspReference`, `occurredAt`, the Display result. Not cardholder data (see [`docs/privacy.md`](privacy.md)); no amount or payment method. Still operationally sensitive as evidence that a transaction happened at a terminal. |

## Trust boundaries

```text
Adyen terminal      ──HTTPS──▶  public Cloudflare Worker  (POST https://adyenoutloud.adam-eea.workers.dev/webhook — anyone can POST)
client app (MAUI)   ──WSS────▶  public Cloudflare Worker  (GET  /ws/<serial>     — anyone can connect)
Worker              ──────▶     Durable Object (internal, holds no persisted state)
app                 ──────▶     plays a bundled MP3 (on-device, no network)
```

## The design choice this document exists to be honest about

**There is no authentication at all.** The relay is one shared, public URL. It does not verify
that a request comes from Adyen (Adyen supports
[HMAC signature verification](https://docs.adyen.com/development-resources/webhooks/verify-hmac-signatures/);
this project does not implement it), and it does not check who connects to a terminal's WebSocket.
The relay stores nothing and simply drops a notification whose terminal serial has no connected app.

Consequences:

- Anyone who knows or guesses a terminal serial number can POST a fabricated approved-payment
  notification for it and make that terminal's app play "Payment successful".
- Anyone who knows a serial can open a WebSocket for it and receive that terminal's real payment
  metadata (terminal ID, transaction ID, PSP reference, timestamp) while connected.
- Serial numbers are short, structured identifiers, not high-entropy secrets.

**Do not treat an announcement as evidence of an actual payment.** The worst-case impact is a spoken
false confirmation, or metadata disclosure to someone who knows a serial — this project never
handles or has authority over money. That is acceptable for the intended use (a convenience
announcement next to a till that staff can cross-check with the terminal) and is a deliberate
trade-off for a zero-configuration setup ([ADR 0008](adr/0008-single-shared-relay-and-prerecorded-audio.md)).
If you need stronger guarantees, add HMAC verification (per-account HMAC key plus a way to route
by account) before deploying for a merchant that relies on the announcement.

## Threats

| Threat | Discussion |
| --- | --- |
| **Forged webhook requests** | Anyone can POST. The Display parser rejects incomplete/malformed structures (`worker/test/parsers.test.ts`) but cannot verify origin. |
| **Eavesdropping on a terminal's feed** | Possible for anyone who knows the serial (see above). |
| **Replay / duplicate delivery** | Not deduplicated server-side (no persistence). The app's recent-event-ID cache (`ISettingsService`) prevents a device from announcing the same event twice. |
| **Malformed payloads** | The Display parser fails closed (`worker/test/parsers.test.ts`, [`docs/testing.md`](testing.md)). |
| **Resource exhaustion** | Request bodies are capped at 64 KiB (`MAX_BODY_BYTES`); the app caps incoming frames at 64 KiB. No per-IP rate limiting at the Worker; use Cloudflare rate limiting if abuse appears. All sockets live in one Durable Object, so a flood of connections affects every user. |
| **Abuse of the public endpoint** | `/health`, `/webhook`, and `/ws/<serial>` are unauthenticated by design. Cloudflare's platform DDoS protection is the first line of defense. |
| **Client compromise** | A compromised device has whatever the app has (the terminal serial in OS secure storage). Out of scope. |
| **Dependency compromise** | Mitigated by pinning, lockfiles, audits, Dependabot, CodeQL — see [`docs/quality.md`](quality.md) and [`docs/dependencies.md`](dependencies.md). |

## Transport

All traffic is HTTPS/WSS. The app never disables TLS certificate validation and
`RelayEndpointFactory` rejects any relay URL that isn't `https://`.
