export interface DisplayState {
  pspReference: string;
  terminalId: string;
  terminalSerial: string;
  transactionId: string;
  occurredAt: string;
  successful: boolean;
  result: string;
}

interface PaymentMessage {
  id: string;
  type: "payment_succeeded";
  occurredAt: string;
  terminalId: string;
  transactionId: string;
  pspReference: string;
}

export interface OutboundEnvelope {
  protocol: 2;
  message: PaymentMessage;
}

// `Env` is declared globally by the generated worker-configuration.d.ts (run `npm run types:generate`
// after any wrangler.jsonc binding change) — it is not re-declared or exported here so it can never drift.
