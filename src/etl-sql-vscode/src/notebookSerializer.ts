import * as vscode from 'vscode';

interface RawNotebook {
    cells: RawNotebookCell[];
}

interface RawNotebookCell {
    language: string;
    value: string;
    kind: vscode.NotebookCellKind;
}

export class ETLNotebookSerializer implements vscode.NotebookSerializer {
    async deserializeNotebook(
        content: Uint8Array
    ): Promise<vscode.NotebookData> {
        const contents = new TextDecoder().decode(content);

        let raw: RawNotebook;
        try {
            raw = JSON.parse(contents);
        } catch {
            raw = { cells: [] };
        }

        const cells = raw.cells.map(
            item => new vscode.NotebookCellData(item.kind, item.value, item.language)
        );

        return new vscode.NotebookData(cells);
    }

    async serializeNotebook(
        data: vscode.NotebookData
    ): Promise<Uint8Array> {
        const contents: RawNotebook = {
            cells: data.cells.map(cell => ({
                kind: cell.kind,
                language: cell.languageId,
                value: cell.value
            }))
        };

        return new TextEncoder().encode(JSON.stringify(contents, null, 2));
    }
}
