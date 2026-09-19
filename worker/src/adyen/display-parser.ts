import { asObject, asString } from "./json";
import type { DisplayState } from "./models";

const refFields = (s: string) => new Map([...new URLSearchParams(s)].map(([k, v]) => [k.toLowerCase(), v]));

/**
 * Extracts PSP reference from Adyen transaction ID (format: `prefix.pspRef`).
 * @param {string} id - The transaction ID.
 * @returns {string | null} The PSP reference, or null if empty.
 */
export const pspReferenceFromTransactionId = (id: string) => {
  const i = id.lastIndexOf(".");
  const psp = i >= 0 ? id.slice(i + 1) : id;
  return psp ? psp : null;
};

/**
 * Extracts terminal serial from Adyen POIID (format: `model-serial`).
 * @param {string} id - The POIID.
 * @returns {string} The terminal serial.
 */
export const terminalSerialFromPoiId = (id: string) => {
  const i = id.lastIndexOf("-");
  return i >= 0 && i < id.length - 1 ? id.slice(i + 1) : id;
};

/**
 * Parses Adyen Display webhook; returns DisplayState for a TENDER_FINAL, null for anything else.
 * @param {unknown} v - The parsed webhook body.
 * @returns {DisplayState | null} The final tender state, or null if the notification is ignored.
 * @throws {Error} If a TENDER_FINAL notification is missing required fields.
 */
export function parseDisplayNotification(v: unknown): DisplayState | null {
  const r = asObject(v)?.SaleToPOIRequest;
  const req = asObject(r);
  const hdr = asObject(req?.MessageHeader);
  const disp = asObject(req?.DisplayRequest);
  if (!req || !hdr || !disp || hdr.MessageCategory !== "Display" || hdr.MessageType !== "Request")
    return null;

  const ref = asString(disp.ReferenceID);
  const tid = asString(hdr.POIID);
  if (!ref || !tid) return null;

  const f = refFields(ref);
  if (f.get("event") !== "TENDER_FINAL") return null;

  const txid = f.get("transactionid");
  const ts = f.get("timestamp");
  const res = f.get("result");
  const psp = txid ? pspReferenceFromTransactionId(txid) : null;
  if (!txid || !ts || !res || !psp) throw new Error("Incomplete TENDER_FINAL");

  return {
    pspReference: psp,
    terminalId: tid,
    terminalSerial: terminalSerialFromPoiId(tid),
    transactionId: txid,
    occurredAt: ts,
    result: res,
    successful: res === "APPROVED",
  };
}
