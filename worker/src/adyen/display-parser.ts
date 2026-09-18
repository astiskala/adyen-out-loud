import { asObject, asString } from "./json";
import type { DisplayState } from "./models";

function referenceFields(reference: string): Map<string, string> {
  const fields = new Map<string, string>();
  for (const [key, value] of new URLSearchParams(reference)) fields.set(key.toLowerCase(), value);
  return fields;
}

/**
 * Extracts the PSP reference from an Adyen transaction ID.
 * The transaction ID format is typically `<something>.<pspReference>`.
 * @param {string} transactionId - The full transaction ID from Adyen.
 * @returns {string|null} The PSP reference portion, or null if not present.
 */
export function pspReferenceFromTransactionId(transactionId: string): string | null {
  const separator = transactionId.lastIndexOf(".");
  const pspReference = separator >= 0 ? transactionId.slice(separator + 1) : transactionId;
  return pspReference.length > 0 ? pspReference : null;
}

/**
 * Extracts the terminal serial number from an Adyen POIID.
 * Adyen's POIID format is `<model>-<serial>` (e.g. "V400m-324688170").
 * The app is configured with just the serial, so the Worker must derive the same substring to route correctly.
 * @param {string} poiId - The point-of-interaction ID from Adyen.
 * @returns {string} The terminal serial number.
 */
export function terminalSerialFromPoiId(poiId: string): string {
  const separator = poiId.lastIndexOf("-");
  if (separator < 0 || separator === poiId.length - 1) return poiId;
  return poiId.slice(separator + 1);
}

/**
 * Parses an Adyen Display notification and returns the final tender state if successful.
 * Returns only final tender display state; all other valid Terminal API shapes are ignored.
 * @param {unknown} value - The raw JSON payload from Adyen's Display webhook.
 * @returns {DisplayState|null} The parsed display state for successful payments, or null for ignored notifications.
 * @throws {Error} If a TENDER_FINAL notification is missing required fields.
 */
export function parseDisplayNotification(value: unknown): DisplayState | null {
  const root = asObject(value);
  const request = asObject(root?.SaleToPOIRequest);
  const header = asObject(request?.MessageHeader);
  const display = asObject(request?.DisplayRequest);
  if (!request || !header || !display) return null;
  if (header.MessageCategory !== "Display" || header.MessageType !== "Request") return null;

  const reference = asString(display.ReferenceID);
  const terminalId = asString(header.POIID);
  if (!reference || !terminalId) return null;
  const fields = referenceFields(reference);
  if (fields.get("event") !== "TENDER_FINAL") return null;

  const transactionId = fields.get("transactionid");
  const occurredAt = fields.get("timestamp");
  const result = fields.get("result");
  const pspReference = transactionId ? pspReferenceFromTransactionId(transactionId) : null;
  if (!transactionId || !occurredAt || !result || !pspReference) {
    throw new Error("Incomplete TENDER_FINAL display notification");
  }

  return {
    pspReference,
    terminalId,
    terminalSerial: terminalSerialFromPoiId(terminalId),
    transactionId,
    occurredAt,
    result,
    successful: result === "APPROVED",
  };
}
