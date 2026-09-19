# 0002: Use a Cloudflare Durable Object for the relay

## Status
Accepted. The single-object routing is described in
[ADR 0008](0008-single-shared-relay-and-prerecorded-audio.md).

## Context
The Worker must push a payment message only to the app connected for the terminal that generated the
webhook. Connections last hours, and no state is needed: if no app is connected the message is dropped.

## Decision
Use a Durable Object (`RelayObject`) with hibernatable WebSockets, each tagged with its terminal serial.
On ingest, fan out to `ctx.getWebSockets(terminalSerial)`. No storage, alarms, or database.

## Consequences
- Idle connections don't keep the object in memory, and tag fan-out is built in.
- The project is tied to Cloudflare Workers.
- One object serves all terminals, so it is the throughput ceiling; shard by serial if that matters.
