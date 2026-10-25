import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, rmSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import os from 'node:os';
import path from 'node:path';

const root = mkdtempSync(path.join(os.tmpdir(), 'complete-deployment-claims-'));
try {
  const result = spawnSync('pwsh', ['-NoProfile', '-File',
    fileURLToPath(new URL('./fixtures/test-complete-deployment-claims.ps1', import.meta.url)), '-Root', root],
  { encoding: 'utf8', windowsHide: true, timeout: 45000 });
  assert.ifError(result.error);
  assert.equal(result.status, 0, result.stdout + result.stderr);
  console.log(result.stdout.trim());
} finally {
  rmSync(root, { recursive: true, force: true });
}
