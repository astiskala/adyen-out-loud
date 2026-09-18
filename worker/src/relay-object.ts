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

  webSocketClose(): void {
    // Nothing to release: a socket carries no server-side state beyond its terminal tag.
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
