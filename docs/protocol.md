# Relay WebSocket protocol

This is the protocol between the Cloudflare Worker (`worker/src/relay-object.ts`) and the MAUI
app (`app/AdyenOutLoud.Core/Services/RelayProtocol.cs`). It is internal to this project — not a
public API — but versioned and documented because both ends are deployed independently (the
Worker can roll out before every app instance has updated).

## Transport

`wss://<worker-host>/v1/i/<43-char-token>/ws`. The client sends no hello, identity, or auth
message — identity is carried entirely by the token in the URL path, which the Worker already
validated during the HTTP upgrade. The connection only exists while the app is foregrounded; see
[`docs/architecture.md`](architecture.md#client-architecture-maui).

## Server → client: envelope

```json
{
  "protocol": 1,
  "message": {
    "id": "payment:NC6HT9CRT65ZGN82",
    "type": "payment_succeeded",
    "occurredAt": "2026-09-18T12:00:00.000Z",
    "terminalId": "V400m-324688170",
    "transactionId": "CWf3001626182307000.NC6HT9CRT65ZGN82",
    "pspReference": "NC6HT9CRT65ZGN82",
    "paymentMethod": "mc",
    "amount": { "currency": "EUR", "valueMinor": 4508 }
  }
}
```

- `protocol` is always the literal integer `1` today. A client that receives any other value, or a
  `message.type` other than `"payment_succeeded"`, must ignore the envelope (log a diagnostic, do
  not crash, do not guess at partial compatibility) and must **not** acknowledge it.
- `id` is stable and used for both deduplication (`AdyenOutLoud.Core`'s recent-ID cache) and
  acknowledgment. For a correlated payment it is `payment:<pspReference>`.
- `paymentMethod` and `amount` are both `null` for a **generic** announcement (see
  [`docs/architecture.md`](architecture.md#durable-object-workersrcrelay-objectts)) — a null
  `amount` and a missing `amount` are different: `amount` is always present in a
  `payment_succeeded` message, its *value* may be `null`.
- Every field other than `paymentMethod`/`amount` is required and non-empty.

On (re)connect, the server replays every currently-unacknowledged message from
`outbound_messages`, oldest first, before any new message. A client that reconnects after being
offline therefore does not miss announcements — it just receives them late.

## Client → server: acknowledgment

After handling an envelope (spoken, or deliberately not spoken — see below), the client sends
exactly:

```json
{ "type": "ack", "id": "payment:NC6HT9CRT65ZGN82" }
```

The server requires **exactly** these two keys (`id`, `type`), `type` exactly `"ack"`, and a
non-empty string `id` — anything else (extra keys, wrong types, malformed JSON, a non-text frame)
closes the socket with close code `1008` (Policy Violation). This is deliberately strict: the
client's only speech act on this connection is "I'm done with this message," and a strict shape
makes that impossible to misinterpret.

An ACK is sent even when the client chooses **not** to speak the announcement — a duplicate
(already in the recent-ID cache), a message with no amount, or a text-to-speech failure. Poison
messages must not be able to replay forever; see
[`docs/architecture.md`](architecture.md#websocket-delivery-acks-and-replay).

## Compatibility rules

- **Unknown fields** in either direction must be ignored, not rejected — this is how the protocol
  stays forward-compatible across independently-deployed client and server versions.
- **A structurally incompatible protocol version must never be partially interpreted.** If Worker
  and app disagree on `protocol`, the correct behavior is "ignore and keep listening," not "try to
  read the fields anyway."
- Changing the *meaning* of an existing field, or removing one, requires bumping `protocol` and
  keeping both the old and new shapes understood by the server for as long as any deployed client
  might still send the old one (there is no forced-upgrade mechanism).
