import { DurableObject } from "cloudflare:workers";
import { parseDisplayNotification } from "./adyen/display-parser";
import { CORRELATION_WAIT_MS, correlatePayment } from "./adyen/payment-correlator";
import { parseStandardAuthorisations } from "./adyen/standard-webhook-parser";
import type { AuthorisationState, DisplayState, OutboundEnvelope } from "./adyen/models";

export const RETENTION_MS = 24 * 60 * 60 * 1_000;
export const MAX_OUTBOUND_MESSAGES = 100;

interface RawRow extends Record<string, SqlStorageValue> {
  dedupe_key: string;
  body: string;
  received_at: number;
}

interface DisplayRow extends Record<string, SqlStorageValue> {
  psp_reference: string;
  terminal_id: string;
  transaction_id: string;
  occurred_at: string;
  successful: number;
  result: string;
  deadline_at: number;
}

interface AuthorisationRow extends Record<string, SqlStorageValue> {
  psp_reference: string;
  occurred_at: string;
  successful: number;
  payment_method: string | null;
  amount_currency: string | null;
  amount_value_minor: number | null;
  terminal_id: string | null;
  transaction_id: string | null;
}

interface OutboundRow extends Record<string, SqlStorageValue> {
  payload: string;
}

export class RelayObject extends DurableObject<Env> {
  private readonly sql: SqlStorage;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    this.sql.exec(`
      CREATE TABLE IF NOT EXISTS raw_ingress (
        dedupe_key TEXT PRIMARY KEY,
        normalized_hash TEXT NOT NULL,
        body TEXT NOT NULL,
        received_at INTEGER NOT NULL,
        processed_at INTEGER,
        processing_error TEXT
      );
      CREATE INDEX IF NOT EXISTS raw_pending ON raw_ingress(processed_at, received_at);

      CREATE TABLE IF NOT EXISTS display_states (
        psp_reference TEXT PRIMARY KEY,
        terminal_id TEXT NOT NULL,
        transaction_id TEXT NOT NULL,
        occurred_at TEXT NOT NULL,
        successful INTEGER NOT NULL,
        result TEXT NOT NULL,
        deadline_at INTEGER NOT NULL,
        updated_at INTEGER NOT NULL
      );
      CREATE INDEX IF NOT EXISTS display_deadlines ON display_states(successful, deadline_at);

      CREATE TABLE IF NOT EXISTS authorisation_states (
        psp_reference TEXT PRIMARY KEY,
        occurred_at TEXT NOT NULL,
        successful INTEGER NOT NULL,
        payment_method TEXT,
        amount_currency TEXT,
        amount_value_minor INTEGER,
        terminal_id TEXT,
        transaction_id TEXT,
        updated_at INTEGER NOT NULL
      );

      CREATE TABLE IF NOT EXISTS published (
        psp_reference TEXT PRIMARY KEY,
        message_id TEXT NOT NULL UNIQUE,
        publication_kind TEXT NOT NULL,
        created_at INTEGER NOT NULL
      );

      CREATE TABLE IF NOT EXISTS outbound_messages (
        position INTEGER PRIMARY KEY AUTOINCREMENT,
        id TEXT NOT NULL UNIQUE,
        payload TEXT NOT NULL,
        created_at INTEGER NOT NULL
      );
    `);
  }

  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/ingest" && request.method === "POST") return this.ingest(request);
    if (url.pathname === "/ws" && request.method === "GET") return this.acceptSocket(request);
    return new Response("Not found", { status: 404 });
  }

  private async ingest(request: Request): Promise<Response> {
    const dedupeKey = request.headers.get("x-dedupe-key");
    const normalizedHash = request.headers.get("x-normalized-hash");
    if (!dedupeKey || !normalizedHash) return new Response("Bad request", { status: 400 });
    const body = await request.text();
    const now = Date.now();
    const inserted = this.sql.exec(
      `INSERT OR IGNORE INTO raw_ingress
       (dedupe_key, normalized_hash, body, received_at) VALUES (?, ?, ?, ?)`,
      dedupeKey,
      normalizedHash,
      body,
      now,
    ).rowsWritten;
    await this.ensureAlarm(now + CORRELATION_WAIT_MS);
    await this.ctx.storage.sync();

    if (inserted > 0) {
      this.ctx.waitUntil(
        Promise.resolve()
          .then(() => this.processRaw(dedupeKey))
          .catch((error: unknown) => {
            this.logError("ingress_processing_failed", error, { dedupeKey });
          }),
      );
    }
    return new Response(null, { status: 202 });
  }

  private acceptSocket(request: Request): Response {
    if (request.headers.get("upgrade")?.toLowerCase() !== "websocket") {
      return new Response("WebSocket upgrade required", { status: 426 });
    }
    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];
    this.ctx.acceptWebSocket(server);
    const rows = this.sql
      .exec<OutboundRow>("SELECT payload FROM outbound_messages ORDER BY position")
      .toArray();
    for (const row of rows) server.send(row.payload);
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(socket: WebSocket, message: string | ArrayBuffer): Promise<void> {
    if (typeof message !== "string") {
      socket.close(1008, "Invalid ACK");
      return;
    }
    try {
      const ack = JSON.parse(message) as Record<string, unknown>;
      const keys = Object.keys(ack).sort();
      if (
        keys.length !== 2 ||
        keys[0] !== "id" ||
        keys[1] !== "type" ||
        ack.type !== "ack" ||
        typeof ack.id !== "string" ||
        ack.id.length === 0
      ) {
        throw new Error("Invalid ACK");
      }
      this.sql.exec("DELETE FROM outbound_messages WHERE id = ?", ack.id);
      await this.ctx.storage.sync();
    } catch {
      socket.close(1008, "Invalid ACK");
    }
  }

  async alarm(): Promise<void> {
    try {
      const pending = this.sql
        .exec<RawRow>(
          `SELECT dedupe_key, body, received_at FROM raw_ingress
           WHERE processed_at IS NULL ORDER BY received_at LIMIT 100`,
        )
        .toArray();
      for (const row of pending) await this.processRaw(row.dedupe_key);

      const due = this.sql
        .exec<{ psp_reference: string } & Record<string, SqlStorageValue>>(
          `SELECT d.psp_reference FROM display_states d
           LEFT JOIN published p ON p.psp_reference = d.psp_reference
           WHERE d.successful = 1 AND d.deadline_at <= ? AND p.psp_reference IS NULL`,
          Date.now(),
        )
        .toArray();
      for (const row of due) this.tryPublish(row.psp_reference, Date.now());
      this.prune(Date.now());
    } catch (error) {
      this.logError("alarm_processing_failed", error);
    }
    await this.scheduleNextAlarm();
  }

  private async processRaw(dedupeKey: string): Promise<void> {
    const row = this.sql
      .exec<RawRow>(
        `SELECT dedupe_key, body, received_at FROM raw_ingress
         WHERE dedupe_key = ? AND processed_at IS NULL`,
        dedupeKey,
      )
      .toArray()[0];
    if (!row) return;

    const touched = new Set<string>();
    try {
      const value: unknown = JSON.parse(row.body);
      const display = parseDisplayNotification(value);
      if (display) {
        this.storeDisplay(display, row.received_at);
        touched.add(display.pspReference);
      }
      for (const authorisation of parseStandardAuthorisations(value)) {
        this.storeAuthorisation(authorisation, row.received_at);
        touched.add(authorisation.pspReference);
      }
      this.sql.exec("UPDATE raw_ingress SET processed_at = ? WHERE dedupe_key = ?", Date.now(), dedupeKey);
    } catch (error) {
      const reason = error instanceof Error ? error.message.slice(0, 200) : "Processing failed";
      this.sql.exec(
        "UPDATE raw_ingress SET processed_at = ?, processing_error = ? WHERE dedupe_key = ?",
        Date.now(),
        reason,
        dedupeKey,
      );
      this.logError("payload_rejected_after_persistence", error, { dedupeKey });
    }

    for (const pspReference of touched) this.tryPublish(pspReference, Date.now());
    this.prune(Date.now());
    await this.scheduleNextAlarm();
  }

  private storeDisplay(display: DisplayState, receivedAt: number): void {
    this.sql.exec(
      `INSERT INTO display_states
       (psp_reference, terminal_id, transaction_id, occurred_at, successful, result, deadline_at, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)
       ON CONFLICT(psp_reference) DO UPDATE SET
         terminal_id = excluded.terminal_id,
         transaction_id = excluded.transaction_id,
         occurred_at = excluded.occurred_at,
         successful = excluded.successful,
         result = excluded.result,
         deadline_at = MIN(display_states.deadline_at, excluded.deadline_at),
         updated_at = excluded.updated_at`,
      display.pspReference,
      display.terminalId,
      display.transactionId,
      display.occurredAt,
      display.successful ? 1 : 0,
      display.result,
      receivedAt + CORRELATION_WAIT_MS,
      receivedAt,
    );
  }

  private storeAuthorisation(authorisation: AuthorisationState, receivedAt: number): void {
    this.sql.exec(
      `INSERT INTO authorisation_states
       (psp_reference, occurred_at, successful, payment_method, amount_currency,
        amount_value_minor, terminal_id, transaction_id, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
       ON CONFLICT(psp_reference) DO UPDATE SET
         occurred_at = excluded.occurred_at,
         successful = excluded.successful,
         payment_method = excluded.payment_method,
         amount_currency = excluded.amount_currency,
         amount_value_minor = excluded.amount_value_minor,
         terminal_id = excluded.terminal_id,
         transaction_id = excluded.transaction_id,
         updated_at = excluded.updated_at`,
      authorisation.pspReference,
      authorisation.occurredAt,
      authorisation.successful ? 1 : 0,
      authorisation.paymentMethod,
      authorisation.amount?.currency ?? null,
      authorisation.amount?.valueMinor ?? null,
      authorisation.terminalId,
      authorisation.transactionId,
      receivedAt,
    );
  }

  private tryPublish(pspReference: string, now: number): void {
    const alreadyPublished =
      this.sql.exec("SELECT 1 FROM published WHERE psp_reference = ?", pspReference).toArray().length > 0;
    if (alreadyPublished) return;

    const displayRow = this.sql
      .exec<DisplayRow>("SELECT * FROM display_states WHERE psp_reference = ?", pspReference)
      .toArray()[0];
    const authorisationRow = this.sql
      .exec<AuthorisationRow>("SELECT * FROM authorisation_states WHERE psp_reference = ?", pspReference)
      .toArray()[0];
    const display = displayRow ? this.displayModel(displayRow) : null;
    const authorisation = authorisationRow ? this.authorisationModel(authorisationRow) : null;
    const envelope = correlatePayment(display, authorisation, displayRow?.deadline_at ?? Infinity, now);
    if (!envelope) return;
    this.publish(envelope, authorisation?.successful === true ? "rich" : "generic", now);
  }

  private publish(envelope: OutboundEnvelope, kind: "rich" | "generic", now: number): void {
    const payload = JSON.stringify(envelope);
    const inserted = this.sql.exec(
      `INSERT OR IGNORE INTO published (psp_reference, message_id, publication_kind, created_at)
       VALUES (?, ?, ?, ?)`,
      envelope.message.pspReference,
      envelope.message.id,
      kind,
      now,
    ).rowsWritten;
    if (inserted === 0) return;
    this.sql.exec(
      "INSERT INTO outbound_messages (id, payload, created_at) VALUES (?, ?, ?)",
      envelope.message.id,
      payload,
      now,
    );
    this.pruneOutbound(now);
    for (const socket of this.ctx.getWebSockets()) {
      try {
        socket.send(payload);
      } catch {
        // A stale socket cannot affect durable publication.
      }
    }
  }

  private displayModel(row: DisplayRow): DisplayState {
    return {
      pspReference: row.psp_reference,
      terminalId: row.terminal_id,
      transactionId: row.transaction_id,
      occurredAt: row.occurred_at,
      successful: row.successful === 1,
      result: row.result,
    };
  }

  private authorisationModel(row: AuthorisationRow): AuthorisationState {
    const amount =
      row.amount_currency !== null && row.amount_value_minor !== null
        ? { currency: row.amount_currency, valueMinor: row.amount_value_minor }
        : null;
    return {
      pspReference: row.psp_reference,
      occurredAt: row.occurred_at,
      successful: row.successful === 1,
      paymentMethod: row.payment_method,
      amount,
      terminalId: row.terminal_id,
      transactionId: row.transaction_id,
    };
  }

  private prune(now: number): void {
    const cutoff = now - RETENTION_MS;
    this.sql.exec("DELETE FROM raw_ingress WHERE received_at < ?", cutoff);
    this.sql.exec("DELETE FROM display_states WHERE updated_at < ?", cutoff);
    this.sql.exec("DELETE FROM authorisation_states WHERE updated_at < ?", cutoff);
    this.sql.exec("DELETE FROM published WHERE created_at < ?", cutoff);
    this.pruneOutbound(now);
  }

  private pruneOutbound(now: number): void {
    this.sql.exec("DELETE FROM outbound_messages WHERE created_at < ?", now - RETENTION_MS);
    this.sql.exec(
      `DELETE FROM outbound_messages WHERE position NOT IN
       (SELECT position FROM outbound_messages ORDER BY position DESC LIMIT ?)`,
      MAX_OUTBOUND_MESSAGES,
    );
  }

  private async ensureAlarm(at: number): Promise<void> {
    const current = await this.ctx.storage.getAlarm();
    if (current === null || at < current) await this.ctx.storage.setAlarm(at);
  }

  private async scheduleNextAlarm(): Promise<void> {
    const pending = this.sql
      .exec<{ count: number } & Record<string, SqlStorageValue>>(
        "SELECT COUNT(*) AS count FROM raw_ingress WHERE processed_at IS NULL",
      )
      .one().count;
    if (pending > 0) {
      await this.ensureAlarm(Date.now() + CORRELATION_WAIT_MS);
      return;
    }
    const deadline = this.sql
      .exec<{ deadline: number | null } & Record<string, SqlStorageValue>>(
        `SELECT MIN(d.deadline_at) AS deadline FROM display_states d
         LEFT JOIN published p ON p.psp_reference = d.psp_reference
         WHERE d.successful = 1 AND p.psp_reference IS NULL`,
      )
      .one().deadline;
    if (deadline === null) await this.ctx.storage.deleteAlarm();
    else await this.ctx.storage.setAlarm(Math.max(deadline, Date.now()));
  }

  private logError(event: string, error: unknown, context: Record<string, string> = {}): void {
    console.error(
      JSON.stringify({
        level: "error",
        event,
        error: error instanceof Error ? error.message : "Unknown error",
        ...context,
      }),
    );
  }
}
