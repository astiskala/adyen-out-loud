# Security implementation notes

Contributor-facing reporting process lives in [`SECURITY.md`](../SECURITY.md); the security
*assumptions* and *trade-offs* of the design live in [`docs/threat-model.md`](threat-model.md).
This document is the implementation-level detail: what the codebase does and what tooling enforces
it.

## Terminal serial handling

- There is no secret token. The only routing key is the terminal serial, validated by shape
  (`^[A-Za-z0-9_-]{1,64}$`, `validTerminalSerial` in `worker/src/index.ts`) and rejected with a generic
  `404` otherwise. It is not a secret (see [`docs/threat-model.md`](threat-model.md)).
- It is stored on-device in app preferences (`Preferences` via
  [`PreferencesRelayConfigurationStore`](../app/AdyenOutLoud/Services/PreferencesRelayConfigurationStore.cs)),
  not in Keychain-backed `SecureStorage`: it is not a secret, and `SecureStorage` fails with
  `MissingEntitlement` in unsigned Mac builds.

## Logging and redaction

The Worker's structured error log (`RelayObject.logError`) emits `{level, event, error}` as JSON;
`error` is a fixed message or an exception's own message, never a raw request body. If a future
change adds a new logged field, keep this invariant: no raw payment payload in a log line at any
level.

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
rejects a non-`https://` relay URL (see
[`docs/threat-model.md#plaintext-transport-and-certificate-validation`](threat-model.md#plaintext-transport-and-certificate-validation)).

## Exception handling and information leaks

User-facing failure messages (the app's "NEEDS ATTENTION" diagnostic panel) show `exception.Message`
directly — by design, for debuggability during setup — but no exception thrown anywhere in this
codebase carries the payment payload in its message; they describe
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
