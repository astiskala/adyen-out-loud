# Security implementation notes

Contributor-facing reporting process lives in [`SECURITY.md`](../SECURITY.md); the security
*assumptions* and *trade-offs* of the design live in [`docs/threat-model.md`](threat-model.md).
This document is the implementation-level detail: what was audited, what the codebase does, and
what tooling enforces it.

## Cryptographically secure randomness

A company token is generated with `openssl rand` (`scripts/generate-company-token.sh`), or an
equivalent 256-bit cryptographically random source — never a non-cryptographic generator. The
Worker's routing hash uses Web Crypto's `crypto.subtle.digest("SHA-256", ...)`
([`worker/src/index.ts`](../worker/src/index.ts)).

## Token handling

- Validated by shape before anything else: `^[A-Za-z0-9_-]{43}$` *and* must decode to exactly 32
  bytes (`validCompanyToken` in `worker/src/index.ts`) — a malformed token is rejected with a
  generic `404`, the same response as an unknown route, so the response can't be used to
  distinguish "malformed" from "well-formed but unknown." The terminal serial in a WebSocket URL is
  validated the same way, against `^[A-Za-z0-9_-]{1,64}$`.
- Never compared or transmitted in cleartext to the Durable Object layer: `index.ts` hashes the
  token (`SHA-256`) into the Durable Object's *name* before dispatch — `relay-object.ts` never
  receives the raw company token at all, which is also why it's structurally impossible for that
  file to log it. The terminal serial *does* reach the Durable Object (as a query parameter and a
  WebSocket tag) — it has to, for routing — but it is not itself a secret (see
  [`docs/threat-model.md`](threat-model.md)).
- Stored on-device only in platform secure storage (`SecureStorage` via
  [`SecureStorageRelayConfigurationStore`](../app/AdyenOutLoud/Services/SecureStorageRelayConfigurationStore.cs))
  — Keychain on Apple platforms, Keystore-backed on Android, Credential Locker on Windows.

## Logging and redaction

Neither the Worker nor the app logs the company token or the full relay URL, anywhere, under any
log level. This is verified by an automated test, not just convention:
`worker/test/worker.test.ts` → "sensitive-data logging" → *never writes the company token to any
console output, including on the error path* — it spies on every `console.*` method across a
malformed-input and a normal ingest/publish flow and asserts the token string never appears in any
captured call.

The Worker's structured error log (`RelayObject.logError`) emits `{level, event, error}` as JSON;
`error` is a fixed message or an exception's own message, never a raw request body or token. If a
future change adds a new logged field, keep this invariant: no token, no full relay URL, no raw
payment payload in a log line at any level.

## JSON assumptions

Every piece of untrusted JSON — the Adyen webhook body on the Worker side, the relay envelope on
the app side — is treated as `unknown`/`JsonElement` and validated field-by-field before any typed
value is trusted:

- Worker: `asObject`/`asString` ([`worker/src/adyen/json.ts`](../worker/src/adyen/json.ts)) gate
  every field access in `display-parser.ts`; nothing is cast directly from `unknown` to a domain
  type.
- App: [`RelayProtocol.TryParsePayment`](../app/AdyenOutLoud.Core/Services/RelayProtocol.cs) walks
  a `System.Text.Json.JsonDocument` field-by-field with explicit `ValueKind` checks — it never
  deserializes the untrusted WebSocket payload directly into `PaymentMessage` via reflection-based
  `JsonSerializer.Deserialize<T>`.

See `worker/test/parsers.test.ts` and `app/AdyenOutLoud.Tests/ProductContractTests.cs`'s
`RejectedMutations` theory for the adversarial cases both are tested against (missing fields, wrong
types, malformed dates, unicode, oversized values).

## Input boundaries

| Boundary | Validated as |
| --- | --- |
| HTTP path company token | Shape + decoded length, before routing (`worker/src/index.ts`) |
| HTTP path terminal serial | Shape (`^[A-Za-z0-9_-]{1,64}$`), before routing |
| HTTP method | Explicit allow-list per route (`405` + `Allow` header otherwise) |
| `Content-Type` | Must be `application/json` on ingest (`415` otherwise) |
| Request body size | Streamed with a 64 KiB hard cap (`413` if exceeded, before the Durable Object ever sees it) |
| JSON body | Checked for valid syntax in `index.ts` (`400` if not), then field-validated inside the Durable Object |
| Webhook schema | Field-by-field, fails closed (see [JSON assumptions](#json-assumptions)) |
| WebSocket messages (client → server) | None expected — any message is ignored (`worker/src/relay-object.ts` → `webSocketMessage`) |
| WebSocket frames (server → client) | App caps incoming frames at 64 KiB and rejects non-text frames (`ClientWebSocketConnection.MaxMessageBytes`) |

## TLS

TLS certificate validation is never disabled anywhere in this codebase — there is no
`ServerCertificateCustomValidationCallback`, no `HttpClientHandler.DangerousAcceptAnyServerCertificateValidator`,
and no equivalent override on either the Worker or the app side. `RelayEndpointFactory` additionally
rejects a non-`https://` relay URL at the point it's saved (see
[`docs/threat-model.md#plaintext-transport-and-certificate-validation`](threat-model.md#plaintext-transport-and-certificate-validation)).

## Exception handling and information leaks

User-facing failure messages (the app's "NEEDS ATTENTION" diagnostic panel) show `exception.Message`
directly — by design, for debuggability during setup — but no exception thrown anywhere in this
codebase carries the company token, relay URL, or payment payload in its message; they describe
protocol/connectivity failures ("The relay closed the connection.", "The relay URL must be an HTTPS
address..."). The Worker never returns exception details to the HTTP caller at all — every error
response is a fixed, generic JSON body (`{"error": "..."}`) with a fixed set of messages, not an
interpolated exception message.

## What CodeQL and dependency scanning add on top of this

Manual review above is backed by automated, continuous checks — see [`docs/quality.md`](quality.md)
for how each is wired into CI:

- **CodeQL** (C# and JavaScript/TypeScript) on every PR, push to `main`, and weekly on schedule.
- **NuGet vulnerability auditing** (`NuGetAuditLevel: high`, restore-time) and **`npm audit`**
  (`--audit-level=high` in CI) for known-vulnerable dependencies.
- **GitHub dependency review** on every PR against a public repository.
- **SonarCloud** (`.github/workflows/sonarcloud.yml`) — a second static-analysis opinion, once
  configured; see [`docs/repository-settings.md`](repository-settings.md).
