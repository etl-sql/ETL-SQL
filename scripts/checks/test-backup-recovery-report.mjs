import assert from 'node:assert/strict';
import { verifyRecoveryReport, validateBackup } from '../test-backup-recovery.mjs';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

const valid = {
  schemaVersion: '1.0', operation: 'validate', status: 'Pass', restored: false,
  backupId: 'candidate-backup', keysBackupId: 'candidate-backup', missingDependencies: [],
  fileCount: 2, achievedRpoSeconds: 1, operatorActions: ['Review deployment before restoring.'],
};
assert.doesNotThrow(() => verifyRecoveryReport(valid));
for (const [key, value] of [
  ['schemaVersion', '2.0'], ['operation', 'restore'], ['status', 'Fail'], ['restored', true],
  ['backupId', ''], ['keysBackupId', 'different'], ['missingDependencies', ['missing key']],
  ['fileCount', 0], ['achievedRpoSeconds', null], ['achievedRpoSeconds', -1], ['operatorActions', []],
]) assert.throws(() => verifyRecoveryReport({ ...valid, [key]: value }), undefined, key);
for (const key of Object.keys(valid)) {
  const partial = { ...valid }; delete partial[key];
  assert.throws(() => verifyRecoveryReport(partial), undefined, `missing ${key}`);
}
console.log('Recovery reports reject failed, mismatched, incomplete and non-validation evidence.');
const directory = mkdtempSync(path.join(os.tmpdir(), 'recovery-drill-'));
try {
  writeFileSync(path.join(directory, 'etl-sql-backup-test.zip'), 'fixture');
  writeFileSync(path.join(directory, 'etl-sql-keys-test.zip'), 'fixture');
  const report = path.join(directory, 'report.json');
  validateBackup(directory, report, 'candidate-cli', (cli, args, options) => {
    assert.equal(cli, 'candidate-cli');
    assert.deepEqual(args, ['admin', 'restore', '--from', path.join(directory, 'etl-sql-backup-test.zip'), '--keys', path.join(directory, 'etl-sql-keys-test.zip'), '--validate', '--report', report]);
    assert.equal(options.timeout, 300000);
    writeFileSync(report, JSON.stringify(valid));
    return { status: 0 };
  });
  assert.throws(() => validateBackup(directory, report), /already exists/);
  assert.throws(() => validateBackup(directory, path.join(directory, 'failed.json'), 'cli', () => ({ status: 1 })), /failed/);
  assert.throws(() => validateBackup(directory, path.join(directory, 'missing.json'), 'cli', () => ({ status: 0 })), /ENOENT/);
  writeFileSync(path.join(directory, 'etl-sql-backup-extra.zip'), 'ambiguous');
  assert.throws(() => validateBackup(directory, path.join(directory, 'ambiguous.json')), /exactly one/);
} finally {
  rmSync(directory, { recursive: true, force: true });
}
