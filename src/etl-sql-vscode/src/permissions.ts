import * as os from 'os';
import * as fs from 'fs';
import * as logger from './logger';

export function ensureExecutable(filePath: string): void {
    if (os.platform() !== 'win32' && fs.existsSync(filePath)) {
        try {
            const stats = fs.statSync(filePath);
            if ((stats.mode & fs.constants.S_IXUSR) === 0) {
                fs.chmodSync(filePath, stats.mode | fs.constants.S_IXUSR | fs.constants.S_IXGRP | fs.constants.S_IXOTH);
                logger.log(`Made executable: ${filePath}`, 'info');
            }
        } catch (err: unknown) {
            const message = err instanceof Error ? err.message : String(err);
            logger.log(`Failed to set execute bit on ${filePath}: ${message}`, 'error');
        }
    }
}
