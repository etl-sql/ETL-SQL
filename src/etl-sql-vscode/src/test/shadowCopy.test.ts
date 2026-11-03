import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import * as fs from 'fs';
import * as os from 'os';
import * as path from 'path';
import { shadowCopyExecutable, cleanupShadowDirectory } from '../shadowCopy';

describe('shadowCopyExecutable', () => {
    let root: string;
    let storage: string;
    let source: string;

    beforeEach(() => {
        root = fs.mkdtempSync(path.join(os.tmpdir(), 'etlsql-shadow-test-'));
        storage = path.join(root, 'storage');
        source = path.join(root, 'bin', 'ETL-SQL-LSP');
        fs.mkdirSync(path.dirname(source), { recursive: true });
        fs.writeFileSync(source, 'real-binary');
    });

    afterEach(() => fs.rmSync(root, { recursive: true, force: true }));

    it('copies into the given per-user storage, never the shared temp folder', () => {
        const copy = shadowCopyExecutable(source, storage, '1.2.3');

        expect(copy).toBe(path.join(storage, 'shadow', '1.2.3', 'ETL-SQL-LSP'));
        expect(fs.readFileSync(copy, 'utf8')).toBe('real-binary');
    });

    it('replaces a same-size copy whose modification time differs', () => {
        const copy = shadowCopyExecutable(source, storage, '1.2.3');
        // A planted or stale binary of exactly the same length used to be trusted on size alone.
        fs.writeFileSync(copy, 'evil-binary');
        expect(fs.statSync(copy).size).toBe(fs.statSync(source).size);
        const past = new Date(Date.now() - 60_000);
        fs.utimesSync(copy, past, past);

        shadowCopyExecutable(source, storage, '1.2.3');

        expect(fs.readFileSync(copy, 'utf8')).toBe('real-binary');
    });

    it('reuses an up-to-date copy', () => {
        const copy = shadowCopyExecutable(source, storage, '1.2.3');
        const before = fs.statSync(copy).mtimeMs;

        shadowCopyExecutable(source, storage, '1.2.3');

        expect(fs.statSync(copy).mtimeMs).toBe(before);
    });

    it('falls back to the source path when the copy fails', () => {
        expect(shadowCopyExecutable(path.join(root, 'missing'), storage, '1.2.3')).toBe(path.join(root, 'missing'));
    });

    it('cleans up other versions only', async () => {
        shadowCopyExecutable(source, storage, '1.0.0');
        shadowCopyExecutable(source, storage, '1.2.3');

        await cleanupShadowDirectory(storage, '1.2.3');

        expect(fs.readdirSync(path.join(storage, 'shadow'))).toEqual(['1.2.3']);
    });
});
