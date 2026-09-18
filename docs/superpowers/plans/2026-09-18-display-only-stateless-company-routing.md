# Display-only, stateless, company-scoped relay — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current two-webhook-correlation, per-app-instance-token, SQLite-persisted
relay architecture with a display-webhook-only, fully stateless, company-scoped relay: one
Cloudflare URL per Adyen company account (shared by every terminal in that company), the app
identifies itself by a user-entered terminal serial number, announcements are always the generic
"payment successful" (Adyen's Display API never carries amount/tender type), and a payment that
arrives while no device is connected is simply dropped rather than queued. The app should also keep
listening while backgrounded wherever the platform allows it (Android via a foreground service,
Windows/Mac Catalyst because desktop apps aren't suspended; iOS stays foreground-only, documented as
a deliberate limitation). Add SonarCloud static analysis, confirm formatting is a required quality
gate, bring dependencies current, delete `CHANGELOG.md` in favor of GitHub releases/tags, and
streamline the docs to match the simpler system.

**Architecture:** The Worker becomes a thin, storage-free relay: `POST /v1/c/<companyToken>` routes
by company to one Durable Object per company; that object accepts WebSocket connections at
`GET /v1/c/<companyToken>/t/<terminalSerial>/ws`, tagging each socket with its terminal serial via
Cloudflare's hibernatable-WebSocket tag API (`ctx.acceptWebSocket(socket, [tag])` /
`ctx.getWebSockets(tag)`); on ingest it parses the Display `TENDER_FINAL` notification, derives the
terminal serial from `POIID`, and pushes a generic `payment_succeeded` envelope only to sockets
tagged with that serial — nothing is written to storage anywhere. The app drops its self-generated
instance-token identity entirely; instead it stores a user-entered relay URL (the full
`https://.../v1/c/<companyToken>` the company admin already configured in Adyen) and a user-entered
terminal serial number, and derives its WebSocket URL from the two. The relay protocol drops
`paymentMethod`/`amount` (bumped to `protocol: 2`) and drops client-to-server ACKs entirely (nothing
server-side depends on them anymore).

**Tech Stack:** Cloudflare Workers + Durable Objects (TypeScript, Vitest, `@cloudflare/vitest-plugin`),
.NET 10 / MAUI (C#, xUnit), GitHub Actions, SonarCloud.

## Global Constraints

- Preserve every existing architecture boundary rule (Core has zero MAUI/platform references;
  `worker/src/adyen/**` and `worker/src/identity.ts`-equivalent pure modules never import Cloudflare
  infrastructure; no test project referenced from production code) — see `AGENTS.md`.
- Every external JSON boundary stays walked field-by-field (`asObject`/`asString` on the Worker,
  `JsonDocument`/`JsonElement` on the app) — never a reflection-based deserialize of untrusted input.
- Never log a company token, a full relay URL, or a terminal serial number, at any log level, on
  either side — carry forward the existing redaction discipline and its regression test.
- Formatting (`dotnet format --verify-no-changes`, `prettier --check`) and all existing analyzers/
  lint/type-check/architecture/coverage gates must stay green; do not lower a coverage threshold or
  suppress a warning to make a gate pass.
- No new dependency without checking it against `docs/dependencies.md`'s checklist (BCL/framework
  sufficiency, maintenance, license, vulnerabilities, stable release).
- Coverage floors stay as documented today (.NET Core: 90%/85% line/branch; Worker `src/adyen/**`:
  90%/85%/90%, `src/relay-object.ts`: 80%/75%/80%) unless a task explicitly retunes them because the
  underlying code shape changed enough to warrant it (state that explicitly in the task).
- Update the relevant doc in the same task that changes the behavior it describes — don't defer
  documentation to a later pass.

---

## Part A — Worker: display-only parsing and company/terminal identity

### Task A1: Add terminal-serial extraction to the Display parser

**Files:**
- Modify: `worker/src/adyen/display-parser.ts`
- Modify: `worker/src/adyen/models.ts`
- Test: `worker/test/parsers.test.ts`

**Interfaces:**
- Produces: `terminalSerialFromPoiId(poiId: string): string`, and `DisplayState.terminalSerial:
  string`, consumed by Task A4 (`relay-object.ts`) and Task A5 (`index.ts` no longer needs it, but
  A4 does).

Adyen's terminal ID (`POIID`, e.g. `"V400m-324688170"`) is `<model>-<serial>`; the app is configured
with just the serial (`"324688170"`), so the Worker must derive the same substring from the
webhook to route correctly.

- [ ] **Step 1: Write the failing tests**

Add to `worker/test/parsers.test.ts` (alongside the existing `parseDisplayNotification` describe
block — read the existing file first to match its structure and fixture style):

```typescript
import { parseDisplayNotification, terminalSerialFromPoiId } from "../src/adyen/display-parser";

describe("terminalSerialFromPoiId", () => {
  it("returns the substring after the last hyphen", () => {
    expect(terminalSerialFromPoiId("V400m-324688170")).toBe("324688170");
  });

  it("returns the substring after the last hyphen when there are several hyphens", () => {
    expect(terminalSerialFromPoiId("V400m-EU-324688170")).toBe("324688170");
  });

  it("returns the whole value when there is no hyphen", () => {
    expect(terminalSerialFromPoiId("324688170")).toBe("324688170");
  });

  it("returns the whole value when the hyphen is the last character", () => {
    expect(terminalSerialFromPoiId("V400m-")).toBe("V400m-");
  });
});

describe("parseDisplayNotification terminalSerial", () => {
  it("includes the derived terminal serial alongside the full terminal id", () => {
    const display = parseDisplayNotification(displayTenderFinalApproved());
    expect(display?.terminalId).toBe("V400m-324688170");
    expect(display?.terminalSerial).toBe("324688170");
  });
});
```

Use whatever existing fixture-loading helper `parsers.test.ts` already uses for
`display-tender-final-approved.json` (check the top of the file — it likely imports the JSON
fixture directly) rather than inventing a new `displayTenderFinalApproved()` helper if one doesn't
already exist; adapt the second test to that pattern.

- [ ] **Step 2: Run to verify failure**

Run: `cd worker && npx vitest run test/parsers.test.ts`
Expected: FAIL — `terminalSerialFromPoiId` is not exported, `terminalSerial` is `undefined`.

- [ ] **Step 3: Implement**

In `worker/src/adyen/display-parser.ts`, add the export and use it:

```typescript
export function terminalSerialFromPoiId(poiId: string): string {
  const separator = poiId.lastIndexOf("-");
  if (separator < 0 || separator === poiId.length - 1) return poiId;
  return poiId.slice(separator + 1);
}
```

Update the return object in `parseDisplayNotification` to include
`terminalSerial: terminalSerialFromPoiId(terminalId)`.

In `worker/src/adyen/models.ts`, add the field to `DisplayState`:

```typescript
export interface DisplayState {
  pspReference: string;
  terminalId: string;
  terminalSerial: string;
  transactionId: string;
  occurredAt: string;
  successful: boolean;
  result: string;
}
```

- [ ] **Step 4: Run to verify pass**

Run: `cd worker && npx vitest run test/parsers.test.ts`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add worker/src/adyen/display-parser.ts worker/src/adyen/models.ts worker/test/parsers.test.ts
git commit -m "feat(worker): derive terminal serial from Display POIID"
```

### Task A2: Simplify the outbound message shape and bump the protocol to 2

**Files:**
- Modify: `worker/src/adyen/models.ts`

**Interfaces:**
- Produces: `OutboundEnvelope` with `protocol: 2` and a `PaymentMessage` with no
  `paymentMethod`/`amount` fields — consumed by Task A4 (`relay-object.ts`) and, on the app side,
  Task B3 (`RelayProtocol.cs`).

Since the relay is display-only and Adyen's Display API never carries payment method or amount
(confirmed in `display-tender-final-approved.json` / `display-parser.ts` — the only fields present
are `event`, `result`, `TimeStamp`, `TransactionID`), every published message is the same shape.
Carrying two permanently-`null` fields forever is dead weight — remove them and bump the protocol
version, since this is a breaking wire-format change.

- [ ] **Step 1: Edit `worker/src/adyen/models.ts`**

Replace the `AuthorisationState` interface, `Amount` interface, `PaymentMessage` interface, and
`OutboundEnvelope` interface with:

```typescript
export interface DisplayState {
  pspReference: string;
  terminalId: string;
  terminalSerial: string;
  transactionId: string;
  occurredAt: string;
  successful: boolean;
  result: string;
}

interface PaymentMessage {
  id: string;
  type: "payment_succeeded";
  occurredAt: string;
  terminalId: string;
  transactionId: string;
  pspReference: string;
}

export interface OutboundEnvelope {
  protocol: 2;
  message: PaymentMessage;
}

// `Env` is declared globally by the generated worker-configuration.d.ts (run `npm run types:generate`
// after any wrangler.jsonc binding change) — it is not re-declared or exported here so it can never drift.
```

(`AuthorisationState` and `Amount` are deleted entirely — nothing imports them after Task A3.)

- [ ] **Step 2: Commit**

This file alone won't type-check yet (its only remaining importers, `standard-webhook-parser.ts`
and `payment-correlator.ts`, are deleted in the very next task) — fold this into Task A3's commit
instead of committing standalone. Proceed directly to Task A3.

### Task A3: Delete the Standard-webhook parser, the correlator, and the dedupe/identity module

**Files:**
- Delete: `worker/src/adyen/standard-webhook-parser.ts`
- Delete: `worker/src/adyen/payment-correlator.ts`
- Delete: `worker/src/identity.ts`
- Delete: `worker/test/fixtures/standard-authorisation-success.json`
- Delete: `worker/test/fixtures/standard-authorisation-failed.json`
- Modify: `worker/test/parsers.test.ts` (remove every `parseStandardAuthorisations`,
  `canonicalJson`, `stableIngressFields` describe block and their imports)

**Interfaces:**
- Removes: `parseStandardAuthorisations`, `correlatePayment`, `CORRELATION_WAIT_MS`,
  `canonicalJson`, `stableIngressFields` — none of these are consumed by any later task.

Display-only means there is no second notification to correlate, and stateless means there is no
persisted state to dedupe against (Task A4 removes all storage; the app's own recent-event-ID cache,
untouched by this plan, is what keeps a retried webhook from being spoken twice — see
`app/AdyenOutLoud/Services/PreferencesSettingsService.cs`, unchanged).

- [ ] **Step 1: Delete the files**

```bash
git rm worker/src/adyen/standard-webhook-parser.ts worker/src/adyen/payment-correlator.ts worker/src/identity.ts
git rm worker/test/fixtures/standard-authorisation-success.json worker/test/fixtures/standard-authorisation-failed.json
```

- [ ] **Step 2: Strip `worker/test/parsers.test.ts`**

Read the current file and remove: the `import { parseStandardAuthorisations } from
"../src/adyen/standard-webhook-parser"` line and its describe block; the `import { canonicalJson,
stableIngressFields } from "../src/identity"` line and its describe block. Keep every
`parseDisplayNotification` test (now including the Task A1 additions) and the `worker/test/fixtures/
unknown-valid.json` / adversarial-input tests that exercise `parseDisplayNotification` and
`asObject`/`asString` (`worker/src/adyen/json.ts` is untouched by this plan).

- [ ] **Step 3: Run the type checker to confirm nothing else references the deleted modules yet**

Run: `cd worker && npm run typecheck`
Expected: FAIL — `worker/src/relay-object.ts` and `worker/src/index.ts` still import the deleted
modules. This is expected; Tasks A4 and A5 fix it. Do not commit yet.

- [ ] **Step 4: Proceed directly to Task A4** (same logical change; commit together at the end of
  A4, since `relay-object.ts` is what actually stops referencing the deleted files).

### Task A4: Rewrite the Durable Object as a stateless, tag-routed relay

**Files:**
- Modify: `worker/src/relay-object.ts` (full rewrite)
- Test: `worker/test/worker.test.ts` (substantial rewrite — see Task A6)

**Interfaces:**
- Consumes: `parseDisplayNotification` (`./adyen/display-parser`), `OutboundEnvelope`
  (`./adyen/models`).
- Produces: `RelayObject` with two internal routes on `fetch()` — `POST /ingest` (body: raw Display
  webhook JSON) and `GET /ws?terminal=<serial>` (WebSocket upgrade) — consumed by Task A5
  (`index.ts`).

Replace the entire file:

```typescript
import { DurableObject } from "cloudflare:workers";
import { parseDisplayNotification } from "./adyen/display-parser";
import type { DisplayState, OutboundEnvelope } from "./adyen/models";

export class RelayObject extends DurableObject<Env> {
  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/ingest" && request.method === "POST") return this.ingest(request);
    if (url.pathname === "/ws" && request.method === "GET") return this.acceptSocket(request, url);
    return new Response("Not found", { status: 404 });
  }

  private async ingest(request: Request): Promise<Response> {
    const body = await request.text();
    try {
      const value: unknown = JSON.parse(body);
      const display = parseDisplayNotification(value);
      if (display?.successful) this.publish(display);
    } catch (error) {
      this.logError("ingest_processing_failed", error);
    }
    return new Response(null, { status: 202 });
  }

  private acceptSocket(request: Request, url: URL): Response {
    if (request.headers.get("upgrade")?.toLowerCase() !== "websocket") {
      return new Response("WebSocket upgrade required", { status: 426 });
    }
    const terminalSerial = url.searchParams.get("terminal");
    if (!terminalSerial) return new Response("Missing terminal", { status: 400 });

    const pair = new WebSocketPair();
    this.ctx.acceptWebSocket(pair[1], [terminalSerial]);
    return new Response(null, { status: 101, webSocket: pair[0] });
  }

  webSocketMessage(): void {
    // The relay is a one-way push (server -> client). Client messages are not part of the
    // protocol; there is no server-side state left for an acknowledgment to reconcile against.
  }

  webSocketClose(ws: WebSocket, code: number, reason: string, wasClean: boolean): void {
    // Nothing to release: a socket carries no server-side state beyond its terminal tag.
    void ws;
    void code;
    void reason;
    void wasClean;
  }

  private publish(display: DisplayState): void {
    const envelope: OutboundEnvelope = {
      protocol: 2,
      message: {
        id: `payment:${display.pspReference}`,
        type: "payment_succeeded",
        occurredAt: display.occurredAt,
        terminalId: display.terminalId,
        transactionId: display.transactionId,
        pspReference: display.pspReference,
      },
    };
    const payload = JSON.stringify(envelope);
    for (const socket of this.ctx.getWebSockets(display.terminalSerial)) {
      try {
        socket.send(payload);
      } catch {
        // A socket that fails to send is already closing/closed; its own close event cleans it up.
      }
    }
  }

  private logError(event: string, error: unknown): void {
    console.error(
      JSON.stringify({
        level: "error",
        event,
        error: error instanceof Error ? error.message : "Unknown error",
      }),
    );
  }
}
```

Notes for the implementer:
- No constructor override, no `ctx.storage` use anywhere — the object holds no durable state at
  all. `wrangler.jsonc`'s existing `migrations` entry (`new_sqlite_classes: ["RelayObject"]`) is left
  as-is (Task A9); a class simply not using storage is valid and requires no migration change.
- `ctx.getWebSockets(tag)` and `ctx.acceptWebSocket(socket, tags)` are Cloudflare's hibernatable-
  WebSocket tag APIs — confirm the exact signature against the currently-installed
  `@cloudflare/workers-types`/`wrangler types` output before relying on it from memory (per
  `AGENTS.md`'s "check current official documentation" rule); this is the same hibernation API the
  previous implementation already used via `this.ctx.getWebSockets()` (no-arg), just with a tag
  filter added.
- `MAX_OUTBOUND_MESSAGES`, `RETENTION_MS`, `CORRELATION_WAIT_MS`, `alarm()`, and every SQL table are
  gone — there is no scheduled work left for this object to do.

- [ ] **Step 1: Run the type checker**

Run: `cd worker && npm run typecheck`
Expected: still FAILS on `worker/src/index.ts` (Task A5 fixes it). `relay-object.ts` itself should
type-check clean in isolation — confirm with `npx tsc --noEmit -p worker/tsconfig.json` mentally
matching, but the full failing run is expected until A5 lands. Don't commit yet — commit at the end
of A5 once the whole `worker/` package type-checks and `worker/test/worker.test.ts` (Task A6) is
rewritten and passing, since A4/A5/A6 are one coherent, mutually-dependent change. Proceed to A5.

### Task A5: Rewrite the HTTP entrypoint for company/terminal routing

**Files:**
- Modify: `worker/src/index.ts` (full rewrite)

**Interfaces:**
- Produces: `GET /health`, `POST /v1/c/<companyToken>`, `GET /v1/c/<companyToken>/t/<terminalSerial>/ws`.
  Removes the old `/v1/i/<token>` and `/v1/i/<token>/ws` routes entirely.

Replace the entire file:

```typescript
import { RelayObject } from "./relay-object";

