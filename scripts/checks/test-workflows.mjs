import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const checker = fileURLToPath(new URL('../check-workflows.mjs', import.meta.url));
const directory = mkdtempSync(path.join(os.tmpdir(), 'workflow-check-'));
try {
  const first = path.join(directory, 'expression.yml');
  const second = path.join(directory, 'syntax.yml');
  const good = path.join(directory, 'good.yml');
  writeFileSync(first, 'on: push\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo "${{ nonexistent.value }}"\n');
  writeFileSync(second, 'on: push\njobs: [\n');
  writeFileSync(good, 'on: push\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo good\n');
  const failed = spawnSync(process.execPath, [checker, first, second, good], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
  assert.equal(failed.error, undefined);
  assert.notEqual(failed.status, 0);
  assert.match(failed.stdout + failed.stderr, /nonexistent/);
  assert.match(failed.stdout + failed.stderr, /syntax\.yml/);
  const passed = spawnSync(process.execPath, [checker, good], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
  assert.equal(passed.error, undefined);
  assert.equal(passed.status, 0, passed.stdout + passed.stderr);
  console.log('Workflow check reports independent syntax and expression failures; valid workflow passes.');
} finally {
  rmSync(directory, { recursive: true, force: true });
}
