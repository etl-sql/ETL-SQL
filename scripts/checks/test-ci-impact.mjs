import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, renameSync, rmSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { runInNewContext } from 'node:vm';
import { fileURLToPath } from 'node:url';
import { manifest, selectLanes, changedPaths, runSelection, validateManifest } from '../select-ci-lanes.mjs';

const all = manifest.lanes;
assert.ok(process.argv.length === 2 || (process.argv.length === 4 && process.argv[2] === '--legacy-prepush' && /^[a-f0-9]{7,40}$/.test(process.argv[3])));
const legacyPrePush = process.argv[3];
assert.equal(selectLanes(['TODO.md']).nativePackages, false);
assert.equal(selectLanes(['tests/ETL-SQL.Portal.BrowserTests/Journey.cs']).nativePackages, false);
assert.equal(selectLanes(['TODO.md', 'tests/ETL-SQL.Portal.BrowserTests/Journey.cs']).nativePackages, false);
assert.equal(selectLanes(['src/etl-sql-vscode/src/extension.ts']).nativePackages, true);
assert.equal(selectLanes(['TODO.md'], { full: true }).nativePackages, true);
assert.equal(selectLanes([], { fallbackReason: 'missing base' }).nativePackages, true);
assert.equal(selectLanes([], { lanes: ['build-vscode'] }).nativePackages, true);
assert.throws(() => validateManifest({ ...manifest, rules: manifest.rules.map((rule, index) => index ? rule : { ...rule, nativePackages: undefined }) }), /Invalid CI impact rule/);
assert.throws(() => validateManifest({ ...manifest, rules: manifest.rules.map(rule => rule.lanes === '*' ? { ...rule, nativePackages: false } : rule) }), /Invalid CI impact rule/);
assert.deepEqual(selectLanes(['TODO.md']).lanes, ['source-preflight']);
assert.deepEqual(selectLanes(['src/etl-sql-vscode/src/extension.ts']).lanes, ['source-preflight', 'build-vscode']);
const uiLanes = ['source-preflight', 'browser-consumer-contracts', 'build-vscode', 'portal-browser-journey', 'studio-performance-budgets'];
for (const file of ['src/etl-sql-vscode/ui/src/App.tsx', 'src/etl-sql-vscode/ui/src/components/Results.test.tsx',
  'src/etl-sql-vscode/ui/src/styles.css', 'src/etl-sql-vscode/ui/src/assets/icon.svg']) {
  assert.deepEqual(selectLanes([file]).lanes, uiLanes);
  assert.equal(selectLanes([file]).nativePackages, true);
  assert.equal(selectLanes([file]).certification, false);
}
assert.deepEqual(selectLanes(['TODO.md', 'src/etl-sql-vscode/ui/src/App.tsx']).lanes, uiLanes);
assert.deepEqual(selectLanes(['src/etl-sql-vscode/src/extension.ts', 'src/etl-sql-vscode/ui/src/App.tsx']).lanes, uiLanes);
assert.deepEqual(selectLanes(['src/etl-sql-vscode/ui/src/App.tsx'], { full: true }).lanes, all);
assert.deepEqual(selectLanes(['src/etl-sql-vscode/ui/src/App.tsx', 'Directory.Build.props']).lanes, all);
assert.deepEqual(selectLanes(['tests/ETL-SQL.Portal.BrowserTests/Journey.cs']).lanes,
  ['source-preflight', 'portal-browser-journey', 'studio-performance-budgets']);