export { RelayObject };

export const MAX_BODY_BYTES = 64 * 1024;
const COMPANY_TOKEN_PATTERN = /^[A-Za-z0-9_-]{43}$/;
const TERMINAL_SERIAL_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;
const JSON_HEADERS = { "content-type": "application/json; charset=utf-8" };

function json(value: unknown, status = 200, headers?: Record<string, string>): Response {
  return new Response(JSON.stringify(value), { status, headers: { ...JSON_HEADERS, ...headers } });
}

function validCompanyToken(token: string): boolean {
  if (!COMPANY_TOKEN_PATTERN.test(token)) return false;
  try {
    const binary = atob(token.replaceAll("-", "+").replaceAll("_", "/") + "=");
    return (
      binary.length === 32 &&
      btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "") === token
    );
  } catch {
    return false;
  }
}

function validTerminalSerial(serial: string): boolean {
  return TERMINAL_SERIAL_PATTERN.test(serial);
}

function base64Url(buffer: ArrayBuffer): string {
  let binary = "";
  for (const byte of new Uint8Array(buffer)) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
}

async function digest(value: string): Promise<string> {
  return base64Url(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
}

export async function companyTokenToObjectName(token: string): Promise<string> {
  return digest(token);
}

async function readLimitedBody(request: Request): Promise<string | null> {
  const length = Number(request.headers.get("content-length"));
  if (Number.isFinite(length) && length > MAX_BODY_BYTES) return null;
  if (!request.body) return "";
  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > MAX_BODY_BYTES) {
      await reader.cancel();
      return null;
    }
    chunks.push(value);
  }
  const joined = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) {
    joined.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return new TextDecoder().decode(joined);
}

async function relay(env: Env, companyToken: string): Promise<DurableObjectStub> {
  const name = await companyTokenToObjectName(companyToken);
  return env.PAYMENT_CHANNELS.get(env.PAYMENT_CHANNELS.idFromName(name));
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/health" && request.method === "GET") return json({ status: "ok" });

    const ingestMatch = /^\/v1\/c\/([^/]+)$/.exec(url.pathname);
    const socketMatch = /^\/v1\/c\/([^/]+)\/t\/([^/]+)\/ws$/.exec(url.pathname);
    if (ingestMatch) {
      const companyToken = ingestMatch[1] ?? "";
      if (!validCompanyToken(companyToken)) return json({ error: "Not found" }, 404);

      if (request.method !== "POST") return json({ error: "Method not allowed" }, 405, { allow: "POST" });
      if (!(request.headers.get("content-type") ?? "").toLowerCase().startsWith("application/json")) {
        return json({ error: "JSON required" }, 415);
      }
      const body = await readLimitedBody(request);
      if (body === null) return json({ error: "Payload too large" }, 413);
      try {
        JSON.parse(body);
      } catch {
        return json({ error: "Invalid JSON" }, 400);
      }
      return (await relay(env, companyToken)).fetch("https://relay.internal/ingest", {
        method: "POST",
        body,
      });
    }

    if (socketMatch) {
      const companyToken = socketMatch[1] ?? "";
      const terminalSerial = socketMatch[2] ?? "";
      if (!validCompanyToken(companyToken) || !validTerminalSerial(terminalSerial)) {
        return json({ error: "Not found" }, 404);
      }

      if (request.method !== "GET") return json({ error: "Method not allowed" }, 405, { allow: "GET" });
      if (request.headers.get("upgrade")?.toLowerCase() !== "websocket") {
        return json({ error: "WebSocket upgrade required" }, 426, { upgrade: "websocket" });
      }
      const target = `https://relay.internal/ws?terminal=${encodeURIComponent(terminalSerial)}`;
      return (await relay(env, companyToken)).fetch(new Request(target, request));
    }

    return json({ error: "Not found" }, 404);
  },
} satisfies ExportedHandler<Env>;
```

Notes:
- `readLimitedBody`/`base64Url`/`digest` are unchanged in behavior from today, just kept because
  they're still needed (body cap, token hashing).
- The dedupe-hash computation (`stableIngressFields`/`canonicalJson`/`x-dedupe-key` header) is gone
  entirely — there's nothing left to dedupe against.
- A malformed company token or terminal serial both fail closed as a generic `404`, matching the
  existing "don't let the response distinguish malformed from unknown" policy from
  `docs/threat-model.md`.

- [ ] **Step 1: Run the full Worker type/lint check**

Run: `cd worker && npm run typecheck && npm run lint`
Expected: PASS (once Task A6's test file is also updated — lint/typecheck don't depend on tests,
so this should pass now even before A6).

- [ ] **Step 2: Proceed to Task A6** before committing — `worker.test.ts` currently references the
  deleted `/v1/i/` routes and won't compile/pass yet.

### Task A6: Rewrite the Worker integration test suite

**Files:**
- Modify: `worker/test/worker.test.ts` (full rewrite)

Read the existing file first for its current `@cloudflare/vitest-plugin` setup conventions
(`runInDurableObject`, `SELF.fetch`, `env` import from `cloudflare:test`, how it constructs a
`Request`) and match that style. Replace the correlation/persistence/replay-focused test suite with
one that proves the new stateless, tag-routed behavior. Write (at minimum) these cases as real,
runnable tests — not a placeholder list:

```typescript
import { describe, it, expect, vi } from "vitest";
import { SELF, env, runInDurableObject } from "cloudflare:test";
import displayApproved from "./fixtures/display-tender-final-approved.json";
import displayDeclined from "./fixtures/display-tender-final-declined.json";

const COMPANY_TOKEN = "A".repeat(43);
const OTHER_COMPANY_TOKEN = "B".repeat(43);

function ingestUrl(companyToken: string) {
  return `https://relay.test/v1/c/${companyToken}`;
}

function socketUrl(companyToken: string, terminalSerial: string) {
  return `https://relay.test/v1/c/${companyToken}/t/${terminalSerial}/ws`;
}

async function connect(companyToken: string, terminalSerial: string) {
  const response = await SELF.fetch(socketUrl(companyToken, terminalSerial), {
    headers: { upgrade: "websocket" },
  });
  expect(response.status).toBe(101);
  const socket = response.webSocket;
  if (!socket) throw new Error("Expected a WebSocket in the response");
  socket.accept();
  return socket;
}

function received(socket: WebSocket): Promise<string> {
  return new Promise((resolve) => {
    socket.addEventListener("message", (event) => resolve(event.data as string), { once: true });
  });
}

describe("health", () => {
  it("responds ok without a token", async () => {
    const response = await SELF.fetch("https://relay.test/health");
    expect(await response.json()).toEqual({ status: "ok" });
  });
});

describe("ingest routing", () => {
  it("rejects a malformed company token with 404", async () => {
    const response = await SELF.fetch(ingestUrl("too-short"), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(displayApproved),
    });
    expect(response.status).toBe(404);
  });

  it("rejects a non-JSON content type with 415", async () => {
    const response = await SELF.fetch(ingestUrl(COMPANY_TOKEN), {
      method: "POST",
      headers: { "content-type": "text/plain" },
      body: "not json",
    });
    expect(response.status).toBe(415);
  });

  it("rejects malformed JSON with 400", async () => {
    const response = await SELF.fetch(ingestUrl(COMPANY_TOKEN), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: "{not valid",
    });
    expect(response.status).toBe(400);
  });

  it("rejects a body over 64 KiB with 413", async () => {
    const response = await SELF.fetch(ingestUrl(COMPANY_TOKEN), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ padding: "x".repeat(70 * 1024) }),
    });
    expect(response.status).toBe(413);
  });

  it("accepts a well-formed request with 202 even with no connected socket", async () => {
    const response = await SELF.fetch(ingestUrl(COMPANY_TOKEN), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(displayApproved),
    });
    expect(response.status).toBe(202);
  });
});

describe("WebSocket relay", () => {
  it("delivers a generic payment_succeeded message only to the matching terminal", async () => {
    const matching = await connect(COMPANY_TOKEN, "324688170");
    const other = await connect(COMPANY_TOKEN, "someone-elses-terminal");
    const otherMessages: string[] = [];
    other.addEventListener("message", (event) => otherMessages.push(event.data as string));

    const nextMessage = received(matching);
    await SELF.fetch(ingestUrl(COMPANY_TOKEN), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(displayApproved),
    });

    const payload = JSON.parse(await nextMessage);
    expect(payload).toEqual({
      protocol: 2,
      message: {
        id: "payment:NC6HT9CRT65ZGN82",
        type: "payment_succeeded",
        occurredAt: "2026-09-18T12:00:00.000Z",
        terminalId: "V400m-324688170",
        transactionId: "CWf3001626182307000.NC6HT9CRT65ZGN82",
        pspReference: "NC6HT9CRT65ZGN82",
      },
    });
    expect(otherMessages).toEqual([]);
  });

  it("never delivers a declined Display notification", async () => {
    const socket = await connect(COMPANY_TOKEN, "324688170");
    const messages: string[] = [];
    socket.addEventListener("message", (event) => messages.push(event.data as string));

    await SELF.fetch(ingestUrl(COMPANY_TOKEN), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(displayDeclined),
    });
    // Give any (incorrect) async delivery a chance to happen before asserting silence.
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(messages).toEqual([]);
  });

  it("isolates two different companies even with the same terminal serial", async () => {
    const companyA = await connect(COMPANY_TOKEN, "324688170");
    const companyB = await connect(OTHER_COMPANY_TOKEN, "324688170");
    const companyBMessages: string[] = [];
    companyB.addEventListener("message", (event) => companyBMessages.push(event.data as string));

    const nextMessage = received(companyA);
    await SELF.fetch(ingestUrl(COMPANY_TOKEN), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(displayApproved),
    });
    await nextMessage;
    expect(companyBMessages).toEqual([]);
  });

  it("does not queue a message for a terminal that connects after ingest", async () => {
    await SELF.fetch(ingestUrl(COMPANY_TOKEN), {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(displayApproved),
    });
    const lateSocket = await connect(COMPANY_TOKEN, "324688170");
    const messages: string[] = [];
    lateSocket.addEventListener("message", (event) => messages.push(event.data as string));
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(messages).toEqual([]);
  });

  it("rejects a socket upgrade with a malformed terminal serial", async () => {
    const response = await SELF.fetch(socketUrl(COMPANY_TOKEN, "has a space"), {
      headers: { upgrade: "websocket" },
    });
    expect(response.status).toBe(404);
  });

  it("ignores an arbitrary client message instead of erroring or closing", async () => {
    const socket = await connect(COMPANY_TOKEN, "324688170");
    let closed = false;
    socket.addEventListener("close", () => {
      closed = true;
    });
    socket.send("not an ack, not anything");
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(closed).toBe(false);
  });
});

