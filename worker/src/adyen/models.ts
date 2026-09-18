export interface DisplayState {
  pspReference: string;
  terminalId: string;
  transactionId: string;
  occurredAt: string;
  successful: boolean;
  result: string;
}

export interface AuthorisationState {
  pspReference: string;
  occurredAt: string;
  successful: boolean;
  paymentMethod: string | null;
  amount: Amount | null;
  terminalId: string | null;
  transactionId: string | null;
}

interface Amount {
  currency: string;
  valueMinor: number;
}

interface PaymentMessage {
  id: string;
  type: "payment_succeeded";
  occurredAt: string;
  terminalId: string;
  transactionId: string;
  pspReference: string;
  paymentMethod: string | null;
  amount: Amount | null;
}

export interface OutboundEnvelope {
  protocol: 1;
  message: PaymentMessage;
}

// `Env` is declared globally by the generated worker-configuration.d.ts (run `npm run types:generate`
// after any wrangler.jsonc binding change) — it is not re-declared or exported here so it can never drift.