assert.deepEqual(selectLanes(['tests/ETL-SQL.Portal.BrowserTests/Journey.cs', 'src/ETL-SQL.Core/Ast.cs']).lanes, all);
for (const file of [
  'src/ETL-SQL.Core/Ast.cs', 'src/ETL-SQL.Portal/BrowserSources/editor.ts',
  'src/ETL-SQL.ReportRuntime/Resources/TypeScript/runtime.ts',
  'src/etl-sql-vscode/ui/package.json', 'src/etl-sql-vscode/ui/package-lock.json',
  'src/etl-sql-vscode/ui/vite.config.ts', 'src/etl-sql-vscode/ui/tsconfig.json',
  'src/etl-sql-vscode/ui/index.html', 'src/etl-sql-vscode/ui/public/icon.svg',
  'Directory.Build.props', 'Directory.Packages.props', '.github/workflows/ci.yml',
  'scripts/ci-impact.json', 'docs/reference/functions/new.md', 'new-component/file.cs',
  'tests/ETL-SQL.Portal.BrowserTests/ETL-SQL.Portal.BrowserTests.csproj',
]) {
  assert.deepEqual(selectLanes([file]).lanes, all, file);
  assert.equal(selectLanes([file]).nativePackages, true, file);
}
assert.deepEqual(selectLanes(['TODO.md'], { full: true }).lanes, all);
assert.deepEqual(selectLanes([], { fallbackReason: 'missing base' }).lanes, all);
assert.equal(selectLanes(['TODO.md']).certification, false);
assert.equal(selectLanes([], { full: true }).certification, false);
assert.deepEqual(selectLanes([], { lanes: ['build-vscode'] }).lanes, ['source-preflight', 'build-vscode']);
assert.deepEqual(selectLanes([], { lanes: ['browser-consumer-contracts'] }).lanes, ['source-preflight', 'browser-consumer-contracts']);
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
assert.equal(selectLanes(['unmapped.file'], { policy: noFallback }).nativePackages, true);