describe("sensitive-data logging", () => {
  it("never writes the company token to any console output", async () => {
    const spies = ["log", "info", "warn", "error", "debug"].map((method) =>
      vi.spyOn(console, method as "log"),
    );
    try {
      await SELF.fetch(ingestUrl(COMPANY_TOKEN), {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: "{not valid",
      });
      await SELF.fetch(ingestUrl(COMPANY_TOKEN), {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(displayApproved),
      });
      for (const spy of spies) {
        for (const call of spy.mock.calls) {
          expect(JSON.stringify(call)).not.toContain(COMPANY_TOKEN);
        }
      }
    } finally {
      for (const spy of spies) spy.mockRestore();
    }
  });
});
```

Adapt exact helper names/imports (`runInDurableObject`, `env`, request-construction idioms) to
whatever the existing file already uses — the scenarios above are the required coverage, not a
literal drop-in replacement if the existing test harness conventions differ.

- [ ] **Step 1: Run the suite**

Run: `cd worker && npx vitest run test/worker.test.ts`
Expected: PASS. Iterate on the exact `@cloudflare/vitest-plugin` API usage until green — the
scenarios above are the contract; the harness plumbing may need small adjustments to match this
project's existing vitest setup (`worker/vitest.config.ts`).

- [ ] **Step 2: Run the full Worker quality suite**

Run: `cd worker && npm run quality`
Expected: PASS — format check, lint, typecheck, `wrangler types --check`, architecture
(`dependency-cruiser`), dead-code (`knip`), and coverage thresholds all green. If `knip` flags a
newly-unused export, remove it; if `wrangler types --check` fails, run `npm run types:generate` and
commit the regenerated `worker-configuration.d.ts`.

- [ ] **Step 3: Commit** (this closes out Tasks A2–A6 together, since they only type-check and test
  as one unit)

```bash
git add worker/src worker/test
git commit -m "feat(worker): stateless display-only relay with company/terminal tag routing"
```

### Task A7: Confirm the Worker coverage thresholds still make sense

**Files:**
- Modify (only if needed): `worker/vitest.config.ts`

`relay-object.ts` shrank drastically (no SQL, no alarm) — its `80%/75%/80%` threshold from
`docs/testing.md` was calibrated against the old, much larger file.

- [ ] **Step 1: Run coverage and inspect the per-file report**

Run: `cd worker && npm run test:coverage`

- [ ] **Step 2: Decide**

If `relay-object.ts` and `src/adyen/**` clear the existing thresholds as-is (likely, given the new
file is small and every branch is exercised by Task A6), leave `vitest.config.ts` unchanged. If a
threshold is now unreachable because of a genuinely untestable line (there shouldn't be one — every
branch above is covered by Task A6), do not lower the threshold to paper over a gap; add the missing
test instead. Only touch `vitest.config.ts` if the *file list* changed (e.g., an `identity.ts`-
specific override needs removing since that file no longer exists) — check for one and remove it if
present.

- [ ] **Step 3: Commit if changed**

```bash
git add worker/vitest.config.ts
git commit -m "chore(worker): drop coverage config for deleted identity.ts"
```
(Skip this commit entirely if no change was needed.)

### Task A8: Regenerate Worker binding types and verify the architecture/dead-code gates

**Files:**
- Modify (generated): `worker/worker-configuration.d.ts` (only if `wrangler.jsonc` changed — it
  hasn't in this plan, so this is a verification step, not an edit)

- [ ] **Step 1: Verify architecture boundaries still hold**

Run: `cd worker && npm run architecture`
Expected: PASS — `relay-object.ts` still only imports `./adyen/display-parser` and `./adyen/models`
(no Cloudflare-infrastructure import from a domain module), `index.ts` still doesn't import `test/`.

- [ ] **Step 2: Verify no stale binding types**

Run: `cd worker && npm run types:check`
Expected: PASS (no `wrangler.jsonc` binding changes in this plan).

- [ ] **Step 3: Nothing to commit** — this task is verification only. If either check fails,
  fix the underlying issue (don't suppress) and fold the fix into Task A6's commit if not already
  pushed, or a new small commit if it was.

### Task A9: Add a company-token generator helper for Worker admins

**Files:**
- Create: `scripts/generate-company-token.sh`

There's no server-side signup step (by design — see the new ADR in Task F1), so whoever deploys the
Worker for a company needs a simple, offline way to mint a valid 43-character token. Add a tiny
script rather than asking them to hand-construct one.

- [ ] **Step 1: Create the script**

```bash
#!/usr/bin/env bash
# Prints a fresh 256-bit company token (43-char base64url) suitable for a relay URL:
# https://<your-worker-host>/v1/c/<token>
# There's no registration step — any well-formed token routes to its own isolated Durable Object
# on first use. Keep the resulting URL secret; see docs/threat-model.md.
set -euo pipefail
openssl rand -base64 32 | tr '+/' '-_' | tr -d '=\n'
echo
```

- [ ] **Step 2: Make it executable and verify it produces a valid token**

```bash
chmod +x scripts/generate-company-token.sh
scripts/generate-company-token.sh | { read -r token; [ "${#token}" -eq 43 ]; } && echo "length OK"
```

- [ ] **Step 3: Commit**

```bash
git add scripts/generate-company-token.sh
git commit -m "feat: add a company-token generator script for Worker admins"
```

---

## Part B — App: drop per-instance identity, add company URL + terminal serial configuration

### Task B1: Replace the instance-identity model with relay configuration

**Files:**
- Delete: `app/AdyenOutLoud.Core/Models/InstanceIdentity.cs`
- Delete: `app/AdyenOutLoud.Core/Abstractions/IInstanceIdentityService.cs`
- Delete: `app/AdyenOutLoud.Core/Abstractions/IInstanceTokenStore.cs`
- Delete: `app/AdyenOutLoud.Core/Services/DeviceIdentityGenerator.cs`
- Delete: `app/AdyenOutLoud.Core/Services/InstanceIdentityService.cs`
- Create: `app/AdyenOutLoud.Core/Models/RelayConfiguration.cs`
- Create: `app/AdyenOutLoud.Core/Abstractions/IRelayConfigurationStore.cs`
- Create: `app/AdyenOutLoud.Core/Abstractions/IRelayConfigurationService.cs`
- Create: `app/AdyenOutLoud.Core/Services/RelayConfigurationService.cs`
- Modify: `app/AdyenOutLoud.Core/Services/RelayEndpointFactory.cs` (rewrite)
- Test: `app/AdyenOutLoud.Tests/RelayConfigurationServiceTests.cs` (new, replaces
  `InstanceIdentityServiceTests.cs`)
- Test: `app/AdyenOutLoud.Tests/ProductContractTests.cs` (remove `InstanceTokenUsesExactly32...`,
  rewrite the `RelayEndpointFactory` tests)
- Delete: `app/AdyenOutLoud.Tests/InstanceIdentityServiceTests.cs`

**Interfaces:**
- Produces: `RelayConfiguration(Uri BaseUrl, string TerminalSerial, Uri WebSocketUrl)`;
  `IRelayConfigurationService.GetAsync(CancellationToken): Task<RelayConfiguration?>` (null means
  "not configured yet — first launch or cleared"); `IRelayConfigurationService.SaveAsync(Uri
  baseUrl, string terminalSerial, CancellationToken): Task<RelayConfiguration>`. Consumed by Task
  B4 (`RelayConnectionService`) and Task B7 (`MainViewModel`).

- [ ] **Step 1: Delete the old identity files**

```bash
git rm app/AdyenOutLoud.Core/Models/InstanceIdentity.cs \
       app/AdyenOutLoud.Core/Abstractions/IInstanceIdentityService.cs \
       app/AdyenOutLoud.Core/Abstractions/IInstanceTokenStore.cs \
       app/AdyenOutLoud.Core/Services/DeviceIdentityGenerator.cs \
       app/AdyenOutLoud.Core/Services/InstanceIdentityService.cs \
       app/AdyenOutLoud.Tests/InstanceIdentityServiceTests.cs
```

- [ ] **Step 2: Write the failing test for `RelayEndpointFactory`**

Replace the `RelayEndpointFactory`-related tests in `app/AdyenOutLoud.Tests/ProductContractTests.cs`
(remove `HttpsRelayBaseBuildsExactWebhookUrl`, `HttpsRelayBaseBuildsExactSecureWebSocketUrl`,
`RelayBaseRejectsAnythingExceptAnHttpsOrigin`, `InstanceTokenUsesExactly32RandomBytesEncodedAsBase64Url`)
with:

```csharp
[Fact]
public void CompanyUrlWithTerminalSerialBuildsExactSecureWebSocketUrl()
{
    var url = RelayEndpointFactory.CreateWebSocketUrl(
        new("https://relay.example.com/v1/c/abc_DEF-123"), "324688170");
    Assert.Equal("wss://relay.example.com/v1/c/abc_DEF-123/t/324688170/ws", url.AbsoluteUri);
}

[Fact]
public void CompanyUrlWithNonDefaultPortIsPreserved()
{
    var url = RelayEndpointFactory.CreateWebSocketUrl(
        new("https://relay.example.com:8443/v1/c/token"), "324688170");
    Assert.Equal("wss://relay.example.com:8443/v1/c/token/t/324688170/ws", url.AbsoluteUri);
}

[Fact]
public void TerminalSerialIsUrlEscaped()
{
    var url = RelayEndpointFactory.CreateWebSocketUrl(
        new("https://relay.example.com/v1/c/token"), "has space");
    Assert.Equal("wss://relay.example.com/v1/c/token/t/has%20space/ws", url.AbsoluteUri);
}

[Theory]
[InlineData("http://relay.example.com/v1/c/token")]
[InlineData("wss://relay.example.com/v1/c/token")]
[InlineData("https://relay.example.com/v1/c/token?x=1")]
public void CompanyUrlRejectsAnythingExceptAnHttpsAddress(string value) =>
    Assert.Throws<ArgumentException>(() => RelayEndpointFactory.CreateWebSocketUrl(new(value), "324688170"));

[Fact]
public void EmptyTerminalSerialIsRejected() =>
    Assert.Throws<ArgumentException>(() => RelayEndpointFactory.CreateWebSocketUrl(new("https://relay.example.com/v1/c/token"), ""));
```

Also update the `CompleteEnvelope` constant and every test using `paymentMethod`/`amount` in this
file — that's Task B3; do it together since the file is shared, but keep the two logical changes
clear in the diff (identity tests in this step, protocol tests in Task B3's step).

- [ ] **Step 3: Run to verify failure**

Run: `cd app && dotnet build AdyenOutLoud.Core/AdyenOutLoud.Core.csproj`
Expected: FAIL — `RelayEndpointFactory.CreateWebSocketUrl` doesn't exist yet.

- [ ] **Step 4: Create `RelayConfiguration.cs`**

```csharp
namespace AdyenOutLoud.Models;

public sealed record RelayConfiguration(Uri BaseUrl, string TerminalSerial, Uri WebSocketUrl);
```

- [ ] **Step 5: Create `IRelayConfigurationStore.cs`**

```csharp
namespace AdyenOutLoud.Abstractions;

public interface IRelayConfigurationStore
{
    Task<(Uri BaseUrl, string TerminalSerial)?> GetAsync();
    Task SetAsync(Uri baseUrl, string terminalSerial);
}
```

- [ ] **Step 6: Create `IRelayConfigurationService.cs`**

```csharp
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Abstractions;

public interface IRelayConfigurationService
{
    Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default);
    Task<RelayConfiguration> SaveAsync(Uri baseUrl, string terminalSerial, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 7: Rewrite `RelayEndpointFactory.cs`**

```csharp
namespace AdyenOutLoud.Services;

public static class RelayEndpointFactory
{
    public static Uri CreateWebSocketUrl(Uri companyUrl, string terminalSerial)
    {
        ArgumentNullException.ThrowIfNull(companyUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalSerial);
        if (!companyUrl.IsAbsoluteUri || companyUrl.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(companyUrl.Query) || !string.IsNullOrEmpty(companyUrl.Fragment))
        {
            throw new ArgumentException("The relay URL must be an HTTPS address with no query or fragment.", nameof(companyUrl));
        }

        var escapedSerial = Uri.EscapeDataString(terminalSerial);
        var builder = new UriBuilder(companyUrl)
        {
            Scheme = "wss",
            Port = companyUrl.IsDefaultPort ? -1 : companyUrl.Port,
            Path = $"{companyUrl.AbsolutePath.TrimEnd('/')}/t/{escapedSerial}/ws",
        };
        return builder.Uri;
    }
}
```

- [ ] **Step 8: Create `RelayConfigurationService.cs`**

```csharp
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public sealed class RelayConfigurationService(IRelayConfigurationStore store) : IRelayConfigurationService
{
    public async Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default)
    {
        var saved = await store.GetAsync().ConfigureAwait(false);
        return saved is { } value ? Build(value.BaseUrl, value.TerminalSerial) : null;
    }

    public async Task<RelayConfiguration> SaveAsync(Uri baseUrl, string terminalSerial, CancellationToken cancellationToken = default)
    {
        var configuration = Build(baseUrl, terminalSerial);
        await store.SetAsync(baseUrl, terminalSerial).ConfigureAwait(false);
        return configuration;
    }

    private static RelayConfiguration Build(Uri baseUrl, string terminalSerial) =>
        new(baseUrl, terminalSerial, RelayEndpointFactory.CreateWebSocketUrl(baseUrl, terminalSerial));
}
```

- [ ] **Step 9: Write `RelayConfigurationServiceTests.cs`**

```csharp
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Services;

namespace AdyenOutLoud.Tests;

public sealed class RelayConfigurationServiceTests
{
    [Fact]
    public async Task GetAsyncReturnsNullWhenNothingIsStoredYet()
    {
        var service = new RelayConfigurationService(new Store());
        Assert.Null(await service.GetAsync());
    }

    [Fact]
    public async Task GetAsyncBuildsTheWebSocketUrlFromStoredValues()
    {
        var store = new Store { Saved = (new("https://relay.example.com/v1/c/token"), "324688170") };
        var service = new RelayConfigurationService(store);

        var configuration = await service.GetAsync();

        Assert.NotNull(configuration);
        Assert.Equal("wss://relay.example.com/v1/c/token/t/324688170/ws", configuration!.WebSocketUrl.AbsoluteUri);
    }

    [Fact]
    public async Task SaveAsyncPersistsAndReturnsTheNewConfiguration()
    {
        var store = new Store();
        var service = new RelayConfigurationService(store);

        var configuration = await service.SaveAsync(new("https://relay.example.com/v1/c/token"), "324688170");

        Assert.Equal(("https://relay.example.com/v1/c/token", "324688170"), (store.Saved!.Value.BaseUrl.AbsoluteUri, store.Saved.Value.TerminalSerial));
        Assert.Equal("wss://relay.example.com/v1/c/token/t/324688170/ws", configuration.WebSocketUrl.AbsoluteUri);
    }

    private sealed class Store : IRelayConfigurationStore
    {
        public (Uri BaseUrl, string TerminalSerial)? Saved { get; set; }
        public Task<(Uri BaseUrl, string TerminalSerial)?> GetAsync() => Task.FromResult(Saved);
        public Task SetAsync(Uri baseUrl, string terminalSerial) { Saved = (baseUrl, terminalSerial); return Task.CompletedTask; }
    }
}
```

- [ ] **Step 10: Run the new tests**

Run: `cd app && dotnet test AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj --filter "FullyQualifiedName~RelayConfigurationServiceTests|FullyQualifiedName~ProductContractTests"`
Expected: FAIL still (other files not yet updated to match — see Tasks B2–B3), or PASS for this
narrow filter if `ProductContractTests.cs`'s protocol-1 tests are temporarily left alone (they'll be
touched in Task B3). If this filter alone doesn't compile because the whole test project must build
together, proceed through Tasks B2 and B3 before running tests, then come back and run the full
suite once at the end of B3.

- [ ] **Step 11: Commit** (hold the commit until Task B3 also lands, since the test project won't
  build standalone until `RelayProtocol`/`PaymentMessage` are updated too — see B3's final step for
  the actual commit point covering B1–B3 together).

### Task B2: Simplify `PaymentMessage` and `AnnouncementResult` (drop payment method/amount, drop ack)

**Files:**
- Modify: `app/AdyenOutLoud.Core/Models/PaymentMessage.cs`

**Interfaces:**
- Produces: `PaymentMessage(string Id, string Type, DateTimeOffset OccurredAt, string TerminalId,
  string TransactionId, string PspReference)` (no `PaymentMethod`, no `Amount`; `PaymentAmount`
  record deleted). `AnnouncementResult(string EventId, bool WasDuplicate, bool WasSpoken, string
  Detail, PaymentMessage Message, SpeechDiagnostic? Speech)` (drops `ShouldAcknowledge` — there is no
  ack anymore, see Task B3).

- [ ] **Step 1: Edit the file**

```csharp
namespace AdyenOutLoud.Models;

public sealed record PaymentMessage(
    string Id,
    string Type,
    DateTimeOffset OccurredAt,
    string TerminalId,
    string TransactionId,
    string PspReference);

public sealed record SpeechDiagnostic(string RequestedLocale, string? SelectedLocale, string Message);

public sealed record AnnouncementResult(
    string EventId,
    bool WasDuplicate,
    bool WasSpoken,
    string Detail,
    PaymentMessage Message,
    SpeechDiagnostic? Speech);

public enum RelayConnectionState
{
    Connecting,
    Listening,
    NeedsAttention
}

public sealed record RelayStatus(RelayConnectionState State, string Detail);
```

This will not build in isolation yet (every consumer still references `PaymentMethod`/`Amount`/
`ShouldAcknowledge`) — proceed directly to Tasks B3–B6 before attempting a build.

### Task B3: Simplify `RelayProtocol` — protocol 2, no ack, no payment method/amount

**Files:**
- Modify: `app/AdyenOutLoud.Core/Services/RelayProtocol.cs`
- Modify: `app/AdyenOutLoud.Core/Abstractions/IRelayConnection.cs` (drop `SendAsync`)
- Modify: `app/AdyenOutLoud/Services/ClientWebSocketConnection.cs` (drop `SendAsync`)
- Modify: `app/AdyenOutLoud.Tests/ProductContractTests.cs` (finish the rewrite started in B1)

**Interfaces:**
- Produces: `RelayProtocol.TryParsePayment(string json, out PaymentMessage? payment)` accepting
  only `protocol: 2` envelopes with no `paymentMethod`/`amount` fields. Removes:
  `RelayProtocol.CreateAck`, `IRelayConnection.SendAsync`.

- [ ] **Step 1: Rewrite `RelayProtocol.cs`**

```csharp
using System.Text.Json;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public static class RelayProtocol
{
    public static bool TryParsePayment(string json, out PaymentMessage? payment)
    {
        payment = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("protocol", out var protocol) || protocol.ValueKind != JsonValueKind.Number ||
                !protocol.TryGetInt32(out var protocolVersion) || protocolVersion != 2 ||
                !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
                !TryString(message, "id", out var id) ||
                !TryString(message, "type", out var type) ||
                !type.Equals("payment_succeeded", StringComparison.Ordinal) ||
                !TryDate(message, "occurredAt", out var occurredAt) ||
                !TryString(message, "terminalId", out var terminalId) ||
                !TryString(message, "transactionId", out var transactionId) ||
                !TryString(message, "pspReference", out var pspReference))
            {
                return false;
            }

            payment = new PaymentMessage(id, type, occurredAt, terminalId, transactionId, pspReference);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryDate(JsonElement element, string name, out DateTimeOffset value)
    {
        value = default;
        return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
               property.TryGetDateTimeOffset(out value);
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
```

- [ ] **Step 2: Drop `SendAsync` from `IRelayConnection`**

```csharp
namespace AdyenOutLoud.Abstractions;

public interface IRelayConnection : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
    Task<string?> ReceiveAsync(CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
}

public interface IRelayConnectionFactory
{
    IRelayConnection Create();
}
```

- [ ] **Step 3: Drop `SendAsync` from `ClientWebSocketConnection`**

Remove the `SendAsync` method body from `app/AdyenOutLoud/Services/ClientWebSocketConnection.cs`
(the `Encoding`/`WebSocketMessageType.Text` send block) — keep `ConnectAsync`, `ReceiveAsync`,
`CloseAsync`, `DisposeAsync` as-is.

- [ ] **Step 4: Finish rewriting `ProductContractTests.cs`**

Replace the `CompleteEnvelope` constant and every protocol test with:

```csharp
private const string CompleteEnvelope = """
    {"protocol":2,"message":{"id":"event-1","type":"payment_succeeded","occurredAt":"2026-09-18T12:00:00Z","terminalId":"P400Plus-123","transactionId":"txn-1","pspReference":"PSP-1"}}
    """;

[Fact]
public void ProtocolTwoPaymentSucceededEnvelopeParsesEveryField()
{
    Assert.True(RelayProtocol.TryParsePayment(CompleteEnvelope, out var message));
    Assert.NotNull(message);
    Assert.Equal("event-1", message.Id);
    Assert.Equal("payment_succeeded", message.Type);
    Assert.Equal("P400Plus-123", message.TerminalId);
    Assert.Equal("txn-1", message.TransactionId);
    Assert.Equal("PSP-1", message.PspReference);
}

[Fact]
public void UnsupportedProtocolVersionIsRejected()
{
    Assert.False(RelayProtocol.TryParsePayment(CompleteEnvelope.Replace("\"protocol\":2", "\"protocol\":1", StringComparison.Ordinal), out _));
}
```

Keep `AnyMessageTypeOtherThanPaymentSucceededIsRejected` and
`MalformedOrIncompleteEnvelopeIsRejectedWithoutThrowing` as-is except adjusting the embedded JSON to
`protocol:2`/the new field set. In `RejectedMutations()`, delete every `paymentMethod`/`amount`
entry (`"paymentMethod key missing entirely"` through `"amount valueMinor is negative"`) — those
fields no longer exist. Delete `AckContainsExactlyTypeAndId` entirely (no more `CreateAck`). Delete
`NullPaymentMethodIsAccepted` and `NullAmountIsAccepted`.

- [ ] **Step 5: Run the Core + Tests build**

Run: `cd app && dotnet build AdyenOutLoud.Core/AdyenOutLoud.Core.csproj && dotnet test AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj --filter "FullyQualifiedName~ProductContractTests|FullyQualifiedName~RelayConfigurationServiceTests"`
Expected: still FAILS to build the full test project (Tasks B4–B6 haven't updated their consumers
yet) — that's fine; keep going without committing.

### Task B4: Update `RelayConnectionService` — configuration instead of identity, no ack send

**Files:**
- Modify: `app/AdyenOutLoud.Core/Services/RelayConnectionService.cs`
- Modify: `app/AdyenOutLoud.Tests/RelayConnectionContractTests.cs`

**Interfaces:**
- Consumes: `IRelayConfigurationService` (Task B1) instead of `IInstanceIdentityService`.

- [ ] **Step 1: Edit `RelayConnectionService.cs`**

Change the primary constructor parameter and every reference:

```csharp
public sealed class RelayConnectionService(
    IRelayConfigurationService configurationService,
    IRelayConnectionFactory connectionFactory,
    IPaymentAnnouncementService announcementService,
    IRetryDelay retryDelay) : IRelayConnectionService, IAsyncDisposable
```

Replace the identity-fetch block in `RunAsync` with:

```csharp
RelayConfiguration configuration;
try
{
    var found = await configurationService.GetAsync(cancellationToken).ConfigureAwait(false);
    if (found is null)
    {
        SetStatus(RelayConnectionState.NeedsAttention, "Enter the relay URL and terminal serial number to start listening.");
        return;
    }
    configuration = found;
}
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
catch (Exception exception)
{
    SetStatus(RelayConnectionState.NeedsAttention, "Could not read the saved relay configuration.");
    Diagnostic?.Invoke(this, exception.Message);
    return;
}
```

Replace `await connection.ConnectAsync(identity.WebSocketUrl, cancellationToken)` with
`await connection.ConnectAsync(configuration.WebSocketUrl, cancellationToken)`.

Remove the ack-send block entirely — after `AnnounceAsync`, there is nothing to send back:

```csharp
var result = await announcementService.AnnounceAsync(payment, cancellationToken).ConfigureAwait(false);
Diagnostic?.Invoke(this, result.Detail);
```

(Delete the `if (result.ShouldAcknowledge) { await connection.SendAsync(...) }` block that followed
it.)

- [ ] **Step 2: Update `RelayConnectionContractTests.cs`**

Rename the test double `Identity : IInstanceIdentityService` to `Configuration :
IRelayConfigurationService` returning a `RelayConfiguration`:

```csharp
private sealed class Configuration : IRelayConfigurationService
{
    public Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult<RelayConfiguration?>(
        new(new("https://relay.example.com/v1/c/token"), "324688170", new("wss://relay.example.com/v1/c/token/t/324688170/ws")));
    public Task<RelayConfiguration> SaveAsync(Uri baseUrl, string terminalSerial, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by these tests.");
}

private sealed class FailingConfiguration : IRelayConfigurationService
{
    public Task<RelayConfiguration?> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromException<RelayConfiguration?>(new InvalidOperationException("configuration store unavailable"));
    public Task<RelayConfiguration> SaveAsync(Uri baseUrl, string terminalSerial, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by these tests.");
}
```

Update every `new RelayConnectionService(new Identity(), ...)` /
`new RelayConnectionService(new FailingIdentity(), ...)` call site to use `new Configuration()` /
`new FailingConfiguration()`. In `ConnectionUsesDerivedWebSocketAndSendsNoHelloBeforeAck`, since
there is no ack to wait for anymore, replace the `AckSent` wait with a different completion signal —
add a `TaskCompletionSource` to the `Connection` test double that completes when `ReceiveAsync` has
been called and returned the envelope (i.e., when the message was received and handed off), and
rename the test to `ConnectionUsesDerivedWebSocketAndReceivesThePayment`:

```csharp
[Fact]
public async Task ConnectionUsesDerivedWebSocketAndReceivesThePayment()
{
    var connection = new Connection(Envelope);
    var service = new RelayConnectionService(new Configuration(), new Factory(connection), new Announcement(), new Delay());

    service.Start();
    await connection.MessageReceived.Task.WaitAsync(TimeSpan.FromSeconds(2));
    await service.StopAsync();

    Assert.Equal("wss://relay.example.com/v1/c/token/t/324688170/ws", connection.ConnectedUri!.AbsoluteUri);
}
```

Update the `Connection` test double: remove `SendAsync`/`Sent`/`AckSent` (the interface no longer
has `SendAsync`), add `MessageReceived` (a `TaskCompletionSource` completed by `ReceiveAsync` right
before it returns the queued envelope). Every other test in this file that referenced
`connection.AckSent`/`connection.Sent` needs the same substitution — read through each remaining
test (`IoFailureReconnectsWithBoundedBackoff`, `StartingTwiceWhileRunningCreatesOnlyOneConnection`,
`RelayClosingTheConnectionTriggersAReconnectWithBackoff`, `DisposeAsyncStopsTheRunningLoop`) and
apply the same `MessageReceived` substitution. `InvalidRelayEnvelopeIsIgnoredWithoutAcknowledgment`
becomes `InvalidRelayEnvelopeIsIgnored` (drop the "without acknowledgment" framing since
acknowledgment no longer exists) and asserts only that the diagnostic fires — the `Assert.Empty(connection.Sent)`
line is removed since `Sent` no longer exists.
`IdentityFailureReportsNeedsAttentionAndStopsWithoutConnecting` becomes
`ConfigurationFailureReportsNeedsAttentionAndStopsWithoutConnecting` using `FailingConfiguration`.

- [ ] **Step 3: Run**

Run: `cd app && dotnet test AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj --filter FullyQualifiedName~RelayConnectionContractTests`
Expected: PASS once B5/B6 also compile (the test project builds as a whole — if it still doesn't
compile, continue to B5/B6 before running).

### Task B5: Update `PaymentAnnouncementService` — always speak, no ack flag

**Files:**
- Modify: `app/AdyenOutLoud.Core/Services/PaymentAnnouncementService.cs`
- Modify: `app/AdyenOutLoud.Tests/AnnouncementContractTests.cs`

**Interfaces:**
- Produces: `AnnounceAsync` always attempts speech for a new (non-duplicate) message — there is no
  more "no amount, skip speech" branch, since every message is now the same generic announcement.

- [ ] **Step 1: Rewrite `PaymentAnnouncementService.cs`**

```csharp
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public sealed class PaymentAnnouncementService(
    ISettingsService settings,
    ITextToSpeechService textToSpeech,
    ILocalizationService localization,
    IClock clock) : IPaymentAnnouncementService
{
    public event EventHandler<AnnouncementResult>? AnnouncementCompleted;

    public async Task<AnnouncementResult> AnnounceAsync(PaymentMessage message, CancellationToken cancellationToken)
    {
        var isNew = await settings.TryReserveEventIdAsync(message.Id, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (!isNew)
        {
            return Complete(new(message.Id, true, false, "Duplicate; not announced again.", message, null));
        }

        try
        {
            var language = settings.SelectedLanguage;
            var text = localization.CreatePaymentAnnouncement(message, language);
            var diagnostic = await textToSpeech.SpeakAsync(text, language, cancellationToken).ConfigureAwait(false);
            return Complete(new(message.Id, false, true, diagnostic.Message, message, diagnostic));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Complete(new(message.Id, false, false, $"Payment recorded; voice failed: {exception.Message}", message, null));
        }
    }

    private AnnouncementResult Complete(AnnouncementResult result)
    {
        try
        {
            AnnouncementCompleted?.Invoke(this, result);
        }
        catch (Exception)
        {
            // UI observers must never prevent processing the next message.
        }
        return result;
    }
}
```

- [ ] **Step 2: Update `AnnouncementContractTests.cs`**

- `Message()` loses its trailing `"visa", new("SGD", 1050)` arguments:
  `new("event-1", "payment_succeeded", DateTimeOffset.Parse("2026-09-18T12:00:00Z",
  CultureInfo.InvariantCulture), "P400Plus-123", "txn-1", "PSP-1")`.
- Delete `NullAmountIsPersistedAndAcknowledgedWithoutSpeech` and
  `CreatingAnAnnouncementForAMessageWithNoAmountThrows` entirely — there is no amount to be null
  anymore, and `CreatePaymentAnnouncement` no longer throws for a missing amount (Task B6).
- In `NewEventIsPersistedBeforeSpeechAndThenAcknowledged`, rename to
  `NewEventIsPersistedThenLocalizedThenSpoken` and drop the `Assert.True(result.ShouldAcknowledge)`
  line (the field no longer exists) — keep `Assert.Equal(["persist", "localize", "speak"], calls)`
  and `Assert.True(result.WasSpoken)`.
- In `DuplicateEventIsAcknowledgedWithoutSpeech`, rename to `DuplicateEventIsNotSpokenAgain`, drop
  the `ShouldAcknowledge` assertion, keep `Assert.True(result.WasDuplicate)` and
  `Assert.False(result.WasSpoken)`.
- In `TextToSpeechFailureStillAcknowledgesPoisonMessage`, rename to
  `TextToSpeechFailureIsRecordedWithoutThrowing`, drop the `ShouldAcknowledge` assertion.
- `FourLanguagesHaveIndependentResxAnnouncementsAndLocalizedFallbackMethod` changes in Task B6 (the
  assertion text changes since there's no amount/method to interpolate) — update it there.

- [ ] **Step 3: Run**

Run: `cd app && dotnet test AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj --filter FullyQualifiedName~AnnouncementContractTests`
Expected: still may fail to compile until B6 lands (resx/localization changes); continue.

### Task B6: Delete `CurrencyFormatter`/`PaymentMethodNames`, simplify announcement templates

**Files:**
- Delete: `app/AdyenOutLoud.Core/Services/CurrencyFormatter.cs`
- Delete: `app/AdyenOutLoud.Core/Services/PaymentMethodNames.cs`
- Delete: `app/AdyenOutLoud.Tests/CurrencyFormatterTests.cs`
- Delete: `app/AdyenOutLoud.Tests/PaymentMethodNamesTests.cs`
- Modify: `app/AdyenOutLoud.Core/Services/ResxLocalizationService.cs`
- Modify: `app/AdyenOutLoud.Core/Resources/Strings.resx`
- Modify: `app/AdyenOutLoud.Core/Resources/Strings.zh.resx`
- Modify: `app/AdyenOutLoud.Core/Resources/Strings.ms.resx`
- Modify: `app/AdyenOutLoud.Core/Resources/Strings.ta.resx`
- Modify: `app/AdyenOutLoud.Tests/AnnouncementContractTests.cs` (finish B5's remaining item)

- [ ] **Step 1: Delete the dead formatting classes and their tests**

```bash
git rm app/AdyenOutLoud.Core/Services/CurrencyFormatter.cs app/AdyenOutLoud.Core/Services/PaymentMethodNames.cs
git rm app/AdyenOutLoud.Tests/CurrencyFormatterTests.cs app/AdyenOutLoud.Tests/PaymentMethodNamesTests.cs
```

- [ ] **Step 2: Simplify `ResxLocalizationService.cs`**

```csharp
using System.Globalization;
using System.Resources;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;

namespace AdyenOutLoud.Services;

public sealed class ResxLocalizationService : ILocalizationService
{
    private static readonly ResourceManager Resources = new("AdyenOutLoud.Resources.Strings", typeof(ResxLocalizationService).Assembly);

    public string CreatePaymentAnnouncement(PaymentMessage message, AppLanguage language)
    {
        var culture = CultureInfo.GetCultureInfo(language.Locale);
        return Get("PaymentReceived", culture);
    }

    public string CreateTestAnnouncement(AppLanguage language)
    {
        var culture = CultureInfo.GetCultureInfo(language.Locale);
        return Get("TestAnnouncement", culture);
    }

    private static string Get(string key, CultureInfo culture) =>
        Resources.GetString(key, culture) ?? throw new MissingManifestResourceException($"Missing resource '{key}' for '{culture.Name}'.");
}
```

- [ ] **Step 3: Update each `.resx` file's `PaymentReceived` value and remove `UnknownPaymentMethod`**

`Strings.resx` (English):
```xml
<data name="PaymentReceived" xml:space="preserve"><value>Payment successful.</value></data>
<data name="TestAnnouncement" xml:space="preserve"><value>Adyen Out Loud is ready to announce payments.</value></data>
```

`Strings.zh.resx`:
```xml
<data name="PaymentReceived" xml:space="preserve"><value>付款成功。</value></data>
<data name="TestAnnouncement" xml:space="preserve"><value>Adyen Out Loud 已准备好播报付款。</value></data>
```

`Strings.ms.resx`:
```xml
<data name="PaymentReceived" xml:space="preserve"><value>Bayaran berjaya.</value></data>
<data name="TestAnnouncement" xml:space="preserve"><value>Adyen Out Loud sedia untuk mengumumkan pembayaran.</value></data>
```

`Strings.ta.resx`:
```xml
<data name="PaymentReceived" xml:space="preserve"><value>பணம் செலுத்துதல் வெற்றிகரமாக முடிந்தது.</value></data>
<data name="TestAnnouncement" xml:space="preserve"><value>Adyen Out Loud பணம் செலுத்தல்களை அறிவிக்க தயாராக உள்ளது.</value></data>
```

Delete the `UnknownPaymentMethod` `<data>` entry from all four files (`resheader`/`TestAnnouncement`
untouched otherwise).

- [ ] **Step 4: Finish updating `AnnouncementContractTests.cs`**

Update `FourLanguagesHaveIndependentResxAnnouncementsAndLocalizedFallbackMethod`:

```csharp
[Fact]
public void FourLanguagesHaveIndependentResxAnnouncements()
{
    var localization = new ResxLocalizationService();
    Assert.Collection(AppLanguage.All,
        language => Assert.Equal("Payment successful.", localization.CreatePaymentAnnouncement(Message(), language)),
        language => Assert.Equal("付款成功。", localization.CreatePaymentAnnouncement(Message(), language)),
        language => Assert.Equal("Bayaran berjaya.", localization.CreatePaymentAnnouncement(Message(), language)),
        language => Assert.Equal("பணம் செலுத்துதல் வெற்றிகரமாக முடிந்தது.", localization.CreatePaymentAnnouncement(Message(), language)));
}
```

Note `AppLanguage.All`'s order must match this file's existing order (English, Chinese, Malay,
Tamil) — verify against `app/AdyenOutLoud.Core/Models/AppLanguage.cs` (unchanged by this plan) before
finalizing which language maps to which assertion.

- [ ] **Step 5: Run the full Core test suite**

Run: `cd app && dotnet build AdyenOutLoud.Core/AdyenOutLoud.Core.csproj && dotnet test AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj`
Expected: still fails to build if `MainViewModel`/`MauiProgram` (head project, Task B7–B9) reference
deleted types — but `AdyenOutLoud.Core` and `AdyenOutLoud.Tests` alone should now build and pass in
full. Fix anything red in this narrower scope before proceeding.

- [ ] **Step 6: Commit B1–B6 together**

```bash
git add app/AdyenOutLoud.Core app/AdyenOutLoud.Tests
git commit -m "feat(app): replace instance-token identity with relay URL + terminal serial configuration"
```

### Task B7: Rewrite `MainViewModel` for URL + terminal-serial configuration entry

**Files:**
- Modify: `app/AdyenOutLoud/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `IRelayConfigurationService` (replaces `IInstanceIdentityService`).
- Produces: `RelayUrlInput`, `TerminalSerialInput`, `ConfigurationStatus` bindable properties and a
  `SaveConfigurationCommand` — consumed by Task B11 (`MainPage.xaml`).

- [ ] **Step 1: Rewrite the file**

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Models;
using AdyenOutLoud.Services;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Graphics;

namespace AdyenOutLoud.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IRelayConnectionService _relay;
    private readonly IRelayConfigurationService _configuration;
    private readonly ITextToSpeechService _textToSpeech;
    private readonly ILocalizationService _localization;
    private readonly ISettingsService _settings;
    private readonly IPaymentAnnouncementService _announcements;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private bool _initialized;
    private string _relayUrlInput = string.Empty;
    private string _terminalSerialInput = string.Empty;
    private string _configurationStatus = string.Empty;
    private string _statusTitle = "CONNECTING";
    private string _statusDetail = "Preparing the payment listener...";
    private Color _statusColor = Color.FromArgb("#F7B955");
    private string _diagnostic = "Run Test voice to inspect the selected installed voice.";
    private string _latestEvent = "No payment event received yet.";

    public MainViewModel(
        IRelayConnectionService relay,
        IRelayConfigurationService configuration,
        ITextToSpeechService textToSpeech,
        ILocalizationService localization,
        ISettingsService settings,
        IPaymentAnnouncementService announcements)
    {
        _relay = relay;
        _configuration = configuration;
        _textToSpeech = textToSpeech;
        _localization = localization;
        _settings = settings;
        _announcements = announcements;
        relay.StatusChanged += OnStatusChanged;
        relay.Diagnostic += OnDiagnostic;
        announcements.AnnouncementCompleted += OnAnnouncementCompleted;
        TestVoiceCommand = new AsyncCommand(TestVoiceAsync);
        SaveConfigurationCommand = new AsyncCommand(SaveConfigurationAsync);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

#pragma warning disable CA1822
    public IReadOnlyList<AppLanguage> Languages => AppLanguage.All;
#pragma warning restore CA1822

    public AppLanguage SelectedLanguage
    {
        get => _settings.SelectedLanguage;
        set
        {
            if (value.Code == _settings.SelectedLanguage.Code) return;
            _settings.SelectedLanguage = value;
            OnPropertyChanged();
        }
    }

    public string RelayUrlInput { get => _relayUrlInput; set => Set(ref _relayUrlInput, value); }
    public string TerminalSerialInput { get => _terminalSerialInput; set => Set(ref _terminalSerialInput, value); }
    public string ConfigurationStatus { get => _configurationStatus; private set => Set(ref _configurationStatus, value); }
    public string StatusTitle { get => _statusTitle; private set => Set(ref _statusTitle, value); }
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }
    public Color StatusColor { get => _statusColor; private set => Set(ref _statusColor, value); }
    public string Diagnostic { get => _diagnostic; private set => Set(ref _diagnostic, value); }
    public string LatestEvent { get => _latestEvent; private set => Set(ref _latestEvent, value); }
    public ICommand TestVoiceCommand { get; }
    public ICommand SaveConfigurationCommand { get; }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        await _initializationGate.WaitAsync();
        try
        {
            if (_initialized) return;
            var configuration = await _configuration.GetAsync();
            if (configuration is not null)
            {
                RelayUrlInput = configuration.BaseUrl.AbsoluteUri;
                TerminalSerialInput = configuration.TerminalSerial;
            }
            _initialized = true;
        }
        catch (Exception exception)
        {
            StatusTitle = "NEEDS ATTENTION";
            StatusColor = Color.FromArgb("#FF6961");
            StatusDetail = "Could not read the saved relay configuration.";
            Diagnostic = exception.Message;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    private async Task SaveConfigurationAsync()
    {
        if (!Uri.TryCreate(RelayUrlInput.Trim(), UriKind.Absolute, out var baseUrl))
        {
            ConfigurationStatus = "Enter a valid https:// relay URL.";
            return;
        }

        var terminalSerial = TerminalSerialInput.Trim();
        if (terminalSerial.Length == 0)
        {
            ConfigurationStatus = "Enter this device's terminal serial number.";
            return;
        }

        try
        {
            await _relay.StopAsync();
            await _configuration.SaveAsync(baseUrl, terminalSerial);
            ConfigurationStatus = "Saved. Connecting...";
            _relay.Start();
        }
        catch (Exception exception)
        {
            ConfigurationStatus = $"Could not save: {exception.Message}";
        }
    }

    private async Task TestVoiceAsync()
    {
        try
        {
            var language = SelectedLanguage;
            var result = await _textToSpeech.SpeakAsync(
                _localization.CreateTestAnnouncement(language), language, CancellationToken.None);
            Diagnostic = result.Message;
        }
        catch (Exception exception)
        {
            Diagnostic = $"Voice test failed: {exception.Message}";
        }
    }

    private void OnStatusChanged(object? sender, RelayStatus status) => MainThread.BeginInvokeOnMainThread(() =>
    {
        StatusTitle = status.State switch
        {
            RelayConnectionState.Listening => "LISTENING",
            RelayConnectionState.NeedsAttention => "NEEDS ATTENTION",
            _ => "CONNECTING"
        };
        StatusColor = status.State switch
        {
            RelayConnectionState.Listening => Color.FromArgb("#0ABF53"),
            RelayConnectionState.NeedsAttention => Color.FromArgb("#FF6961"),
            _ => Color.FromArgb("#F7B955")
        };
        StatusDetail = status.Detail;
    });

    private void OnDiagnostic(object? sender, string message) =>
        MainThread.BeginInvokeOnMainThread(() => Diagnostic = message);

    private void OnAnnouncementCompleted(object? sender, AnnouncementResult result) => MainThread.BeginInvokeOnMainThread(() =>
    {
        var payment = result.Message;
        LatestEvent = $"Terminal {payment.TerminalId} / {payment.OccurredAt.ToLocalTime():g}\nTransaction {payment.TransactionId} / PSP {payment.PspReference}\nEvent {payment.Id}";
        Diagnostic = result.Speech?.Message ?? result.Detail;
    });

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new(propertyName));

    public void Dispose()
    {
        _relay.StatusChanged -= OnStatusChanged;
        _relay.Diagnostic -= OnDiagnostic;
        _announcements.AnnouncementCompleted -= OnAnnouncementCompleted;
        _initializationGate.Dispose();
    }
}
```

Note: `Microsoft.Maui.ApplicationModel.DataTransfer` (`Clipboard`) is no longer used — its `using`
is dropped along with `CopyWebhookAsync`/`WebhookUrl`/`CopyStatus`/`CopyWebhookCommand`.

- [ ] **Step 2: This file has no dedicated unit tests** (per `docs/testing.md`, the MAUI head
  project's ViewModels aren't coverage-gated — they're exercised by the manual UI smoke checklist,
  updated in Task F5). Proceed to Task B8.

### Task B8: Replace the token store with a relay-configuration store

**Files:**
- Delete: `app/AdyenOutLoud/Services/SecureStorageTokenStore.cs`
- Create: `app/AdyenOutLoud/Services/SecureStorageRelayConfigurationStore.cs`

- [ ] **Step 1: Delete the old store**

```bash
git rm app/AdyenOutLoud/Services/SecureStorageTokenStore.cs
```

- [ ] **Step 2: Create the new store**

```csharp
using AdyenOutLoud.Abstractions;
using Microsoft.Maui.Storage;

namespace AdyenOutLoud.Services;

public sealed class SecureStorageRelayConfigurationStore : IRelayConfigurationStore
{
    private const string BaseUrlKey = "relay-base-url-v1";
    private const string TerminalSerialKey = "relay-terminal-serial-v1";

    public async Task<(Uri BaseUrl, string TerminalSerial)?> GetAsync()
    {
        var baseUrlText = await SecureStorage.Default.GetAsync(BaseUrlKey).ConfigureAwait(false);
        var terminalSerial = await SecureStorage.Default.GetAsync(TerminalSerialKey).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(baseUrlText) || string.IsNullOrWhiteSpace(terminalSerial)) return null;
        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl)) return null;
        return (baseUrl, terminalSerial);
    }

    public async Task SetAsync(Uri baseUrl, string terminalSerial)
    {
        await SecureStorage.Default.SetAsync(BaseUrlKey, baseUrl.AbsoluteUri).ConfigureAwait(false);
        await SecureStorage.Default.SetAsync(TerminalSerialKey, terminalSerial).ConfigureAwait(false);
    }
}
```

- [ ] **Step 3: Nothing to run yet** — wired up in Task B9.

### Task B9: Update DI registration and remove the build-time `RelayBaseUrl` mechanism

**Files:**
- Modify: `app/AdyenOutLoud/MauiProgram.cs`
- Modify: `app/AdyenOutLoud/AdyenOutLoud.csproj`

The relay URL is now entered at runtime by the installer, not compiled in at build time — drop
`RelayBaseUrl`/`AssemblyMetadataAttribute`/`GetRelayUri()` entirely.

- [ ] **Step 1: Rewrite `MauiProgram.cs`**

```csharp
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Services;
using AdyenOutLoud.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;

namespace AdyenOutLoud;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IRetryDelay, TaskRetryDelay>();
        builder.Services.AddSingleton<ITextToSpeechService, MauiSpeechService>();
        builder.Services.AddSingleton<ILocalizationService, ResxLocalizationService>();
        builder.Services.AddSingleton<ISettingsService, PreferencesSettingsService>();
        builder.Services.AddSingleton<IRelayConfigurationStore, SecureStorageRelayConfigurationStore>();
        builder.Services.AddSingleton<IRelayConfigurationService, RelayConfigurationService>();
        builder.Services.AddSingleton<IRelayConnectionFactory, ClientWebSocketConnectionFactory>();
        builder.Services.AddSingleton<IPaymentAnnouncementService, PaymentAnnouncementService>();
        builder.Services.AddSingleton<IRelayConnectionService>(provider => new RelayConnectionService(
            provider.GetRequiredService<IRelayConfigurationService>(),
            provider.GetRequiredService<IRelayConnectionFactory>(),
            provider.GetRequiredService<IPaymentAnnouncementService>(),
            provider.GetRequiredService<IRetryDelay>()));
        builder.Services.AddSingleton<IBackgroundExecutionService, BackgroundExecutionService>();
        builder.Services.AddSingleton<AppLifecycleCoordinator>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
```

(`IBackgroundExecutionService`/`BackgroundExecutionService` are added in Task C1 — this
registration line is written now so B9 and C1 don't conflict; if executing tasks strictly in order,
leave this line out until C1 lands and add it then instead.)

- [ ] **Step 2: Remove `RelayBaseUrl` from `AdyenOutLoud.csproj`**

Delete the `<RelayBaseUrl Condition="'$(RelayBaseUrl)' == ''">https://relay.example.invalid</RelayBaseUrl>`
property and the `<AssemblyAttribute Include="System.Reflection.AssemblyMetadataAttribute">`
`ItemGroup` entry that references it (the `_Parameter1`/`_Parameter2` block for `RelayBaseUrl`).

- [ ] **Step 3: Build the Android head project**

Run: `cd app && dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android`
Expected: FAIL until Task B11 (XAML) is also updated — `MainPage.xaml`'s bindings
(`WebhookUrl`/`CopyWebhookCommand`) no longer exist on `MainViewModel`. Continue to B10/B11.

### Task B10: Update the architecture tests' fixtures if they reference removed types

**Files:**
- Read (verify, modify only if needed): `app/AdyenOutLoud.ArchitectureTests/*.cs`

- [ ] **Step 1: Read `CoreAssemblyBoundaryTests.cs`, `MauiSourceBoundaryTests.cs`,
  `ProjectReferenceGraphTests.cs`**

Confirm none of them reference a specific deleted type by name (e.g., `InstanceIdentityService`,
`DeviceIdentityGenerator`) — these tests are typically structural (reflection over the assembly,
`.csproj` source scanning), not type-name-specific, so they likely need no change. If any test does
name a deleted type directly, update it to name an equivalent still-present type (e.g.,
`RelayConfigurationService`) instead.

- [ ] **Step 2: Run**

Run: `cd app && dotnet test AdyenOutLoud.ArchitectureTests/AdyenOutLoud.ArchitectureTests.csproj`
Expected: PASS (once the rest of Part B compiles).

- [ ] **Step 3: Commit only if a change was made**

### Task B11: Rewrite `MainPage.xaml` for the new configuration form

**Files:**
- Modify: `app/AdyenOutLoud/MainPage.xaml`

- [ ] **Step 1: Replace the "CONNECT ADYEN WEBHOOKS" section**

Replace the `VerticalStackLayout` currently containing the webhook-URL `Entry`/copy button/warning
(the block starting `<Label Text="CONNECT ADYEN WEBHOOKS" .../>`) with:

```xml
<VerticalStackLayout Spacing="14">
    <Label Text="RELAY CONFIGURATION" Style="{StaticResource SectionTitle}" />
    <Label Text="Ask whoever manages your Adyen account for the relay URL they configured as this company's Display webhook, then enter this specific device's terminal serial number below." Style="{StaticResource Body}" />
    <Label Text="RELAY URL" Style="{StaticResource FieldLabel}" />
    <Border Style="{StaticResource InputBorder}">
        <Entry Text="{Binding RelayUrlInput}"
               Placeholder="https://relay.example.com/v1/c/..."
               Keyboard="Url"
               TextColor="{StaticResource Paper}"
               FontFamily="monospace"
               SemanticProperties.Description="Relay URL" />
    </Border>
    <Label Text="TERMINAL SERIAL NUMBER" Style="{StaticResource FieldLabel}" />
    <Border Style="{StaticResource InputBorder}">
        <Entry Text="{Binding TerminalSerialInput}"
               Placeholder="324688170"
               TextColor="{StaticResource Paper}"
               FontFamily="monospace"
               SemanticProperties.Description="Terminal serial number" />
    </Border>
    <Button Text="Save" Command="{Binding SaveConfigurationCommand}"
            SemanticProperties.Description="Save the relay configuration" />
    <Label Text="{Binding ConfigurationStatus}" Style="{StaticResource Caption}" />
    <Border Style="{StaticResource WarningPanel}">
        <Label Text="SECRET URL: Anyone with this relay URL can send payment announcements to every terminal in this company. Share it only through your webhook configuration." Style="{StaticResource WarningText}" />
    </Border>
</VerticalStackLayout>
```

- [ ] **Step 2: Update the footer** (Task C3 finalizes the exact wording once background support is
  implemented — for now, remove the unconditional "Keep Adyen Out Loud in the foreground" line and
  leave a placeholder the plan will revisit):

```xml
<Label Grid.Row="2" Text="{Binding FooterText}" Style="{StaticResource Footer}" />
```

Hold off wiring `FooterText` until Task C3 — for this task, it's acceptable to leave the original
static footer text in place and let Task C3 replace it, to avoid a `MainViewModel` binding to a
property that doesn't exist yet. Simplest: skip this step here and do it entirely in Task C3.

- [ ] **Step 3: Build**

Run: `cd app && dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android`
Expected: PASS (compiled bindings resolve against the new `MainViewModel` members).

- [ ] **Step 4: Run the full non-platform-specific test suite**

Run:
```bash
cd app
dotnet format AdyenOutLoud.slnx --verify-no-changes
dotnet build AdyenOutLoud.Core/AdyenOutLoud.Core.csproj
dotnet build AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj
dotnet build AdyenOutLoud.ArchitectureTests/AdyenOutLoud.ArchitectureTests.csproj
dotnet test AdyenOutLoud.Tests/AdyenOutLoud.Tests.csproj
dotnet test AdyenOutLoud.ArchitectureTests/AdyenOutLoud.ArchitectureTests.csproj
../scripts/dotnet-coverage.sh
```
Expected: PASS. If `dotnet format` reports changes, run it without `--verify-no-changes` to fix,
then re-verify.

- [ ] **Step 5: Commit**

```bash
git add app/AdyenOutLoud app/AdyenOutLoud.ArchitectureTests
git commit -m "feat(app): relay URL + terminal serial configuration UI, drop build-time RelayBaseUrl"
```

---

## Part C — Background execution

### Task C1: Add a `IBackgroundExecutionService` abstraction with per-platform implementations

**Files:**
- Create: `app/AdyenOutLoud.Core/Abstractions/IBackgroundExecutionService.cs`
- Create: `app/AdyenOutLoud/Services/BackgroundExecutionService.android.cs`
- Create: `app/AdyenOutLoud/Services/BackgroundExecutionService.ios.cs`
- Create: `app/AdyenOutLoud/Services/BackgroundExecutionService.maccatalyst.cs`
- Create: `app/AdyenOutLoud/Services/BackgroundExecutionService.windows.cs`
- Create: `app/AdyenOutLoud/Platforms/Android/PaymentListenerForegroundService.cs`
- Modify: `app/AdyenOutLoud/Platforms/Android/AndroidManifest.xml`

**Interfaces:**
- Produces: `IBackgroundExecutionService.EnterBackgroundAsync(): Task`,
  `IBackgroundExecutionService.EnterForegroundAsync(): Task` — consumed by Task C2
  (`AppLifecycleCoordinator`).

MAUI's `.<platform>.cs` filename suffix convention (e.g. `Foo.android.cs`) scopes a file to that
target framework automatically, without needing it under `Platforms/`, which is why DI registration
in `MauiProgram.cs` can reference one `IBackgroundExecutionService` type name that resolves
differently per build.

- [ ] **Step 1: Create the Core interface**

```csharp
namespace AdyenOutLoud.Abstractions;

public interface IBackgroundExecutionService
{
    Task EnterForegroundAsync();
    Task EnterBackgroundAsync();
}
```

- [ ] **Step 2: Create the Android implementation**

`app/AdyenOutLoud/Services/BackgroundExecutionService.android.cs`:

```csharp
using Android.Content;
using AdyenOutLoud.Abstractions;
using AdyenOutLoud.Platforms.Android;

namespace AdyenOutLoud.Services;

public sealed class BackgroundExecutionService : IBackgroundExecutionService
{
    public Task EnterForegroundAsync()
    {
        var context = global::Android.App.Application.Context;
        context.StopService(new Intent(context, typeof(PaymentListenerForegroundService)));
        return Task.CompletedTask;
    }

    public Task EnterBackgroundAsync()
    {
        var context = global::Android.App.Application.Context;
        var intent = new Intent(context, typeof(PaymentListenerForegroundService));
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Create the Android foreground service**

`app/AdyenOutLoud/Platforms/Android/PaymentListenerForegroundService.cs`:

```csharp
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace AdyenOutLoud.Platforms.Android;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class PaymentListenerForegroundService : Service
{
    private const string ChannelId = "payment-listener";
    private const int NotificationId = 1;

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var manager = (NotificationManager)GetSystemService(NotificationService)!;
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O && manager.GetNotificationChannel(ChannelId) is null)
        {
            var channel = new NotificationChannel(ChannelId, "Payment listener", NotificationImportance.Low)
            {
                Description = "Keeps Adyen Out Loud listening for payments while the app is in the background.",
            };
            manager.CreateNotificationChannel(channel);
        }

        var notification = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle("Adyen Out Loud")
            .SetContentText("Listening for payments in the background.")
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(true)
            .Build();

        StartForeground(NotificationId, notification);
        return StartCommandResult.Sticky;
    }
}
```

Notes for the implementer: check the currently-available `Android.Resource.Drawable` icon or add a
dedicated small icon under `Resources/drawable/` if `IcDialogInfo` looks wrong at runtime — this is
a placeholder that compiles and renders, not a design requirement; swap it for a proper monochrome
notification icon during manual verification (Task C4) if it looks off. `ForegroundServiceType =
ForegroundService.TypeDataSync` matches Android 14's requirement that every foreground service
declare a type; `dataSync` is the closest fit for "maintaining a network connection to receive
data" (there is no purpose-built "network listener" type as of this writing — verify against
current Android documentation per `AGENTS.md`'s "check current official docs" rule before shipping).

- [ ] **Step 4: Update `AndroidManifest.xml`**

```xml
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">
    <uses-permission android:name="android.permission.INTERNET" />
    <uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
    <uses-permission android:name="android.permission.FOREGROUND_SERVICE_DATA_SYNC" />
    <uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
    <queries>
        <intent>
            <action android:name="android.intent.action.TTS_SERVICE" />
        </intent>
    </queries>
    <application android:allowBackup="true"
                 android:fullBackupContent="@xml/auto_backup_rules"
                 android:dataExtractionRules="@xml/data_extraction_rules"
                 android:supportsRtl="true">
        <service android:name="AdyenOutLoud.Platforms.Android.PaymentListenerForegroundService"
                 android:exported="false"
                 android:foregroundServiceType="dataSync" />
    </application>
</manifest>
```

- [ ] **Step 5: Create the iOS implementation (foreground-only by decision)**

`app/AdyenOutLoud/Services/BackgroundExecutionService.ios.cs`:

```csharp
using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

/// <summary>
/// iOS is deliberately foreground-only: aggressively suspending backgrounded apps means the only
/// realistic way to keep a WebSocket open is registering the "audio" background mode, which Apple
/// can reject for an app that isn't actually playing continuous audio. This reproduces exactly
/// today's pre-plan behavior on this one platform — stop the relay connection on background, and
/// let the next EnterForeground()-triggered relay.Start() (in AppLifecycleCoordinator, Task C2)
/// reconnect it.
/// </summary>
public sealed class BackgroundExecutionService(IRelayConnectionService relayConnection) : IBackgroundExecutionService
{
    public Task EnterForegroundAsync() => Task.CompletedTask;
    public Task EnterBackgroundAsync() => relayConnection.StopAsync();
}
```

- [ ] **Step 6: Create the Mac Catalyst implementation (no-op — desktop apps aren't suspended)**

`app/AdyenOutLoud/Services/BackgroundExecutionService.maccatalyst.cs`:

```csharp
using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

/// <summary>Mac Catalyst apps run as ordinary macOS processes and are not suspended when the
/// window loses focus or is minimized, so there is nothing to do here — the relay connection is
/// simply never stopped on background/foreground transitions on this platform.</summary>
public sealed class BackgroundExecutionService : IBackgroundExecutionService
{
    public Task EnterForegroundAsync() => Task.CompletedTask;
    public Task EnterBackgroundAsync() => Task.CompletedTask;
}
```

- [ ] **Step 7: Create the Windows implementation (no-op — same reasoning as Mac Catalyst)**

`app/AdyenOutLoud/Services/BackgroundExecutionService.windows.cs`:

```csharp
using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

/// <summary>Windows desktop apps are not suspended when minimized, so there is nothing to do here
/// — the relay connection is simply never stopped on background/foreground transitions on this
/// platform.</summary>
public sealed class BackgroundExecutionService : IBackgroundExecutionService
{
    public Task EnterForegroundAsync() => Task.CompletedTask;
    public Task EnterBackgroundAsync() => Task.CompletedTask;
}
```

- [ ] **Step 8: Register in `MauiProgram.cs`** (if not already added in Task B9)

```csharp
builder.Services.AddSingleton<IBackgroundExecutionService, BackgroundExecutionService>();
```

- [ ] **Step 9: Build each platform head that's locally buildable**

Run: `cd app && dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android`
Expected: PASS. iOS/Mac Catalyst/Windows builds are verified in CI (`platform-builds.yml`) per this
project's existing "not locally verifiable on this OS" pattern — report explicitly which platforms
you could and couldn't build locally, per `AGENTS.md`'s reporting rule.

- [ ] **Step 10: Commit**

```bash
git add app/AdyenOutLoud.Core/Abstractions/IBackgroundExecutionService.cs \
        app/AdyenOutLoud/Services/BackgroundExecutionService.*.cs \
        app/AdyenOutLoud/Platforms/Android \
        app/AdyenOutLoud/MauiProgram.cs
git commit -m "feat(app): background execution — Android foreground service, desktop no-op, iOS foreground-only"
```

### Task C2: Wire `IBackgroundExecutionService` into `AppLifecycleCoordinator`

**Files:**
- Modify: `app/AdyenOutLoud/Services/AppLifecycleCoordinator.cs`

- [ ] **Step 1: Rewrite the file**

```csharp
using AdyenOutLoud.Abstractions;

namespace AdyenOutLoud.Services;

public sealed class AppLifecycleCoordinator(IRelayConnectionService relayConnection, IBackgroundExecutionService background)
{
    private int _isForeground;

    public void EnterForeground()
    {
        if (Interlocked.Exchange(ref _isForeground, 1) == 0)
        {
            _ = background.EnterForegroundAsync();
            relayConnection.Start();
        }
    }

    public async Task LeaveForegroundAsync()
    {
        if (Interlocked.Exchange(ref _isForeground, 0) == 1)
        {
            await background.EnterBackgroundAsync();
        }
    }
}
```

Note the behavior split now lives entirely in `IBackgroundExecutionService`'s platform
implementations (Task C1): on Android, `EnterBackgroundAsync` starts the foreground service and the
relay connection is never stopped, so it just keeps running; on iOS, `EnterBackgroundAsync` calls
`relayConnection.StopAsync()` directly (Task C1 Step 5), reproducing today's exact behavior; on
Windows/Mac Catalyst, both methods are no-ops. `relayConnection.Start()` on the next
`EnterForeground()` call already covers reconnecting when the app returns to the foreground on iOS.

- [ ] **Step 2: Build**

Run: `cd app && dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add app/AdyenOutLoud/Services/AppLifecycleCoordinator.cs app/AdyenOutLoud/Services/BackgroundExecutionService.ios.cs
git commit -m "feat(app): delegate background/foreground transitions to IBackgroundExecutionService"
```

### Task C3: Update the footer copy for platform-specific background behavior

**Files:**
- Modify: `app/AdyenOutLoud/ViewModels/MainViewModel.cs`
- Modify: `app/AdyenOutLoud/MainPage.xaml`

- [ ] **Step 1: Add a `FooterText` property to `MainViewModel`**

```csharp
public string FooterText { get; } = OperatingSystem.IsIOS()
    ? "On iOS, keep Adyen Out Loud in the foreground while taking payments."
    : "Adyen Out Loud keeps listening for payments while running in the background on this platform.";
```

(`OperatingSystem.IsIOS()` is a BCL runtime check available in the MAUI head project — this
property never changes after construction, so it doesn't need to be a full bindable `Set(...)`
property; a simple `{ get; }` auto-property is enough and XAML compiled bindings can bind to it
read-only.)

- [ ] **Step 2: Bind it in `MainPage.xaml`**

```xml
<Label Grid.Row="2" Text="{Binding FooterText}" Style="{StaticResource Footer}" />
```

- [ ] **Step 3: Build**

Run: `cd app && dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add app/AdyenOutLoud/ViewModels/MainViewModel.cs app/AdyenOutLoud/MainPage.xaml
git commit -m "feat(app): platform-aware footer copy for background listening"
```

### Task C4: Manual verification checklist for background execution (not automatable)

**Files:** none — this is a manual verification task, recorded here so it isn't silently skipped.

- [ ] **Step 1: Android** — install a debug build on a device/emulator, configure relay URL +
  terminal serial, background the app (Home button), send a test ingest request from `curl` against
  the deployed/local Worker for that terminal's serial, confirm: (a) a persistent "Listening for
  payments in the background" notification appears when backgrounded, (b) the announcement is still
  spoken while backgrounded, (c) returning to the foreground stops the foreground service/dismisses
  the notification.
- [ ] **Step 2: Windows / Mac Catalyst** — minimize the app window, send a test ingest request,
  confirm the announcement is still spoken while minimized.
- [ ] **Step 3: iOS** — confirm the relay status shows "NEEDS ATTENTION"/disconnected shortly after
  backgrounding (matching today's existing behavior) and reconnects automatically when foregrounded
  again.
- [ ] **Step 4: Record results** in the PR description or `docs/testing.md`'s UI smoke checklist
  (Task F5 updates that checklist's text; this step is where you actually run it once, at least on
  whatever platform(s) are available in your environment) — per `AGENTS.md`, explicitly report any
  platform you could not verify.

---

## Part D — Quality gates: SonarCloud, formatting confirmation, dependency updates

### Task D1: Confirm formatting is already a required quality gate (verification only)

**Files:** none to change if the verification below passes.

- [ ] **Step 1: Verify `dotnet format --verify-no-changes` is required in CI**

Read `.github/workflows/quality.yml`'s `dotnet` job — it already runs
`dotnet format AdyenOutLoud.slnx --no-restore --verify-no-changes --verbosity diagnostic` as a
required step (no `continue-on-error`), so a formatting violation already fails the `dotnet` job and
therefore the PR. No change needed.

- [ ] **Step 2: Verify `prettier --check` is already required in CI**

Read the `worker` job in the same file — it runs `npm run quality`, which per
`worker/package.json`'s `quality` script includes `format:check` first in the chain (`&&`-joined, so
a failure there stops the rest and fails the job). No change needed.

- [ ] **Step 3: Conclusion**

Formatting is already a hard-required quality gate on both sides — this requirement is satisfied by
the existing setup. Do not add a redundant check. Note this explicitly in the PR description/summary
so it's clear it was verified, not skipped.

### Task D2: Add SonarCloud static analysis

**Files:**
- Create: `.github/workflows/sonarcloud.yml`
- Create: `sonar-project.properties`
- Modify: `docs/quality.md`
- Modify: `docs/repository-settings.md`

SonarCloud needs a `SONAR_TOKEN` (and knowing the organization/project key) that only a human with
SonarCloud admin access can create — this task wires up the workflow and config; a human must
still create the SonarCloud project and add the repository secret before the workflow will
succeed (documented explicitly, not assumed).

- [ ] **Step 1: Create `sonar-project.properties`** at the repo root

```properties
sonar.projectKey=REPLACE_WITH_SONARCLOUD_PROJECT_KEY
sonar.organization=REPLACE_WITH_SONARCLOUD_ORG
sonar.sources=worker/src,app/AdyenOutLoud.Core,app/AdyenOutLoud
sonar.tests=worker/test,app/AdyenOutLoud.Tests
sonar.exclusions=worker/coverage/**,worker/worker-configuration.d.ts,artifacts/**
sonar.javascript.lcov.reportPaths=worker/coverage/lcov.info
sonar.cs.vscoveragexml.reportsPaths=artifacts/coverage/dotnet/**/coverage.cobertura.xml
```

Adjust `sonar.cs.*` coverage-report property to whichever format
`scripts/dotnet-coverage.sh`/`coverlet` actually emits (Cobertura, per `docs/dependencies.md` —
SonarCloud's C# analyzer expects either its own `vscoveragexml` format or, more commonly for a
Cobertura-producing pipeline, no separate coverage import at all if C# analysis is driven entirely
through `dotnet-sonarscanner`'s own coverage ingestion; verify the exact property name against
current SonarCloud documentation before finalizing — this is exactly the kind of "check official
docs, don't rely on training data" case `AGENTS.md` calls out).

- [ ] **Step 2: Check the Worker's coverage reporter includes `lcov`**

Read `worker/vitest.config.ts`'s `coverage.reporter` array. If `"lcov"` isn't already listed
alongside whatever reporters exist today, add it (needed for `sonar.javascript.lcov.reportPaths`
above). This is an additive change to an existing array — do not remove an existing reporter.

- [ ] **Step 3: Create `.github/workflows/sonarcloud.yml`**

```yaml
name: SonarCloud

