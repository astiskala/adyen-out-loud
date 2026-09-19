# Threat model

The relay is one shared public URL, and the terminal serial (which isn't secret) routes each
notification. A device can only listen after **pairing** with the terminal, which proves it can see
the terminal's receipts ([ADR 0009](adr/0009-pair-devices-with-receipt-codes.md)).

## Pairing

1. The Worker remembers, per terminal, the last 4 characters of the PSP reference of each approved
   payment from the last 15 minutes (at most 20). An alarm deletes them once they expire.
2. `POST /pair/<serial>` with `{"receipts":["AB12","CD34"]}` must quote two *different* recent
   payments on that terminal. Two are required because every customer holds one receipt, and the
   serial is printed on the terminal.
3. On a match the Worker returns a random 256-bit token, stores only its SHA-256 hash (the 10 most
   recent devices per terminal), and marks both receipts as used.
4. `GET /ws/<serial>` requires `Authorization: Bearer <token>`; otherwise it returns `401` and the app
   asks to be paired again.

A terminal allows 10 failed attempts per 15 minutes (then `429`). With two alphanumeric codes, a blind
guess matches roughly once in 10⁹ tries; legacy all-digit PSP references would make that about 1 in 10⁵.

Remaining gaps:

- Anyone holding two recent receipts from the same terminal (staff, or a customer who paid twice) can
  pair within the window.
- Someone who knows a serial can use up its failed attempts and block pairing for 15 minutes. It
  doesn't affect devices that are already paired.
- A leaked token keeps working until 10 newer devices pair with that terminal. There is no per-device
  revocation.

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
- Knowing a serial isn't enough to listen; see the gaps under [Pairing](#pairing).
- An announcement confirms that *a* payment was approved on the terminal, not which one; check the
  terminal or receipt for the amount. The project never touches money.

## Data

- **In transit:** the metadata above, over HTTPS/WSS only (the app rejects non-`https` relay URLs and
  never disables certificate validation). No card data, amount, or payment method is ever received.
- **At rest:** per terminal, the last 4 characters and time of up to 20 approved PSP references from the
  last 15 minutes, a failed-attempt counter, and SHA-256 hashes of up to 10 device tokens. Worker logs
  contain no payloads. The device keeps the terminal serial, its token (in app-private `Preferences`,
  excluded from Android backups), the language, and about 40 recent event IDs to avoid announcing a
  payment twice.
- **Third parties:** Cloudflare hosts the relay; no analytics or other services are used.

## Other risks

| Risk | Mitigation |
| --- | --- |
| Malformed or oversized requests | Strict parsing that fails closed, 64 KiB body and frame limits |
| Flooding | Cloudflare's platform protection only; there is no per-IP rate limiting, and all sockets share one Durable Object |
| Duplicate or replayed webhooks | Not deduplicated on the server; each app ignores an event ID it has already played |
| Vulnerable dependencies | Lockfiles, `npm audit`, NuGet audit, Dependabot, CodeQL |
