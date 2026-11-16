import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const result = spawnSync('pwsh', ['-NoProfile', '-File',
  fileURLToPath(new URL('./fixtures/test-production-canary-evidence.ps1', import.meta.url))],
{ encoding: 'utf8', windowsHide: true, timeout: 45000 });
assert.ifError(result.error);
assert.equal(result.status, 0, result.stdout + result.stderr);
console.log(result.stdout.trim());
