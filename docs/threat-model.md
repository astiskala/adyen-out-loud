# Threat model

This document describes what this project protects, from whom, and — just as importantly — what
it deliberately does not protect against in its current (v1) design. If you're evaluating this
project for a real deployment, read this before the feature list.

## Assets

| Asset | Why it matters |
| --- | --- |
| The generated instance token | Bearer secret — see [Trust boundaries](#trust-boundaries) below. Anyone who has it can send the instance fabricated payment announcements and read its live WebSocket feed. |
| The webhook URL (`https://<worker>/v1/i/<token>`) | Contains the token in plain sight. Treat it exactly like a password. |
| Payment metadata in transit and at rest | Amount, currency, payment method, terminal ID, transaction ID, PSP reference. Not cardholder data (see [`docs/privacy.md`](privacy.md)), but still operationally sensitive — it reveals a merchant's sales activity. |
| Terminal identifiers | Identify a specific physical device at a specific merchant location. |
| WebSocket messages in flight | Carry the payment metadata above between Worker and app. |
| Cloudflare Durable Object state (SQLite) | Durable copy of recent webhook bodies and correlation state, retained for 24 hours (see [`docs/privacy.md`](privacy.md)). |

## Trust boundaries

```text
Adyen terminal / Adyen platform  ──HTTPS──▶  public Cloudflare Worker
                                              (anyone with the URL can POST here — see below)

client app (MAUI)                ──WSS───▶   public Cloudflare Worker
                                              (anyone with the URL can connect here — see below)

Worker                           ──────▶     Durable Object (internal, not publicly addressable)

Durable Object                   ──────▶     persisted SQLite state (24h retention)

app                              ──────▶     OS-level text-to-speech (on-device, no network)
```

The Worker's public HTTP/WebSocket surface is the only externally-reachable boundary. It is
reached by two parties with very different trust levels: **Adyen** (sending real webhooks — we
have no cryptographic proof it's actually Adyen, see below) and **anyone who has the webhook URL**
(who can send *anything* that looks like a webhook, and can read the live WebSocket feed).

## The v1 design choice this document exists to be honest about

**The high-entropy URL token is a bearer secret, not an HMAC-verified request.** Adyen supports
[HMAC signature verification](https://docs.adyen.com/development-resources/webhooks/verify-hmac-signatures/)
for webhooks — a cryptographic proof that a request genuinely came from Adyen and wasn't
forged or tampered with in transit. **This project does not implement HMAC verification in v1.**
Instead, it relies entirely on the URL itself being secret: a 32-byte random token that's
infeasible to guess, delivered once, shown once in the app, and never logged (see
[Logging](#logging) below). Possession of the URL is treated as proof of authorization to send
that instance announcements.

This is a materially weaker guarantee than HMAC verification:

- If the URL leaks (screenshot, shoulder-surfing, a support ticket pasted with it still visible,
  a misconfigured webhook forwarder), an attacker can send arbitrary fabricated
  "payment succeeded" events to that instance until the token is rotated (currently: reinstall the
  app; there is no in-app rotation flow yet).
- There is no cryptographic proof-of-origin on any individual request — the Worker cannot
  distinguish "a request from Adyen's real infrastructure" from "a request from anyone who knows
  the URL."

**Do not treat a fabricated announcement as evidence of an actual payment.** The worst-case impact
of this weakness is a spoken false-positive confirmation, not financial loss — this project never
handles, stores, or has any authority over the actual movement of money; it only announces what a
webhook *claims* happened. But that impact is real for a staff member trusting the announcement, so
the mitigations below matter.

**Mitigations already implemented:**

- The token is 256 bits of `RandomNumberGenerator`-sourced entropy (see
  [`DeviceIdentityGenerator`](../app/AdyenOutLoud.Core/Services/DeviceIdentityGenerator.cs)) —
  infeasible to guess by brute force.
- The token is shown once, as a read-only field with an explicit "treat this as a secret" warning
  in the UI, and is never logged in full anywhere (Worker logs, app diagnostics) — see
  [Logging](#logging).
- The Worker validates strict token *shape* (43-char base64url decoding to exactly 32 bytes)
  before doing anything else, so malformed requests are rejected before touching a Durable Object.
- All traffic is HTTPS/WSS only — see [Plaintext transport](#plaintext-transport-and-certificate-validation)
  below.

**Planned/possible future hardening (not implemented today — do not assume any of this exists):**
per-webhook HMAC verification using Adyen's HMAC key (would require a one-time HMAC-key entry step
in setup, trading away some of the zero-provisioning simplicity described in
[ADR 0003](adr/0003-zero-provisioning-instance-token-routing.md)), and an in-app token
rotation flow. If you need HMAC verification today, do not deploy this project as-is for a
production merchant without adding it yourself.

## Threats

| Threat | Discussion |
| --- | --- |
| **Leaked webhook URL** | See [above](#the-v1-design-choice-this-document-exists-to-be-honest-about). Primary mitigation is treating the URL as a secret in the UI/UX and never logging it. |
| **Forged webhook requests** | Anyone with the URL can POST a fabricated notification. The Worker validates the *shape* of Adyen-like payloads (parsers reject incomplete/malformed structures — see `worker/test/parsers.test.ts`) but cannot verify *origin* without HMAC (see above). |
| **Replay** | A captured, legitimate request replayed later is deduplicated by content (`dedupe_key`, `raw_ingress` `PRIMARY KEY`) — it will not re-trigger a new announcement. It does **not** prevent an attacker who has captured a valid request from replaying it once before the original arrives; this is a lower-severity variant of the forged-request threat above. |
| **Duplicate delivery** | Adyen's own retries are expected and handled — see [`docs/architecture.md#deduplication-before-persistence`](architecture.md#deduplication-before-persistence). |
| **Malformed payloads** | Every parser is adversarially tested (invalid JSON, missing fields, wrong types, oversized/unicode values — see `worker/test/parsers.test.ts` and [`docs/testing.md`](testing.md)) to fail closed (reject, don't crash, don't silently misinterpret). |
| **Resource exhaustion** | Request bodies are capped at 64 KiB before they reach the Durable Object (`MAX_BODY_BYTES` in `worker/src/index.ts`); the outbound WebSocket queue is bounded to 100 messages with 24h retention (`MAX_OUTBOUND_MESSAGES`, `RETENTION_MS` in `worker/src/relay-object.ts`); the app's client-side WebSocket message reader also caps incoming frames at 64 KiB. None of these are currently rate-limited per IP or per token at the Worker's HTTP layer — see [Repository settings](repository-settings.md) and consider Cloudflare-level rate limiting if you deploy this publicly at scale. |
| **Random-token Durable Object creation (cost/abuse risk)** | Because routing is pure computation (see [ADR 0003](adr/0003-zero-provisioning-instance-token-routing.md)), any request with a *well-formed* token (43 base64url chars, decodes to 32 bytes) causes a Durable Object to be created if one doesn't already exist — even a nonsense token nobody's app generated. An attacker spraying random well-formed tokens can create Durable Objects at will, each incurring at least empty-schema storage and Durable Object billing. **Mitigation implemented:** every created object immediately gets 24-hour retention/pruning (`RETENTION_MS`), so an abandoned spam object's storage cost is naturally bounded and self-cleaning rather than growing forever. **Not implemented:** a rate limit on distinct-token creation per source IP, and a maximum global count of live objects — both are legitimate, low-cost additions if this is deployed somewhere abuse-exposed, and neither requires changing the zero-provisioning UX. |
| **Abuse of the public endpoint** | The `/health` endpoint and the ingest/WebSocket routes are unauthenticated by design (see [ADR 0003](adr/0003-zero-provisioning-instance-token-routing.md)). Cloudflare's platform-level DDoS protection is the first line of defense; this project adds body-size and queue-size bounds on top but does not implement its own rate limiting. |
| **Sensitive logging** | See [Logging](#logging) below — enforced by structured logging conventions and (on the Worker side) by tests asserting the token/URL never appear in log output. |
| **Unbounded storage** | Bounded by the retention sweep described in [`docs/architecture.md`](architecture.md#durable-object-workersrcrelay-objectts) and tested directly (`worker/test/worker.test.ts`, retention + outbound-count-bound test). |
| **Stale queued announcements** | A device offline for a very long time will have old undelivered messages pruned after 24 hours rather than receiving an unbounded backlog on reconnect — see [`docs/privacy.md`](privacy.md). |
| **Client compromise** | A compromised device has access to whatever the app itself has: the instance token (via OS secure storage APIs, which a fully-compromised OS/device can generally always defeat) and locally-cached announcement history. This project does not attempt to defend against a fully compromised end-user device — that's an OS/device security problem outside this project's scope. |
| **Dependency compromise** | Mitigated by dependency pinning, lockfiles, `npm audit`/NuGet audit in CI, Dependabot, and CodeQL — see [`docs/quality.md`](quality.md) and [`docs/dependencies.md`](dependencies.md). Not eliminated: a sufficiently novel supply-chain attack against a pinned, audited dependency is a residual risk shared by every project with third-party dependencies. |

## Plaintext transport and certificate validation

All traffic is HTTPS/WSS. The app never disables TLS certificate validation (there is no code path
that does this — see [`docs/security.md`](security.md) for the audit) and the Worker only speaks
HTTPS by construction (Cloudflare Workers do not serve plaintext HTTP to the internet for a
`workers.dev`/custom-domain deployment). `RelayEndpointFactory` additionally rejects any configured
`RelayBaseUrl` that isn't an `https://` origin at build/startup time, so the app cannot be
accidentally pointed at a plaintext endpoint.

## Logging

See [`docs/security.md`](security.md#logging-and-redaction) for the concrete redaction rules and
tests. In short: the instance token and full webhook URL are never logged in either the Worker or
the app; where correlating log lines to a specific instance is operationally useful, a truncated
one-way hash of the token is used instead of the token itself.
