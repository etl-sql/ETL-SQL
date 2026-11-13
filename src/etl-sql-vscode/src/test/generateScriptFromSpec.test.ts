import { describe, it, expect } from 'vitest';
import { isTextSpec, cleanJsonResponse } from '../generateScriptFromSpec';

describe('generateScriptFromSpec helpers', () => {
    describe('isTextSpec', () => {
        it('accepts the text formats a chat model can read', () => {
            for (const name of ['a.csv', 'a.tsv', 'a.json', 'a.xml', 'a.txt', 'a.md', 'A.CSV']) {
                expect(isTextSpec(name)).toBe(true);
            }
        });

        it('rejects binary specs and files without an extension', () => {
            for (const name of ['a.pdf', 'a.xlsx', 'a.xls', 'a.docx', 'a.doc', 'a', 'a.unknown']) {
                expect(isTextSpec(name)).toBe(false);
            }
        });
    });

    describe('cleanJsonResponse', () => {
        it('should clean raw json text blocks', () => {
            const raw = '{"a": 1}';
            expect(cleanJsonResponse(raw)).toBe('{"a": 1}');
        });

        it('should clean markdown json blocks', () => {
            const raw = '```json\n{"a": 1}\n```';
            expect(cleanJsonResponse(raw)).toBe('{"a": 1}');
        });

        it('should clean markdown blocks without language specifier', () => {
            const raw = '```\n{"a": 1}\n```';
            expect(cleanJsonResponse(raw)).toBe('{"a": 1}');
        });

        it('should trim surrounding whitespace', () => {
            const raw = '  \n```json\n{"a": 1}\n```  \n';
            expect(cleanJsonResponse(raw)).toBe('{"a": 1}');
        });
    });
});
