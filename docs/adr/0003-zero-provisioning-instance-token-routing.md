# 0003: Zero-provisioning instance-token routing

## Status
Accepted

## Context
The project's usability goal is: install the app, get a webhook URL, paste it into Adyen, done —
no account creation, no API keys to request, no merchant registration step against this project's
own infrastructure. That rules out any design where a server-side signup step allocates an
instance ID, a database row, or a routing table entry before the first webhook can arrive.

## Decision
The app generates its own identity locally: 32 cryptographically random bytes
(`RandomNumberGenerator.GetBytes(32)`), base64url-encoded into a 43-character token, stored only in
platform secure storage. The Worker never sees this generation happen and holds no
token-registration table. It routes purely by computation: `SHA-256(token)` (base64url) becomes the
Durable Object's name, and `env.PAYMENT_CHANNELS.idFromName(name)` either creates or finds that
object on first use. See [`docs/architecture.md`](../architecture.md#instance-identity-and-routing).

## Consequences
- No signup flow, no server-side provisioning step, no account database — the entire "backend" is
  the Worker code plus whatever Durable Objects have been lazily created by real traffic.
- The token is the *only* thing that makes a webhook URL or WebSocket connection belong to a
  specific instance. Anyone who has the token can send it announcements and read its live
  WebSocket feed — the token is a **bearer secret**, not a username with a separately-verified
  password. This is explicitly not equivalent to Adyen's HMAC webhook signature verification. See
  [`docs/threat-model.md`](../threat-model.md), which documents this trade-off in detail rather
  than obscuring it.
- Because routing is pure computation, an attacker who can guess or brute-force a valid-*shaped*
  token (43 base64url characters decoding to 32 bytes) can cause the Worker to create a new Durable
  Object for it — there is no registration step to reject an "unknown" token before object
  creation. 32 bytes of entropy makes guessing infeasible, but the *cost* of an attacker
  deliberately spraying random well-formed tokens is a separate, real consideration, covered in the
  threat model's abuse/cost-risk section.
- Losing the token (uninstalling the app without noting it down) means losing access to that
  instance's webhook URL permanently — there is no account-recovery flow, by design.
