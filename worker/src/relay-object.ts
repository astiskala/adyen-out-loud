import { DurableObject } from "cloudflare:workers";
import { parseDisplayNotification } from "./adyen/display-parser";
import type { DisplayState, OutboundEnvelope } from "./adyen/models";
import {
  type Attempts,
  type Receipt,
  RECEIPT_WINDOW_MS,
  addDevice,
  addReceipt,
  attemptsAllowed,
  bearerToken,
  consumeReceipts,
  hashToken,
  newToken,
  pruneReceipts,
  recordFailure,
  retryAfterSeconds,
} from "./pairing";

const RECEIPTS = "receipts:";
const ATTEMPTS = "attempts:";
const DEVICES = "devices:";

const json = (value: unknown, status: number, headers: Record<string, string> = {}) =>
  Response.json(value, { status, headers });

/**
 * Durable Object relaying Adyen payment notifications to paired terminal apps via WebSocket.
 * Storage holds, per terminal: the last 4 characters of recent approved PSP references (for pairing, pruned
 * by an alarm), failed pairing attempts, and SHA-256 hashes of paired devices' tokens.
 */
export class RelayObject extends DurableObject<Env> {
  /**
   * Routes the Worker's internal requests: /ingest, /ws and /pair.
   * @param {Request} request - The internal request.
   * @returns {Promise<Response>} The response to relay to the caller.
   */
  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    const serial = url.searchParams.get("terminal");
    if (url.pathname === "/ingest" && request.method === "POST") return this.ingest(request);
    if (url.pathname === "/ws" && request.method === "GET") return this.acceptSocket(request, serial);
    if (url.pathname === "/pair" && request.method === "POST") return this.pair(request, serial);
    return new Response("Not found", { status: 404 });
  }

  /**
   * Deletes receipts older than the pairing window and schedules the next cleanup, if any remain.
   * @returns {Promise<void>} Resolves when storage is updated.
   */
  async alarm(): Promise<void> {
    const now = Date.now();
    let next: number | null = null;
    for (const [key, receipts] of await this.ctx.storage.list<Receipt[]>({ prefix: RECEIPTS })) {
      const kept = pruneReceipts(receipts, now);
      if (kept.length === 0) await this.ctx.storage.delete(key);
      else {
        if (kept.length !== receipts.length) await this.ctx.storage.put(key, kept);
        next = Math.min(next ?? Infinity, ...kept.map((r) => r.at + RECEIPT_WINDOW_MS));
      }
    }
    if (next !== null) await this.ctx.storage.setAlarm(next);
  }

  private async ingest(request: Request): Promise<Response> {
    try {
      const display = parseDisplayNotification(JSON.parse(await request.text()));
      if (display?.successful) {
        await this.rememberReceipt(display);
        this.publish(display);
      }
    } catch (e) {
      console.error(
        JSON.stringify({
          level: "error",
          event: "ingest_processing_failed",
          error: e instanceof Error ? e.message : "Unknown",
        }),
      );
    }
    return new Response(null, { status: 202 });
  }

  private async rememberReceipt(d: DisplayState): Promise<void> {
    const key = RECEIPTS + d.terminalSerial;
    const now = Date.now();
    await this.ctx.storage.put(
      key,
      addReceipt((await this.ctx.storage.get<Receipt[]>(key)) ?? [], d.pspReference, now),
    );
    if ((await this.ctx.storage.getAlarm()) === null)
      await this.ctx.storage.setAlarm(now + RECEIPT_WINDOW_MS);
  }

  private async pair(request: Request, serial: string | null): Promise<Response> {
    if (!serial) return new Response("Missing terminal", { status: 400 });
    const codes: unknown = await request.json();
    if (!Array.isArray(codes) || !codes.every((c) => typeof c === "string"))
      return new Response("Bad codes", { status: 400 });

    const now = Date.now();
    const attempts = await this.ctx.storage.get<Attempts>(ATTEMPTS + serial);
    if (attempts && !attemptsAllowed(attempts, now))
      return json({ error: "Too many attempts" }, 429, {
        "retry-after": String(retryAfterSeconds(attempts, now)),
      });

    const receipts = pruneReceipts((await this.ctx.storage.get<Receipt[]>(RECEIPTS + serial)) ?? [], now);
    const remaining = consumeReceipts(codes, receipts);
    if (!remaining) {
      await this.ctx.storage.put(ATTEMPTS + serial, recordFailure(attempts, now));
      return json({ error: "Receipts do not match recent payments on this terminal" }, 403);
    }

    const token = newToken();
    const devices = (await this.ctx.storage.get<string[]>(DEVICES + serial)) ?? [];
    await this.ctx.storage.put({
      [RECEIPTS + serial]: remaining,
      [DEVICES + serial]: addDevice(devices, await hashToken(token)),
    });
    await this.ctx.storage.delete(ATTEMPTS + serial);
    return json({ token }, 200);
  }

  private async acceptSocket(request: Request, serial: string | null): Promise<Response> {
    if (request.headers.get("upgrade")?.toLowerCase() !== "websocket")
      return new Response("WebSocket upgrade required", { status: 426 });
    if (!serial) return new Response("Missing terminal", { status: 400 });
    const token = bearerToken(request.headers.get("authorization"));
    const devices = (await this.ctx.storage.get<string[]>(DEVICES + serial)) ?? [];
    if (!token || !devices.includes(await hashToken(token)))
      return json({ error: "Not paired" }, 401, { "www-authenticate": "Bearer" });
    const pair = new WebSocketPair();
    this.ctx.acceptWebSocket(pair[1], [serial]);
    return new Response(null, { status: 101, webSocket: pair[0] });
  }

  /** Ignores client messages: the protocol is a one-way push. */
  webSocketMessage(): void {
    // Intentionally empty.
  }

  /** Nothing to clean up: sockets hold no server-side state. */
  webSocketClose(): void {
    // Intentionally empty.
  }

  private publish(d: DisplayState): void {
    const payload = JSON.stringify({
      protocol: 2,
      message: {
        id: `payment:${d.pspReference}`,
        type: "payment_succeeded",
        occurredAt: d.occurredAt,
        terminalId: d.terminalId,
        transactionId: d.transactionId,
        pspReference: d.pspReference,
      },
    } satisfies OutboundEnvelope);
    for (const ws of this.ctx.getWebSockets(d.terminalSerial))
      try {
        ws.send(payload);
      } catch {
        // Socket already closing; its close event cleans up.
      }
  }
}
