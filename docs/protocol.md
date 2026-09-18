# Relay WebSocket protocol

This is the protocol between the Cloudflare Worker (`worker/src/relay-object.ts`) and the MAUI
app (`app/AdyenOutLoud.Core/Services/RelayProtocol.cs`). It is internal to this project — not a
public API — but versioned and documented because both ends are deployed independently (the
Worker can roll out before every app instance has updated).

## Transport

`wss://<worker-host>/v1/c/<43-char-company-token>/t/<terminalSerial>/ws`. The client sends no
hello, identity, or auth message — identity is carried entirely by the company token and terminal
serial in the URL path, which the Worker already validated during the HTTP upgrade. The connection
is a pure server-to-client push (see [Client → server](#client--server) below) and exists whenever
the app is running (see [`docs/architecture.md`](architecture.md#client-architecture-maui) for the
platform-specific background/foreground behavior).

## Server → client: envelope

```json
{
  "protocol": 2,
  "message": {
    "id": "payment:NC6HT9CRT65ZGN82",
    "type": "payment_succeeded",
    "occurredAt": "2026-09-18T12:00:00.000Z",
    "terminalId": "V400m-324688170",
    "transactionId": "CWf3001626182307000.NC6HT9CRT65ZGN82",
    "pspReference": "NC6HT9CRT65ZGN82"
  }
}
```

- `protocol` is always the literal integer `2` today. A client that receives any other value, or a
  `message.type` other than `"payment_succeeded"`, must ignore the envelope (log a diagnostic, do
  not crash, do not guess at partial compatibility).
- `id` is stable and used for client-side deduplication (`AdyenOutLoud.Core`'s recent-ID cache — a
  retried Display webhook produces the same `id` and is not spoken twice). For a Display
  notification it is `payment:<pspReference>`.
- There is no `paymentMethod` or `amount` field — Adyen's Display API never carries either, so every
  message is the same generic "payment successful" announcement. Every field shown above is
  required and non-empty.
- The Worker never queues or replays messages: a client that connects after a notification was
  ingested does not receive it. See
  [ADR 0007](adr/0007-display-only-stateless-company-scoped-relay.md) for why.

## Client → server

The client sends no messages on this connection at all — it is a pure server-to-client push. Any
message a client does send is ignored by the server (`webSocketMessage` in
`worker/src/relay-object.ts` is a no-op); there is no acknowledgment step, because there is no
server-side state left for an acknowledgment to reconcile against.

## Compatibility rules

- **Unknown fields** in the server → client envelope must be ignored, not rejected — this is how
  the protocol stays forward-compatible across independently-deployed client and server versions.
- **A structurally incompatible protocol version must never be partially interpreted.** If Worker
  and app disagree on `protocol`, the correct behavior is "ignore and keep listening," not "try to
  read the fields anyway."
- Changing the *meaning* of an existing field, or removing one, requires bumping `protocol` (as this
  project did, `1` → `2`, when `paymentMethod`/`amount` were removed and the client ACK was
  dropped) and keeping both the old and new shapes understood by the server for as long as any
  deployed client might still send the old one (there is no forced-upgrade mechanism).
