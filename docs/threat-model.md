# Threat model

**There is no authentication.** The relay is one shared public URL. It doesn't verify that a webhook
comes from Adyen (Adyen offers
[HMAC signatures](https://docs.adyen.com/development-resources/webhooks/verify-hmac-signatures/);
this project doesn't use them), and anyone can open a WebSocket for any terminal serial. The serial
is the only routing key and it isn't secret.

## What that means

- Anyone who knows or guesses a terminal serial can make that terminal's app play "Payment
  successful" by posting a fake notification.
- Anyone who knows a serial can listen to that terminal's real payment metadata (terminal ID,
  transaction ID, PSP reference, timestamp) while they are connected.
- **Don't treat an announcement as proof of payment.** The worst case is a false confirmation or
  metadata disclosure; the project never touches money. That trade-off is deliberate
  ([ADR 0008](adr/0008-single-shared-relay-and-prerecorded-audio.md)). If you need stronger guarantees,
  add HMAC verification and account-level routing first.

## Data

- **In transit:** the metadata above, over HTTPS/WSS only (the app rejects non-`https` relay URLs and
  never disables certificate validation). No card data, amount, or payment method is ever received.
- **At rest:** nothing on the server, and Worker logs contain no payloads. The device keeps only the
  terminal serial, the language, and about 40 recent event IDs to avoid announcing a payment twice.
- **Third parties:** Cloudflare hosts the relay; no analytics or other services are used.

## Other risks

| Risk | Mitigation |
| --- | --- |
| Malformed or oversized requests | Strict parsing that fails closed, 64 KiB body and frame limits |
| Flooding | Cloudflare's platform protection only; there is no per-IP rate limiting, and all sockets share one Durable Object |
| Duplicate or replayed webhooks | Not deduplicated on the server; each app ignores an event ID it has already played |
| Vulnerable dependencies | Lockfiles, `npm audit`, NuGet audit, Dependabot, CodeQL |
