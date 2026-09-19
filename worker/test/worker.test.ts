import { env } from "cloudflare:workers";
import { reset, runDurableObjectAlarm } from "cloudflare:test";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { parseDisplayNotification } from "../src/adyen/display-parser";
import type { OutboundEnvelope } from "../src/adyen/models";
import worker from "../src/index";
import { MAX_BODY_BYTES, MAX_PAIR_BODY_BYTES, RELAY_OBJECT_NAME } from "../src/ingress-rules";
import { MAX_DEVICES, MAX_FAILED_ATTEMPTS, RECEIPT_WINDOW_MS } from "../src/pairing";
import approvedDisplay from "./fixtures/display-tender-final-approved.json";
import declinedDisplay from "./fixtures/display-tender-final-declined.json";
import unknownValid from "./fixtures/unknown-valid.json";

declare module "cloudflare:workers" {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type -- re-exports the generated global `Env` (see worker-configuration.d.ts) so `cloudflare:workers`'s `env` is typed without duplicating its shape.
  interface ProvidedEnv extends Env {}
}

const BASE = "https://worker.test";
const testEnv = env;
const ADYEN_IP = "203.0.113.10";

/**
 * Answers the worker's DNS-over-HTTPS lookups of out.adyen.com without touching the network.
 * @param {(type: string) => unknown[] | Response} answer - Returns the DNS answers (or a raw response) for a record type.
 */
function stubDns(
  answer: (type: string) => unknown[] | Response = (type) =>
    type === "A" ? [{ type: 1, TTL: 300, data: ADYEN_IP }] : [{ type: 28, TTL: 300, data: "2001:db8::10" }],
): void {
  vi.stubGlobal(
    "fetch",
    vi.fn((input: string) => {
      const type = new URL(input).searchParams.get("type") ?? "";
      const result = answer(type);
      return Promise.resolve(
        result instanceof Response ? result : Response.json({ Status: 0, Answer: result }),
      );
    }),
  );
}

async function fetchWorker(path: string, init?: RequestInit): Promise<Response> {
  return worker.fetch(new Request(`${BASE}${path}`, init), testEnv);
}

function displayFor(pspReference: string, terminalId?: string): typeof approvedDisplay {
  const value = structuredClone(approvedDisplay);
  value.SaleToPOIRequest.DisplayRequest.ReferenceID =
    value.SaleToPOIRequest.DisplayRequest.ReferenceID.replace("NC6HT9CRT65ZGN82", pspReference);
  if (terminalId) value.SaleToPOIRequest.MessageHeader.POIID = terminalId;
  return value;
}

async function post(value: unknown, serialized = JSON.stringify(value)): Promise<Response> {
  return fetchWorker("/webhook", {
    method: "POST",
    headers: { "content-type": "application/json", "cf-connecting-ip": ADYEN_IP },
    body: serialized,
  });
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

let receiptCounter = 0;

/**
 * Takes an approved payment on the terminal.
 * @param {string} terminalSerial - The terminal that takes the payment.
 * @returns {Promise<string>} The code at the end of the receipt's PSP reference.
 */
async function takePayment(terminalSerial: string): Promise<string> {
  const psp = `RCPT${String(++receiptCounter).padStart(12, "0")}`;
  expect((await post(displayFor(psp, `V400m-${terminalSerial}`))).status).toBe(202);
  return psp.slice(-4);
}

async function requestPairing(terminalSerial: string, receipts: unknown): Promise<Response> {
  return fetchWorker(`/pair/${terminalSerial}`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ receipts }),
  });
}

/**
 * Pairs a device the way a merchant would: two payments, then the codes from both receipts.
 * @param {string} terminalSerial - The terminal to pair with.
 * @returns {Promise<string>} The device token.
 */
async function pairDevice(terminalSerial: string): Promise<string> {
  const receipts = [await takePayment(terminalSerial), await takePayment(terminalSerial)];
  const response = await requestPairing(terminalSerial, receipts);
  expect(response.status).toBe(200);
  const { token } = await response.json<{ token: string }>();
  return token;
}

