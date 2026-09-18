# 0002: Use Cloudflare Durable Objects for per-company WebSocket routing

## Status
Accepted

## Context
The Worker receives Adyen Display webhooks and must push "payment succeeded" messages to only the
app instance(s) connected for the specific terminal that generated the webhook. Requirements:

- One relay URL per Adyen company account; all terminals in that company POST to the same URL.
- Each app instance connects via WebSocket with its terminal serial number; only connections for
  the matching terminal should receive the message.
- Connections may be long-lived (hours); idle connections should not consume compute.
- No persistent state is needed — if no device is connected when a webhook arrives, the
  announcement is simply not delivered (the client's recent-event-ID cache handles retries).

A shared database plus separate WebSocket infrastructure would add complexity without benefit.

## Decision
Model each company as its own Cloudflare Durable Object (`RelayObject`), addressed by
`SHA-256(companyToken)`. The object:

- Accepts hibernatable WebSocket connections, tagging each with the terminal serial
  (`ctx.acceptWebSocket(socket, [terminalSerial])`).
- On ingest, parses the Display notification, extracts the terminal serial from `POIID`, and fans
  the message out via `ctx.getWebSockets(terminalSerial)` — no storage, no alarms, no correlation.
- Uses zero SQLite storage (the class declares no tables).

See [`docs/architecture.md`](../architecture.md#durable-object-workersrcrelay-objectts) for the
current lifecycle.

## Consequences
- Durable Objects give per-company isolation: a bug or abusive payload affects only that company's
  object, never a shared resource.
- Hibernatable WebSockets mean idle connections don't keep the object (and billed compute) pinned
  in memory.
- Tag-based fan-out (`ctx.getWebSockets(tag)`) is a native DO feature — no application-level
  connection registry or pub/sub layer needed.
- The project runs on Cloudflare Workers specifically; Durable Objects with hibernatable WebSockets
  are not portable without a rewrite.
- An attacker who generates arbitrary well-formed company tokens can create arbitrary Durable
  Objects — see [`docs/threat-model.md`](../threat-model.md) for abuse/cost analysis.
- No persistent state is stored at rest anywhere — see [`docs/privacy.md`](../privacy.md).
