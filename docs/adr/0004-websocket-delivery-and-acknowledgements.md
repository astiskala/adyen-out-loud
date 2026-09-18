# 0004: WebSocket delivery with client acknowledgments

## Status
Accepted

## Context
An announcement needs to reach the app promptly (payments happen live, at a till, with a customer
waiting) but must also survive the app being backgrounded, the connection dropping, or the device
being briefly offline. Push notifications don't carry enough payload reliably across platforms and
don't give the Worker positive confirmation that a message was actually handled; polling would add
latency and cost proportional to poll frequency.

## Decision
The client holds a single WebSocket connection open while foregrounded
(`wss://.../v1/i/<token>/ws`). The Durable Object persists every outbound message in
`outbound_messages` before sending it, replays every still-unacknowledged message (oldest first) on
every new connection, and only removes a message once the client sends a well-formed
`{"type":"ack","id":...}`. See [`docs/protocol.md`](../protocol.md) for the exact wire format and
[`docs/architecture.md`](../architecture.md#websocket-delivery-acks-and-replay) for the server-side
mechanics.

## Consequences
- Delivery is at-least-once with durable server-side state, not fire-and-forget: a message
  survives the app being closed, the device losing connectivity, or the Worker itself restarting,
  because it lives in SQLite until acknowledged.
- The client's recent-event-ID cache (`ISettingsService.TryReserveEventIdAsync`, backed by MAUI
  `Preferences`) is what turns "at-least-once delivery" into "announced at most once" — a replayed
  message is acknowledged immediately without being spoken again.
- The connection only exists while the app is foregrounded (see
  [`AppLifecycleCoordinator`](../../app/AdyenOutLoud/Services/AppLifecycleCoordinator.cs)), which
  is a deliberate trade-off: a payment that happens while the app is backgrounded is not announced
  live, but is not lost either — it's sitting in `outbound_messages` and gets spoken (or silently
  caught as a duplicate/expired-context event) the next time the app comes to the foreground.
- Acknowledgment is per-**message**, not per-connection: an ACK from any connected socket deletes
  the row for all of them, which is what keeps "at most one logical announcement" true even with
  two devices briefly connected to the same instance.
- The bounded outbound queue (`MAX_OUTBOUND_MESSAGES = 100`, 24-hour retention) means a device that
  stays offline for a very long time will have old messages pruned rather than receiving an
  unbounded backlog — see [`docs/privacy.md`](../privacy.md) for the retention rationale.
