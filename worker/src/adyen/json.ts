/** A plain JSON object (non-null, non-array). */
export type JsonObject = Record<string, unknown>;

/**
 * Safely casts an unknown value to a JsonObject if it is a non-null, non-array object.
 * @param {unknown} value - The value to check.
 * @returns {JsonObject|null} The value as JsonObject, or null if not a plain object.
 */
export function asObject(value: unknown): JsonObject | null {
  return value !== null && typeof value === "object" && !Array.isArray(value) ? (value as JsonObject) : null;
}

/**
 * Safely extracts a non-empty string from an unknown value.
 * @param {unknown} value - The value to check.
 * @returns {string|null} The string value, or null if not a non-empty string.
 */
export function asString(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}
