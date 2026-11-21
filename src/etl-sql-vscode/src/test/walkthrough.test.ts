import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as fs from 'fs';
import * as path from 'path';
import * as vscode from 'vscode';
import {
    WALKTHROUGH_ID,
    openWalkthroughSample,
    runWalkthroughPipeline,
    previewWalkthroughReport
} from '../walkthrough';

const root = path.join(__dirname, '..', '..');
const pkg = JSON.parse(fs.readFileSync(path.join(root, 'package.json'), 'utf8'));
const normalize = (s: string) => s.replace(/\r\n/g, '\n').trim();

function readmeBlock(heading: string): string {
    const readme = normalize(fs.readFileSync(path.join(root, 'README.md'), 'utf8'));
    const start = readme.indexOf(heading);
    expect(start, `README heading ${heading}`).toBeGreaterThanOrEqual(0);
    const open = readme.indexOf('```sql\n', start) + '```sql\n'.length;
    return readme.substring(open, readme.indexOf('\n```', open)).trim();
}

describe('Get Started walkthrough manifest', () => {
    const walkthrough = (pkg.contributes.walkthroughs ?? []).find((w: { id: string }) => w.id === WALKTHROUGH_ID);
    const declared = new Set<string>(pkg.contributes.commands.map((c: { command: string }) => c.command));

    it('is contributed with steps', () => {
        expect(walkthrough).toBeDefined();
        expect(walkthrough.steps.length).toBeGreaterThanOrEqual(3);
    });

    it('only points buttons and completion events at declared commands', () => {
        for (const step of walkthrough.steps) {
            const linked = [...String(step.description).matchAll(/\(command:([\w.]+)/g)].map(m => m[1]);
            const completing = (step.completionEvents ?? [])
                .filter((e: string) => e.startsWith('onCommand:'))
                .map((e: string) => e.substring('onCommand:'.length));
            for (const command of [...linked, ...completing]) {
                expect(declared.has(command), `${step.id} uses undeclared ${command}`).toBe(true);
            }
        }
    });

    it('has media files that exist and ship', () => {
        const ignore = fs.readFileSync(path.join(root, '.vscodeignore'), 'utf8');
        for (const step of walkthrough.steps) {
            const media = step.media?.markdown ?? step.media?.image;
            expect(media, `${step.id} media`).toBeTruthy();
            expect(fs.existsSync(path.join(root, media)), media).toBe(true);
        }
        expect(ignore).not.toMatch(/^media\//m);
    });

    it('activates the extension when a sample command runs', () => {
        for (const command of ['etlsql.walkthrough.openPipeline', 'etlsql.walkthrough.runPipeline', 'etlsql.walkthrough.previewReport']) {
            expect(declared.has(command)).toBe(true);
            expect(pkg.activationEvents).toContain(`onCommand:${command}`);
        }
    });
});

describe('walkthrough samples', () => {
    it('match the README examples', () => {
        const media = path.join(root, 'media', 'walkthrough');
        expect(normalize(fs.readFileSync(path.join(media, 'pipeline.etlsql'), 'utf8')))
            .toBe(readmeBlock('### ETL Pipeline (`pipeline.etlsql`)'));
        expect(normalize(fs.readFileSync(path.join(media, 'sales_report.rptsql'), 'utf8')))
            .toBe(readmeBlock('### Interactive Dashboard (`sales_report.rptsql`)'));
    });
});

describe('walkthrough commands', () => {
    const extensionUri = { fsPath: root, toString: () => `file://${root}` } as unknown as vscode.Uri;
    let documents: Map<string, { text: string }>;
    let edits: string[];

    beforeEach(() => {
        vi.clearAllMocks();
        documents = new Map();
        edits = [];
        (vscode.workspace as any).openTextDocument = vi.fn(async (uri: vscode.Uri) => {
            const key = uri.toString();
            if (!documents.has(key)) documents.set(key, { text: '' });
            const state = documents.get(key)!;
            return { uri, getText: () => state.text };
        });
        (vscode.window as any).showTextDocument = vi.fn(async (document: any) => ({
            document,
            edit: async (build: (b: { insert: (p: unknown, t: string) => void }) => void) => {
                build({ insert: (_p, t) => { edits.push(t); documents.get(document.uri.toString())!.text += t; } });
                return true;
            }
        }));
        (vscode as any).Position = class { constructor(public line: number, public character: number) {} };
    });

    it('opens the sample as an untitled file named after it, filled once', async () => {
        await openWalkthroughSample(extensionUri, 'pipeline.etlsql');
        await openWalkthroughSample(extensionUri, 'pipeline.etlsql');

        expect(vscode.Uri.parse).toHaveBeenCalledWith('untitled:pipeline.etlsql');
        expect(edits).toHaveLength(1);
        expect(normalize(edits[0])).toBe(readmeBlock('### ETL Pipeline (`pipeline.etlsql`)'));
    });

    it('focuses the sample before running it, so Run Script has an editor', async () => {
        await runWalkthroughPipeline(extensionUri);

        const showOrder = (vscode.window.showTextDocument as any).mock.invocationCallOrder[0];
        const runCall = (vscode.commands.executeCommand as any).mock.calls.findIndex((c: unknown[]) => c[0] === 'etlsql.runScript');
        expect(runCall).toBeGreaterThanOrEqual(0);
        expect((vscode.commands.executeCommand as any).mock.invocationCallOrder[runCall]).toBeGreaterThan(showOrder);
    });

    it('opens the dashboard sample and previews it', async () => {
        await previewWalkthroughReport(extensionUri);

        expect(vscode.Uri.parse).toHaveBeenCalledWith('untitled:sales_report.rptsql');
        expect(vscode.commands.executeCommand).toHaveBeenCalledWith('etlsql.previewReport');
    });
});
