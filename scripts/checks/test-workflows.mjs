import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { runInNewContext } from 'node:vm';

// Exercise the actual release conditions, including deliberate skips on artifact reuse.
const workflow = readFileSync(new URL('../../.github/workflows/release.yml', import.meta.url), 'utf8');
const sections = workflow.split(/^  ([\w-]+):\r?$/m);
const jobs = new Map();
for (let i = 1; i < sections.length; i += 2) jobs.set(sections[i], sections[i + 1]);
for (const name of ['promote-msi', 'promote-deb', 'attest-provenance', 'publish-release']) {
  const block = jobs.get(name);
  const dependencies = block.match(/^    needs: \[([^\]]+)\]/m)[1].split(',').map(value => value.trim());
  const condition = block.match(/^    if: [\s\S]*?\$\{\{([\s\S]*?)\}\}/m)?.[1];
  assert.ok(condition, `${name} must explicitly handle skipped ancestors.`);
  assert.match(condition, /!cancelled\(\)/);
  const expression = condition.replace(/needs\.([\w-]+)/g, (_, key) => `needs[${JSON.stringify(key)}]`);
  const evaluate = (needs, cancelled = false) => runInNewContext(expression, { needs, cancelled: () => cancelled }, { timeout: 1000 });
  const needs = Object.fromEntries(dependencies.map(key => [key, { result: 'success', outputs: { artifact_id: '' } }]));
  assert.equal(evaluate(needs), true, `${name}: successful fresh certification`);
  assert.equal(evaluate(needs, true), false, `${name}: cancellation`);
  for (const key of dependencies) for (const result of ['failure', 'cancelled', 'skipped']) {
    assert.equal(evaluate({ ...needs, [key]: { ...needs[key], result } }), false, `${name}: ${key} ${result}`);
  }
  if (name.startsWith('promote-')) {
    const kind = name.slice('promote-'.length);
    needs[`find-${kind}`].outputs.artifact_id = '123';
    needs[`certify-${kind}`].result = 'skipped';
    assert.equal(evaluate(needs), true, `${name}: retained candidate skips redundant certification`);
    assert.equal(evaluate(needs, true), false);
    for (const key of dependencies.filter(key => key !== `certify-${kind}`)) {
      assert.equal(evaluate({ ...needs, [key]: { ...needs[key], result: 'failure' } }), false);
    }
  }
}

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
