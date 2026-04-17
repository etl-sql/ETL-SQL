import { describe, it, expect } from 'vitest';
import { extractVariables } from '../src/utils/variable_utils';

describe('Variable Extraction Protocol Resilience', () => {
    it('should extract from .variables key (New LSP format)', () => {
        const msg = { type: 'variables', variables: [{ name: '@a', value: '1', type: 'int' }] };
        const result = extractVariables(msg);
        expect(result).toHaveLength(1);
        expect(result[0].name).toBe('@a');
    });

    it('should extract from .data key (Legacy Engine format)', () => {
        const msg = { type: 'variables', data: [{ name: '@b', value: '2', type: 'string' }] };
        const result = extractVariables(msg);
        expect(result).toHaveLength(1);
        expect(result[0].name).toBe('@b');
    });

    it('should handle scriptVariables type (LSP format)', () => {
        const msg = { type: 'scriptVariables', variables: [{ name: '@c', type: 'int' }] };
        const result = extractVariables(msg);
        expect(result).toHaveLength(1);
        expect(result[0].name).toBe('@c');
    });

    it('should handle missing properties gracefully (prevent .length crash)', () => {
        const msg = { type: 'variables' };
        expect(extractVariables(msg)).toEqual([]);
    });

    it('should ignore unrelated message types', () => {
        const msg = { type: 'results', rows: [] };
        expect(extractVariables(msg)).toEqual([]);
    });

    it('should handle null/undefined messages', () => {
        expect(extractVariables(null)).toEqual([]);
        expect(extractVariables(undefined)).toEqual([]);
    });

    it('should handle empty arrays in both formats', () => {
        expect(extractVariables({ type: 'variables', variables: [] })).toEqual([]);
        expect(extractVariables({ type: 'variables', data: [] })).toEqual([]);
    });
});
