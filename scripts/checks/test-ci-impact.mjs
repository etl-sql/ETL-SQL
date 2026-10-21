import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, renameSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { manifest, selectLanes, changedPaths, runSelection, validateManifest } from '../select-ci-lanes.mjs';

const all = manifest.lanes;
assert.deepEqual(selectLanes(['TODO.md']).lanes, ['source-preflight']);
assert.deepEqual(selectLanes(['src/etl-sql-vscode/src/extension.ts']).lanes, ['source-preflight', 'build-vscode']);
assert.deepEqual(selectLanes(['tests/ETL-SQL.Portal.BrowserTests/Journey.cs']).lanes,
  ['source-preflight', 'portal-browser-journey', 'studio-performance-budgets']);
for (const file of [
  'src/ETL-SQL.Core/Ast.cs', 'src/ETL-SQL.Portal/BrowserSources/editor.ts',
  'src/etl-sql-vscode/ui/src/App.tsx', 'src/ETL-SQL.ReportRuntime/Resources/TypeScript/runtime.ts',
  'Directory.Build.props', 'Directory.Packages.props', '.github/workflows/ci.yml',
  'scripts/ci-impact.json', 'docs/reference/functions/new.md', 'new-component/file.cs',
  'tests/ETL-SQL.Portal.BrowserTests/ETL-SQL.Portal.BrowserTests.csproj',
]) assert.deepEqual(selectLanes([file]).lanes, all, file);
assert.deepEqual(selectLanes(['TODO.md'], { full: true }).lanes, all);
assert.deepEqual(selectLanes([], { fallbackReason: 'missing base' }).lanes, all);
assert.equal(selectLanes(['TODO.md']).certification, false);
assert.equal(selectLanes([], { full: true }).certification, false);
assert.deepEqual(selectLanes([], { lanes: ['build-vscode'] }).lanes, ['source-preflight', 'build-vscode']);
const diagnosticLanes = all.filter(lane => lane !== 'msi-upgrade');
assert.equal(selectLanes([], { lanes: diagnosticLanes }).selection, 'diagnostic');
assert.equal(selectLanes([], { lanes: diagnosticLanes }).certification, false);
assert.throws(() => selectLanes([], { lanes: ['msi-upgrade'] }), /known lane/);
assert.throws(() => selectLanes([], { lanes: ['typo'] }), /known lane/);
assert.throws(() => selectLanes([], { full: true, lanes: all }), /known lane/);
assert.deepEqual(selectLanes(['TODO.md', 'TODO.md']).reasons.length, 1);
assert.throws(() => selectLanes(['../outside']), /relative/);
assert.throws(() => validateManifest({ ...manifest, always: ['misspelled-lane'] }), /Unknown/);
const noFallback = { ...manifest, rules: manifest.rules.slice(0, -1) };
assert.deepEqual(selectLanes(['unmapped.file'], { policy: noFallback }).lanes, all);

