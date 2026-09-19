import { env } from "cloudflare:workers";
import { reset } from "cloudflare:test";
import { afterEach, describe, expect, it, vi } from "vitest";
import { parseDisplayNotification } from "../src/adyen/display-parser";
import type { OutboundEnvelope } from "../src/adyen/models";
import worker from "../src/index";
import { MAX_BODY_BYTES, RELAY_OBJECT_NAME } from "../src/ingress-rules";
import approvedDisplay from "./fixtures/display-tender-final-approved.json";
import declinedDisplay from "./fixtures/display-tender-final-declined.json";
import unknownValid from "./fixtures/unknown-valid.json";

declare module "cloudflare:workers" {
  // eslint-disable-next-line @typescript-eslint/no-empty-object-type -- re-exports the generated global `Env` (see worker-configuration.d.ts) so `cloudflare:workers`'s `env` is typed without duplicating its shape.
  interface ProvidedEnv extends Env {}
}

const BASE = "https://worker.test";
const testEnv = env;

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
    headers: { "content-type": "application/json" },
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

async function connect(terminalSerial: string): Promise<WebSocket> {
  const response = await fetchWorker(`/ws/${terminalSerial}`, { headers: { upgrade: "websocket" } });
  expect(response.status).toBe(101);
  if (!response.webSocket) throw new Error("Missing WebSocket");
  response.webSocket.accept();
  return response.webSocket;
}

afterEach(async () => reset());

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
    expect((await fetchWorker("/webhook", { method: "POST", body: "{}" })).status).toBe(415);
    expect(
      (
        await fetchWorker("/webhook", {
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
        await fetchWorker("/webhook", {
          method: "POST",
          headers: { "content-type": "application/json" },
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
    const other = await connect("someone-elses-terminal");
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

describe("Durable Object internal contract", () => {
  function stub(): DurableObjectStub {
    return testEnv.PAYMENT_CHANNELS.get(testEnv.PAYMENT_CHANNELS.idFromName(RELAY_OBJECT_NAME));
  }

  it("returns 404 for an unrecognized internal path", async () => {
    const response = await stub().fetch("https://relay.internal/unknown");
    expect(response.status).toBe(404);
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