on:
  pull_request:
  push:
    branches: [main]

permissions:
  contents: read

concurrency:
  group: sonarcloud-${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true

jobs:
  analyze:
    name: SonarCloud analysis
    runs-on: ubuntu-latest
    timeout-minutes: 20
    steps:
      - uses: actions/checkout@fbc6f3992d24b796d5a048ff273f7fcc4a7b6c09 # v5
        with:
          fetch-depth: 0
      - uses: actions/setup-node@a0853c24544627f65ddf259abe73b1d18a591444 # v5
        with:
          node-version-file: worker/.node-version
          cache: npm
          cache-dependency-path: worker/package-lock.json
      - name: Worker coverage
        working-directory: worker
        run: |
          npm ci
          npm run test:coverage
      - uses: actions/setup-dotnet@26b0ec14cb23fa6904739307f278c14f94c95bf1 # v5
        with:
          global-json-file: global.json
      - name: .NET coverage
        working-directory: app
        run: |
          dotnet restore AdyenOutLoud.slnx
          dotnet workload install android
          ../scripts/dotnet-coverage.sh
      # NOTE TO IMPLEMENTER: pin this to the current SonarSource scan action's released tag/SHA —
      # check https://github.com/SonarSource/sonarqube-scan-action for the current major version
      # before pinning; do not guess a version from memory (see AGENTS.md's version-sensitivity rule).
      - name: SonarCloud scan
        uses: SonarSource/sonarqube-scan-action@REPLACE_WITH_CURRENT_PINNED_SHA
        env:
          SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
```

- [ ] **Step 4: Document the setup requirement**

Add a short section to `docs/quality.md` (in the tooling table, add a `Static analysis (second
opinion)` row for both columns pointing at SonarCloud) and to `docs/repository-settings.md` (a new
bullet under a "SonarCloud" heading): create a SonarCloud organization + project pointed at this
repository, set `sonar.projectKey`/`sonar.organization` in `sonar-project.properties` to the real
values, and add a `SONAR_TOKEN` repository secret — the workflow is inert (fails cleanly) until
that's done, exactly like the branch-protection checklist already documents for other
not-yet-configured settings.

- [ ] **Step 5: Verify what can be verified without SonarCloud credentials**

Run: `cd worker && npm run test:coverage` and `cd app && ../scripts/dotnet-coverage.sh` — confirm
both still pass and that `worker/coverage/lcov.info` (or wherever the lcov reporter writes) and
`artifacts/coverage/dotnet/**/coverage.cobertura.xml` actually exist afterward. The SonarCloud scan
step itself cannot be verified without real credentials — report this explicitly, per `AGENTS.md`.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/sonarcloud.yml sonar-project.properties docs/quality.md docs/repository-settings.md worker/vitest.config.ts
git commit -m "feat: add SonarCloud static analysis workflow"
```

### Task D3: Bring dependencies current

**Files:**
- Modify: `worker/package.json`, `worker/package-lock.json`
- Modify: `Directory.Packages.props`
- Modify (if needed): `global.json`
- Modify (if needed): every `packages.lock.json` under `app/*/`

- [ ] **Step 1: Check for outdated npm packages**

Run: `cd worker && npm outdated`

For each outdated devDependency, check its current major-version release notes before bumping
(especially `eslint`, `typescript-eslint`, `wrangler`, `vitest` — this project has already hit one
real version-skew issue, the TypeScript 7 peer-dependency conflict documented in
`docs/dependencies.md#typescript-7`; do not blindly bump `typescript` past `6.x` without re-checking
that constraint against the *current* `typescript-eslint` release, since the constraint may have
lifted since 2026-09-18). Bump what's safe:

```bash
npm install <package>@latest --save-dev   # once per package, or npm update for in-range bumps
npm run quality
```

- [ ] **Step 2: Check for outdated NuGet packages**

Run: `cd app && dotnet list package --outdated` (may need `dotnet restore` first). For each
outdated package in `Directory.Packages.props`, bump the `Version` attribute, then:

```bash
dotnet restore AdyenOutLoud.slnx --force-evaluate
scripts/quality.sh
```

`RestorePackagesWithLockFile` (`Directory.Build.props`) means each project's `packages.lock.json`
must be regenerated after a version bump — `dotnet restore --force-evaluate` does this; commit the
updated lock files alongside the version bump.

- [ ] **Step 3: Check for a newer .NET SDK/workload set**

Run: `dotnet --list-sdks` and check against the current .NET 10 servicing releases. If a newer
patch/workload-set is available and compatible with this project's iOS/Mac Catalyst Xcode
constraint (`docs/development.md#xcode-version-ios--mac-catalyst`), update `global.json`'s
`sdk.version`/`sdk.workloadVersion` together, per `docs/dependencies.md`'s update policy, and re-run
`dotnet workload --info` to confirm.

- [ ] **Step 4: Run the full quality suite**

Run: `scripts/quality.sh` (or `.ps1`)
Expected: PASS. If a bump breaks something, either fix the break or hold that specific package back
and note why (mirroring the existing TypeScript-7 precedent) — don't force a bump that regresses a
gate.

- [ ] **Step 5: Update `docs/dependencies.md` if any bump is noteworthy**

If any bump resolved the TypeScript-7 constraint, or hit a new version-skew issue worth recording,
update the relevant section — otherwise no doc change is needed for routine version bumps (per the
existing update policy, routine bumps don't need documentation, only the *reasons* for pins/holds
do).

- [ ] **Step 6: Commit**

```bash
git add worker/package.json worker/package-lock.json Directory.Packages.props global.json app/*/packages.lock.json docs/dependencies.md
git commit -m "chore: update dependencies to current stable releases"
```

---

## Part E — Delete the changelog

### Task E1: Remove `CHANGELOG.md` and its references

**Files:**
- Delete: `CHANGELOG.md`
- Modify: `docs/releasing.md`
- Modify: `CONTRIBUTING.md`
- Modify: `AGENTS.md`

- [ ] **Step 1: Delete the file**

```bash
git rm CHANGELOG.md
```

- [ ] **Step 2: Update `docs/releasing.md`**

Remove step 2 ("Update `CHANGELOG.md`") from the numbered release process, renumber the remaining
steps, and change step 7 ("Tag and release notes") to say the GitHub Release body is written
directly from the commits/PRs in that release (e.g., using `gh release create --generate-notes`)
instead of copying a changelog section.

- [ ] **Step 3: Search for and remove any other reference**

Run: `grep -rn "CHANGELOG" --include="*.md" .` from the repo root and fix every remaining hit (there
should be none left in `CONTRIBUTING.md`/`AGENTS.md` after this — confirm neither actually mentions
it by name today, since it wasn't seen during research; if the grep finds one, update it to say
"GitHub Releases/tags" instead).

- [ ] **Step 4: Commit**

```bash
git add -u
git commit -m "chore: remove CHANGELOG.md in favor of GitHub tags/releases"
```

---

## Part F — Documentation: ADRs, architecture/protocol/setup docs, streamlining

### Task F1: Write the superseding ADR

**Files:**
- Create: `docs/adr/0007-display-only-stateless-company-scoped-relay.md`
- Modify: `docs/adr/0003-zero-provisioning-instance-token-routing.md` (status line only)
- Modify: `docs/adr/0004-websocket-delivery-and-acknowledgements.md` (status line only)
- Modify: `docs/adr/0005-correlate-display-and-standard-webhooks.md` (status line only)
- Modify: `docs/adr/0002-use-cloudflare-durable-objects.md` (amend consequences, not superseded)
- Modify: `docs/adr/README.md` (index table)

- [ ] **Step 1: Mark 0003, 0004, 0005 as superseded**

Change each file's `## Status` line to `Superseded by [0007](0007-display-only-stateless-company-scoped-relay.md)`
— per `docs/adr/README.md`'s own rule, don't delete or rewrite their history, just update the status.

- [ ] **Step 2: Write ADR 0007**

```markdown
# 0007: Display-only, stateless, company-scoped relay

## Status
Accepted

## Context
Three constraints changed at once, all pointing the same direction:

1. Adyen's Display `TENDER_FINAL` notification never carries payment method or amount (only
   approval result, terminal ID, transaction ID, and timestamp) — see
   [`worker/src/adyen/display-parser.ts`](../../worker/src/adyen/display-parser.ts). The Standard
   `AUTHORISATION` webhook (previously correlated in, per
   [ADR 0005](0005-correlate-display-and-standard-webhooks.md)) was the only source of that data.
   Relying on Display alone means every announcement is the same generic "payment successful" —
   there is nothing left to correlate.
2. Whoever installs the app on a given terminal often has no Adyen Customer Area access at all
   (e.g., a submerchant of a payment-facilitator partner) — the original per-instance-token model
   (each app instance generating its own token and needing its own webhook configured against it,
   per [ADR 0003](0003-zero-provisioning-instance-token-routing.md)) assumed webhook-configuration
   access that doesn't exist for every deployer.
3. With nothing left to correlate and no requirement to survive a disconnected period (see
   Decision below), the durable state that [ADR 0002](0002-use-cloudflare-durable-objects.md) and
   [ADR 0004](0004-websocket-delivery-and-acknowledgements.md) existed to serve — correlation state,
   a replay queue, at-least-once delivery with acknowledgments — no longer has a job to do.

## Decision
- **Display-only.** The Worker only ever ingests Display `TENDER_FINAL` notifications. A successful
  one always produces the same generic `payment_succeeded` message (no `paymentMethod`, no
  `amount` — the relay protocol is bumped to `protocol: 2`, a breaking wire-format change since
  those fields are removed, not just always-null).
- **One relay URL per Adyen company account**, not per app instance. The company's Display webhook
  is configured once, by whoever has Customer Area access, pointing at
  `https://<worker-host>/v1/c/<companyToken>`. Every terminal in that company sends its Display
  notifications to that same URL.
- **Terminal identity is user-entered, not generated.** Each app instance is configured with the
  same company relay URL (shared out-of-band by whoever set it up) plus that specific device's
  terminal serial number, typed in by the installer — no Customer Area access is needed to install
  and configure the app itself. The Worker recovers the same serial from the webhook's `POIID`
  field (`<model>-<serial>`, e.g. `V400m-324688170` -> `324688170`) to know which connected
  terminal(s) to notify.
- **One Durable Object per company**, not per terminal — routed the same way ADR 0003 already
  routed per-instance objects (`SHA-256(companyToken)` as the object name), just with "company"
  instead of "instance" as the unit. Within that object, each terminal's WebSocket connection is
  tagged with its terminal serial via Cloudflare's hibernatable-WebSocket tag API
  (`ctx.acceptWebSocket(socket, [terminalSerial])` / `ctx.getWebSockets(terminalSerial)`), which is
  what fans an ingested notification out to only the matching terminal's connection(s).
- **No persistence, anywhere.** The Durable Object holds no SQLite tables at all. A Display
  notification is parsed and, if successful, pushed directly to whatever sockets are currently
  connected and tagged with the matching terminal serial. If no device for that terminal is
  connected at that instant, the announcement is simply not delivered — there is no queue, no
  replay-on-reconnect, and no server-side deduplication of retried webhooks (the app's own
  client-side recent-event-ID cache, unchanged, is what keeps a retried webhook from being spoken
  twice to a device that *is* connected).
- **No client-to-server acknowledgment.** With nothing durable to reconcile an ACK against, the
  relay protocol drops it entirely — the WebSocket becomes a pure one-way server-to-client push.

## Consequences
- A payment that happens while its terminal's app instance is disconnected (backgrounded on iOS,
  network blip, device off) is never announced, live or later — this is a deliberate trade-off for
  simplicity and reduced data retention, not an oversight. Background execution (Android foreground
  service, desktop platforms not being suspended) narrows the disconnected window but does not
  eliminate it, especially on iOS (foreground-only by decision — see
  [`docs/architecture.md`](../architecture.md#client-architecture-maui)).
- There is no announcement richer than "payment successful" — no amount, no card scheme — because
  the only notification type in use never carries that data. A future richer announcement would
  require re-adding a second, amount-carrying notification source and re-introducing exactly the
  correlation/persistence machinery this decision removes; that trade-off should be revisited
  explicitly (a new ADR), not silently reversed.
- The Worker no longer stores any payment metadata at rest, anywhere, at any retention window —
  [`docs/privacy.md`](../privacy.md) is updated accordingly (Task F4).
- Installing the app no longer requires Adyen Customer Area access; only whoever configures the
  company's Display webhook once needs it. This directly serves partner/submerchant deployments
  that motivated this change.
- The Durable Object class (`RelayObject`) and its Cloudflare Durable Objects infrastructure choice
  from [ADR 0002](0002-use-cloudflare-durable-objects.md) are unchanged and still the right fit —
  only its storage usage is removed; the per-company isolation and hibernatable-WebSocket delivery
  reasoning in ADR 0002 still holds.
```

- [ ] **Step 3: Amend ADR 0002's consequences**

Add one sentence to its `## Consequences` section: "As of [ADR 0007](0007-display-only-stateless-company-scoped-relay.md),
`RelayObject` no longer uses SQLite storage at all — the Durable Objects choice is retained purely
for per-company isolation and hibernatable-WebSocket connection fan-out, not for durable state."

- [ ] **Step 4: Update `docs/adr/README.md`'s index table**

Add a row for `0007` and update the table's implicit reading order note if any (it's just a table —
append the row).

- [ ] **Step 5: Commit**

```bash
git add docs/adr
git commit -m "docs: add ADR 0007 superseding 0003/0004/0005"
```

### Task F2: Rewrite `docs/architecture.md`

**Files:**
- Modify: `docs/architecture.md`

- [ ] **Step 1: Rewrite the end-to-end flow diagram and prose**

Replace the Mermaid diagram and the "Two independent Adyen notifications..." section with a
single-notification flow:

```mermaid
flowchart TD
    T[Adyen terminal] -->|Display webhook: TENDER_FINAL| W[Cloudflare Worker]
    W -->|POST /v1/c/company-token| DO[Durable Object: RelayObject, one per company]
    DO -->|push to sockets tagged with the matching terminal serial| APP[MAUI app]
    APP -->|localized "payment successful" text| TTS[On-device TTS]
```

Explain: the Worker only ingests Display `TENDER_FINAL`; it never carries payment method or amount,
so every announcement is generic; there is no second notification type, no correlation, and no
persistence — see [ADR 0007](adr/0007-display-only-stateless-company-scoped-relay.md).

- [ ] **Step 2: Rewrite "Instance identity and routing" as "Company and terminal identity and
  routing"**

Describe: one relay URL per company (`https://<worker-host>/v1/c/<companyToken>`), configured once
in Adyen by whoever has Customer Area access; each app instance configured with that URL plus its
own terminal serial number; WebSocket URL shape
`wss://<worker-host>/v1/c/<companyToken>/t/<terminalSerial>/ws`; the Worker still derives the
Durable Object name as `SHA-256(companyToken)` (unchanged mechanism, different unit); within that
object, `ctx.acceptWebSocket(socket, [terminalSerial])`/`ctx.getWebSockets(terminalSerial)` route an
ingested notification to the right terminal's connection(s) by tag.

- [ ] **Step 3: Rewrite "HTTP ingress"**

Update the route list to `GET /health`, `POST /v1/c/<companyToken>`, `GET
/v1/c/<companyToken>/t/<terminalSerial>/ws`; remove the dedupe-key paragraph entirely (Task A5
removed it); explain both companyToken and terminalSerial validation fail closed to a generic `404`.

- [ ] **Step 4: Delete "Deduplication before persistence" section entirely**

- [ ] **Step 5: Rewrite "Durable Object" section**

Remove the schema table and the ingress/correlation/alarm prose; replace with: "`RelayObject` holds
no durable state — see [ADR 0007](adr/0007-display-only-stateless-company-scoped-relay.md). `fetch()`
handles two internal routes: `POST /ingest` parses the Display body and, if it's a successful
`TENDER_FINAL`, pushes the generic `payment_succeeded` envelope to every currently-connected socket
tagged with the matching terminal serial; `GET /ws` accepts a new hibernatable WebSocket connection
tagged with the terminal serial from its URL."

- [ ] **Step 6: Rewrite "WebSocket delivery, ACKs, and replay" as "WebSocket delivery"**

Remove ACK/replay content entirely; explain the connection is a pure server-to-client push, and a
message that can't be delivered (no connected socket for that terminal) is simply not delivered —
link to ADR 0007's consequences for the trade-off.

- [ ] **Step 7: Update "Client architecture (MAUI)" for the new abstractions**

Replace `RelayConnectionService`/`PaymentAnnouncementService`/`InstanceIdentityService`/
`RelayProtocol` bullet with the current set (`RelayConnectionService`, `PaymentAnnouncementService`,
`RelayConfigurationService`, `RelayProtocol`); replace the `AppLifecycleCoordinator` paragraph to
describe the new `IBackgroundExecutionService` delegation (Android foreground service keeps it
running in the background; Windows/Mac Catalyst never stop it; iOS still stops on background,
matching today).

- [ ] **Step 8: Update "Dependency direction" diagram** — should already be accurate (no new
  cross-boundary imports were introduced by this plan) — verify and leave as-is if still correct.

- [ ] **Step 9: Delete "Schema and migrations" section entirely** (no schema anymore).

- [ ] **Step 10: Update "Protocol versioning"** — `protocol` is now `2`.

- [ ] **Step 11: Commit**

```bash
git add docs/architecture.md
git commit -m "docs: rewrite architecture.md for the display-only stateless relay"
```

### Task F3: Rewrite `docs/protocol.md`

**Files:**
- Modify: `docs/protocol.md`

- [ ] **Step 1: Update the envelope example**

```json
{
  "protocol": 2,
  "message": {
    "id": "payment:NC6HT9CRT65ZGN82",
    "type": "payment_succeeded",
    "occurredAt": "2026-09-18T12:00:00.000Z",
    "terminalId": "V400m-324688170",
    "transactionId": "CWf3001626182307000.NC6HT9CRT65ZGN82",
    "pspReference": "NC6HT9CRT65ZGN82"
  }
}
```

- [ ] **Step 2: Remove every `paymentMethod`/`amount`-related sentence** and the "generic vs rich"
  distinction — every message is the same shape now.

- [ ] **Step 3: Delete the entire "Client → server: acknowledgment" section.** Replace with a short
  note: "The client sends no messages on this connection — it is a pure server-to-client push. Any
  message a client does send is ignored by the server (see
  [`worker/src/relay-object.ts`](../worker/src/relay-object.ts))."

- [ ] **Step 4: Update "On (re)connect..." paragraph** — remove the replay claim; state instead
  that a new connection receives only messages ingested after it connects — nothing is queued for
  it.

- [ ] **Step 5: Update "Compatibility rules"** — keep the unknown-fields-ignored and
  protocol-version rules; update the versioning example to reference `protocol: 2` as current.

- [ ] **Step 6: Commit**

```bash
git add docs/protocol.md
git commit -m "docs: rewrite protocol.md for protocol 2 (no ack, no paymentMethod/amount)"
```

### Task F4: Rewrite `docs/adyen-setup.md`, `docs/privacy.md`, `docs/threat-model.md`, `docs/security.md`

**Files:**
- Modify: `docs/adyen-setup.md`
- Modify: `docs/privacy.md`
- Modify: `docs/threat-model.md`
- Modify: `docs/security.md`

- [ ] **Step 1: Rewrite `docs/adyen-setup.md`**

Restructure around: (1) an admin with Customer Area access runs
`scripts/generate-company-token.sh` (or generates an equivalent 43-character token any other way)
and configures **only a Display webhook** (no Standard webhook at all anymore) pointed at
`https://<your-worker-host>/v1/c/<token>`; (2) that same admin shares the resulting URL with
whoever installs the app on each terminal; (3) each installer opens the app, pastes the URL into
"Relay URL", and types that specific terminal's serial number (the portion of the terminal ID after
the model prefix — e.g. `324688170` from `V400m-324688170`) into "Terminal serial number", then taps
Save; (4) verification steps updated to mention only the Display webhook and the generic "Payment
successful" announcement (no mention of "rich" announcements with amount/method — that capability
no longer exists).

