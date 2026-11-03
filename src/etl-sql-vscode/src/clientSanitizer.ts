function isObject(value: unknown): value is Record<string, unknown> {
    return typeof value === 'object' && value !== null;
}

export function isSensitiveName(name: unknown): boolean {
    const text = String(name ?? '');
    return /(password|passwd|pwd|secret|token|api[_-]?key|credential|private[_-]?key)/i.test(text);
}

/** `ENC:` ciphertext and `SECRET:` references never leave the extension host, whatever the name. */
function isProtectedValue(value: unknown): boolean {
    return typeof value === 'string' && /^\s*(ENC|SECRET):/i.test(value);
}

/**
 * Masks a variable before it is posted to a webview or tree. Every path that shows variables
 * (language-server script variables and runtime REPL variables) goes through this.
 */
export function sanitizeVariableForClient(value: unknown): unknown {
    if (!isObject(value)) {
        return value;
    }

    const name = value.name;
    const typeName = value.typeName ?? value.type;
    if (isSensitiveName(name) || isSensitiveName(typeName) || isProtectedValue(value.value)) {
        return { ...value, value: '(secret)' };
    }

    return value;
}

/** Masks a whole variable list; anything that is not a list becomes an empty one. */
export function sanitizeVariablesForClient(values: unknown): unknown[] {
    return Array.isArray(values) ? values.map(sanitizeVariableForClient) : [];
}
