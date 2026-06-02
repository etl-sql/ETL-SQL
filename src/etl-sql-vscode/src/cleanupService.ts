import * as os from 'os';
import * as path from 'path';
import * as fs from 'fs';

export async function cleanupTempFiles() {
    try {
        const tempDir = os.tmpdir();
        const files = await fs.promises.readdir(tempDir);
        const now = Date.now();
        const maxAge = 24 * 60 * 60 * 1000; // 24 hours

        for (const file of files) {
            if (file.startsWith('etlsql-preview-') || file.startsWith('etlsql-script-')) {
                const filePath = path.join(tempDir, file);
                try {
                    const stats = await fs.promises.stat(filePath);
                    if (now - stats.mtimeMs > maxAge) {
                        await fs.promises.unlink(filePath);
                    }
                } catch {
                    // Ignore
                }
            }
        }

        const etlsqlTempDir = path.join(tempDir, 'etlsql_temp');
        if (fs.existsSync(etlsqlTempDir)) {
            const tempFiles = await fs.promises.readdir(etlsqlTempDir);
            for (const file of tempFiles) {
                const filePath = path.join(etlsqlTempDir, file);
                try {
                    const stats = await fs.promises.stat(filePath);
                    if (now - stats.mtimeMs > maxAge) {
                        await fs.promises.unlink(filePath);
                    }
                } catch {
                    // Ignore
                }
            }
        }
    } catch {
        // Ignore
    }
}
