import { RelayObject } from "./relay-object";
import { canonicalJson, stableIngressFields } from "./identity";

export { RelayObject };

export const MAX_BODY_BYTES = 64 * 1024;
const TOKEN_PATTERN = /^[A-Za-z0-9_-]{43}$/;
const JSON_HEADERS = { "content-type": "application/json; charset=utf-8" };

function json(value: unknown, status = 200, headers?: Record<string, string>): Response {
  return new Response(JSON.stringify(value), { status, headers: { ...JSON_HEADERS, ...headers } });
}

function validToken(token: string): boolean {
  if (!TOKEN_PATTERN.test(token)) return false;
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

function base64Url(buffer: ArrayBuffer): string {
  let binary = "";
  for (const byte of new Uint8Array(buffer)) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
}

async function digest(value: string): Promise<string> {
  return base64Url(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value)));
}

export async function tokenToObjectName(token: string): Promise<string> {
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

async function relay(env: Env, token: string): Promise<DurableObjectStub> {
  const name = await tokenToObjectName(token);
  return env.PAYMENT_CHANNELS.get(env.PAYMENT_CHANNELS.idFromName(name));
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/health" && request.method === "GET") return json({ status: "ok" });

    const ingestMatch = /^\/v1\/i\/([^/]+)$/.exec(url.pathname);
    const socketMatch = /^\/v1\/i\/([^/]+)\/ws$/.exec(url.pathname);
    const match = ingestMatch ?? socketMatch;
    if (!match) return json({ error: "Not found" }, 404);
    const token = match[1] ?? "";
    if (!validToken(token)) return json({ error: "Not found" }, 404);

    if (ingestMatch) {
      if (request.method !== "POST") return json({ error: "Method not allowed" }, 405, { allow: "POST" });
      if (!(request.headers.get("content-type") ?? "").toLowerCase().startsWith("application/json")) {
        return json({ error: "JSON required" }, 415);
      }
      const body = await readLimitedBody(request);
      if (body === null) return json({ error: "Payload too large" }, 413);
      let value: unknown;
      try {
        value = JSON.parse(body);
      } catch {
        return json({ error: "Invalid JSON" }, 400);
      }
      let stableFields: string;
      try {
        stableFields = stableIngressFields(value);
      } catch {
        stableFields = "recognized-invalid";
      }
      const normalizedHash = await digest(canonicalJson(value));
      const dedupeKey = await digest(`${stableFields}|${normalizedHash}`);
      return (await relay(env, token)).fetch("https://relay.internal/ingest", {
        method: "POST",
        headers: { "x-dedupe-key": dedupeKey, "x-normalized-hash": normalizedHash },
        body,
      });
    }

    if (request.method !== "GET") return json({ error: "Method not allowed" }, 405, { allow: "GET" });
    if (request.headers.get("upgrade")?.toLowerCase() !== "websocket") {
      return json({ error: "WebSocket upgrade required" }, 426, { upgrade: "websocket" });
    }
    return (await relay(env, token)).fetch(new Request("https://relay.internal/ws", request));
  },
} satisfies ExportedHandler<Env>;
