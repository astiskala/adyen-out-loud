import { env } from "cloudflare:workers";
import { evictDurableObject, reset, runDurableObjectAlarm, runInDurableObject } from "cloudflare:test";
import { afterEach, describe, expect, it, vi } from "vitest";
import { parseDisplayNotification } from "../src/adyen/display-parser";
import type { OutboundEnvelope } from "../src/adyen/models";
import { parseStandardAuthorisations } from "../src/adyen/standard-webhook-parser";
import worker, { MAX_BODY_BYTES, tokenToObjectName } from "../src/index";
import { MAX_OUTBOUND_MESSAGES, RelayObject, RETENTION_MS } from "../src/relay-object";
import approvedDisplay from "./fixtures/display-tender-final-approved.json";
import declinedDisplay from "./fixtures/display-tender-final-declined.json";
import failedAuthorisation from "./fixtures/standard-authorisation-failed.json";
import successfulAuthorisation from "./fixtures/standard-authorisation-success.json";
import unknownValid from "./fixtures/unknown-valid.json";

declare module "cloudflare:workers" {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type -- re-exports the generated global `Env` (see worker-configuration.d.ts) so `cloudflare:workers`'s `env` is typed without duplicating its shape.
  interface ProvidedEnv extends Env {}
}

const TOKEN = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
const BASE = "https://worker.test";
const testEnv = env;

async function fetchWorker(path: string, init?: RequestInit): Promise<Response> {
  return worker.fetch(new Request(`${BASE}${path}`, init), testEnv);
}

async function stub(): Promise<DurableObjectStub> {
  const name = await tokenToObjectName(TOKEN);
  return testEnv.PAYMENT_CHANNELS.get(testEnv.PAYMENT_CHANNELS.idFromName(name));
}

