/** Normalize thrown values without trusting the shape of a server error payload. */
export function asError(value: unknown): Error {
    return value instanceof Error ? value : new Error(String(value));
}

export function isObject(value: unknown): value is Record<string, unknown> {
    return value !== null && typeof value === 'object' && !Array.isArray(value);
}
