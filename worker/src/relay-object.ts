import { DurableObject } from "cloudflare:workers";
import { parseDisplayNotification } from "./adyen/display-parser";
import type { DisplayState, OutboundEnvelope } from "./adyen/models";

/**
 * Durable Object that manages WebSocket connections for all terminals.
 * Routes payment notifications from Adyen webhooks to connected terminals via WebSocket,
 * dropping any notification whose terminal has no connected app.
 */
export class RelayObject extends DurableObject<Env> {
  /**
   * Handles incoming HTTP requests to the Durable Object.
   * @param {Request} request - The incoming request.
   * @returns {Promise<Response>} Response for the request.
   */
  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/ingest" && request.method === "POST") return this.ingest(request);
    if (url.pathname === "/ws" && request.method === "GET") return this.acceptSocket(request, url);
    return new Response("Not found", { status: 404 });
  }

  /**
   * Processes an Adyen Display webhook payload.
   * Parses the notification and publishes to connected terminals if payment succeeded.
   * @param {Request} request - The webhook request from Adyen.
   * @returns {Promise<Response>} 202 Accepted (always succeeds to avoid Adyen retries).
   */
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

  /**
   * Handles WebSocket upgrade requests from terminal apps.
   * @param {Request} request - The upgrade request.
   * @param {URL} url - The request URL containing terminal serial in query params.
   * @returns {Response} 101 Switching Protocols on success, or error response.
   */
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

  /**
   * Handles incoming WebSocket messages from clients.
   * The relay is a one-way push (server -> client). Client messages are not part of the
   * protocol; there is no server-side state left for an acknowledgment to reconcile against.
   */
  webSocketMessage(): void {
    // Intentionally empty - client messages are not part of the protocol.
  }

  /**
   * Handles WebSocket close events.
   * Nothing to release: a socket carries no server-side state beyond its terminal tag.
   */
  webSocketClose(): void {
    // Intentionally empty - no server-side state to clean up.
  }

  /**
   * Publishes a successful payment to all WebSocket connections for the terminal.
   * @param {DisplayState} display - The parsed display state for a successful payment.
   */
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

  /**
   * Logs an error event with structured JSON.
   * @param {string} event - The event name for categorization.
   * @param {unknown} error - The error that occurred.
   */
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
