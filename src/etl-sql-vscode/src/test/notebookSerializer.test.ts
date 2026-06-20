import { describe, it, expect } from 'vitest';
import * as vscode from 'vscode';
import { ETLNotebookSerializer } from '../notebookSerializer';

describe('ETLNotebookSerializer', () => {
    const serializer = new ETLNotebookSerializer();

    describe('deserializeNotebook', () => {
        it('should correctly deserialize valid notebook JSON into NotebookData', async () => {
            const rawNotebook = {
                cells: [
                    {
                        kind: vscode.NotebookCellKind.Code,
                        value: 'SELECT 1;',
                        language: 'etlsql'
                    },
                    {
                        kind: vscode.NotebookCellKind.Markup,
                        value: '## Title',
                        language: 'markdown'
                    }
                ]
            };

            const bytes = new TextEncoder().encode(JSON.stringify(rawNotebook));
            const notebookData = await serializer.deserializeNotebook(bytes);

            expect(notebookData).toBeInstanceOf(vscode.NotebookData);
            expect(notebookData.cells).toHaveLength(2);
            expect(notebookData.cells[0].kind).toBe(vscode.NotebookCellKind.Code);
            expect(notebookData.cells[0].value).toBe('SELECT 1;');
            expect(notebookData.cells[0].languageId).toBe('etlsql');

            expect(notebookData.cells[1].kind).toBe(vscode.NotebookCellKind.Markup);
            expect(notebookData.cells[1].value).toBe('## Title');
            expect(notebookData.cells[1].languageId).toBe('markdown');
        });

        it('should safely fall back to an empty notebook structure if JSON parsing fails', async () => {
            const invalidBytes = new TextEncoder().encode('invalid json string');
            const notebookData = await serializer.deserializeNotebook(invalidBytes);

            expect(notebookData).toBeInstanceOf(vscode.NotebookData);
            expect(notebookData.cells).toEqual([]);
        });
    });

    describe('serializeNotebook', () => {
        it('should serialize NotebookData into Uint8Array representing JSON matches schema', async () => {
            const cells = [
                new vscode.NotebookCellData(vscode.NotebookCellKind.Code, 'SELECT * FROM src.Table;', 'etlsql'),
                new vscode.NotebookCellData(vscode.NotebookCellKind.Markup, 'Some markdown description', 'markdown')
            ];
            const notebookData = new vscode.NotebookData(cells);

            const bytes = await serializer.serializeNotebook(notebookData);
            const decoded = new TextDecoder().decode(bytes);
            const parsed = JSON.parse(decoded);

            expect(parsed).toHaveProperty('cells');
            expect(parsed.cells).toHaveLength(2);
            expect(parsed.cells[0]).toEqual({
                kind: vscode.NotebookCellKind.Code,
                value: 'SELECT * FROM src.Table;',
                language: 'etlsql'
            });
            expect(parsed.cells[1]).toEqual({
                kind: vscode.NotebookCellKind.Markup,
                value: 'Some markdown description',
                language: 'markdown'
            });
        });
    });
});
