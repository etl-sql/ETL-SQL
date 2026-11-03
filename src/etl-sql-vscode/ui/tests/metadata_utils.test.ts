import { describe, it, expect } from 'vitest';
import type { ProtocolMessage } from '../src/types';
import { newestMetadataResponse, scriptVariablesFor } from '../src/utils/metadata_utils';

describe('newestMetadataResponse', () => {
    const tables: ProtocolMessage = { type: 'tablesResponse', requestId: 'a', tables: ['Users'] };
    const other: ProtocolMessage = { type: 'tablesResponse', requestId: 'b', tables: ['Orders'] };
    const log = { type: 'activeEditorChanged', uri: 'file:///x.etlsql' } as ProtocolMessage;

    it('finds a response that was followed by unrelated messages in the same render', () => {
        // Only the last message used to be checked, so this item stayed on "loading" forever.
        expect(newestMetadataResponse([tables, other, log], null, 'a')).toBe(tables);
    });

    it('ignores responses it has already handled', () => {
        expect(newestMetadataResponse([tables, log], log, 'a')).toBeUndefined();
        expect(newestMetadataResponse([tables, log, other], log, 'b')).toBe(other);
    });

    it('treats every message as new after the list was cleared', () => {
        expect(newestMetadataResponse([tables], log, 'a')).toBe(tables);
    });

    it('ignores responses for other items', () => {
        expect(newestMetadataResponse([other, log], null, 'a')).toBeUndefined();
    });
});

describe('scriptVariablesFor', () => {
    const a: ProtocolMessage = { type: 'scriptVariables', uri: 'file:///a.etlsql', variables: [{ name: '@a', value: '1', typeName: 'INT' }] };
    const b: ProtocolMessage = { type: 'scriptVariables', uri: 'file:///b.etlsql', variables: [{ name: '@b', value: '2', typeName: 'INT' }] };

    it('returns the active script variables, not the newest script that reported', () => {
        expect(scriptVariablesFor([a, b], 'file:///a.etlsql').map(v => v.name)).toEqual(['@a']);
        expect(scriptVariablesFor([a, b], 'file:///b.etlsql').map(v => v.name)).toEqual(['@b']);
    });

    it('returns nothing for a script that has not reported', () => {
        expect(scriptVariablesFor([a, b], 'file:///c.etlsql')).toEqual([]);
        expect(scriptVariablesFor([a, b], null)).toEqual([]);
    });
});