- [ ] **Step 2: Rewrite `docs/privacy.md`**

Replace "What is persisted, and why" / "Retention" sections — nothing is persisted anywhere in the
Worker anymore. State this plainly: the only data that ever exists is (a) in-flight, in the HTTP
request and the momentary in-memory dispatch inside the Durable Object, and (b) on-device, the
client's bounded recent-event-ID cache (unchanged, still `Preferences`-backed, still 40 entries).
Remove the retention-window paragraph and its rationale entirely (there's no window because there's
no storage). Update "The full parsed set of fields the Worker extracts" to the smaller current set
(no `paymentMethod`, no `amount`).

- [ ] **Step 3: Update `docs/threat-model.md`**

- Update the "Assets" table: remove "Cloudflare Durable Object state (SQLite)" row (no longer
  exists); rename "The generated instance token" / "The webhook URL" rows to "The company relay
  token" / "The company relay URL", noting it's now shared across every terminal in the company (a
  larger blast radius than before — a leaked URL now exposes every terminal in the company, not one
  device; call this out explicitly as a new, real trade-off, not gloss over it).
- Update the "Replay" threat row — a captured request replayed later is no longer deduplicated at
  all (no persistence); it would simply cause a duplicate announcement to whatever terminal is
  currently connected for that serial (mitigated client-side by the existing recent-event-ID cache,
  same as any duplicate delivery).
