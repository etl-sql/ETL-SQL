import { describe, it, expect, afterEach } from 'vitest';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { cleanupTempFiles } from '../cleanupService';

describe('cleanupTempFiles', () => {
    let root: string | undefined;
    afterEach(() => { if (root) fs.rmSync(root, { recursive: true, force: true }); });

    const age = (p: string) => {
        const past = new Date(Date.now() - 60_000);
        fs.utimesSync(p, past, past);
    };

    it('removes expired etlsql-preview-* directories with their contents', async () => {
        root = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-cleanup-test-'));
        const previewDir = path.join(root, 'etlsql-preview-abc123');
        fs.mkdirSync(previewDir);
        fs.writeFileSync(path.join(previewDir, 'manifest.json'), '{}');
        age(previewDir);
        const scriptFile = path.join(root, 'etlsql-script-1.etlsql');
        fs.writeFileSync(scriptFile, 'SELECT 1;');
        age(scriptFile);
        const unrelated = path.join(root, 'other-folder');
        fs.mkdirSync(unrelated);
        age(unrelated);

        await cleanupTempFiles(0, root);

        expect(fs.existsSync(previewDir)).toBe(false);
        expect(fs.existsSync(scriptFile)).toBe(false);
        expect(fs.existsSync(unrelated)).toBe(true);
    });

    it('keeps entries younger than the maximum age', async () => {
        root = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-cleanup-test-'));
        const previewDir = path.join(root, 'etlsql-preview-fresh');
        fs.mkdirSync(previewDir);

        await cleanupTempFiles(60 * 60 * 1000, root);

        expect(fs.existsSync(previewDir)).toBe(true);
    });
});