async function post(value: unknown, serialized = JSON.stringify(value)): Promise<Response> {
  return fetchWorker(`/v1/i/${TOKEN}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: serialized,
  });
}

async function count(table: string): Promise<number> {
  return runInDurableObject(
    await stub(),
    (_instance: RelayObject, state) =>
      state.storage.sql
        .exec<{ count: number } & Record<string, SqlStorageValue>>(`SELECT COUNT(*) AS count FROM ${table}`)
        .one().count,
  );
}

async function waitForCount(table: string, expected: number): Promise<void> {
  for (let attempt = 0; attempt < 20; attempt++) {
    if ((await count(table)) === expected) return;
    await new Promise((resolve) => setTimeout(resolve, 0));
  }
  expect(await count(table)).toBe(expected);
}

async function expireDisplayDeadline(): Promise<void> {
  const relay = await stub();
  await runInDurableObject(relay, async (instance: RelayObject, state) => {
    state.storage.sql.exec("UPDATE display_states SET deadline_at = ?", Date.now() - 1);
    await instance.alarm();
  });
}

async function queue(): Promise<OutboundEnvelope[]> {
  return runInDurableObject(await stub(), (_instance: RelayObject, state) =>
    state.storage.sql
      .exec<{ payload: string } & Record<string, SqlStorageValue>>(
        "SELECT payload FROM outbound_messages ORDER BY position",
      )
      .toArray()
      .map((row) => JSON.parse(row.payload) as OutboundEnvelope),
  );
}

function displayFor(pspReference: string): typeof approvedDisplay {
  const value = structuredClone(approvedDisplay);
  value.SaleToPOIRequest.DisplayRequest.ReferenceID =
    value.SaleToPOIRequest.DisplayRequest.ReferenceID.replace("NC6HT9CRT65ZGN82", pspReference);
  return value;
}

function authorisationFor(pspReference: string): typeof successfulAuthorisation {
  const value = structuredClone(successfulAuthorisation);
  value.notificationItems[0]!.NotificationRequestItem.pspReference = pspReference;
  return value;
}

function nextMessage(socket: WebSocket): Promise<string> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      reject(new Error("WebSocket message timed out"));
    }, 2_000);
    socket.addEventListener(
      "message",
      (event) => {
        clearTimeout(timeout);
        resolve(String(event.data));
      },
      { once: true },
    );
  });
}

function nextClose(socket: WebSocket): Promise<CloseEvent> {
  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      reject(new Error("WebSocket close timed out"));
    }, 2_000);
    socket.addEventListener(
      "close",
      (event) => {
        clearTimeout(timeout);
        resolve(event);
      },
      { once: true },
    );
  });
}

async function connect(): Promise<WebSocket> {
  const response = await fetchWorker(`/v1/i/${TOKEN}/ws`, { headers: { upgrade: "websocket" } });
  expect(response.status).toBe(101);
  if (!response.webSocket) throw new Error("Missing WebSocket");
  response.webSocket.accept();
  return response.webSocket;
}

afterEach(async () => reset());

describe("public contract and durable ingress", () => {
  it("1. exposes exactly the required health response and route family", async () => {
    const health = await fetchWorker("/health");
    expect(health.status).toBe(200);
    await expect(health.json()).resolves.toEqual({ status: "ok" });
    expect((await fetchWorker(`/webhooks/adyen/${TOKEN}`, { method: "POST" })).status).toBe(404);
    expect((await fetchWorker(`/ws/${TOKEN}`)).status).toBe(404);
  });

  it("2. strictly validates canonical 43-character base64url tokens and hashes DO names", async () => {
    expect((await post(unknownValid)).status).toBe(202);
    expect((await fetchWorker("/v1/i/short", { method: "POST" })).status).toBe(404);
    expect((await fetchWorker(`/v1/i/${"A".repeat(42)}B`, { method: "POST" })).status).toBe(404);
    const name = await tokenToObjectName(TOKEN);
    expect(name).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(name).not.toContain(TOKEN.slice(0, 10));
  });

  it("3. enforces route methods and WebSocket upgrade", async () => {
    expect((await fetchWorker(`/v1/i/${TOKEN}`)).status).toBe(405);
    expect((await fetchWorker(`/v1/i/${TOKEN}/ws`, { method: "POST" })).status).toBe(405);
    const socket = await fetchWorker(`/v1/i/${TOKEN}/ws`);
    expect(socket.status).toBe(426);
    expect(socket.headers.get("upgrade")).toBe("websocket");
  });

  it("4. rejects unsupported media, invalid JSON, and oversized streamed bodies cleanly", async () => {
    expect((await fetchWorker(`/v1/i/${TOKEN}`, { method: "POST", body: "{}" })).status).toBe(415);
    expect(
      (
        await fetchWorker(`/v1/i/${TOKEN}`, {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: "{bad",
        })
      ).status,
    ).toBe(400);
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new Uint8Array(MAX_BODY_BYTES));
        controller.enqueue(new Uint8Array([1]));
        controller.close();
      },
    });
    expect(
      (
        await fetchWorker(`/v1/i/${TOKEN}`, {
          method: "POST",
          headers: { "content-type": "application/json" },
          body: stream,
        })
      ).status,
    ).toBe(413);
    expect(await count("raw_ingress")).toBe(0);
  });

  it("5. durably stores unknown valid Adyen shapes before returning 202", async () => {
    const serialized = JSON.stringify(unknownValid, null, 2);
    expect((await post(unknownValid, serialized)).status).toBe(202);
    const raw = await runInDurableObject(
      await stub(),
      (_instance: RelayObject, state) =>
        state.storage.sql
          .exec<{ body: string } & Record<string, SqlStorageValue>>("SELECT body FROM raw_ingress")
          .one().body,
    );
    expect(raw).toBe(serialized);
    expect(await count("outbound_messages")).toBe(0);
  });

  it("6. deduplicates equivalent JSON using normalized content and stable parsed fields", async () => {
    expect((await post(approvedDisplay)).status).toBe(202);
    expect((await post(approvedDisplay, JSON.stringify(approvedDisplay, null, 4))).status).toBe(202);
    expect(await count("raw_ingress")).toBe(1);
  });

  it("7. isolates post-persistence parser failures from the successful ingress response", async () => {
    const incomplete = structuredClone(approvedDisplay) as unknown as Record<string, unknown>;
    const request = (incomplete.SaleToPOIRequest as Record<string, unknown>).DisplayRequest as Record<
      string,
      unknown
    >;
    request.ReferenceID = "event=TENDER_FINAL&result=APPROVED";
    expect((await post(incomplete)).status).toBe(202);
    await waitForCount("raw_ingress", 1);
    const errors = await runInDurableObject(
      await stub(),
      (_instance: RelayObject, state) =>
        state.storage.sql
          .exec<{ count: number } & Record<string, SqlStorageValue>>(
            "SELECT COUNT(*) AS count FROM raw_ingress WHERE processing_error IS NOT NULL",
          )
          .one().count,
    );
    expect(errors).toBe(1);
  });
});

describe("parsing and correlation", () => {
  it("8. parses and persists required TENDER_FINAL terminal fields without direct publication", async () => {
    expect(parseDisplayNotification(approvedDisplay)).toEqual({
      pspReference: "NC6HT9CRT65ZGN82",
      terminalId: "V400m-324688170",
      transactionId: "CWf3001626182307000.NC6HT9CRT65ZGN82",
      occurredAt: "2026-09-18T12:00:00.000Z",
      result: "APPROVED",
      successful: true,
    });
    expect((await post(approvedDisplay)).status).toBe(202);
    await waitForCount("display_states", 1);
    expect(await count("outbound_messages")).toBe(0);
  });

  it("9. parses and persists Standard AUTHORISATION details without direct publication", async () => {
    expect(parseStandardAuthorisations(successfulAuthorisation)).toEqual([
      {
        pspReference: "NC6HT9CRT65ZGN82",
        occurredAt: "2026-09-18T12:00:01+00:00",
        successful: true,
        paymentMethod: "mc",
        amount: { currency: "EUR", valueMinor: 4508 },
        terminalId: "V400m-324688170",
        transactionId: "CWf3001626182307000",
      },
    ]);
    expect((await post(successfulAuthorisation)).status).toBe(202);
    await waitForCount("authorisation_states", 1);
    expect(await count("outbound_messages")).toBe(0);
  });

  it("10. correlates display then authorisation into exactly one rich envelope", async () => {
    await post(approvedDisplay);
    await post(successfulAuthorisation);
    await waitForCount("outbound_messages", 1);
    expect(await queue()).toEqual([
      {
        protocol: 1,
        message: {
          id: "payment:NC6HT9CRT65ZGN82",
          type: "payment_succeeded",
          occurredAt: "2026-09-18T12:00:00.000Z",
          terminalId: "V400m-324688170",
          transactionId: "CWf3001626182307000.NC6HT9CRT65ZGN82",
          pspReference: "NC6HT9CRT65ZGN82",
          paymentMethod: "mc",
          amount: { currency: "EUR", valueMinor: 4508 },
        },
      },
    ]);
    expect(await count("published")).toBe(1);
  });

  it("11. correlates authorisation then display with the same rich result", async () => {
    await post(successfulAuthorisation);
    await post(approvedDisplay);
    await waitForCount("outbound_messages", 1);
    expect((await queue())[0]?.message).toMatchObject({
      pspReference: "NC6HT9CRT65ZGN82",
      paymentMethod: "mc",
      amount: { currency: "EUR", valueMinor: 4508 },
    });
  });

  it("12. never publishes a declined TENDER_FINAL", async () => {
    await post(declinedDisplay);
    await waitForCount("display_states", 1);
    await expireDisplayDeadline();
    expect(await count("outbound_messages")).toBe(0);
  });

  it("13. never publishes an unmatched authorisation, even when its fallback alarm runs", async () => {
    await post(successfulAuthorisation);
    await waitForCount("authorisation_states", 1);
    const relay = await stub();
    await runInDurableObject(relay, (_instance: RelayObject, state) => state.storage.setAlarm(Date.now()));
    await runDurableObjectAlarm(relay);
    expect(await count("outbound_messages")).toBe(0);
  });

  it("14. publishes a generic envelope only after the durable display deadline", async () => {
    await post(approvedDisplay);
    await waitForCount("display_states", 1);
    expect(await count("outbound_messages")).toBe(0);
    const scheduled = await runInDurableObject(await stub(), (_instance: RelayObject, state) =>
      state.storage.getAlarm(),
    );
    expect(scheduled).not.toBeNull();
    await expireDisplayDeadline();
    expect((await queue())[0]).toEqual({
      protocol: 1,
      message: {
        id: "payment:NC6HT9CRT65ZGN82",
        type: "payment_succeeded",
        occurredAt: "2026-09-18T12:00:00.000Z",
        terminalId: "V400m-324688170",
        transactionId: "CWf3001626182307000.NC6HT9CRT65ZGN82",
        pspReference: "NC6HT9CRT65ZGN82",
        paymentMethod: null,
        amount: null,
      },
    });
  });

  it("15. treats failed authorisation as unmatched and falls back to generic", async () => {
    await post(failedAuthorisation);
    await post(approvedDisplay);
    await waitForCount("display_states", 1);
    expect(await count("outbound_messages")).toBe(0);
    await expireDisplayDeadline();
    expect((await queue())[0]?.message).toMatchObject({ paymentMethod: null, amount: null });
  });

  it("16. never republishes or upgrades a generic event when authorisation arrives late", async () => {
    await post(approvedDisplay);
    await waitForCount("display_states", 1);
    await expireDisplayDeadline();
    await post(successfulAuthorisation);
    await waitForCount("authorisation_states", 1);
    expect(await count("published")).toBe(1);
    expect(await count("outbound_messages")).toBe(1);
    expect((await queue())[0]?.message.amount).toBeNull();
  });

  it("17. suppresses duplicate display and authorisation deliveries after rich publication", async () => {
    await post(approvedDisplay);
    await post(successfulAuthorisation);
    await waitForCount("outbound_messages", 1);
    const displayVariant = structuredClone(approvedDisplay);
    displayVariant.SaleToPOIRequest.DisplayRequest.DisplayOutput = [];
    const authVariant = structuredClone(successfulAuthorisation);
    authVariant.notificationItems[0]!.NotificationRequestItem.reason = "different raw content";
    await post(displayVariant);
    await post(authVariant);
    expect(await count("published")).toBe(1);
    expect(await count("outbound_messages")).toBe(1);
  });

  it("18. applies both 24-hour retention and the outbound count bound", async () => {
    const relay = await stub();
    await runInDurableObject(relay, (_instance: RelayObject, state) => {
      const old = Date.now() - RETENTION_MS - 1;
      state.storage.sql.exec("INSERT INTO raw_ingress VALUES ('old', 'hash', '{}', ?, ?, NULL)", old, old);
      for (let index = 0; index <= MAX_OUTBOUND_MESSAGES; index++) {
        state.storage.sql.exec(
          "INSERT INTO outbound_messages (id, payload, created_at) VALUES (?, '{}', ?)",
          `message-${index}`,
          index === 0 ? old : Date.now(),
        );
      }
      return state.storage.setAlarm(Date.now());
    });
    await runDurableObjectAlarm(relay);
    expect(await count("raw_ingress")).toBe(0);
    expect(await count("outbound_messages")).toBe(MAX_OUTBOUND_MESSAGES);
  });
});

describe("Hibernation WebSocket protocol", () => {
  it("19. broadcasts and replays every unacknowledged envelope in durable order", async () => {
    const live = await connect();
    const firstLive = nextMessage(live);
    await post(displayFor("PSP0000000000001"));
    await post(authorisationFor("PSP0000000000001"));
    expect(JSON.parse(await firstLive).message.pspReference).toBe("PSP0000000000001");
    const secondLive = nextMessage(live);
    await post(displayFor("PSP0000000000002"));
    await post(authorisationFor("PSP0000000000002"));
    expect(JSON.parse(await secondLive).message.pspReference).toBe("PSP0000000000002");
    live.close(1000, "reconnect");

    const replay = await connect();
    expect(JSON.parse(await nextMessage(replay)).message.pspReference).toBe("PSP0000000000001");
    expect(JSON.parse(await nextMessage(replay)).message.pspReference).toBe("PSP0000000000002");
    replay.close(1000, "complete");
  });

  it("20. accepts exactly {type:ack,id}, deletes globally, and excludes ACKed replay", async () => {
    await post(displayFor("PSP0000000000001"));
    await post(authorisationFor("PSP0000000000001"));
    await post(displayFor("PSP0000000000002"));
    await post(authorisationFor("PSP0000000000002"));
    await waitForCount("outbound_messages", 2);

    const invalid = await connect();
    await nextMessage(invalid);
    await nextMessage(invalid);
    const rejected = nextClose(invalid);
    invalid.send(JSON.stringify({ type: "ack", id: "payment:PSP0000000000001", extra: true }));
    expect((await rejected).code).toBe(1008);
    expect(await count("outbound_messages")).toBe(2);

    const socket = await connect();
    const first = JSON.parse(await nextMessage(socket)) as OutboundEnvelope;
    expect((JSON.parse(await nextMessage(socket)) as OutboundEnvelope).message.pspReference).toBe(
      "PSP0000000000002",
    );
    socket.send(JSON.stringify({ type: "ack", id: first.message.id }));
    await waitForCount("outbound_messages", 1);
    await evictDurableObject(await stub(), { webSockets: "hibernate" });

    const replay = await connect();
    const remaining = JSON.parse(await nextMessage(replay)) as OutboundEnvelope;
    expect(remaining.message.pspReference).toBe("PSP0000000000002");
    expect(await count("outbound_messages")).toBe(1);
    socket.close(1000, "acked");
    replay.close(1000, "complete");
  });
});

describe("sensitive-data logging", () => {
  it("never writes the raw instance token to any console output, including on the error path", async () => {
    const calls: unknown[][] = [];
    const spies = (["log", "info", "warn", "error", "debug"] as const).map((method) =>
      vi.spyOn(console, method).mockImplementation((...args: unknown[]) => {
        calls.push(args);
      }),
    );

    try {
      // Exercise the error-logging path (see relay-object.ts `logError`), not just the happy path.
      const incomplete = structuredClone(approvedDisplay) as unknown as Record<string, unknown>;
      const request = (incomplete.SaleToPOIRequest as Record<string, unknown>).DisplayRequest as Record<
        string,
        unknown
      >;
      request.ReferenceID = "event=TENDER_FINAL&result=APPROVED";
      await post(incomplete);
      await waitForCount("raw_ingress", 1);

      // And a normal successful flow, including opening the WebSocket the token is embedded in.
      await post(approvedDisplay);
      await post(successfulAuthorisation);
      await waitForCount("outbound_messages", 1);
      const socket = await connect();
      socket.close(1000, "done");
    } finally {
      for (const spy of spies) spy.mockRestore();
    }

    expect(calls.length).toBeGreaterThan(0);
    const serialized = JSON.stringify(calls);
    expect(serialized).not.toContain(TOKEN);
  });
});
