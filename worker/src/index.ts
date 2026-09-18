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
