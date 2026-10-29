import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync, existsSync, cpSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { runInNewContext } from 'node:vm';

const root = mkdtempSync(path.join(os.tmpdir(), 'engine-coverage-'));
const script = fileURLToPath(new URL('../Merge-EngineCoverage.ps1', import.meta.url));
const sha = 'a'.repeat(40), runId = '123';
const coverage = '<coverage lines-valid="20" lines-covered="15" line-rate="0.75"><packages/></coverage>';
const digest = value => createHash('sha256').update(value).digest('hex');
const plan = Array.from({ length: 12 }, (_, index) => {
  const number = index + 1, method = `ETL_SQL.Fixture.C${number}.Test`;
  return { number, group: Math.floor(index / 4) + 1, expectedTests: 2, classes: [`ETL_SQL.Fixture.C${number}`],
    methods: [method], expectedMethodCounts: [{ method, count: 2 }], results: `shard-${number}.trx` };
});
function trx(shard, { count = 2, outcome = 'Passed', method = shard.methods[0], id = `id-${shard.number}` } = {}) {
  const split = method.lastIndexOf('.');
  return `<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><TestSettings><Deployment runDeploymentRoot="run-${shard.number}"/></TestSettings><Results>${Array.from({ length: count }, (_, row) =>
    `<UnitTestResult testId="${id}-${row}" outcome="${outcome}"/>`).join('')}</Results><TestDefinitions>${Array.from({ length: count }, (_, row) =>
    `<UnitTest id="${id}-${row}"><TestMethod className="${method.slice(0, split)}" name="${method.slice(split + 1)}"/></UnitTest>`).join('')}</TestDefinitions><ResultSummary><CollectorDataEntries><Collector><UriAttachments><UriAttachment><A href="coverage.cobertura.xml"/></UriAttachment></UriAttachments></Collector></CollectorDataEntries></ResultSummary></TestRun>`;
}
function manifest(group, attempt = 1) {
  return { schemaVersion: 1, commit: sha, runId, runAttempt: String(attempt), shardGroup: group, shardGroupCount: 3, shardCount: 12,
    configuration: 'Release', project: 'tests\\ETL-SQL.Tests\\ETL-SQL.Tests.csproj', filter: 'Category!=Integration',
    discoveredTests: 24, discoveredClasses: 12, shards: structuredClone(plan).map(shard => ({ ...shard,
      ...(shard.group === group ? { coverageFiles: [{ path: `run-${shard.number}/In/coverage.cobertura.xml`, sha256: digest(coverage) }] } : {}) })) };
}
function put(directory, name, data) { mkdirSync(path.dirname(path.join(directory, name)), { recursive: true }); writeFileSync(path.join(directory, name), typeof data === 'string' ? data : JSON.stringify(data)); }
function fixture() {
  const input = mkdtempSync(path.join(root, 'case-'));
  for (let group = 1; group <= 3; group++) {
    const directory = path.join(input, `engine-coverage-group-${group}-${runId}-1`);
    mkdirSync(directory);
    put(directory, 'manifest.json', manifest(group));
    for (const shard of plan.filter(item => item.group === group)) {
      put(directory, shard.results, trx(shard));
      put(directory, `run-${shard.number}/In/coverage.cobertura.xml`, coverage);
    }
  }
  return input;
}
const groupPath = (input, group = 1, attempt = 1) => path.join(input, `engine-coverage-group-${group}-${runId}-${attempt}`);
function changeManifest(input, mutate, group = 1, attempt = 1) {
  const directory = groupPath(input, group, attempt), value = JSON.parse(readFileSync(path.join(directory, 'manifest.json'), 'utf8'));
  mutate(value); put(directory, 'manifest.json', value);
}
function merge(input, expected = true) {
  const output = input + '-merged';
  const result = spawnSync('pwsh', ['-NoProfile', '-File', script, '-InputDirectory', input, '-OutputDirectory', output,
    '-Commit', sha, '-RunId', runId], { encoding: 'utf8', windowsHide: true, timeout: 15000 });
  assert.ifError(result.error);
  assert.equal(result.status === 0, expected, result.stdout + result.stderr);
  assert.equal(existsSync(output), expected, 'Failed evidence must not produce report input.');
  return expected ? JSON.parse(readFileSync(path.join(output, 'merged-inventory.json'), 'utf8')) : result.stderr;
}
try {
  assert.equal(merge(fixture()).passed, 24);
  const expanded = fixture(); put(groupPath(expanded), plan[0].results, trx(plan[0], { count: 3 }));
  assert.equal(merge(expanded).passed, 25, 'Runtime-expanded theories remain covered.');
  const skipped = fixture(); put(groupPath(skipped), plan[0].results, trx(plan[0], { outcome: 'NotExecuted' }));
  assert.equal(merge(skipped).skipped, 2, 'Preserve the existing engine lane skip policy and report it.');
  const recovered = fixture(); cpSync(groupPath(recovered), groupPath(recovered, 1, 2), { recursive: true });
  changeManifest(recovered, value => { value.runAttempt = '2'; }, 1, 2);
  assert.deepEqual(merge(recovered).groups.map(group => group.artifact), [
    'engine-coverage-group-1-123-2', 'engine-coverage-group-2-123-1', 'engine-coverage-group-3-123-1']);
  const mutations = [
    input => rmSync(groupPath(input, 2), { recursive: true }),
    input => changeManifest(input, value => { value.commit = 'b'.repeat(40); }),
    input => changeManifest(input, value => { value.runId = '124'; }),
    input => changeManifest(input, value => { value.runAttempt = '2'; }),
    input => changeManifest(input, value => { value.configuration = 'Debug'; }),
    input => changeManifest(input, value => { value.project = 'other.csproj'; }),
    input => changeManifest(input, value => { value.shardGroupCount = 2; }),
    input => changeManifest(input, value => { value.shards.pop(); }),
    input => changeManifest(input, value => { value.filter = 'less-coverage'; }, 2),
    input => changeManifest(input, value => { value.shards[0].group = 2; }),
    input => changeManifest(input, value => { value.shards[1].methods = value.shards[0].methods; value.shards[1].expectedMethodCounts = value.shards[0].expectedMethodCounts; }),
    input => put(groupPath(input), plan[0].results, trx(plan[0], { count: 1 })),
    input => put(groupPath(input), plan[0].results, trx(plan[0], { outcome: 'Failed' })),
    input => put(groupPath(input), plan[0].results, trx(plan[0], { method: 'ETL_SQL.Other.Extra' })),
    input => rmSync(path.join(groupPath(input), plan[0].results)),
    input => changeManifest(input, value => { value.shards[0].coverageFiles = []; }),
    input => put(groupPath(input), 'run-1/In/coverage.cobertura.xml', coverage + 'changed'),
    input => changeManifest(input, value => { value.shards[0].coverageFiles[0].path = '../escape.xml'; }),
    input => { put(groupPath(input), 'run-1/In/coverage.cobertura.xml', '<coverage lines-valid="0"/>'); changeManifest(input, value => { value.shards[0].coverageFiles[0].sha256 = digest('<coverage lines-valid="0"/>'); }); },
    input => { cpSync(groupPath(input), groupPath(input, 1, 2), { recursive: true }); changeManifest(input, value => { value.runAttempt = '2'; value.shards[0].coverageFiles = []; }, 1, 2); },
  ];
  for (const mutate of mutations) { const input = fixture(); mutate(input); merge(input, false); }

  // Exercise the actual worker-selection expressions and aggregate status check on PR/push/manual
  // paths. An engine skip is allowed only on a PR; worker failures cannot yield a green gate.
  const source = readFileSync(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8');
  const job = name => source.split(new RegExp(`^  ${name}:\\r?\\n`, 'm'))[1].split(/^  [\w-]+:\r?\n/m)[0];
  for (const event of ['pull_request', 'push', 'workflow_dispatch']) for (const selected of [true, false]) {
    const context = { needs: { source: { result: 'success', outputs: { lanes: selected ? '["build-and-test"]' : '[]' } } },
      github: { event_name: event }, fromJSON: JSON.parse, contains: (items, item) => items.includes(item), always: () => true };
    for (const worker of ['build-verification', 'portal-smoke', 'engine-coverage', 'build-and-test']) {
      const expression = job(worker).match(/^    if: (.+)$/m)[1].replaceAll('needs.source-preflight', 'needs.source');
      assert.equal(runInNewContext(expression, context), selected && (worker !== 'engine-coverage' || event !== 'pull_request'));
    }
  }
  assert.match(job('engine-coverage'), /fail-fast: false/);
  assert.match(job('engine-coverage'), /group: \[1, 2, 3\]/);
  assert.match(job('build-and-test'), /MinimumLineCoverage', '70'/);
  const statusCheck = job('build-and-test').split('      - name: Require all selected build and test workers')[1]
    .split('      - uses:')[0].split('        run: |')[1].trim().replace(/^          /gm, '');
  const statusScript = path.join(root, 'check-status.ps1'); put(root, 'check-status.ps1', statusCheck);
  for (const [event, build, portal, engine, expected] of [
    ['push', 'success', 'success', 'success', true], ['pull_request', 'success', 'success', 'skipped', true],
    ['workflow_dispatch', 'success', 'success', 'success', true],
    ['push', 'failure', 'success', 'success', false], ['push', 'success', 'cancelled', 'success', false],
    ['push', 'success', 'success', 'skipped', false], ['pull_request', 'skipped', 'success', 'skipped', false],
    ['pull_request', 'success', 'failure', 'skipped', false], ['pull_request', 'success', 'success', 'success', false],
  ]) {
    const result = spawnSync('pwsh', ['-NoProfile', '-File', statusScript], { encoding: 'utf8', windowsHide: true, timeout: 15000,
      env: { ...process.env, CI_EVENT: event, BUILD_RESULT: build, PORTAL_RESULT: portal, ENGINE_RESULT: engine } });
    assert.ifError(result.error); assert.equal(result.status === 0, expected, `${event}/${build}/${portal}/${engine}: ${result.stdout}${result.stderr}`);
  }
  const policy = JSON.parse(readFileSync(new URL('../hosted-release-policy.json', import.meta.url), 'utf8'));
  for (const name of ['Build & Test', 'Build Verification', 'Portal Smoke & Security', 'Engine Coverage (1/3)', 'Engine Coverage (2/3)', 'Engine Coverage (3/3)'])
    assert.ok(policy.workflows[0].jobs.includes(name), `Release policy omitted ${name}`);
  console.log(`Engine coverage: complete and expanded inventories, explicit existing skips, retained successful groups and ${mutations.length} fail-closed recovery cases passed.`);
} finally { rmSync(root, { recursive: true, force: true }); }
