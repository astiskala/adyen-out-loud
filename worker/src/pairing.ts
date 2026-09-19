import { asObject } from "./adyen/json";

/** How many recent receipts a device must quote to pair (one receipt alone could come from any customer). */
export const RECEIPTS_REQUIRED = 2;
/** Characters quoted from the end of each receipt's PSP reference. */
export const RECEIPT_CODE_LENGTH = 4;
/** How long an approved payment's receipt can be used for pairing. */
export const RECEIPT_WINDOW_MS = 15 * 60_000;
/** Most recent receipts remembered per terminal. */
export const MAX_RECEIPTS = 20;
/** Failed pairing attempts allowed per terminal within {@link RECEIPT_WINDOW_MS}. */
export const MAX_FAILED_ATTEMPTS = 10;
/** Paired devices remembered per terminal; pairing another evicts the oldest. */
export const MAX_DEVICES = 10;

const CODE_PATTERN = new RegExp(`^[A-Za-z0-9]{${RECEIPT_CODE_LENGTH}}$`);
const TOKEN_BYTES = 32;

/** A receipt that can be quoted for pairing: the end of its PSP reference and when it was received. */
export interface Receipt {
  code: string;
  at: number;
}

/** Failed pairing attempts for one terminal since `since`. */
export interface Attempts {
  count: number;
  since: number;
}

/**
 * The code a merchant reads off a receipt: the last characters of the PSP reference, upper-cased.
 * @param {string} pspReference - The PSP reference printed on the receipt.
 * @returns {string} The receipt code.
 */
export const receiptCode = (pspReference: string): string =>
  pspReference.slice(-RECEIPT_CODE_LENGTH).toUpperCase();

/**
 * Drops receipts older than the pairing window.
 * @param {readonly Receipt[]} receipts - Remembered receipts, oldest first.
 * @param {number} now - Current time in epoch milliseconds.
 * @returns {Receipt[]} The receipts still inside the window.
 */
export const pruneReceipts = (receipts: readonly Receipt[], now: number): Receipt[] =>
  receipts.filter((r) => now - r.at < RECEIPT_WINDOW_MS);

/**
 * Remembers a new receipt, keeping only the most recent {@link MAX_RECEIPTS} still inside the window.
 * @param {readonly Receipt[]} receipts - Remembered receipts, oldest first.
 * @param {string} pspReference - The approved payment's PSP reference.
 * @param {number} now - Current time in epoch milliseconds.
 * @returns {Receipt[]} The updated receipts, oldest first.
 */
export const addReceipt = (receipts: readonly Receipt[], pspReference: string, now: number): Receipt[] =>
  [...pruneReceipts(receipts, now), { code: receiptCode(pspReference), at: now }].slice(-MAX_RECEIPTS);

/**
 * Validates a pairing request body: `{ "receipts": ["AB12", "CD34"] }`.
 * @param {unknown} value - The parsed JSON body.
 * @returns {string[] | null} The upper-cased codes, or null if the body is malformed.
 */
export function parsePairRequest(value: unknown): string[] | null {
  const receipts = asObject(value)?.receipts;
  if (!Array.isArray(receipts) || receipts.length !== RECEIPTS_REQUIRED) return null;
  const codes: string[] = [];
  for (const r of receipts) {
    if (typeof r !== "string" || !CODE_PATTERN.test(r)) return null;
    codes.push(r.toUpperCase());
  }
  return codes;
}

/**
 * Matches each quoted code to a different recent receipt.
 * @param {readonly string[]} codes - Upper-cased codes from {@link parsePairRequest}.
 * @param {readonly Receipt[]} receipts - Recent receipts, already pruned.
 * @returns {Receipt[] | null} The receipts left after removing the matched ones, or null if any code has no match.
 */
export function consumeReceipts(codes: readonly string[], receipts: readonly Receipt[]): Receipt[] | null {
  const remaining = [...receipts];
  for (const code of codes) {
    const i = remaining.findIndex((r) => r.code === code);
    if (i < 0) return null;
    remaining.splice(i, 1);
  }
  return remaining;
}

/**
 * Whether another pairing attempt is allowed; failures older than the window no longer count.
 * @param {Attempts | undefined} attempts - Failed attempts recorded for the terminal, if any.
 * @param {number} now - Current time in epoch milliseconds.
 * @returns {boolean} True if the attempt may proceed.
 */
export const attemptsAllowed = (attempts: Attempts | undefined, now: number): boolean =>
  !attempts || now - attempts.since >= RECEIPT_WINDOW_MS || attempts.count < MAX_FAILED_ATTEMPTS;

/**
 * Records one more failed attempt, starting a new window if the previous one has expired.
 * @param {Attempts | undefined} attempts - Failed attempts recorded for the terminal, if any.
 * @param {number} now - Current time in epoch milliseconds.
 * @returns {Attempts} The updated record.
 */
export const recordFailure = (attempts: Attempts | undefined, now: number): Attempts =>
  !attempts || now - attempts.since >= RECEIPT_WINDOW_MS
    ? { count: 1, since: now }
    : { count: attempts.count + 1, since: attempts.since };

/**
 * Seconds until the current attempt window ends.
 * @param {Attempts} attempts - Failed attempts recorded for the terminal.
 * @param {number} now - Current time in epoch milliseconds.
 * @returns {number} Whole seconds, at least 1.
 */
export const retryAfterSeconds = (attempts: Attempts, now: number): number =>
  Math.max(1, Math.ceil((attempts.since + RECEIPT_WINDOW_MS - now) / 1000));

/**
 * Adds a device's token hash, evicting the oldest beyond {@link MAX_DEVICES}.
 * @param {readonly string[]} hashes - Paired devices' token hashes, oldest first.
 * @param {string} hash - The new device's token hash.
 * @returns {string[]} The updated hashes, oldest first.
 */
export const addDevice = (hashes: readonly string[], hash: string): string[] =>
  [...hashes, hash].slice(-MAX_DEVICES);

/**
 * Extracts the token from an `Authorization: Bearer <token>` header.
 * @param {string | null} header - The Authorization header value.
 * @returns {string | null} The token, or null if the header is missing or malformed.
 */
export function bearerToken(header: string | null): string | null {
  return /^Bearer ([A-Za-z0-9_-]{1,128})$/.exec(header ?? "")?.[1] ?? null;
}

/**
 * Creates a random device token.
 * @returns {string} 256 random bits, base64url-encoded.
 */
export function newToken(): string {
  const bytes = crypto.getRandomValues(new Uint8Array(TOKEN_BYTES));
  return btoa(String.fromCharCode(...bytes))
    .replaceAll("+", "-")
    .replaceAll("/", "_")
    .replace(/=+$/, "");
}

/**
 * Hashes a token for storage; tokens themselves are never stored.
 * @param {string} token - The device token.
 * @returns {Promise<string>} Its SHA-256 digest as lowercase hex.
 */
export async function hashToken(token: string): Promise<string> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(token));
  return [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, "0")).join("");
}
