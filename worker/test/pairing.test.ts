import { describe, expect, it } from "vitest";
import {
  MAX_DEVICES,
  MAX_FAILED_ATTEMPTS,
  MAX_RECEIPTS,
  RECEIPTS_REQUIRED,
  RECEIPT_CODE_LENGTH,
  RECEIPT_WINDOW_MS,
  addDevice,
  addReceipt,
  attemptsAllowed,
  bearerToken,
  consumeReceipts,
  hashToken,
  newToken,
  parsePairRequest,
  pruneReceipts,
  receiptCode,
  recordFailure,
  retryAfterSeconds,
} from "../src/pairing";

// Pure pairing rules; the Durable Object wiring around them is covered in worker.test.ts.

describe("receipt codes", () => {
  it("are the last characters of the PSP reference, upper-cased", () => {
    expect(RECEIPT_CODE_LENGTH).toBe(4);
    expect(receiptCode("NC6HT9CRT65zgn82")).toBe("GN82");
  });

  it("are remembered within the window, newest last, up to the limit", () => {
    let receipts = addReceipt([], "OLDOLDOLDOLDAAAA", 0);
    receipts = addReceipt(receipts, "NEWNEWNEWNEWBBBB", RECEIPT_WINDOW_MS);
    expect(receipts).toEqual([{ code: "BBBB", at: RECEIPT_WINDOW_MS }]);

    for (let i = 0; i < MAX_RECEIPTS + 5; i++)
      receipts = addReceipt(receipts, `PSP${String(i).padStart(4, "0")}`, 1);
    expect(receipts).toHaveLength(MAX_RECEIPTS);
    expect(receipts.at(-1)?.code).toBe(String(MAX_RECEIPTS + 4).padStart(4, "0"));
  });

  it("expire once the window has passed", () => {
    const receipts = [
      { code: "AAAA", at: 0 },
      { code: "BBBB", at: 10 },
    ];
    expect(pruneReceipts(receipts, RECEIPT_WINDOW_MS - 1)).toEqual(receipts);
    expect(pruneReceipts(receipts, RECEIPT_WINDOW_MS)).toEqual([{ code: "BBBB", at: 10 }]);
  });
});

describe("parsePairRequest", () => {
  it("accepts exactly the required number of alphanumeric codes and upper-cases them", () => {
    expect(RECEIPTS_REQUIRED).toBe(2);
    expect(parsePairRequest({ receipts: ["ab12", "CD34"] })).toEqual(["AB12", "CD34"]);
  });

  it.each([
    null,
    [],
    {},
    { receipts: "AB12" },
    { receipts: ["AB12"] },
    { receipts: ["AB12", "CD34", "EF56"] },
    { receipts: ["AB12", "CD345"] },
    { receipts: ["AB12", "C-34"] },
    { receipts: ["AB12", 1234] },
  ])("rejects %j", (body) => {
    expect(parsePairRequest(body)).toBeNull();
  });
});

describe("consumeReceipts", () => {
  const receipts = [
    { code: "AAAA", at: 1 },
    { code: "BBBB", at: 2 },
    { code: "AAAA", at: 3 },
  ];

  it("matches each code to a different receipt and returns the rest", () => {
    expect(consumeReceipts(["BBBB", "AAAA"], receipts)).toEqual([{ code: "AAAA", at: 3 }]);
    expect(consumeReceipts(["AAAA", "AAAA"], receipts)).toEqual([{ code: "BBBB", at: 2 }]);
  });

  it("fails if any code is unmatched or a receipt would be used twice", () => {
    expect(consumeReceipts(["AAAA", "CCCC"], receipts)).toBeNull();
    expect(consumeReceipts(["BBBB", "BBBB"], receipts)).toBeNull();
  });
});

describe("failed attempts", () => {
  it("allow up to the limit within a window, then block until it ends", () => {
    let attempts = recordFailure(undefined, 0);
    expect(attempts).toEqual({ count: 1, since: 0 });
    expect(attemptsAllowed(undefined, 0)).toBe(true);
    for (let i = 1; i < MAX_FAILED_ATTEMPTS; i++) attempts = recordFailure(attempts, 100);
    expect(attempts).toEqual({ count: MAX_FAILED_ATTEMPTS, since: 0 });
    expect(attemptsAllowed(attempts, RECEIPT_WINDOW_MS - 1)).toBe(false);
    expect(retryAfterSeconds(attempts, RECEIPT_WINDOW_MS - 1)).toBe(1);
    expect(retryAfterSeconds(attempts, 0)).toBe(RECEIPT_WINDOW_MS / 1000);
    expect(attemptsAllowed(attempts, RECEIPT_WINDOW_MS)).toBe(true);
    expect(recordFailure(attempts, RECEIPT_WINDOW_MS)).toEqual({ count: 1, since: RECEIPT_WINDOW_MS });
  });
});

describe("devices", () => {
  it("keep the most recent token hashes", () => {
    let hashes: string[] = [];
    for (let i = 0; i <= MAX_DEVICES; i++) hashes = addDevice(hashes, String(i));
    expect(hashes).toHaveLength(MAX_DEVICES);
    expect(hashes[0]).toBe("1");
  });

  it("read the token from a Bearer header only", () => {
    expect(bearerToken("Bearer abc_DEF-123")).toBe("abc_DEF-123");
    expect(bearerToken(null)).toBeNull();
    expect(bearerToken("bearer abc")).toBeNull();
    expect(bearerToken("Bearer abc def")).toBeNull();
    expect(bearerToken(`Bearer ${"a".repeat(129)}`)).toBeNull();
  });

  it("get distinct URL-safe tokens and store only a SHA-256 hash", async () => {
    const a = newToken();
    expect(a).toMatch(/^[A-Za-z0-9_-]{43}$/);
    expect(newToken()).not.toBe(a);
    expect(await hashToken("abc")).toBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
  });
});
