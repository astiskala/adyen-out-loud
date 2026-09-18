import type { AuthorisationState, DisplayState, OutboundEnvelope } from "./models";

export const CORRELATION_WAIT_MS = 5_000;

export function correlatePayment(
  display: DisplayState | null,
  authorisation: AuthorisationState | null,
  deadlineAt: number,
  now: number,
): OutboundEnvelope | null {
  if (!display?.successful) return null;
  if (authorisation?.successful) {
    return envelope(display, authorisation.paymentMethod, authorisation.amount);
  }
  if (now >= deadlineAt) return envelope(display, null, null);
  return null;
}

function envelope(
  display: DisplayState,
  paymentMethod: AuthorisationState["paymentMethod"],
  amount: AuthorisationState["amount"],
): OutboundEnvelope {
  return {
    protocol: 1,
    message: {
      id: `payment:${display.pspReference}`,
      type: "payment_succeeded",
      occurredAt: display.occurredAt,
      terminalId: display.terminalId,
      transactionId: display.transactionId,
      pspReference: display.pspReference,
      paymentMethod,
      amount,
    },
  };
}