- Update "Duplicate delivery" row similarly — dedup is now entirely client-side, not
  content-addressed server-side.
- Remove the "Unbounded storage" and "Stale queued announcements" rows entirely (no storage, no
  queue).
- Update "Random-token Durable Object creation" row — still applies, just "company" instead of
  "instance"; note there's no `RETENTION_MS` self-cleaning anymore since there's no data to clean,
  but also nothing accumulates per created object since nothing is stored — an abandoned spam
  object now costs effectively nothing beyond the object's own minimal existence.

- [ ] **Step 4: Update `docs/security.md`**

- "Token handling" section: rename "instance token" to "company token" throughout; note the app no
  longer generates one — it's admin-generated and user-pasted; keep the "never compared or
  transmitted in cleartext to the Durable Object layer" claim (still true — `index.ts` still hashes
  before dispatch) but note the terminal serial *does* reach the Durable Object in cleartext (it has
  to, to route/tag) and is not itself a secret component (it's derivable from any Display webhook
  the company already receives) — document this precisely rather than overclaiming.
- "Logging and redaction" section: update the regression-test description to reference the rewritten
  Task A6 test and "company token" instead of "instance token".

- [ ] **Step 5: Commit**

```bash
git add docs/adyen-setup.md docs/privacy.md docs/threat-model.md docs/security.md
git commit -m "docs: update setup/privacy/threat-model/security docs for the new relay design"
```

