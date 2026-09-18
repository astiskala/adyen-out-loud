# 0002: Use Cloudflare Durable Objects for per-instance state

## Status
Accepted

## Context
Each app instance needs somewhere to durably hold pending webhook data, correlation state, and a
replay log of unacknowledged WebSocket messages, and needs a WebSocket endpoint that a single
device can hold open for hours. A shared database (D1, an external Postgres, KV) would need an
explicit sharding or partitioning scheme per instance, plus a separate mechanism for holding
WebSocket connections and running timers. The project also has an explicit zero-provisioning goal
(see [ADR 0003](0003-zero-provisioning-instance-token-routing.md)) — there is no signup step where
a shard, region, or database row could be allocated in advance.

## Decision
Model each app instance as its own Cloudflare Durable Object (`RelayObject`, one class, SQLite
storage backend), addressed by a name derived from the instance token. The Durable Object owns its
own tables (`raw_ingress`, `display_states`, `authorisation_states`, `published`,
`outbound_messages`), accepts the instance's single WebSocket connection via hibernatable
WebSockets, and schedules its own correlation/retention work via the Durable Object alarm API. See
[`docs/architecture.md`](../architecture.md#durable-object-workersrcrelay-objectts) for the schema
and lifecycle.

## Consequences
- Durable Objects give strong single-instance consistency (all operations against one object are
  serialized) without needing application-level locking — the correlation and dedup logic can
  assume no concurrent writer.
- Hibernatable WebSockets mean an idle connection doesn't keep the object (and its billed compute)
  pinned in memory, and the Durable Object alarm replaces a cron/queue worker for the
  correlation-wait and retention sweeps.
- One object per instance means naturally isolated state and blast radius: a bug or an abusive
  payload affects exactly one merchant's object, never a shared table.
- The corresponding cost is that this project runs on Cloudflare Workers specifically — Durable
  Objects with SQLite storage are not portable to a generic serverless platform without a rewrite.
- It also means an attacker who can generate arbitrary well-formed tokens can create arbitrary
  Durable Objects (each incurring at least the empty-schema storage cost) — see
  [`docs/threat-model.md`](../threat-model.md) for the abuse/cost analysis and mitigations.
- As of [ADR 0007](0007-display-only-stateless-company-scoped-relay.md), `RelayObject` no longer
  uses SQLite storage at all — the Durable Objects choice is retained purely for per-company
  isolation and hibernatable-WebSocket connection fan-out (routing an ingested notification to the
  connections tagged with the matching terminal serial), not for durable state.
