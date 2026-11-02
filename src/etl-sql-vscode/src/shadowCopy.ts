import * as fs from 'fs';
import * as path from 'path';

/**
 * Copies a bundled executable to a per-user folder and returns the copy's path, so running it does
 * not lock the extension's own install folder.
 *
 * `storageRoot` must be private to the user (the extension's global storage). The shared temp
 * folder is not: on Linux and macOS anyone can create `/tmp/etl-sql-shadow/<version>` first and
 * plant a binary there that the extension would then run.
 *
 * The copy is reused only when its size and modification time both match the source. Size alone
 * let a stale or planted binary of the same length stand in for the real one.
 */
export function shadowCopyExecutable(
    srcPath: string,
    storageRoot: string,
    version: string,
    log: (message: string) => void = () => { }
): string {
    try {
        const shadowDir = path.join(storageRoot, 'shadow', version);
        const destPath = path.join(shadowDir, path.basename(srcPath));

        fs.mkdirSync(shadowDir, { recursive: true, mode: 0o700 });

        const srcStats = fs.statSync(srcPath);
        let needsCopy = true;

        if (fs.existsSync(destPath)) {
            const destStats = fs.statSync(destPath);
            if (srcStats.size === destStats.size && Math.trunc(srcStats.mtimeMs) === Math.trunc(destStats.mtimeMs)) {
                needsCopy = false;
            }
        }

        if (needsCopy) {
            log(`[Extension] Shadow copying executable to: ${destPath}`);
            fs.copyFileSync(srcPath, destPath);
            // Carry the source time across so the next activation can recognise an up-to-date copy.
            fs.utimesSync(destPath, srcStats.atime, srcStats.mtime);
            if (process.platform !== 'win32') {
                fs.chmodSync(destPath, 0o700);
            }
        }

        return destPath;
    } catch (err: unknown) {
        const message = err instanceof Error ? err.message : String(err);
        log(`[Extension] Failed to shadow copy executable: ${message}. Using source path.`);
        return srcPath;
    }
}

/** Removes shadow copies left by other extension versions. Best effort. */
export async function cleanupShadowDirectory(storageRoot: string, currentVersion: string, log: (message: string) => void = () => { }): Promise<void> {
    try {
        const parentDir = path.join(storageRoot, 'shadow');
        if (!fs.existsSync(parentDir)) {
            return;
        }

        const entries = await fs.promises.readdir(parentDir, { withFileTypes: true });
        for (const entry of entries) {
            if (entry.isDirectory() && entry.name !== currentVersion) {
                const oldDir = path.join(parentDir, entry.name);
                log(`[Extension] Cleaning up old shadow directory: ${oldDir}`);
                await fs.promises.rm(oldDir, { recursive: true, force: true });
            }
        }
    } catch {
        // Best-effort cleanup.
    }
}
