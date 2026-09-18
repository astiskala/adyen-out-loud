import { parseDisplayNotification } from "./adyen/display-parser";
import { parseStandardAuthorisations } from "./adyen/standard-webhook-parser";

export function canonicalJson(value: unknown): string {
  if (value === null || typeof value !== "object") return JSON.stringify(value);
  if (Array.isArray(value)) return `[${value.map(canonicalJson).join(",")}]`;
  const object = value as Record<string, unknown>;
  return `{${Object.keys(object)
    .sort()
    .map((key) => `${JSON.stringify(key)}:${canonicalJson(object[key])}`)
    .join(",")}}`;
}

export function stableIngressFields(value: unknown): string {
  const display = parseDisplayNotification(value);
  if (display) return `display:${display.pspReference}:${display.result}:${display.transactionId}`;
  const authorisations = parseStandardAuthorisations(value);
  if (authorisations.length > 0) {
    return authorisations
      .map((item) => `auth:${item.pspReference}:${item.successful}:${item.occurredAt}`)
      .sort()
      .join("|");
  }
  return "unknown";
}