### Task F5: Update `docs/testing.md`, `docs/quality.md`, `docs/dependencies.md`, `docs/development.md`, README

**Files:**
- Modify: `docs/testing.md`
- Modify: `docs/quality.md`
- Modify: `docs/dependencies.md`
- Modify: `docs/development.md`
- Modify: `README.md`

- [ ] **Step 1: `docs/testing.md`**

- "Test layers and who owns them" table: update `worker/test/worker.test.ts`'s "What it covers"
  cell to drop "alarms... dedup, retention" and add "company/terminal tag routing, stateless
  delivery"; update `app/AdyenOutLoud.Tests`'s cell to drop "instance identity" wording, keep
  "relay configuration" instead.
- "Concurrency and ordering tests" section: replace its bullet list entirely with the Task A6
  scenarios (terminal isolation, company isolation, no-queue-on-late-connect, malformed input
  handling).
- "UI smoke scenarios" list: replace "generated URL is displayed" with "relay URL and terminal
  serial number can be entered and saved"; add the background-execution checks from Task C4 as new
  bullet points (Android notification appears when backgrounded and payment still announced; iOS
  disconnects when backgrounded and reconnects on foreground).
- Coverage section: update `src/relay-object.ts`'s threshold note if Task A7 changed it; otherwise
  leave as-is.

