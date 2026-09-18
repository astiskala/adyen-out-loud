import { describe, expect, it } from "vitest";
import { asObject, asString } from "../src/adyen/json";
import {
  parseDisplayNotification,
  pspReferenceFromTransactionId,
  terminalSerialFromPoiId,
} from "../src/adyen/display-parser";

// Fast, dependency-free tests for the pure parsing/normalization helpers — no Workers runtime needed.
// See docs/testing.md for how this file relates to the Durable Object integration tests in worker.test.ts.

describe("pspReferenceFromTransactionId", () => {
  it("takes everything after the last dot", () => {
    expect(pspReferenceFromTransactionId("CWf3001626182307000.NC6HT9CRT65ZGN82")).toBe("NC6HT9CRT65ZGN82");
  });

  it("returns the whole value when there is no dot", () => {
    expect(pspReferenceFromTransactionId("NC6HT9CRT65ZGN82")).toBe("NC6HT9CRT65ZGN82");
  });

  it("returns null for an empty string", () => {
    expect(pspReferenceFromTransactionId("")).toBeNull();
  });

  it("returns null when the value ends in a trailing dot", () => {
    expect(pspReferenceFromTransactionId("CWf3001626182307000.")).toBeNull();
  });

  it("uses the last dot when the transaction id contains several", () => {
    expect(pspReferenceFromTransactionId("a.b.c")).toBe("c");
  });
});

describe("terminalSerialFromPoiId", () => {
  it("returns the substring after the last hyphen", () => {
    expect(terminalSerialFromPoiId("V400m-324688170")).toBe("324688170");
  });

  it("returns the substring after the last hyphen when there are several hyphens", () => {
    expect(terminalSerialFromPoiId("V400m-EU-324688170")).toBe("324688170");
  });

  it("returns the whole value when there is no hyphen", () => {
    expect(terminalSerialFromPoiId("324688170")).toBe("324688170");
  });

  it("returns the whole value when the hyphen is the last character", () => {
    expect(terminalSerialFromPoiId("V400m-")).toBe("V400m-");
  });
});

describe("asObject / asString", () => {
  it("accepts a plain object and rejects arrays, primitives, and null", () => {
    expect(asObject({ a: 1 })).toEqual({ a: 1 });
    expect(asObject([])).toBeNull();
    expect(asObject("x")).toBeNull();
    expect(asObject(1)).toBeNull();
    expect(asObject(null)).toBeNull();
    expect(asObject(undefined)).toBeNull();
  });

  it("accepts a non-empty string and rejects everything else, including whitespace-only", () => {
    expect(asString("x")).toBe("x");
    expect(asString("")).toBeNull();
    expect(asString(123)).toBeNull();
    expect(asString(null)).toBeNull();
    expect(asString(undefined)).toBeNull();
    // Adyen field values are never intentionally whitespace-only, but asString only rejects
    // *empty* strings — whitespace normalization is each parser's responsibility, not this helper's.
    expect(asString("   ")).toBe("   ");
  });
});

describe("parseDisplayNotification adversarial input", () => {
  const validReference =
    "event=TENDER_FINAL&result=APPROVED&transactionId=CWf1.PSP1&timestamp=2026-09-18T12:00:00Z";

  function envelope(referenceId: unknown, overrides: Record<string, unknown> = {}) {
    return {
      SaleToPOIRequest: {
        MessageHeader: { MessageCategory: "Display", MessageType: "Request", POIID: "P400Plus-1" },
        DisplayRequest: { ReferenceID: referenceId },
        ...overrides,
      },
    };
  }

  it("rejects non-object, array, and null payloads without throwing", () => {
    expect(parseDisplayNotification(null)).toBeNull();
    expect(parseDisplayNotification(undefined)).toBeNull();
    expect(parseDisplayNotification([])).toBeNull();
    expect(parseDisplayNotification("a string")).toBeNull();
    expect(parseDisplayNotification(42)).toBeNull();
  });

  it("ignores non-Display, non-Request message categories (forward compatibility)", () => {
    const other = envelope(validReference);
    other.SaleToPOIRequest.MessageHeader.MessageCategory = "Payment";
    expect(parseDisplayNotification(other)).toBeNull();
  });

  it("ignores events other than TENDER_FINAL", () => {
    expect(
      parseDisplayNotification(envelope("event=TENDER_STARTED&result=APPROVED&transactionId=x&timestamp=y")),
    ).toBeNull();
  });

  it("is case-insensitive and URL-decodes the ReferenceID query fields", () => {
    const encoded =
      "event=TENDER_FINAL&result=APPROVED&transactionId=CWf%201.PSP1&timestamp=2026-09-18T12%3A00%3A00Z";
    const parsed = parseDisplayNotification(envelope(encoded));
    expect(parsed?.transactionId).toBe("CWf 1.PSP1");
    expect(parsed?.occurredAt).toBe("2026-09-18T12:00:00Z");
  });

  it("throws (not returns null) for a recognized-but-incomplete TENDER_FINAL", () => {
    expect(() => parseDisplayNotification(envelope("event=TENDER_FINAL&result=APPROVED"))).toThrow();
  });

  it("tolerates unknown extra fields in both the envelope and the ReferenceID", () => {
    const withExtra = envelope(`${validReference}&futureField=1`, {
      unknownTopLevelField: { nested: true },
    });
    expect(parseDisplayNotification(withExtra)).not.toBeNull();
  });

  it("tolerates unusual unicode and very long values without throwing unexpectedly", () => {
    const long = "x".repeat(10_000);
    const unicode = `event=TENDER_FINAL&result=APPROVED&transactionId=${encodeURIComponent("交易.🎉" + long)}&timestamp=2026-09-18T12:00:00Z`;
    const parsed = parseDisplayNotification(envelope(unicode));
    expect(parsed?.pspReference.length).toBeGreaterThan(0);
  });

  it("includes the derived terminal serial alongside the full terminal id", () => {
    const parsed = parseDisplayNotification(envelope(validReference));
    expect(parsed?.terminalId).toBe("P400Plus-1");
    expect(parsed?.terminalSerial).toBe("1");
  });
});
