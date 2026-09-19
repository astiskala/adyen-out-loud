import { MAX_BODY_BYTES, MAX_PAIR_BODY_BYTES, RELAY_OBJECT_NAME } from "./ingress-rules";
import { parsePairRequest } from "./pairing";
import { RelayObject } from "./relay-object";
import { isWebhookSource } from "./webhook-source";

export { RelayObject };

const TERMINAL_SERIAL_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;
const JSON_HEADERS = { "content-type": "application/json; charset=utf-8" };

const json = (value: unknown, status = 200, headers?: Record<string, string>) =>
  new Response(JSON.stringify(value), { status, headers: { ...JSON_HEADERS, ...headers } });

const validTerminalSerial = (serial: string) => TERMINAL_SERIAL_PATTERN.test(serial);

async function readLimitedBody(request: Request, limit = MAX_BODY_BYTES): Promise<string | null> {
  const len = Number(request.headers.get("content-length"));
  if (Number.isFinite(len) && len > limit) return null;
  if (!request.body) return "";
  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > limit) return (reader.cancel(), null);
    chunks.push(value);
  }
  return new TextDecoder().decode(Uint8Array.from(chunks.flatMap((c) => [...c])));
}

const relay = (env: Env) => env.PAYMENT_CHANNELS.get(env.PAYMENT_CHANNELS.idFromName(RELAY_OBJECT_NAME));

/**
 * Cloudflare Worker entry point:
 * - GET /health: Health check
 * - POST /webhook: Adyen Display webhook (validated by source IP via DNS)
 * - POST /pair/{serial}: pairs a device by quoting recent receipts; returns its token
 * - GET /ws/{serial}: WebSocket for a paired device (Authorization: Bearer <token>)
 */
export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === "/health" && request.method === "GET") return json({ status: "ok" });

    if (url.pathname === "/webhook") {
      if (request.method !== "POST") return json({ error: "Method not allowed" }, 405, { allow: "POST" });
      const host: string = env.ADYEN_WEBHOOK_HOST;
      if (host) {
        try {
          if (!(await isWebhookSource(host, request.headers.get("cf-connecting-ip"))))
            return json({ error: "Forbidden" }, 403);
        } catch {
          return json({ error: "Source verification unavailable" }, 503, { "retry-after": "60" });
        }
      }
      if (!(request.headers.get("content-type") ?? "").toLowerCase().startsWith("application/json"))
        return json({ error: "JSON required" }, 415);
      const body = await readLimitedBody(request);
      if (body === null) return json({ error: "Payload too large" }, 413);
      try {
        JSON.parse(body);
      } catch {
        return json({ error: "Invalid JSON" }, 400);
      }
      return relay(env).fetch("https://relay.internal/ingest", { method: "POST", body });
    }

    const pairSerial = /^\/pair\/([^/]+)$/.exec(url.pathname)?.[1];
    if (pairSerial !== undefined) {
      const serial = pairSerial;
      if (!validTerminalSerial(serial)) return json({ error: "Not found" }, 404);
      if (request.method !== "POST") return json({ error: "Method not allowed" }, 405, { allow: "POST" });
      if (!(request.headers.get("content-type") ?? "").toLowerCase().startsWith("application/json"))
        return json({ error: "JSON required" }, 415);
      const body = await readLimitedBody(request, MAX_PAIR_BODY_BYTES);
      if (body === null) return json({ error: "Payload too large" }, 413);
      let codes: string[] | null;
      try {
        codes = parsePairRequest(JSON.parse(body));
      } catch {
        codes = null;
      }
      if (!codes) return json({ error: "Expected two receipt codes" }, 400);
      return relay(env).fetch(`https://relay.internal/pair?terminal=${encodeURIComponent(serial)}`, {
        method: "POST",
        body: JSON.stringify(codes),
      });
    }

    const socketSerial = /^\/ws\/([^/]+)$/.exec(url.pathname)?.[1];
    if (socketSerial !== undefined) {
      const serial = socketSerial;
      if (!validTerminalSerial(serial)) return json({ error: "Not found" }, 404);
      if (request.method !== "GET") return json({ error: "Method not allowed" }, 405, { allow: "GET" });
      if (request.headers.get("upgrade")?.toLowerCase() !== "websocket")
        return json({ error: "WebSocket upgrade required" }, 426, { upgrade: "websocket" });
      return relay(env).fetch(
        new Request(`https://relay.internal/ws?terminal=${encodeURIComponent(serial)}`, request),
      );
    }

    return json({ error: "Not found" }, 404);
  },
} satisfies ExportedHandler<Env>;