function openSocket(terminalSerial: string, token?: string): Promise<Response> {
  return fetchWorker(`/ws/${terminalSerial}`, {
    headers: { upgrade: "websocket", ...(token ? { authorization: `Bearer ${token}` } : {}) },
  });
}

async function connect(terminalSerial: string): Promise<WebSocket> {
  const response = await openSocket(terminalSerial, await pairDevice(terminalSerial));
  expect(response.status).toBe(101);
  if (!response.webSocket) throw new Error("Missing WebSocket");
  response.webSocket.accept();
  return response.webSocket;
}

beforeEach(() => {
  stubDns();
});

afterEach(async () => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  await reset();
});

describe("entrypoint module exports", () => {
  it("exports only the default handler and the Durable Object class", async () => {
    // workerd treats every named export of the entrypoint module as a class or handler and refuses
    // to start ("Incorrect type for map entry") if one is anything else, such as a constant or a
    // helper function. In-process tests import the module directly and never see that failure, so
    // keep non-class exports in other modules (see src/ingress-rules.ts).
    const entrypoint = await import("../src/index");
    expect(Object.keys(entrypoint).sort()).toEqual(["RelayObject", "default"]);
  });
});

describe("webhook source verification", () => {
  const send = (ip?: string): Promise<Response> =>
    fetchWorker("/webhook", {
      method: "POST",
      headers: { "content-type": "application/json", ...(ip ? { "cf-connecting-ip": ip } : {}) },
      body: JSON.stringify(unknownValid),
    });

  it("accepts an IPv4 or IPv6 address that out.adyen.com resolves to", async () => {
    expect((await send(ADYEN_IP)).status).toBe(202);
    expect((await send("2001:DB8::10")).status).toBe(202);
  });

  it("rejects other addresses and requests without a client address", async () => {
    expect((await send("198.51.100.7")).status).toBe(403);
    expect((await send()).status).toBe(403);
  });

  it("does not gate health checks or WebSockets", async () => {
    expect((await fetchWorker("/health")).status).toBe(200);
    expect((await fetchWorker("/ws/324688170")).status).toBe(426);
  });

  it("fails closed with 503 when the address list cannot be resolved", async () => {
    vi.useFakeTimers({ toFake: ["Date"], now: Date.now() + 10 * 60_000 });
    stubDns(() => new Response("nope", { status: 500 }));
    const response = await send(ADYEN_IP);
    expect(response.status).toBe(503);
    expect(response.headers.get("retry-after")).toBe("60");
  });

  it("is skipped when ADYEN_WEBHOOK_HOST is empty (local development)", async () => {
    const response = await worker.fetch(
      new Request(`${BASE}/webhook`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(unknownValid),
      }),
      { ...testEnv, ADYEN_WEBHOOK_HOST: "" } as unknown as typeof testEnv,
    );
    expect(response.status).toBe(202);
  });
});