const fixture = mkdtempSync(path.join(os.tmpdir(), 'ci-impact-'));
function git(...args) {
  const result = spawnSync('git', args, { cwd: fixture, encoding: 'utf8', windowsHide: true });
  assert.equal(result.status, 0, result.stderr);
  return result.stdout.trim();
}
function file(name, content = name) {
  const destination = path.join(fixture, name);
  mkdirSync(path.dirname(destination), { recursive: true });
  writeFileSync(destination, content);
}
function commit(message) {
  git('add', '-A');
  git('-c', 'user.name=CI fixture', '-c', 'user.email=fixture@example.invalid',
    '-c', 'core.hooksPath=/dev/null', '-c', 'commit.gpgsign=false', 'commit', '-m', message);
  return git('rev-parse', 'HEAD');
}
try {
  git('init');
  file('TODO.md', 'first');
  const base = commit('base');
  file('src/etl-sql-vscode/src/extension.ts', 'extension');
  const extension = commit('extension');
  assert.deepEqual(runSelection({ cwd: fixture, base, head: extension }).lanes, ['source-preflight', 'build-vscode']);
  git('checkout', '-b', 'other', base);
  file('Directory.Build.props', 'shared');
  const other = commit('other branch');
  // PR selection uses the merge base, not unrelated changes on the target branch.
  assert.deepEqual(changedPaths({ cwd: fixture, base: other, head: extension, comparison: 'merge-base' }).paths,
    ['src/etl-sql-vscode/src/extension.ts']);
  assert.deepEqual(runSelection({ cwd: fixture, base: other, head: extension }).lanes, all);
  git('checkout', '--detach', extension);
  renameSync(path.join(fixture, 'src/etl-sql-vscode/src/extension.ts'), path.join(fixture, 'TODO.md'));
  const renamed = commit('rename');
  const changes = changedPaths({ cwd: fixture, base: extension, head: renamed });
  assert.deepEqual(changes.paths, ['TODO.md', 'src/etl-sql-vscode/src/extension.ts']);
  assert.deepEqual(runSelection({ cwd: fixture, base: extension, head: renamed }).lanes, ['source-preflight', 'build-vscode']);
  file('new untracked.cs');
  file('TODO.md', 'unstaged');
  file('ROADMAP.md', 'staged');
  git('add', 'ROADMAP.md');
  const local = runSelection({ cwd: fixture, base: renamed, workingTree: true });
  assert.deepEqual(local.comparison.paths, ['ROADMAP.md', 'TODO.md', 'new untracked.cs']);
  assert.deepEqual(local.lanes, all);
  for (const missing of [undefined, '0'.repeat(40), 'not-a-ref']) {
    const fallback = runSelection({ cwd: fixture, base: missing });
    assert.equal(fallback.selection, 'full');
    assert.ok(fallback.comparison.fallbackReason);
  }
  const script = path.resolve('scripts/select-ci-lanes.mjs');
  const bad = spawnSync(process.execPath, [script, '--typo'], { cwd: fixture });
  assert.equal(bad.status, 1);
  file('scripts/Test-PrePush.ps1', readFileSync(new URL('../Test-PrePush.ps1', import.meta.url), 'utf8'));
  file('prepush-fixture.ps1', `
$ErrorActionPreference = 'Stop'
function node {
    if ($args[0].EndsWith('select-ci-lanes.mjs')) {
        $output = Join-Path $env:IMPACT_FIXTURE_ROOT 'release-validation/preflight/local-ci-impact.json'
        New-Item -ItemType Directory -Force (Split-Path -Parent $output) | Out-Null
        @{ lanes = @($env:IMPACT_FIXTURE_LANES -split ',') } | ConvertTo-Json | Set-Content $output
    } elseif (-not $args[0].EndsWith('release-preflight.mjs') -and -not $args[0].EndsWith('setup-actionlint.mjs')) {
        throw 'EXPENSIVE_VALIDATION_REACHED'
    }
    $global:LASTEXITCODE = 0
}
& (Join-Path $env:IMPACT_FIXTURE_ROOT 'scripts/Test-PrePush.ps1')
`);
  for (const [lanes, expected] of [['source-preflight', 0], ['source-preflight,build-and-test', 1]]) {
    const local = spawnSync('pwsh', ['-NoProfile', '-File', path.join(fixture, 'prepush-fixture.ps1')], {
      encoding: 'utf8', windowsHide: true, cwd: fixture,
      env: { ...process.env, IMPACT_FIXTURE_ROOT: fixture, IMPACT_FIXTURE_LANES: lanes },
    });
    assert.equal(local.status, expected, local.stdout + local.stderr);
    if (expected) assert.match(local.stderr, /EXPENSIVE_VALIDATION_REACHED/);
  }
  console.log('CI impact: narrow changes, shared consumers, unknown paths, full override, real Git comparisons, renames, local changes and missing bases passed.');
} finally {
  // Only the private fixture created by mkdtemp is removed.
  rmSync(fixture, { recursive: true, force: true });
}
