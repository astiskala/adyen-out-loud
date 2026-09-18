# 0007: Display-only, stateless, company-scoped relay

## Status
Accepted

## Context
Three constraints define the current architecture:

1. Adyen's Display `TENDER_FINAL` notification never carries payment method or amount (only
   approval result, terminal ID, transaction ID, and timestamp) — see
   [`worker/src/adyen/display-parser.ts`](../../worker/src/adyen/display-parser.ts). The Standard
   `AUTHORISATION` webhook is not used. Relying on Display alone means every announcement is the
   same generic "payment successful" — there is nothing to correlate.
2. Whoever installs the app on a given terminal often has no Adyen Customer Area access at all
   (e.g., a submerchant of a payment-facilitator partner). The app is configured with a
   company-wide relay URL and a user-entered terminal serial number — no Customer Area access is
   needed to install and configure the app.
3. With nothing to correlate and no requirement to survive a disconnected period (see Decision
   below), no durable state is needed — no correlation state, no replay queue, no at-least-once
   delivery with acknowledgments.

## Decision
- **Display-only.** The Worker only ever ingests Display `TENDER_FINAL` notifications. A successful
  one always produces the same generic `payment_succeeded` message (no `paymentMethod`, no
  `amount` — the relay protocol is bumped to `protocol: 2`, a breaking wire-format change since
  those fields are removed, not just always-null).
- **One relay URL per Adyen company account**, not per app instance. The company's Display webhook
  is configured once, by whoever has Customer Area access, pointing at
  `https://<worker-host>/v1/c/<companyToken>`. Every terminal in that company sends its Display
  notifications to that same URL.
- **Terminal identity is user-entered, not generated.** Each app instance is configured with the
  same company relay URL (shared out-of-band by whoever set it up) plus that specific device's
  terminal serial number, typed in by the installer — no Customer Area access is needed to install
  and configure the app itself. The Worker recovers the same serial from the webhook's `POIID`
  field (`<model>-<serial>`, e.g. `V400m-324688170` -> `324688170`) to know which connected
  terminal(s) to notify.
- **One Durable Object per company**, not per terminal — routed by `SHA-256(companyToken)` as the
   object name. Within that object, each terminal's WebSocket connection is tagged with its terminal
   serial via Cloudflare's hibernatable-WebSocket tag API (`ctx.acceptWebSocket(socket,
   [terminalSerial])` / `ctx.getWebSockets(terminalSerial)`), which fans an ingested notification
   out to only the matching terminal's connection(s).
- **No persistence, anywhere.** The Durable Object holds no SQLite tables at all. A Display
  notification is parsed and, if successful, pushed directly to whatever sockets are currently
  connected and tagged with the matching terminal serial. If no device for that terminal is
  connected at that instant, the announcement is simply not delivered — there is no queue, no
  replay-on-reconnect, and no server-side deduplication of retried webhooks (the app's own
  client-side recent-event-ID cache, unchanged, is what keeps a retried webhook from being spoken
  twice to a device that *is* connected).
- **No client-to-server acknowledgment.** With nothing durable to reconcile an ACK against, the
  relay protocol drops it entirely — the WebSocket becomes a pure one-way server-to-client push.

## Consequences
- A payment that happens while its terminal's app instance is disconnected (backgrounded on iOS,
  network blip, device off) is never announced, live or later — this is a deliberate trade-off for
  simplicity and reduced data retention, not an oversight. Background execution (Android foreground
  service, desktop platforms not being suspended) narrows the disconnected window but does not
  eliminate it, especially on iOS (foreground-only by decision — see
  [`docs/architecture.md`](../architecture.md#client-architecture-maui)).
- There is no announcement richer than "payment successful" — no amount, no card scheme — because
  the only notification type in use never carries that data. A future richer announcement would
  require re-adding a second, amount-carrying notification source and re-introducing exactly the
  correlation/persistence machinery this decision removes; that trade-off should be revisited
  explicitly (a new ADR), not silently reversed.
- The Worker no longer stores any payment metadata at rest, anywhere, at any retention window —
  [`docs/privacy.md`](../privacy.md) is updated accordingly.
- Installing the app no longer requires Adyen Customer Area access; only whoever configures the
  company's Display webhook once needs it. This directly serves partner/submerchant deployments
  that motivated this change.
- The relay URL is a bearer secret shared across an entire company's terminals — a leaked URL
   affects every terminal in the company. See [`docs/threat-model.md`](../threat-model.md) for the
   full discussion.
- The Durable Object class (`RelayObject`) and its Cloudflare Durable Objects infrastructure choice
   from [ADR 0002](0002-use-cloudflare-durable-objects.md) are unchanged and still the right fit.
   The per-company isolation and hibernatable-WebSocket delivery reasoning in ADR 0002 still holds.