describe("public contract and routing", () => {
  it("1. exposes exactly the health response and the webhook/ws route family", async () => {
    const health = await fetchWorker("/health");
    expect(health.status).toBe(200);
    await expect(health.json()).resolves.toEqual({ status: "ok" });
    expect((await fetchWorker("/v1/c/anything", { method: "POST" })).status).toBe(404);
    expect((await fetchWorker("/ws")).status).toBe(404);
  });

  it("2. accepts a webhook with no connected terminal and stores nothing", async () => {
    expect((await post(unknownValid)).status).toBe(202);
    expect(RELAY_OBJECT_NAME).toBe("relay");
  });

  it("3. enforces route methods and WebSocket upgrade", async () => {
    expect((await fetchWorker("/webhook")).status).toBe(405);
    expect((await fetchWorker("/ws/324688170", { method: "POST" })).status).toBe(405);
    const socket = await fetchWorker("/ws/324688170");
    expect(socket.status).toBe(426);
    expect(socket.headers.get("upgrade")).toBe("websocket");
  });

  it("4. rejects unsupported media, invalid JSON, and oversized streamed bodies cleanly", async () => {
    const asAdyen = { "cf-connecting-ip": ADYEN_IP };
    expect((await fetchWorker("/webhook", { method: "POST", headers: asAdyen, body: "{}" })).status).toBe(
      415,
    );
    expect(
      (
        await fetchWorker("/webhook", {
          method: "POST",
          headers: { "content-type": "application/json", ...asAdyen },
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
        await fetchWorker("/webhook", {
          method: "POST",
          headers: { "content-type": "application/json", ...asAdyen },
          body: stream,
        })
      ).status,
    ).toBe(413);
  });

  it("5. accepts a well-formed request with 202 even with no connected socket", async () => {
    expect((await post(approvedDisplay)).status).toBe(202);
  });

  it("6. rejects a malformed terminal serial on WebSocket upgrade", async () => {
    const response = await fetchWorker(`/ws/${encodeURIComponent("bad serial")}`, {
      headers: { upgrade: "websocket" },
    });
    expect(response.status).toBe(404);
  });
});

describe("Display parsing", () => {
  it("7. parses required TENDER_FINAL terminal fields, including the derived serial", () => {
    expect(parseDisplayNotification(approvedDisplay)).toEqual({
      pspReference: "NC6HT9CRT65ZGN82",
      terminalId: "V400m-324688170",
      terminalSerial: "324688170",
      transactionId: "CWf3001626182307000.NC6HT9CRT65ZGN82",
      occurredAt: "2026-09-18T12:00:00.000Z",
      result: "APPROVED",
      successful: true,
    });
  });
});

describe("WebSocket relay", () => {
  it("8. delivers a generic payment_succeeded message only to the matching terminal", async () => {
    const matching = await connect("324688170");
    const other = await connect("555000111");
    const otherMessages: string[] = [];
    other.addEventListener("message", (event) => otherMessages.push(String(event.data)));

    const nextForMatching = nextMessage(matching);
    expect((await post(approvedDisplay)).status).toBe(202);

    const payload = JSON.parse(await nextForMatching) as OutboundEnvelope;
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
    matching.close(1000, "done");
    other.close(1000, "done");
  });

  it("9. never delivers a declined Display notification", async () => {
    const socket = await connect("324688170");
    const messages: string[] = [];
    socket.addEventListener("message", (event) => messages.push(String(event.data)));

    expect((await post(declinedDisplay)).status).toBe(202);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(messages).toEqual([]);
    socket.close(1000, "done");
  });

  it("10. drops a notification for a terminal that has no connected app", async () => {
    const bystander = await connect("999");
    const messages: string[] = [];
    bystander.addEventListener("message", (event) => messages.push(String(event.data)));

    expect((await post(approvedDisplay)).status).toBe(202);
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(messages).toEqual([]);
    bystander.close(1000, "done");
  });

  it("11. does not queue a message for a terminal that connects after ingest", async () => {
    expect((await post(displayFor("PSP-LATE"))).status).toBe(202);
    const lateSocket = await connect("324688170");
    const messages: string[] = [];
    lateSocket.addEventListener("message", (event) => messages.push(String(event.data)));
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(messages).toEqual([]);
    lateSocket.close(1000, "done");
  });

  it("12. ignores an arbitrary client message instead of erroring or closing", async () => {
    const socket = await connect("324688170");
    let closed = false;
    socket.addEventListener("close", () => {
      closed = true;
    });
    socket.send("not an ack, not anything");
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(closed).toBe(false);
    socket.close(1000, "done");
  });

  it("13. routes multiple terminals independently", async () => {
    const terminalA = await connect("111");
    const terminalB = await connect("222");
    const aMessage = nextMessage(terminalA);
    const bMessages: string[] = [];
    terminalB.addEventListener("message", (event) => bMessages.push(String(event.data)));

    expect((await post(displayFor("PSP-A", "V400m-111"))).status).toBe(202);
    expect((JSON.parse(await aMessage) as OutboundEnvelope).message.pspReference).toBe("PSP-A");
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(bMessages).toEqual([]);
    terminalA.close(1000, "done");
    terminalB.close(1000, "done");
  });
});

describe("pairing", () => {
  const Terminal = "324688170";

  it("issues a token for two recent receipts, and only that token opens the terminal's socket", async () => {
    const token = await pairDevice(Terminal);
    expect(token).toMatch(/^[A-Za-z0-9_-]{43}$/);

    const socket = await openSocket(Terminal, token);
    expect(socket.status).toBe(101);
    socket.webSocket?.accept();
    socket.webSocket?.close(1000, "done");
  });

  it("refuses a socket with no token, an unknown token, or another terminal's token", async () => {
    const otherToken = await pairDevice("111");
    for (const response of [
      await openSocket(Terminal),
      await openSocket(Terminal, "not-a-real-token"),
      await openSocket(Terminal, otherToken),
      await fetchWorker(`/ws/${Terminal}`, { headers: { upgrade: "websocket", authorization: "Basic abc" } }),
    ]) {
      expect(response.status).toBe(401);
      expect(response.headers.get("www-authenticate")).toBe("Bearer");
    }
  });

  it("accepts receipt codes in any case and order", async () => {
    const first = await takePayment(Terminal);
    const second = await takePayment(Terminal);
    expect((await requestPairing(Terminal, [second.toLowerCase(), first])).status).toBe(200);
  });

  it("rejects codes that do not match, belong to another terminal, or repeat one receipt", async () => {
    const mine = await takePayment(Terminal);
    const theirs = await takePayment("111");
    expect((await requestPairing(Terminal, [mine, "ZZZZ"])).status).toBe(403);
    expect((await requestPairing(Terminal, [mine, theirs])).status).toBe(403);
    expect((await requestPairing(Terminal, [mine, mine])).status).toBe(403);
  });

  it("uses each receipt for one pairing only", async () => {
    const receipts = [await takePayment(Terminal), await takePayment(Terminal)];
    expect((await requestPairing(Terminal, receipts)).status).toBe(200);
    expect((await requestPairing(Terminal, receipts)).status).toBe(403);
  });

  it("does not accept a declined payment's receipt", async () => {
    expect((await post(declinedDisplay)).status).toBe(202);
    const approved = await takePayment(Terminal);
    expect((await requestPairing(Terminal, [approved, "INED"])).status).toBe(403);
  });

  it("rejects receipts older than the pairing window", async () => {
    const receipts = [await takePayment(Terminal), await takePayment(Terminal)];
    vi.useFakeTimers({ toFake: ["Date"], now: Date.now() + RECEIPT_WINDOW_MS + 1 });
    expect((await requestPairing(Terminal, receipts)).status).toBe(403);
  });

  it("throttles failed attempts per terminal until the window passes", async () => {
    for (let i = 0; i < MAX_FAILED_ATTEMPTS; i++)
      expect((await requestPairing(Terminal, ["AAAA", "BBBB"])).status).toBe(403);

    const receipts = [await takePayment(Terminal), await takePayment(Terminal)];
    const throttled = await requestPairing(Terminal, receipts);
    expect(throttled.status).toBe(429);
    expect(Number(throttled.headers.get("retry-after"))).toBeGreaterThan(0);
    expect((await pairDevice("111")).length).toBeGreaterThan(0);

    vi.useFakeTimers({ toFake: ["Date"], now: Date.now() + RECEIPT_WINDOW_MS });
    expect((await requestPairing(Terminal, ["AAAA", "BBBB"])).status).toBe(403);
  });

  it("keeps the most recent devices and forgets the oldest", async () => {
    const tokens: string[] = [];
    for (let i = 0; i <= MAX_DEVICES; i++) tokens.push(await pairDevice(Terminal));
    expect((await openSocket(Terminal, tokens[0])).status).toBe(401);
    const newest = await openSocket(Terminal, tokens[MAX_DEVICES]);
    expect(newest.status).toBe(101);
    newest.webSocket?.accept();
    newest.webSocket?.close(1000, "done");
  });

  it("validates the request before it reaches the relay", async () => {
    const pair = (init: RequestInit, serial = Terminal) => fetchWorker(`/pair/${serial}`, init);
    const asJson = { "content-type": "application/json" };
    expect((await pair({ method: "GET" })).status).toBe(405);
    expect((await pair({ method: "POST", body: "{}" })).status).toBe(415);
    expect((await pair({ method: "POST", headers: asJson, body: "{bad" })).status).toBe(400);
    expect(
      (await pair({ method: "POST", headers: asJson, body: "x".repeat(MAX_PAIR_BODY_BYTES + 1) })).status,
    ).toBe(413);
    expect((await requestPairing(Terminal, ["AB12"])).status).toBe(400);
    expect((await requestPairing(Terminal, ["AB12", "CD3"])).status).toBe(400);
    expect((await requestPairing(Terminal, ["AB12", "CD3!"])).status).toBe(400);
    expect((await requestPairing(Terminal, ["AB12", 1234])).status).toBe(400);
    expect((await pair({ method: "POST", headers: asJson, body: "{}" }, "bad%20serial")).status).toBe(404);
  });

  it("prunes expired receipts when the alarm fires", async () => {
    const stale = [await takePayment(Terminal), await takePayment(Terminal)];
    vi.useFakeTimers({ toFake: ["Date"], now: Date.now() + RECEIPT_WINDOW_MS / 2 });
    const fresh = [await takePayment("111"), await takePayment("111")];
    vi.useFakeTimers({ toFake: ["Date"], now: Date.now() + RECEIPT_WINDOW_MS / 2 + 1 });

    const stub = testEnv.PAYMENT_CHANNELS.get(testEnv.PAYMENT_CHANNELS.idFromName(RELAY_OBJECT_NAME));
    expect(await runDurableObjectAlarm(stub)).toBe(true);

    expect((await requestPairing(Terminal, stale)).status).toBe(403);
    expect((await requestPairing("111", fresh)).status).toBe(200);
  });
});

describe("Durable Object internal contract", () => {
  function stub(): DurableObjectStub {
    return testEnv.PAYMENT_CHANNELS.get(testEnv.PAYMENT_CHANNELS.idFromName(RELAY_OBJECT_NAME));
  }

  it("returns 404 for an unrecognized internal path", async () => {
    const response = await stub().fetch("https://relay.internal/unknown");
    expect(response.status).toBe(404);
  });

  it("returns 400 when /ws or /pair is missing the terminal, or /pair has no code list", async () => {
    expect(
      (await stub().fetch("https://relay.internal/ws", { headers: { upgrade: "websocket" } })).status,
    ).toBe(400);
    expect((await stub().fetch("https://relay.internal/pair", { method: "POST", body: "[]" })).status).toBe(
      400,
    );
    expect(
      (await stub().fetch("https://relay.internal/pair?terminal=1", { method: "POST", body: '{"a":1}' }))
        .status,
    ).toBe(400);
  });

  it("returns 426 when /ws is requested without an upgrade header", async () => {
    const response = await stub().fetch("https://relay.internal/ws?terminal=324688170");
    expect(response.status).toBe(426);
  });

  it("logs and swallows a recognized-but-incomplete TENDER_FINAL instead of throwing", async () => {
    const calls: unknown[][] = [];
    const spy = vi.spyOn(console, "error").mockImplementation((...args: unknown[]) => {
      calls.push(args);
    });
    try {
      const response = await stub().fetch("https://relay.internal/ingest", {
        method: "POST",
        body: JSON.stringify({
          SaleToPOIRequest: {
            MessageHeader: { MessageCategory: "Display", MessageType: "Request", POIID: "V400m-324688170" },
            DisplayRequest: { ReferenceID: "event=TENDER_FINAL&result=APPROVED" },
          },
        }),
      });
      expect(response.status).toBe(202);
    } finally {
      spy.mockRestore();
    }
    expect(calls.length).toBe(1);
    expect(JSON.stringify(calls)).toContain("ingest_processing_failed");
  });
});
