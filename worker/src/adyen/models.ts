/**
 * Parsed state from a successful Adyen TENDER_FINAL display notification.
 */
export interface DisplayState {
  /** The Adyen PSP reference for the transaction. */
  pspReference: string;
  /** The full terminal POIID (e.g., "V400m-324688170"). */
  terminalId: string;
  /** The terminal serial number extracted from POIID. */
  terminalSerial: string;
  /** The Adyen transaction ID. */
  transactionId: string;
  /** ISO 8601 timestamp when the payment occurred. */
  occurredAt: string;
  /** Whether the payment was approved. */
  successful: boolean;
  /** The raw result code from Adyen (e.g., "APPROVED"). */
  result: string;
}

/**
 * Internal payment message structure for the outbound WebSocket envelope.
 * Not exported — used only within the Worker.
 */
interface PaymentMessage {
  /** Unique message ID (e.g., "payment:{pspReference}"). */
  id: string;
  /** Message type, always "payment_succeeded". */
  type: "payment_succeeded";
  /** ISO 8601 timestamp when the payment occurred. */
  occurredAt: string;
  /** The full terminal POIID. */
  terminalId: string;
  /** The Adyen transaction ID. */
  transactionId: string;
  /** The Adyen PSP reference. */
  pspReference: string;
}

/**
 * Outbound WebSocket envelope sent to terminal apps.
 */
export interface OutboundEnvelope {
  /** Protocol version (currently 2). */
  protocol: 2;
  /** The payment message payload. */
  message: PaymentMessage;
}

// `Env` is declared globally by the generated worker-configuration.d.ts (run `npm run types:generate`
// after any wrangler.jsonc binding change) — it is not re-declared or exported here so it can never drift.
