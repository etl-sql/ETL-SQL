import { describe, it, expect } from 'vitest';
import { sanitizeVariableForClient, sanitizeVariablesForClient } from '../clientSanitizer';

describe('sanitizeVariablesForClient', () => {
    it('masks runtime variables with secret-looking names', () => {
        // The REPL stream sends CLR type names (String), so the name is the signal.
        const safe = sanitizeVariablesForClient([
            { name: '@db_password', value: 'hunter2', type: 'String' },
            { name: '@apiKey', value: 'sk-123', type: 'String' },
            { name: '@rows', value: '42', type: 'Decimal' }
        ]) as { name: string; value: string }[];

        expect(safe.map(v => v.value)).toEqual(['(secret)', '(secret)', '42']);
    });

    it('masks ENC: and SECRET: values whatever the variable is called', () => {
        const safe = sanitizeVariablesForClient([
            { name: '@conn', value: 'ENC:abc==', type: 'String' },
            { name: '@ref', value: 'SECRET:vault/db', type: 'String' }
        ]) as { value: string }[];

        expect(safe.map(v => v.value)).toEqual(['(secret)', '(secret)']);
    });

    it('masks SECRET-typed variables', () => {
        expect(sanitizeVariableForClient({ name: '@x', value: 'v', typeName: 'SECRET' })).toEqual({ name: '@x', value: '(secret)', typeName: 'SECRET' });
    });

    it('turns a non-list payload into an empty list', () => {
        expect(sanitizeVariablesForClient(undefined)).toEqual([]);
        expect(sanitizeVariablesForClient({ name: '@x' })).toEqual([]);
    });
});
