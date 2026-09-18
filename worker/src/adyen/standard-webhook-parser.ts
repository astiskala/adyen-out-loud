import { asObject, asString } from "./json";
import type { AuthorisationState } from "./models";

function successful(value: unknown): boolean {
  return value === true || value === "true";
}

/** Extracts all Standard AUTHORISATION items and ignores forward-compatible event types. */
export function parseStandardAuthorisations(value: unknown): AuthorisationState[] {
  const root = asObject(value);
  if (!root || !Array.isArray(root.notificationItems)) return [];
  const parsed: AuthorisationState[] = [];

  for (const wrapperValue of root.notificationItems) {
    const wrapper = asObject(wrapperValue);
    const item = asObject(wrapper?.NotificationRequestItem);
    if (item?.eventCode !== "AUTHORISATION") continue;
    const pspReference = asString(item.pspReference);
    const occurredAt = asString(item.eventDate);
    if (!pspReference || !occurredAt) throw new Error("Incomplete AUTHORISATION webhook");

    const amountValue = asObject(item.amount);
    const currency = asString(amountValue?.currency);
    const valueMinor = amountValue?.value;
    const amount =
      currency && Number.isSafeInteger(valueMinor) ? { currency, valueMinor: valueMinor as number } : null;
    const additional = asObject(item.additionalData);
    parsed.push({
      pspReference,
      occurredAt,
      successful: successful(item.success),
      paymentMethod: asString(item.paymentMethod),
      amount,
      terminalId: asString(additional?.terminalId) ?? asString(additional?.poiId),
      transactionId: asString(additional?.tenderReference),
    });
  }
  return parsed;
}