- [ ] **Step 2: `docs/quality.md`**

Add the SonarCloud row from Task D2 if not already added there. Update the "Baseline" section's
prose to note it needs re-verification after this plan (don't fabricate new numbers — either
re-measure and record real ones, or explicitly mark the baseline table as "needs re-verification
after the display-only relay change" rather than leaving stale numbers that no longer describe the
codebase).

- [ ] **Step 3: `docs/dependencies.md`**

Reflect whatever Task D3 actually changed (new pins, any newly-resolved or newly-hit version-skew
notes). If `typescript` moved off `6.0.3`, update or remove the "TypeScript 7" section accordingly
(don't leave a stale pin explanation for a pin that no longer exists).

- [ ] **Step 4: `docs/development.md`**

Remove the `RelayBaseUrl` subsection entirely (Task B9 deleted the mechanism) — replace the "Quick
start" build command
(`dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android -p:RelayBaseUrl=...`) with a plain
`dotnet build AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-android` and a note that the relay URL and
terminal serial are entered at runtime in the app's UI, not at build time. Update the "Adyen
configuration" pointer sentence if its wording assumed Standard-webhook configuration too.

- [ ] **Step 5: `README.md`**

- "How it works" text: remove the "Adyen sends two independent webhooks..." paragraph; replace with
  a one-sentence version of ADR 0007's summary.
- "No signup step and no shared backend database" paragraph: update to describe company/terminal
  configuration instead of per-instance token generation.
- "Current limitations" list: remove "No HMAC webhook signature verification yet" line's specific
  wording only if inaccurate (it's still accurate — HMAC still isn't implemented — leave it); update
  "No in-app token rotation" to reflect that the relay URL/terminal serial are now freely editable
  in-app (this limitation is resolved — remove that bullet); add a new bullet: "No announcement
  richer than 'payment successful' — Adyen's Display API doesn't carry payment method or amount,
  and there's no correlated second webhook anymore (see
  [ADR 0007](docs/adr/0007-display-only-stateless-company-scoped-relay.md))." Add a bullet noting
  iOS doesn't support background listening (Android/Windows/macOS do).
- "Quick start (development)" section: remove `-p:RelayBaseUrl=https://relay.example.com` from the
  build command example (Task F4 Step 4 already did this in `docs/development.md`; mirror it here).
- Badge/workflow references: no change needed unless a workflow file name changed (it hasn't).

- [ ] **Step 6: Commit**

```bash
git add docs/testing.md docs/quality.md docs/dependencies.md docs/development.md README.md
git commit -m "docs: update testing/quality/dependencies/development docs and README"
```

### Task F6: Streamline — remove redundant/over-detailed documentation

**Files:** any `docs/*.md` touched above, plus a fresh read of the full set.

This is a review pass, not a mechanical step — after Tasks F1–F5 land, re-read every touched doc
file plus `AGENTS.md`/`CONTRIBUTING.md` with fresh eyes and look specifically for:

- **Redundant explanations** — the same fact explained in near-identical words in two files (e.g.,
  if both `docs/architecture.md` and `docs/adyen-setup.md` now separately explain "terminal serial
  is the part of POIID after the model prefix" in full, pick the one place that should own that
  explanation — likely `docs/architecture.md` — and have the other link to it instead of
  re-explaining).
- **Detail disproportionate to project size** — this is a small, single-maintainer-shaped project;
  a paragraph that reads like it's defending a decision to a large team's review board (multiple
  sentences justifying something already obvious from the code) is a candidate to trim to one
  sentence plus a link to the ADR that has the full reasoning, rather than repeating the reasoning
  inline.
- **Stale forward-references** — anything still describing removed behavior in a file this plan
  didn't explicitly touch (search: `grep -rln "Standard webhook\|AUTHORISATION\|paymentMethod\|instance token\|CORRELATION_WAIT_MS\|outbound_messages\|RelayBaseUrl" docs/ README.md AGENTS.md CONTRIBUTING.md .github/` and fix every real hit — a hit inside a Task file under
  `docs/superpowers/plans/` referring to *old* behavior as historical context is fine and expected;
  a hit in a doc that's supposed to describe *current* behavior is a bug).

- [ ] **Step 1: Run the grep above and triage every hit.**

- [ ] **Step 2: For each doc file, make one editorial pass** trimming duplicated/over-long
  explanation per the criteria above. Do not remove content that's the *only* place a genuine
  trade-off is explained (e.g., ADR 0007's Consequences section) — streamlining means cutting
  duplication and disproportionate detail, not cutting substance.

- [ ] **Step 3: Commit**

```bash
git add -u docs README.md AGENTS.md CONTRIBUTING.md
git commit -m "docs: streamline — remove redundant and disproportionate detail"
```

### Task F7: Update `.github` templates and `AGENTS.md`/`CONTRIBUTING.md` repository-map references

**Files:**
- Modify: `.github/ISSUE_TEMPLATE/bug_report.yml` (only if it names removed concepts like
  "instance token" specifically)
- Modify: `AGENTS.md` (repository map / project overview paragraph)
- Modify: `CONTRIBUTING.md` (if it names removed concepts)

- [ ] **Step 1: Read `.github/ISSUE_TEMPLATE/bug_report.yml`**

If it warns against pasting "your webhook URL, instance token, or any live payment data," update the
wording to "your relay URL, terminal serial number, or any live payment data" — keep the warning,
just correct the nouns.

- [ ] **Step 2: Update `AGENTS.md`'s "Project overview" paragraph**

Replace "receives Adyen's two independent webhook types (Display and Standard), correlates them
per-instance in a Durable Object" with "receives Adyen's Display webhook, routed by company and
terminal"; replace "each app instance generates its own high-entropy token, which both identifies it
and routes it to its own Durable Object" with "each app instance is configured with its company's
shared relay URL and its own terminal serial number, which together route it to the right
connection."

- [ ] **Step 3: Verify `CONTRIBUTING.md`** doesn't name any removed concept specifically (it mostly
  links out to `docs/*.md` rather than re-describing behavior, per the earlier read — likely needs
  no change; confirm and leave as-is if so).

- [ ] **Step 4: Commit**

```bash
git add .github/ISSUE_TEMPLATE/bug_report.yml AGENTS.md CONTRIBUTING.md
git commit -m "docs: correct AGENTS.md and issue template wording for the new relay design"
```

---

## Final verification

### Task G1: Full-repository quality suite and manual smoke pass

**Files:** none — verification only.

- [ ] **Step 1: Worker**

```bash
cd worker
npm ci
npm run quality
npm audit --audit-level=high
```
Expected: PASS.

- [ ] **Step 2: .NET**

```bash
scripts/quality.sh   # or scripts/quality.ps1 on Windows
```
Expected: PASS (covers Worker again, plus .NET format/build/architecture-tests/coverage/Android
head build).

- [ ] **Step 3: Platform builds you can run locally**

Run whichever of these your current OS supports (per `docs/development.md`):
```bash
dotnet build app/AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-ios            # macOS + Xcode 26.6 only
dotnet build app/AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-maccatalyst    # macOS + Xcode 26.6 only
dotnet build app/AdyenOutLoud/AdyenOutLoud.csproj -f net10.0-windows10.0.19041.0   # Windows only
```
Report explicitly which of these you could not run and why (per `AGENTS.md`'s reporting rule) —
CI's `platform-builds.yml` covers whatever's not locally verifiable.

- [ ] **Step 4: grep sweep for leftover references**

```bash
grep -rn "v1/i/\|InstanceIdentityService\|IInstanceTokenStore\|DeviceIdentityGenerator\|CurrencyFormatter\|PaymentMethodNames\|CORRELATION_WAIT_MS\|RelayBaseUrl\b" \
  --include="*.cs" --include="*.ts" --include="*.xaml" --include="*.md" --include="*.jsonc" --include="*.json" \
  --exclude-dir=bin --exclude-dir=obj --exclude-dir=node_modules --exclude-dir=coverage --exclude-dir=artifacts .
```
Expected: no hits outside `docs/superpowers/plans/` (this plan file itself, which legitimately
narrates the old-to-new transition) and `docs/adr/000{3,4,5}-*.md` (which legitimately describe the
superseded historical decision). Fix any other hit found.

- [ ] **Step 5: Confirm no dangling `git status` surprises**

```bash
git status
```
Expected: only intentional changes from this plan remain staged/committed; nothing unexpected.

This task has no commit of its own — it's the closing verification gate before opening a PR.
