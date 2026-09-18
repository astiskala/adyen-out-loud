# Threat model

This document describes what this project protects, from whom, and — just as importantly — what
it deliberately does not protect against in its current (v1) design. If you're evaluating this
project for a real deployment, read this before the feature list.

## Assets

| Asset | Why it matters |
| --- | --- |
| The generated company token | Bearer secret — see [Trust boundaries](#trust-boundaries) below. Anyone who has it can send fabricated payment announcements to *every terminal in the company* and connect to any of their live WebSocket feeds. |
| The relay URL (`https://<worker>/v1/c/<companyToken>`) | Contains the token in plain sight. Treat it exactly like a password, and share it only through the company's own webhook configuration and whatever channel installers use to receive it. |
| The terminal serial number | Not secret by itself — it's the same value Adyen already sends in every Display webhook's `POIID` field, and knowing it alone grants nothing without the company token too. It only matters as *routing*, not as an authorization credential. |
| Payment metadata in transit | `terminalId`, `transactionId`, `pspReference`, `occurredAt`, the Display result. Not cardholder data (see [`docs/privacy.md`](privacy.md)). No payment method or amount is included — the Display notification never carries either — still operationally sensitive as evidence a transaction happened at a specific terminal. |
| WebSocket messages in flight | Carry the payment metadata above between Worker and app. Nothing is retained after delivery — see [`docs/privacy.md`](privacy.md). |

## Trust boundaries

```text
Adyen terminal                   ──HTTPS──▶  public Cloudflare Worker
                                              (anyone with the URL can POST here — see below)

client app (MAUI)                ──WSS───▶   public Cloudflare Worker
                                              (anyone with the URL can connect here — see below)

Worker                           ──────▶     Durable Object (internal, not publicly addressable,
                                              holds no persisted state)

app                              ──────▶     OS-level text-to-speech (on-device, no network)
```

The Worker's public HTTP/WebSocket surface is the only externally-reachable boundary. It is
reached by two parties with very different trust levels: **Adyen** (sending real webhooks — we
have no cryptographic proof it's actually Adyen, see below) and **anyone who has the relay URL**
(who can send *anything* that looks like a webhook, and can read any live WebSocket feed for that
company).

## The v1 design choice this document exists to be honest about

**The high-entropy URL token is a bearer secret, not an HMAC-verified request.** Adyen supports
[HMAC signature verification](https://docs.adyen.com/development-resources/webhooks/verify-hmac-signatures/)
for webhooks — a cryptographic proof that a request genuinely came from Adyen and wasn't
forged or tampered with in transit. **This project does not implement HMAC verification in v1.**
Instead, it relies entirely on the URL itself being secret: a 32-byte random token that's
infeasible to guess, generated once by whoever sets up the company's webhook, and never logged (see
[Logging](#logging) below). Possession of the URL is treated as proof of authorization to send that
company's terminals announcements.

This is a materially weaker guarantee than HMAC verification:

- If the URL leaks (screenshot, shoulder-surfing, a support ticket pasted with it still visible,
  a misconfigured webhook forwarder), an attacker can send arbitrary fabricated
  "payment successful" events to every terminal in the company until the token is rotated
  (currently: generate a new one and reconfigure both the Adyen webhook and every app instance;
  there is no automated rotation flow).
- There is no cryptographic proof-of-origin on any individual request — the Worker cannot
  distinguish "a request from Adyen's real infrastructure" from "a request from anyone who knows
  the URL."
- The URL is shared across an entire company rather than generated per-device; a leak
   affects every terminal at once — a deliberate trade-off for not requiring Customer Area access
   per installer (see [ADR 0007](adr/0007-display-only-stateless-company-scoped-relay.md)).

**Do not treat a fabricated announcement as evidence of an actual payment.** The worst-case impact
of this weakness is a spoken false-positive confirmation, not financial loss — this project never
handles, stores, or has any authority over the actual movement of money; it only announces what a
webhook *claims* happened. But that impact is real for a staff member trusting the announcement, so
the mitigations below matter.

**Mitigations already implemented:**

- The token is 256 bits of cryptographically random entropy
  (`scripts/generate-company-token.sh`, or the equivalent used by whoever set it up) — infeasible
  to guess by brute force.
- The app never logs it, and never displays it after it's been entered except as an editable field
  the installer typed it into themselves — see [Logging](#logging).
- The Worker validates strict token *shape* (43-char base64url decoding to exactly 32 bytes) and
  terminal-serial shape before doing anything else, so malformed requests are rejected before
  touching a Durable Object.
- All traffic is HTTPS/WSS only — see [Plaintext transport](#plaintext-transport-and-certificate-validation)
  below.

**Planned/possible future hardening (not implemented today — do not assume any of this exists):**
per-webhook HMAC verification using Adyen's HMAC key (would require a one-time HMAC-key entry step
in setup), and an in-app or Worker-side token rotation flow. If you need HMAC verification today,
do not deploy this project as-is for a production merchant without adding it yourself.

## Threats

| Threat | Discussion |
| --- | --- |
| **Leaked relay URL** | See [above](#the-v1-design-choice-this-document-exists-to-be-honest-about). Primary mitigation is treating the URL as a secret in the UI/UX and never logging it. |
| **Forged webhook requests** | Anyone with the URL can POST a fabricated notification. The Worker validates the *shape* of Adyen-like payloads (the Display parser rejects incomplete/malformed structures — see `worker/test/parsers.test.ts`) but cannot verify *origin* without HMAC (see above). |
| **Replay** | A captured, legitimate request replayed later is **not** deduplicated server-side — with no persistence (see [ADR 0007](adr/0007-display-only-stateless-company-scoped-relay.md)), the Worker has nothing to check a replay against. A replay simply causes the same announcement to be pushed again to whatever is currently connected; the app's own client-side recent-event-ID cache (`ISettingsService`) is what keeps it from being spoken twice on a device that's already seen it. This is a lower-severity variant of the forged-request threat above, not a new one. |
| **Duplicate delivery** | Adyen's own retries are expected; handled the same way as Replay above — client-side, not server-side. |
| **Malformed payloads** | The Display parser is adversarially tested (invalid JSON, missing fields, wrong types, oversized/unicode values — see `worker/test/parsers.test.ts` and [`docs/testing.md`](testing.md)) to fail closed (reject, don't crash, don't silently misinterpret). |
| **Resource exhaustion** | Request bodies are capped at 64 KiB before they reach the Durable Object (`MAX_BODY_BYTES` in `worker/src/index.ts`); the app's client-side WebSocket message reader also caps incoming frames at 64 KiB. None of these are currently rate-limited per IP or per token at the Worker's HTTP layer — see [Repository settings](repository-settings.md) and consider Cloudflare-level rate limiting if you deploy this publicly at scale. |
| **Random-token Durable Object creation (cost/abuse risk)** | Because routing is pure computation, any request with a *well-formed* token (43 base64url chars, decodes to 32 bytes) causes a Durable Object to be created if one doesn't already exist — even a nonsense token nobody generated. An attacker spraying random well-formed tokens can create Durable Objects at will. **With no persistence at all (ADR 0007), the cost of each is now negligible** — there's no schema, no storage, nothing to accumulate or need pruning; a spammed object costs essentially nothing beyond its own minimal existence. **Not implemented:** a rate limit on distinct-token creation per source IP — a legitimate, low-cost addition if this is deployed somewhere abuse-exposed. |
| **Abuse of the public endpoint** | The `/health` endpoint and the ingest/WebSocket routes are unauthenticated by design. Cloudflare's platform-level DDoS protection is the first line of defense; this project adds a body-size bound on top but does not implement its own rate limiting. |
| **Sensitive logging** | See [Logging](#logging) below — enforced by structured logging conventions and (on the Worker side) by a test asserting the company token never appears in log output. |
| **Client compromise** | A compromised device has access to whatever the app itself has: the configured relay URL and terminal serial (via OS secure storage APIs, which a fully-compromised OS/device can generally always defeat) and locally-cached announcement history. This project does not attempt to defend against a fully compromised end-user device — that's an OS/device security problem outside this project's scope. |
| **Dependency compromise** | Mitigated by dependency pinning, lockfiles, `npm audit`/NuGet audit in CI, Dependabot, and CodeQL — see [`docs/quality.md`](quality.md) and [`docs/dependencies.md`](dependencies.md). Not eliminated: a sufficiently novel supply-chain attack against a pinned, audited dependency is a residual risk shared by every project with third-party dependencies. |

## Plaintext transport and certificate validation

All traffic is HTTPS/WSS. The app never disables TLS certificate validation (there is no code path
that does this — see [`docs/security.md`](security.md) for the audit) and the Worker only speaks
HTTPS by construction (Cloudflare Workers do not serve plaintext HTTP to the internet for a
`workers.dev`/custom-domain deployment). `RelayEndpointFactory` additionally rejects any relay URL
that isn't an `https://` address at the point the installer saves it, so the app cannot be
accidentally pointed at a plaintext endpoint.

## Logging

See [`docs/security.md`](security.md#logging-and-redaction) for the concrete redaction rules and
tests. In short: the company token and full relay URL are never logged in either the Worker or the
app.
