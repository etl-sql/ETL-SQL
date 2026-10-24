// Validate the fresh candidate backup without restoring over the installed service.
import { existsSync, readdirSync, readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function verifyRecoveryReport(report) {
  if (report.schemaVersion !== '1.0' || report.operation !== 'validate' || report.status !== 'Pass' || report.restored !== false) {
    throw new Error('Recovery report does not prove successful validation.');
  }
  if (typeof report.backupId !== 'string' || !report.backupId || report.backupId !== report.keysBackupId) throw new Error('Recovery backup pair mismatch.');
  if (!Array.isArray(report.missingDependencies) || report.missingDependencies.length) throw new Error('Recovery dependencies are unresolved.');
  if (!Number.isInteger(report.fileCount) || report.fileCount < 1 || !Number.isFinite(report.achievedRpoSeconds) || report.achievedRpoSeconds < 0) throw new Error('Recovery inventory or RPO evidence is missing.');
  if (!Array.isArray(report.operatorActions) || !report.operatorActions.length) throw new Error('Recovery operator actions are missing.');
}

export function validateBackup(directory, reportPath, cli = 'etl-sql', run = spawnSync) {
  if (existsSync(reportPath)) throw new Error('Recovery report already exists; use a fresh evidence path.');
  const files = readdirSync(directory);
  const archive = prefix => {
    const matches = files.filter(name => name.startsWith(prefix) && name.endsWith('.zip'));
    if (matches.length !== 1) throw new Error(`Expected exactly one ${prefix} archive.`);
    return path.resolve(directory, matches[0]);
  };
  const result = run(cli, ['admin', 'restore', '--from', archive('etl-sql-backup-'), '--keys', archive('etl-sql-keys-'), '--validate', '--report', path.resolve(reportPath)], {
    stdio: 'inherit', windowsHide: true, timeout: 300000,
  });
  if (result.error || result.status !== 0) throw new Error(`Recovery validation failed: ${result.error?.message ?? result.status}`);
  verifyRecoveryReport(JSON.parse(readFileSync(reportPath, 'utf8').replace(/^\uFEFF/, '')));
  console.log(`Recovery validation passed: ${reportPath}`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.length !== 4) throw new Error('Usage: node scripts/test-backup-recovery.mjs <backup-directory> <new-report-path>');
  validateBackup(process.argv[2], process.argv[3]);
}
