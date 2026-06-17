import { describe, it, expect } from 'vitest';
import { getMimeType, cleanJsonResponse } from '../generateScriptFromSpec';

describe('generateScriptFromSpec helpers', () => {
    describe('getMimeType', () => {
        it('should resolve standard extensions to MIME types', () => {
            expect(getMimeType('test.pdf')).toBe('application/pdf');
            expect(getMimeType('test.xlsx')).toBe('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
            expect(getMimeType('test.xls')).toBe('application/vnd.ms-excel');
            expect(getMimeType('test.docx')).toBe('application/vnd.openxmlformats-officedocument.wordprocessingml.document');
            expect(getMimeType('test.doc')).toBe('application/msword');
            expect(getMimeType('test.csv')).toBe('text/csv');
            expect(getMimeType('test.tsv')).toBe('text/tab-separated-values');
            expect(getMimeType('test.json')).toBe('application/json');
            expect(getMimeType('test.xml')).toBe('application/xml');
            expect(getMimeType('test.txt')).toBe('text/plain');
            expect(getMimeType('test.md')).toBe('text/markdown');
        });

        it('should fallback to application/octet-stream for unknown extensions', () => {
            expect(getMimeType('test.unknown')).toBe('application/octet-stream');
            expect(getMimeType('test')).toBe('application/octet-stream');
        });

        it('should handle case insensitivity', () => {
            expect(getMimeType('test.PDF')).toBe('application/pdf');
            expect(getMimeType('test.XLSX')).toBe('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
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
