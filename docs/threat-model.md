# Threat model

**There is no authentication.** The relay is one shared public URL and the terminal serial is the
only routing key. It isn't secret, so anyone who knows it can open a WebSocket for that terminal.

## Webhook source check

`POST /webhook` is accepted only from the IP addresses `out.adyen.com` resolves to, which is where
Adyen sends webhooks from. The Worker compares Cloudflare's `CF-Connecting-IP` (set by the edge, so
the sender can't forge it) with that host's A and AAAA records, fetched over DNS-over-HTTPS and
cached for 1 to 5 minutes. Other addresses get `403`. If the lookup fails the Worker returns `503`,
so Adyen retries and the check is never skipped.

Set `ADYEN_WEBHOOK_HOST` in `worker/wrangler.jsonc` to change the host; empty disables the check, as
the local E2E tests do.

Remaining gaps:

- Other Adyen customers send from the same addresses, but Adyen builds each notification from a real
  terminal event, so they can't choose a serial or payload.
- A poisoned DNS answer for `out.adyen.com` would widen the allowed set.
- Adyen's [HMAC signatures](https://docs.adyen.com/development-resources/webhooks/verify-hmac-signatures/)
  aren't verified. They would add defence in depth but need a per-account secret.

## What that means

- Fake announcements can't be posted directly, so a known serial doesn't let anyone trigger one.
- Anyone who knows a serial can listen to that terminal's real payment metadata (terminal ID,
  transaction ID, PSP reference, timestamp) while they are connected.
- **Don't treat an announcement as proof of payment.** The worst case is metadata disclosure or a
  wrong announcement; the project never touches money. That trade-off is deliberate
  ([ADR 0008](adr/0008-single-shared-relay-and-prerecorded-audio.md)). For stronger guarantees, add
  account-level routing first.

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
