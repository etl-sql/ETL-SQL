import * as os from 'os';
import * as path from 'path';
import * as fs from 'fs';

const DEFAULT_MAX_AGE_MS = 24 * 60 * 60 * 1000; // 24 hours

/**
 * Removes expired preview and script leftovers. `etlsql-preview-*` entries are directories
 * (the preview panel makes them with `mkdtemp`), so entries are removed recursively; `unlink`
 * fails on a directory and the failure was swallowed, leaving every preview folder behind.
 */
export async function cleanupTempFiles(maxAgeMs: number = DEFAULT_MAX_AGE_MS, tempDir: string = os.tmpdir()): Promise<void> {
    try {
        const files = await fs.promises.readdir(tempDir);
        const now = Date.now();

        for (const file of files) {
            if (file.startsWith('etlsql-preview-') || file.startsWith('etlsql-script-')) {
                await removeIfExpired(path.join(tempDir, file), now, maxAgeMs);
            }
        }

        const etlsqlTempDir = path.join(tempDir, 'etlsql_temp');
        if (fs.existsSync(etlsqlTempDir)) {
            const tempFiles = await fs.promises.readdir(etlsqlTempDir);
            for (const file of tempFiles) {
                await removeIfExpired(path.join(etlsqlTempDir, file), now, maxAgeMs);
            }
        }
    } catch {
        // Best-effort cleanup.
    }
}

async function removeIfExpired(entryPath: string, now: number, maxAgeMs: number): Promise<void> {
    try {
        // lstat: a link is removed itself; its target is never followed.
        const stats = await fs.promises.lstat(entryPath);
        if (now - stats.mtimeMs > maxAgeMs) {
            await fs.promises.rm(entryPath, { recursive: true, force: true });
        }
    } catch {
        // Another process may hold or remove it; try again next activation.
    }
}