// Bind path selection to the actual job conditions, including the independent consumer lane.
const ciSource = readFileSync(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8');
const browserJob = ciSource.split(/^  portal-browser-journey:\r?\n/m)[1].split(/^  [\w-]+:\r?\n/m)[0];
const browserGate = ciSource.split(/^  portal-browser-gate:\r?\n/m)[1].split(/^  [\w-]+:\r?\n/m)[0];
const matrixExpression = browserJob.match(/^        shard: \$\{\{(.+)\}\}/m)[1].replaceAll('needs.source-preflight', 'needs.source');
const gateExpression = browserGate.match(/^    if: (.+)$/m)[1].replaceAll('needs.source-preflight', 'needs.source');
for (const browserShard of [undefined, '1', '2', '3']) {
  const result = runSelection({ base: 'HEAD', head: 'HEAD', lanes: ['portal-browser-journey'], browserShard });
  assert.deepEqual(result.browserShards, browserShard ? [Number(browserShard)] : [1, 2, 3]);
  assert.equal(result.selection, 'diagnostic');
  assert.equal(result.certification, false);
  const context = { needs: { source: { result: 'success', outputs: { lanes: JSON.stringify(result.lanes), browser_shards: JSON.stringify(result.browserShards) } } },
    fromJSON: JSON.parse, contains: (items, item) => items.includes(item), always: () => true };
  assert.deepEqual(Array.from(runInNewContext(matrixExpression, context)), result.browserShards);
  assert.equal(runInNewContext(gateExpression, context), !browserShard, 'Partial diagnostics must not run the full-coverage gate.');
  context.needs.source.result = 'failure';
  assert.equal(runInNewContext(gateExpression, context), false);
}
assert.deepEqual(runSelection({ base: 'HEAD', full: true }).browserShards, [1, 2, 3]);
for (const options of [
  { browserShard: '1' }, { browserShard: '2', lanes: ['build-vscode'] },
  { browserShard: '1', lanes: ['portal-browser-journey'], full: true },
  ...['0', '4', '1,2', '01', '', '1; echo bad'].map(browserShard => ({ browserShard, lanes: ['portal-browser-journey'] })),
]) assert.throws(() => runSelection(options), /browser shard requires/);
for (const selection of [selectLanes(['src/etl-sql-vscode/ui/src/App.tsx']), selectLanes(['TODO.md']),
  selectLanes(['src/etl-sql-vscode/src/extension.ts']), selectLanes([], { full: true })]) {
  for (const event of ['push', 'pull_request', 'workflow_dispatch']) {
    for (const lane of manifest.lanes.filter(lane => !['source-preflight', 'msi-upgrade'].includes(lane))) {
      const job = ciSource.split(new RegExp(`^  ${lane}:\\r?\\n`, 'm'))[1]?.split(/^  [\w-]+:\r?\n/m)[0];
      assert.ok(job, `Missing workflow job for ${lane}`);
      const expression = job.match(/^    if: (.+)$/m)[1].replaceAll('needs.source-preflight', 'needs.source');
      const selected = runInNewContext(expression, { needs: { source: { outputs: { lanes: JSON.stringify(selection.lanes) } } },
        github: { event_name: event }, fromJSON: JSON.parse, contains: (items, value) => items.includes(value) }, { timeout: 1000 });
      const excludedOnPr = event === 'pull_request' && ['enterprise-certification', 'docker-integration-tests'].includes(lane);
      assert.equal(selected, selection.lanes.includes(lane) && !excludedOnPr, `${lane}/${event}`);
    }
  }
}

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
  const packagePaths = ['TODO.md', 'docs/releases/v0.22.0.md', 'docs/reference/functions/test.md',
    'tests/ETL-SQL.Portal.BrowserTests/Journey.cs', 'tests/ETL-SQL.Portal.BrowserTests/Fixtures/Portal.cs',
    'tests/ETL-SQL.Portal.BrowserTests/project.csproj', 'src/etl-sql-vscode/ui/src/App.tsx',
    'src/etl-sql-vscode/src/extension.ts', 'src/ETL-SQL.Orchestrator.Service/Program.cs',
    'Directory.Build.props', '.github/workflows/msi-upgrade.yml', 'unknown/file', 'TODO.MD'];
  file('package-paths.json', JSON.stringify(packagePaths));
  file('package-parity.ps1', `
$ErrorActionPreference = 'Stop'
. (Join-Path $env:PACKAGE_SCRIPTS 'MsiImpact.Helpers.ps1')
$paths = Get-Content package-paths.json -Raw | ConvertFrom-Json
$results = foreach ($path in $paths) { @{ path = $path; shouldRun = (Get-MsiUpgradeImpact -ChangedPaths @($path)).ShouldRun } }
ConvertTo-Json -InputObject @($results)
`);
  const parity = spawnSync('pwsh', ['-NoProfile', '-File', path.join(fixture, 'package-parity.ps1')], {
    cwd: fixture, encoding: 'utf8', windowsHide: true, timeout: 10000,
    env: { ...process.env, PACKAGE_SCRIPTS: path.resolve('scripts') },
  });
  assert.equal(parity.status, 0, parity.stdout + parity.stderr);
  const packageResults = JSON.parse(parity.stdout);
  assert.equal(packageResults.length, packagePaths.length);
  for (const result of packageResults) assert.equal(result.shouldRun, selectLanes([result.path]).nativePackages, result.path);
  rmSync(path.join(fixture, 'package-paths.json'));
  rmSync(path.join(fixture, 'package-parity.ps1'));
  git('init');
  file('TODO.md', 'first');
  const base = commit('base');
  file('src/etl-sql-vscode/src/extension.ts', 'extension');
  const extension = commit('extension');
  assert.deepEqual(runSelection({ cwd: fixture, base, head: extension }).lanes, ['source-preflight', 'build-vscode']);
  file('src/etl-sql-vscode/ui/src/App.tsx', 'UI source');
  const ui = commit('UI source');
  assert.deepEqual(runSelection({ cwd: fixture, base: extension, head: ui }).lanes, uiLanes);
  mkdirSync(path.join(fixture, 'docs/releases'), { recursive: true });
  renameSync(path.join(fixture, 'src/etl-sql-vscode/ui/src/App.tsx'), path.join(fixture, 'docs/releases/ui.md'));
  const movedUi = commit('UI moved to documentation');
  assert.deepEqual(runSelection({ cwd: fixture, base: ui, head: movedUi }).lanes, uiLanes);
  assert.equal(runSelection({ cwd: fixture, base: ui, head: movedUi }).nativePackages, true);
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
  assert.equal(runSelection({ cwd: fixture, base: extension, head: renamed }).nativePackages, true);
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
    assert.equal(fallback.nativePackages, true);
  }
  const script = path.resolve('scripts/select-ci-lanes.mjs');
  const bad = spawnSync(process.execPath, [script, '--typo'], { cwd: fixture });
  assert.equal(bad.status, 1);
  file('scripts/select-ci-lanes.mjs', readFileSync(script, 'utf8'));
  file('scripts/ci-impact.json', JSON.stringify(manifest));
  const impactStep = ciSource.split('      - name: Select affected lanes or full certification')[1].split('      - name:')[0];
  const impactCommands = impactStep.split('        run: |')[1].replace(/^          /gm, '');
  file('workflow-impact.ps1', `$ErrorActionPreference = 'Stop'\n${impactCommands}`);
  for (const [event, full, lanes, shard, expected] of [
    ['push', 'false', '', '', 0],
    ['workflow_dispatch', 'false', 'portal-browser-journey', 'all', 0],
    ['workflow_dispatch', 'false', 'portal-browser-journey', '2', 0],
    ['workflow_dispatch', 'false', '', '2', 1],
    ['workflow_dispatch', 'false', 'portal-browser-journey', '4', 1],
    ['push', 'false', 'portal-browser-journey', '2', 1],
    ['workflow_dispatch', 'true', '', '', 0],
    ['workflow_dispatch', 'true', '', '2', 1],
  ]) {
    const output = path.join(fixture, 'workflow-output.txt');
    writeFileSync(output, '');
    const result = spawnSync('pwsh', ['-NoProfile', '-File', path.join(fixture, 'workflow-impact.ps1')], {
      cwd: fixture, encoding: 'utf8', windowsHide: true, timeout: 10000,
      env: { ...process.env, GITHUB_SHA: renamed, CANDIDATE_SHA: renamed, IMPACT_BASE: renamed, IMPACT_COMPARISON: 'direct',
        FULL_CERTIFICATION: full, DIAGNOSTIC_LANES: lanes, DIAGNOSTIC_BROWSER_SHARD: shard, GITHUB_EVENT_NAME: event, GITHUB_OUTPUT: output },
    });
    assert.equal(result.error, undefined);
    assert.equal(result.status, expected, `${event}/${full}/${lanes}/${shard}: ${result.stdout} ${result.stderr}`);
    if (expected) assert.equal(readFileSync(output, 'utf8'), '');
    else {
      const values = Object.fromEntries(readFileSync(output, 'utf8').trim().split(/\r?\n/).map(line => line.split('=')));
      assert.deepEqual(JSON.parse(values.browser_shards), shard === '2' ? [2] : [1, 2, 3]);
      if (full === 'true') assert.deepEqual(JSON.parse(values.lanes), all);
    }
  }
  const old = legacyPrePush ? spawnSync('git', ['show', `${legacyPrePush}:scripts/Test-PrePush.ps1`],
    { cwd: fileURLToPath(new URL('../../', import.meta.url)), encoding: 'utf8', windowsHide: true }) : null;
  if (old) assert.equal(old.status, 0, old.stderr);
  file('scripts/Test-PrePush.ps1', old?.stdout ?? readFileSync(new URL('../Test-PrePush.ps1', import.meta.url), 'utf8'));
  file('prepush-fixture.ps1', `
$ErrorActionPreference = 'Stop'
$global:ImpactAssetChecks = 0
function node {
    if (($env:IMPACT_EXTENSION -eq '1' -or $env:IMPACT_BROWSER -eq '1') -and $args[0].EndsWith('sync-assets.js')) {
        $key = 'prepare-assets'
        if ($args -contains '-Check') { $global:ImpactAssetChecks++; $key = "assets-$global:ImpactAssetChecks" }
        if ($env:IMPACT_UI -eq '1') { $key | Add-Content (Join-Path $env:IMPACT_FIXTURE_ROOT 'commands.txt') }
        $global:LASTEXITCODE = if (($env:IMPACT_FAIL_TASK -eq 'assets' -and $args -contains '-Check') -or
            $env:IMPACT_FAIL_TASK -eq $key) { 1 } else { 0 }
        return
    }
    if ($env:IMPACT_UI -eq '1') {
        $checks = @{ 'browser-asset-ownership.mjs' = 'ownership'; 'typecheck-browser.mjs' = 'types';
            'lint-browser.mjs' = 'browser-lint'; 'check-consumer-contracts.mjs' = 'consumers' }
        $key = $checks[[IO.Path]::GetFileName($args[0])]
        if ($key) {
            if ($key -eq 'ownership' -and $args -notcontains '--check') { throw 'MISSING_OWNERSHIP_CHECK' }
            if ($key -eq 'consumers' -and $args -notcontains '--exclude-preflight') { throw 'REPEATED_PREFLIGHT' }
            $key | Add-Content (Join-Path $env:IMPACT_FIXTURE_ROOT 'commands.txt')
            $global:LASTEXITCODE = if ($env:IMPACT_FAIL_TASK -eq $key) { 1 } else { 0 }
            return
        }
    }
    if ($args[0].EndsWith('select-ci-lanes.mjs')) {
        $output = Join-Path $env:IMPACT_FIXTURE_ROOT 'release-validation/preflight/local-ci-impact.json'
        New-Item -ItemType Directory -Force (Split-Path -Parent $output) | Out-Null
        @{ lanes = @($env:IMPACT_FIXTURE_LANES -split ',') } | ConvertTo-Json | Set-Content $output
    } elseif (-not $args[0].EndsWith('release-preflight.mjs') -and -not $args[0].EndsWith('setup-actionlint.mjs')) {
        throw 'EXPENSIVE_VALIDATION_REACHED'
    }
    $global:LASTEXITCODE = 0
}
function dotnet {
    if ($env:IMPACT_UI -eq '1' -and $args[0] -eq 'format' -and $args[1].EndsWith('ETL-SQL.slnx')) {
        if ($env:IMPACT_EXPECT_BROAD -eq '1') { throw 'BROAD_VALIDATION_REACHED' }
        'solution-format' | Add-Content (Join-Path $env:IMPACT_FIXTURE_ROOT 'commands.txt')
        if ($args -notcontains '--no-restore' -or $args -notcontains '--verify-no-changes') { throw 'WRONG_SOLUTION_FORMAT_FLAGS' }
        $global:LASTEXITCODE = if ($env:IMPACT_FAIL_TASK -eq 'solution-format') { 1 } else { 0 }
        return
    }
    if ($env:IMPACT_UI -eq '1' -and $args[0] -eq 'test') {
        $key = if ($args[1].EndsWith('ETL-SQL.Portal.Tests.csproj')) { 'generated-contracts' } elseif ($args[1].EndsWith('ETL-SQL.Tests.csproj')) { 'smoke' } else { throw 'UNEXPECTED_TEST_PROJECT' }
        $filter = $args[[Array]::IndexOf($args, '--filter') + 1]
        $expectedFilter = if ($key -eq 'generated-contracts') { 'FullyQualifiedName~BrowserContractsGeneratorTests|Category=Smoke.Portal' } else { 'Category=Architecture|Category=Docs|Category=Smoke.Core|Category=Smoke.Reporting|Category=Smoke.Security' }
        if ($filter -cne $expectedFilter -or $args -notcontains '--no-restore' -or $args -notcontains 'Release') { throw 'WRONG_SMOKE_ARGS' }
        $key | Add-Content (Join-Path $env:IMPACT_FIXTURE_ROOT 'commands.txt')
        $global:LASTEXITCODE = if ($env:IMPACT_FAIL_TASK -eq $key) { 1 } else { 0 }
        return
    }
    if ($env:IMPACT_BROWSER -eq '1' -and $args[1].EndsWith('ETL-SQL.Portal.BrowserTests.csproj') -and $args[0] -in @('build', 'format')) {
        "browser-$($args[0])" | Add-Content (Join-Path $env:IMPACT_FIXTURE_ROOT 'commands.txt')
        if ($args[0] -eq 'build' -and ($args -notcontains '--configuration' -or $args -notcontains 'Release')) { throw 'WRONG_BROWSER_CONFIGURATION' }
        if ($args[0] -eq 'format' -and ($args -notcontains '--no-restore' -or $args -notcontains '--verify-no-changes')) { throw 'WRONG_FORMAT_FLAGS' }
        $global:LASTEXITCODE = if ($env:IMPACT_FAIL_TASK -in @($args[0], "browser-$($args[0])")) { 1 } else { 0 }
        return
    }
    if ($env:IMPACT_EXTENSION -ne '1' -or $args[0] -ne 'build' -or -not $args[1].EndsWith('ETL-SQL.App.csproj')) { throw 'UNEXPECTED_DOTNET' }
    if ($args -notcontains '--configuration' -or $args -notcontains 'Debug') { throw 'WRONG_EXTENSION_CONFIGURATION' }
    'engine' | Add-Content (Join-Path $env:IMPACT_FIXTURE_ROOT 'commands.txt')
    $global:LASTEXITCODE = if ($env:IMPACT_FAIL_TASK -eq 'engine-build') { 1 } else { 0 }
}
function pwsh {
    if ($env:IMPACT_BROWSER -ne '1' -or $args -notcontains '-NoBuild' -or $args -notcontains '-File' -or $args -notcontains '-EvidenceDirectory') { throw 'UNEXPECTED_CHILD' }
    $file = $args[[Array]::IndexOf($args, '-File') + 1]
    if (-not $file.EndsWith('Test-BrowserShard.ps1')) { throw 'WRONG_CHILD' }
    foreach ($option in @('-Shard', '-ShardCount')) {
        if ($args[[Array]::IndexOf($args, $option) + 1] -ne 1) { throw 'BROWSER_NOT_FULL_INVENTORY' }
    }
    'browser-tests' | Add-Content (Join-Path $env:IMPACT_FIXTURE_ROOT 'commands.txt')
    $evidence = $args[[Array]::IndexOf($args, '-EvidenceDirectory') + 1]
    if ((Test-Path (Join-Path $evidence 'studio-performance.json')) -or $env:ETLSQL_STUDIO_PERF_OUTPUT -ne (Join-Path $evidence 'studio-performance.json')) { throw 'STALE_OR_WRONG_PERFORMANCE_PATH' }
    $method = if ($env:IMPACT_FAIL_TASK -eq 'missing-method') { 'Other.Test' } else { 'ETL_SQL.Portal.BrowserTests.StudioPerformanceBudgetTests.CanonicalStudioFixture_StaysWithinPlatformBudgets' }
    @{ shardCount = 1; shards = @(@{ methods = @($method) }) } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $evidence 'manifest.json')
    if ($env:IMPACT_FAIL_TASK -ne 'missing-performance') {
        $schema = if ($env:IMPACT_FAIL_TASK -eq 'invalid-performance') { 2 } else { 1 }
        $platform = if ($env:IMPACT_FAIL_TASK -eq 'wrong-platform') { 'unknown' } elseif ($IsWindows) { 'windows' } elseif ($IsLinux) { 'linux' } else { 'macos' }
        @{ SchemaVersion = $schema; Platform = $platform } | ConvertTo-Json | Set-Content $env:ETLSQL_STUDIO_PERF_OUTPUT
    }
    $global:LASTEXITCODE = if ($env:IMPACT_FAIL_TASK -eq 'tests') { 1 } else { 0 }
}
function npm {
    "$($args[1]) / $($args[3])" | Add-Content (Join-Path $env:IMPACT_FIXTURE_ROOT 'commands.txt')
    $prefix = if ([IO.Path]::GetFileName($args[1]) -eq 'ui') { 'ui' } else { 'extension' }
    $global:LASTEXITCODE = if ($env:IMPACT_FAIL_TASK -in @($args[3], "$prefix-$($args[3])")) { 1 } else { 0 }
}
try { & (Join-Path $env:IMPACT_FIXTURE_ROOT 'scripts/Test-PrePush.ps1') }
finally { $env:ETLSQL_STUDIO_PERF_OUTPUT | Set-Content (Join-Path $env:IMPACT_FIXTURE_ROOT 'restored-performance-output.txt') }
`);
  for (const [lanes, expected] of [['source-preflight', 0], ['source-preflight,build-and-test', 1],
    ['source-preflight,portal-browser-journey,studio-performance-budgets,build-vscode', 1], [uiLanes.join(','), 1]]) {
    const local = spawnSync('pwsh', ['-NoProfile', '-File', path.join(fixture, 'prepush-fixture.ps1')], {
      encoding: 'utf8', windowsHide: true, cwd: fixture,
      env: { ...process.env, IMPACT_FIXTURE_ROOT: fixture, IMPACT_FIXTURE_LANES: lanes },
    });
    assert.equal(local.status, expected, local.stdout + local.stderr);
    if (expected) assert.match(local.stderr, /EXPENSIVE_VALIDATION_REACHED/);
  }
  for (const failure of ['', 'build', 'format', 'tests', 'missing-performance', 'invalid-performance', 'wrong-platform', 'missing-method', 'assets']) {
    file('commands.txt', '');
    const local = spawnSync('pwsh', ['-NoProfile', '-File', path.join(fixture, 'prepush-fixture.ps1')], {
      encoding: 'utf8', windowsHide: true, cwd: fixture,
      env: { ...process.env, IMPACT_FIXTURE_ROOT: fixture,
        IMPACT_FIXTURE_LANES: 'source-preflight,portal-browser-journey,studio-performance-budgets',
        IMPACT_BROWSER: '1', IMPACT_FAIL_TASK: failure, ETLSQL_STUDIO_PERF_OUTPUT: 'original-performance-output' },
    });
    assert.equal(local.status, failure ? 1 : 0, local.stdout + local.stderr);
    const commands = readFileSync(path.join(fixture, 'commands.txt'), 'utf8').trim().split(/\r?\n/);
    assert.deepEqual(commands, failure === 'build' ? ['browser-build'] : failure === 'format' ?
      ['browser-build', 'browser-format'] : ['browser-build', 'browser-format', 'browser-tests']);
    assert.equal(readFileSync(path.join(fixture, 'restored-performance-output.txt'), 'utf8').trim(), 'original-performance-output');
    if (failure) assert.ok(!(local.stdout + local.stderr).includes('Browser pre-push passed'));
    else assert.match(local.stdout, /Browser pre-push passed/);
  }
  for (const failure of ['', 'compile', 'test:unit']) {
    file('commands.txt', '');
    const local = spawnSync('pwsh', ['-NoProfile', '-File', path.join(fixture, 'prepush-fixture.ps1')], {
      encoding: 'utf8', windowsHide: true, cwd: fixture,
      env: { ...process.env, IMPACT_FIXTURE_ROOT: fixture, IMPACT_FIXTURE_LANES: 'source-preflight,build-vscode',
        IMPACT_EXTENSION: '1', IMPACT_FAIL_TASK: failure },
    });
    assert.equal(local.status, failure ? 1 : 0, local.stdout + local.stderr);
    if (failure) assert.match(local.stderr, /Extension validation failed/);
    else {
      const commands = readFileSync(path.join(fixture, 'commands.txt'), 'utf8').trim().split(/\r?\n/);
      assert.equal(commands[0], 'engine');
      assert.deepEqual(commands.slice(1).map(line => line.split(' / ')[1]),
        ['compile', 'lint', 'test:unit', 'lint', 'build', 'test:unit']);
      assert.match(commands[4], /etl-sql-vscode[/\\]ui/);
    }
  }
  const uiCommands = ['prepare-assets', 'engine', 'extension-compile', 'extension-lint', 'extension-test:unit',
    'ui-lint', 'ui-build', 'ui-test:unit', 'assets-1', 'solution-format', 'assets-2', 'ownership', 'types', 'browser-lint',
    'consumers', 'assets-3', 'generated-contracts', 'smoke', 'browser-build', 'browser-format', 'browser-tests', 'assets-4'];
  const uiFailures = ['', 'prepare-assets', 'engine-build', 'extension-compile', 'extension-lint', 'extension-test:unit',
    'ui-lint', 'ui-build', 'ui-test:unit', 'assets-1', 'solution-format', 'assets-2', 'ownership', 'types', 'browser-lint',
    'consumers', 'assets-3', 'generated-contracts', 'smoke', 'browser-build', 'browser-format', 'tests', 'missing-performance', 'invalid-performance',
    'wrong-platform', 'missing-method', 'assets-4'];
  for (const failure of legacyPrePush ? [''] : uiFailures) {
    file('commands.txt', '');
    const local = spawnSync('pwsh', ['-NoProfile', '-File', path.join(fixture, 'prepush-fixture.ps1')], {
      encoding: 'utf8', windowsHide: true, cwd: fixture, timeout: 15000,
      env: { ...process.env, IMPACT_FIXTURE_ROOT: fixture, IMPACT_FIXTURE_LANES: (failure ? uiLanes : uiLanes.toReversed()).join(','),
        IMPACT_EXTENSION: '1', IMPACT_BROWSER: '1', IMPACT_UI: '1', IMPACT_FAIL_TASK: failure,
        ETLSQL_STUDIO_PERF_OUTPUT: 'original-performance-output' },
    });
    assert.ifError(local.error);
    const commands = readFileSync(path.join(fixture, 'commands.txt'), 'utf8').trim().split(/\r?\n/).filter(Boolean).map(line => {
      if (!line.includes(' / ')) return line;
      const [packagePath, task] = line.split(' / ');
      return `${/[/\\]ui$/.test(packagePath) ? 'ui' : 'extension'}-${task}`;
    });
    if (legacyPrePush) {
      assert.equal(local.status, 0, local.stdout + local.stderr);
      assert.deepEqual(commands, ['prepare-assets', 'solution-format', 'assets-1', 'ownership', 'types', 'browser-lint',
        'consumers', 'assets-2', 'generated-contracts', 'smoke']);
      assert.match(local.stdout, /PRE-PUSH VALIDATION SUCCEEDED/);
      console.log('Reproduced UI pre-push success without selected extension/browser validation.');
      continue;
    }
    assert.equal(local.status, failure ? 1 : 0, local.stdout + local.stderr);
    const stop = !failure ? uiCommands.length : uiCommands.indexOf(failure === 'engine-build' ? 'engine' :
      ['tests', 'missing-performance', 'invalid-performance', 'wrong-platform', 'missing-method'].includes(failure) ? 'browser-tests' : failure) + 1;
    assert.ok(stop > 0);
    assert.deepEqual(commands, uiCommands.slice(0, stop), failure);
    assert.equal(readFileSync(path.join(fixture, 'restored-performance-output.txt'), 'utf8').trim(), 'original-performance-output');
    assert.equal(local.stdout.includes('UI pre-push passed'), !failure);
  }
  for (const lanes of [all, [...uiLanes, 'enterprise-certification'], uiLanes.filter(lane => lane !== 'browser-consumer-contracts')]) {
    file('commands.txt', '');
    const local = spawnSync('pwsh', ['-NoProfile', '-File', path.join(fixture, 'prepush-fixture.ps1')], {
      encoding: 'utf8', windowsHide: true, cwd: fixture, timeout: 15000,
      env: { ...process.env, IMPACT_FIXTURE_ROOT: fixture, IMPACT_FIXTURE_LANES: lanes.join(','),
        IMPACT_EXTENSION: '1', IMPACT_BROWSER: '1', IMPACT_UI: '1', IMPACT_EXPECT_BROAD: '1', IMPACT_FAIL_TASK: '' },
    });
    assert.ifError(local.error);
    assert.equal(local.status, 1, local.stdout + local.stderr);
    assert.match(local.stderr, /BROAD_VALIDATION_REACHED/);
    assert.deepEqual(readFileSync(path.join(fixture, 'commands.txt'), 'utf8').trim().split(/\r?\n/), ['prepare-assets']);
    assert.ok(!local.stdout.includes('UI pre-push passed'));
  }
  console.log(`UI pre-push: ${legacyPrePush ? 'legacy gap reproduced' : 'all selected consumers, retained broad checks and 26 failure paths verified'}.`);
  console.log('CI impact: narrow changes, shared consumers, unknown paths, full override, real Git comparisons, renames, local changes and missing bases passed.');
} finally {
  // Only the private fixture created by mkdtemp is removed.
  rmSync(fixture, { recursive: true, force: true });
}
