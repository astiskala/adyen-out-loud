import { MAX_BODY_BYTES, RELAY_OBJECT_NAME } from "./ingress-rules";
import { RelayObject } from "./relay-object";
import { isWebhookSource } from "./webhook-source";

export { RelayObject };

const TERMINAL_SERIAL_PATTERN = /^[A-Za-z0-9_-]{1,64}$/;
const JSON_HEADERS = { "content-type": "application/json; charset=utf-8" };

function json(value: unknown, status = 200, headers?: Record<string, string>): Response {
  return new Response(JSON.stringify(value), { status, headers: { ...JSON_HEADERS, ...headers } });
}

function validTerminalSerial(serial: string): boolean {
  return TERMINAL_SERIAL_PATTERN.test(serial);
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

function relay(env: Env): DurableObjectStub {
  return env.PAYMENT_CHANNELS.get(env.PAYMENT_CHANNELS.idFromName(RELAY_OBJECT_NAME));
}

/**
 * Cloudflare Worker entry point handling:
 * - GET /health: Health check endpoint.
 * - POST /webhook: Ingest a Display webhook from Adyen (the one URL every account configures).
 *   Only accepted from the addresses ADYEN_WEBHOOK_HOST (out.adyen.com) resolves to; an empty value
 *   disables the check for local development.
 * - GET /ws/{terminalSerial}: WebSocket upgrade for a terminal's app.
 * A webhook for a terminal with no connected app is dropped; nothing is stored.
 */
export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/health" && request.method === "GET") return json({ status: "ok" });

    if (url.pathname === "/webhook") {
      if (request.method !== "POST") return json({ error: "Method not allowed" }, 405, { allow: "POST" });
      const sourceHost: string = env.ADYEN_WEBHOOK_HOST;
      if (sourceHost) {
        let allowed: boolean;
        try {
          allowed = await isWebhookSource(sourceHost, request.headers.get("cf-connecting-ip"));
        } catch {
          return json({ error: "Source verification unavailable" }, 503, { "retry-after": "60" });
        }
        if (!allowed) return json({ error: "Forbidden" }, 403);
      }
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
      return relay(env).fetch("https://relay.internal/ingest", { method: "POST", body });
    }

    const socketMatch = /^\/ws\/([^/]+)$/.exec(url.pathname);
    if (socketMatch) {
      const terminalSerial = socketMatch[1] ?? "";
      if (!validTerminalSerial(terminalSerial)) return json({ error: "Not found" }, 404);

      if (request.method !== "GET") return json({ error: "Method not allowed" }, 405, { allow: "GET" });
      if (request.headers.get("upgrade")?.toLowerCase() !== "websocket") {
        return json({ error: "WebSocket upgrade required" }, 426, { upgrade: "websocket" });
      }
      const target = `https://relay.internal/ws?terminal=${encodeURIComponent(terminalSerial)}`;
      return relay(env).fetch(new Request(target, request));
    }

    return json({ error: "Not found" }, 404);
  },
} satisfies ExportedHandler<Env>;
