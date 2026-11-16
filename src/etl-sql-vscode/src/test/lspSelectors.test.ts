import { describe, it, expect } from 'vitest';
import * as fs from 'fs';
import * as path from 'path';
import { ETL_DOCUMENT_SELECTOR, ETL_FILE_WATCH_GLOB } from '../lspSelectors';

describe('language client selectors', () => {
    it('select every language the extension contributes', () => {
        const pkg = JSON.parse(fs.readFileSync(path.join(__dirname, '..', '..', 'package.json'), 'utf8'));
        const languages: string[] = pkg.contributes.languages.map((l: { id: string }) => l.id);

        for (const language of languages) {
            expect(ETL_DOCUMENT_SELECTOR).toContainEqual({ scheme: 'file', language });
            expect(ETL_DOCUMENT_SELECTOR).toContainEqual({ scheme: 'untitled', language });
        }
        expect(languages).toEqual(expect.arrayContaining(['etlsql', 'rptsql']));
    });

    it('select notebook cells, whose context the notebook path already sends', () => {
        expect(ETL_DOCUMENT_SELECTOR).toContainEqual({ scheme: 'vscode-notebook-cell', language: 'etlsql' });
    });

    it('watch report files as well as scripts', () => {
        expect(ETL_FILE_WATCH_GLOB).toBe('**/*.{etlsql,rptsql}');
    });
});
