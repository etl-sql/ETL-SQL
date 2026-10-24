import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, writeFileSync, rmSync, existsSync, mkdirSync } from 'node:fs';
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
const ciSource = readFileSync(new URL('../../.github/workflows/ci.yml', import.meta.url), 'utf8');
const consumerJob = ciSource.split(/^  browser-consumer-contracts:\r?\n/m)[1].split(/^  [\w-]+:\r?\n/m)[0];
assert.match(consumerJob, /if: contains\(fromJSON\(needs\.source-preflight\.outputs\.lanes \|\| '\[\]'\), 'browser-consumer-contracts'\)/);
assert.doesNotMatch(consumerJob, /'build-and-test'/);
assert.match(consumerJob, /name: Browser and Consumer Contracts/);
for (const command of ['verify-source-only-preparation.mjs', 'browser-asset-ownership.mjs', 'typecheck-browser.mjs',
  'lint-browser.mjs', 'check-consumer-contracts.mjs', 'Test-HaSoakContracts.ps1']) assert.ok(consumerJob.includes(command));
// Upload artifacts are immutable within a run. Failed-only retries must preserve old evidence
// and create distinct names, including separate matrix executions.
const ciSections = ciSource.split(/^  ([\w-]+):\r?$/m);
const artifactNames = new Map();
for (let index = 1; index < ciSections.length; index += 2) {
  const id = ciSections[index], job = ciSections[index + 1];
  const platforms = [...job.matchAll(/^            platform: ([\w-]+)/gm)].map(match => match[1]);
  const shardList = job.match(/^        shard: \[([^\]]+)\]/m)?.[1];
  const shardExpression = job.match(/^        shard: \$\{\{(.+)\}\}/m)?.[1];
  const dynamicShards = shardExpression ? runInNewContext(shardExpression.replaceAll('needs.source-preflight', 'needs.source'), {
    needs: { source: { outputs: { browser_shards: '[1,2,3]' } } }, fromJSON: JSON.parse,
  }, { timeout: 1000 }) : null;
  const matrices = platforms.length ? platforms.map(platform => ({ platform })) :
    dynamicShards ? Array.from(dynamicShards, shard => ({ shard })) :
      shardList ? shardList.split(',').map(shard => ({ shard: Number(shard.trim()) })) : [{}];
  for (const step of job.split(/^      - /m).filter(block => block.includes('uses: actions/upload-artifact@'))) {
    const template = step.match(/^          name: (.+)$/m)?.[1];
    assert.ok(template?.includes('github.run_attempt'), `${id} upload must preserve earlier attempts.`);
    assert.doesNotMatch(step, /^          overwrite: true/m);
    for (const matrix of matrices) for (const attempt of [1, 2]) {
      const name = template.replace(/\$\{\{([\s\S]*?)\}\}/g, (_, expression) =>
        runInNewContext(expression, { github: { run_id: '123', run_attempt: attempt }, matrix }, { timeout: 1000 }));
      assert.ok(!name.includes('undefined'), `${id} artifact omitted a matrix dimension.`);
      assert.ok(!artifactNames.has(name), `${id} artifact collides with ${artifactNames.get(name)}: ${name}`);
      artifactNames.set(name, `${id}/${JSON.stringify(matrix)}/${attempt}`);
    }
  }
}
for (const prefix of ['browser-asset-ownership-', 'studio-performance-windows-', 'enterprise-certification-windows-']) {
  assert.ok(artifactNames.has(`${prefix}123-1`));
  assert.ok(artifactNames.has(`${prefix}123-2`));
}
const windowsUpload = jobs.get('upload-windows');
assert.equal(jobs.has('build-windows'), false, 'Release must promote the accepted Windows bytes without rebuilding.');
assert.match(jobs.get('validate'), /Require certified pre-tag Windows packages/);
assert.match(jobs.get('validate'), /collect-certified-windows\.mjs/);
assert.match(windowsUpload, /^    needs: \[validate, prepare-release\]/m);
assert.match(windowsUpload, /artifact-ids: \$\{\{ needs\.validate\.outputs\.windows_artifact_id \}\}/);
assert.match(windowsUpload, /run-id: \$\{\{ needs\.validate\.outputs\.windows_run_id \}\}/);
assert.match(windowsUpload, /github-token: \$\{\{ github.token \}\}/);
assert.doesNotMatch(windowsUpload, /publish-release\.ps1|dotnet build|npm ci/);
assert.ok(windowsUpload.indexOf('windows-release-packages.mjs verify') < windowsUpload.indexOf('gh release upload'));
assert.match(jobs.get('attest-provenance'), /needs: \[validate, upload-windows,/);
assert.match(jobs.get('attest-provenance'), /MANIFEST_SHA256: \$\{\{ needs\.upload-windows\.outputs\.manifest_sha256 \}\}/);
assert.match(jobs.get('attest-provenance'), /windows-release-packages\.mjs verify dist/);
for (const [suffix, rid] of [['intel', 'osx-x64'], ['arm64', 'osx-arm64']]) {
  const build = jobs.get(`build-macos-${suffix}`);
  assert.match(build, /^    needs: validate\r?$/m);
  assert.match(build, /uses: \.\/\.github\/workflows\/macos-release-package\.yml/);
  assert.match(build, new RegExp(`rid: ${rid}`));
  const upload = jobs.get('upload-macos');
  assert.ok(upload.includes('artifact-ids: ${{ needs.build-macos-' + suffix + '.outputs.artifact_id }}'));
  assert.ok(upload.includes(suffix + '_manifest_sha256: ${{ needs.build-macos-' + suffix + '.outputs.manifest_sha256 }}'));
  assert.ok(jobs.get('attest-provenance').includes(`needs.upload-macos.outputs.${suffix}_manifest_sha256`));
}
const macosBuild = readFileSync(new URL('../../.github/workflows/macos-release-package.yml', import.meta.url), 'utf8');
assert.doesNotMatch(macosBuild, /gh release upload|contents: write/);
assert.match(macosBuild, /artifact_id:[\s\S]+steps\.packages\.outputs\.artifact-id/);
assert.match(macosBuild, /manifest_sha256:[\s\S]+steps\.identity\.outputs\.manifest_sha256/);
assert.match(macosBuild, /DMG_OUTCOME: \$\{\{ steps\.dmg\.outcome \}\}/);
assert.match(macosBuild, /if \(\$env:DMG_OUTCOME -ceq 'success'\)/);
assert.match(macosBuild, /continue-on-error: true/);
assert.match(macosBuild, /macos-release-packages\.mjs @arguments/);
assert.match(macosBuild, /Invoke-CertificationProcess\.ps1[^\r\n]+-TimeoutSeconds 5400/);
const macosUpload = jobs.get('upload-macos');
for (const [job, platform] of [[windowsUpload, 'Windows'], [macosUpload, 'macOS']]) {
  assert.match(job, /timeout-minutes: 10/);
  const recovery = job.split(`      - name: Report ${platform} upload recovery`)[1];
  assert.ok(recovery, `${platform} upload must provide recovery instructions.`);
  assert.match(recovery, /^        if: failure\(\)/m);
  assert.ok(recovery.includes(`PACKAGE_PLATFORM: ${platform}`));
  assert.match(recovery, /PACKAGE_ARTIFACT_IDS:/);
  assert.match(recovery, /SOURCE_SHA: \$\{\{ needs\.validate\.outputs\.source_sha \}\}/);
  assert.match(recovery, /run: node scripts\/report-package-upload-recovery\.mjs/);
}
assert.doesNotMatch(macosUpload, /publish-release\.ps1|dotnet build|npm ci/);
assert.match(macosUpload, /^    needs: \[validate, prepare-release, build-macos-intel, build-macos-arm64\]/m);
for (const rid of ['osx-x64', 'osx-arm64']) {
  const verification = macosUpload.indexOf(`${rid} "$`);
  assert.ok(verification >= 0 && verification < macosUpload.indexOf('gh release upload'));
  assert.ok(jobs.get('attest-provenance').includes(`${rid} "$`));
}
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
  const extensionJob = ciSource.split(/^  build-vscode:\r?\n/m)[1].split(/^  [\w-]+:\r?\n/m)[0];
  assert.match(extensionJob, /^    timeout-minutes: 75/m);
  assert.match(extensionJob, /timeout-minutes: 17[\s\S]+Invoke-CertificationProcess\.ps1 -Command dotnet -CommandArguments @\('build', 'src\/ETL-SQL.App\/ETL-SQL.App.csproj', '--configuration', 'Debug'\)[^\r\n]+-TimeoutSeconds 900/);
  assert.match(extensionJob, /name: Retain extension CI diagnostics\r?\n        if: always\(\)/);
  assert.ok(extensionJob.includes('vscode-checks-${{ github.run_id }}-${{ github.run_attempt }}'));
  assert.match(extensionJob, /path: release-validation\/vscode\//);
  const expectedNpm = [
    ['Install browser compiler', 'ci --prefix scripts/typecheck', 'restore-compiler'],
    ['Install dependencies', 'ci', 'restore-extension'], ['Install UI dependencies', 'ci', 'restore-ui'],
    ['Audit npm packages for known vulnerabilities', 'audit', 'audit-extension'],
    ['Audit UI npm packages for known vulnerabilities', 'audit', 'audit-ui'],
    ['Compile extension', 'run compile', 'compile-extension'], ['Lint extension', 'run lint', 'lint-extension'],
    ['Lint UI package', 'run lint', 'lint-ui'], ['Build UI package', 'run build', 'build-ui'],
    ['Run UI vitest', 'run test:unit', 'test-ui'], ['Run vitest', 'run test:unit', 'test-extension'],
  ];
  const fixtureBash = process.platform === 'win32' && existsSync('C:/Program Files/Git/bin/bash.exe') ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
  for (const [name, args, log] of expectedNpm) {
    const step = extensionJob.split(/^      - /m).find(block => block.split(/\r?\n/)[0] === `name: ${name}`);
    assert.ok(step, `Missing extension step ${name}`);
    assert.match(step, /^        shell: bash/m);
    const limit = Number(step.match(/^        timeout-minutes: (\d+)/m)?.[1]);
    assert.ok(limit > 0 && limit < 75, `${name} needs a step limit within the job.`);
    const run = step.match(/^        run: (.+)$/m)[1];
    const commands = run === '|' ? step.split('        run: |')[1].replace(/^          /gm, '') : run;
    const relative = step.match(/^        working-directory: (.+)$/m)?.[1] ?? '';
    const cwd = path.join(directory, relative);
    mkdirSync(cwd, { recursive: true });
    for (const exit of [0, 7]) {
      const result = spawnSync(fixtureBash, ['-euo', 'pipefail', '-c', `npm() { printf 'args=%s\\n' "$*"; printf 'error-channel\\n' >&2; return "$FIXTURE_EXIT"; }\n${commands}`], {
        cwd, encoding: 'utf8', windowsHide: true, timeout: 10000,
        env: { ...process.env, GITHUB_WORKSPACE: directory.replaceAll('\\', '/'), FIXTURE_EXIT: String(exit) },
      });
      assert.equal(result.error, undefined);
      assert.equal(result.status, exit, `${name}: ${result.stdout} ${result.stderr}`);
      const output = readFileSync(path.join(directory, `release-validation/vscode/${log}.log`), 'utf8');
      assert.ok(output.includes(`args=${args}`));
      assert.match(output, /error-channel/);
    }
  }
  console.log('Extension CI: bounded steps retain both output channels and every npm pipeline propagates its original failure.');
  const studioJob = ciSource.split(/^  studio-performance-budgets:\r?\n/m)[1].split(/^  [\w-]+:\r?\n/m)[0];
  assert.match(studioJob, /^    timeout-minutes: 60/m);
  assert.ok(studioJob.includes('STUDIO_PLATFORM: ${{ matrix.platform }}'));
  assert.match(studioJob, /name: Retain Studio performance diagnostics\r?\n        if: always\(\)/);
  assert.ok(studioJob.includes('studio-performance-diagnostics-${{ matrix.platform }}-${{ github.run_id }}-${{ github.run_attempt }}'));
  assert.match(studioJob, /path: release-validation\/studio-performance\//);
  assert.match(studioJob, /PLAYWRIGHT_BROWSERS_PATH: \$\{\{ runner.temp \}\}\/ms-playwright/);
  assert.match(studioJob, /path: artifacts\/studio-performance\/\$\{\{ matrix.platform \}\}\.json/);
  mkdirSync(path.join(directory, 'scripts'), { recursive: true });
  writeFileSync(path.join(directory, 'scripts/Invoke-CertificationProcess.ps1'), `
param($Command, [string[]]$CommandArguments, $ScriptPath, [string[]]$ScriptArguments, $EvidenceDirectory, [int]$TimeoutSeconds)
@{ Command = $Command; CommandArguments = $CommandArguments; ScriptPath = $ScriptPath; ScriptArguments = $ScriptArguments; EvidenceDirectory = $EvidenceDirectory; TimeoutSeconds = $TimeoutSeconds } | ConvertTo-Json | Set-Content $env:WORKFLOW_RECEIPT
exit ([int]$env:FIXTURE_EXIT)
`);
  const studioStages = [
    ['Prepare browser assets', 'preparation', 360, 'node', ['scripts/sync-assets.js']],
    ['Build Studio performance gate', 'build', 900, 'dotnet', ['build', 'tests/ETL-SQL.Portal.BrowserTests/ETL-SQL.Portal.BrowserTests.csproj', '--configuration', 'Release']],
    ['Measure Studio performance', 'measurement', 900, null, null],
  ];
  for (const [name, phase, seconds, command, args] of studioStages) {
    const step = studioJob.split(/^      - /m).find(block => block.split(/\r?\n/)[0] === `name: ${name}`);
    assert.match(step, /^        shell: pwsh/m);
    const minutes = Number(step.match(/^        timeout-minutes: (\d+)/m)?.[1]);
    assert.ok(minutes * 60 > seconds && minutes < 60, `${name}: child deadline must leave diagnostic time.`);
    const commands = step.match(/^        run: (.+)$/m)[1];
    const harness = path.join(directory, 'studio-stage.ps1');
    writeFileSync(harness, `${commands}\nexit $LASTEXITCODE\n`);
    for (const platform of ['windows', 'linux', 'macos']) {
      for (const exit of [0, 7]) {
        const receipt = path.join(directory, 'studio-receipt.json');
        const result = spawnSync('pwsh', ['-NoProfile', '-File', harness], {
          cwd: directory, encoding: 'utf8', windowsHide: true, timeout: 10000,
          env: { ...process.env, STUDIO_PLATFORM: platform, WORKFLOW_RECEIPT: receipt, FIXTURE_EXIT: String(exit) },
        });
        assert.equal(result.error, undefined);
        assert.equal(result.status, exit, `${name}/${platform}: ${result.stdout} ${result.stderr}`);
        const actual = JSON.parse(readFileSync(receipt, 'utf8'));
        assert.equal(actual.TimeoutSeconds, seconds);
        assert.equal(actual.EvidenceDirectory, `release-validation/studio-performance/${platform}/${phase}`);
        if (command) {
          assert.equal(actual.Command, command);
          assert.deepEqual(actual.CommandArguments, args);
        } else {
          assert.equal(actual.ScriptPath, 'scripts/Measure-StudioPerformance.ps1');
          assert.deepEqual(actual.ScriptArguments, ['-Configuration', 'Release', '-NoBuild', '-OutputPath', `artifacts/studio-performance/${platform}.json`]);
        }
      }
    }
  }
  const studioRestore = studioJob.split(/^      - /m).find(block => block.startsWith('name: Install browser compiler'));
  assert.match(studioRestore, /timeout-minutes: 8/);
  assert.match(studioRestore, /shell: bash/);
  const restoreCommands = studioRestore.split('        run: |')[1].replace(/^          /gm, '');
  for (const exit of [0, 7]) {
    const result = spawnSync(fixtureBash, ['-euo', 'pipefail', '-c', `npm() { printf 'args=%s\\n' "$*"; printf 'restore-error\\n' >&2; return "$FIXTURE_EXIT"; }\n${restoreCommands}`], {
      cwd: directory, encoding: 'utf8', windowsHide: true, timeout: 10000,
      env: { ...process.env, FIXTURE_EXIT: String(exit) },
    });
    assert.equal(result.error, undefined);
    assert.equal(result.status, exit, result.stdout + result.stderr);
    const log = readFileSync(path.join(directory, 'release-validation/studio-performance/restore-compiler.log'), 'utf8');
    assert.match(log, /args=ci --prefix scripts\/typecheck/);
    assert.match(log, /restore-error/);
  }
  console.log('Studio performance CI: workflow commands preserve platform paths, deadlines, arguments and failure exits; restore retains both output channels.');
  const browserJob = ciSource.split(/^  portal-browser-journey:\r?\n/m)[1].split(/^  [\w-]+:\r?\n/m)[0];
  assert.match(browserJob, /^    timeout-minutes: 50/m);
  assert.equal([...browserJob.matchAll(/uses: actions\/setup-node@/g)].length, 1);
  assert.match(browserJob, /name: Retain browser shard diagnostics\r?\n        if: always\(\)/);
  assert.match(browserJob, /path: artifacts\/browser-shard\//);
  assert.match(browserJob, /-ScriptArguments @\('-Shard', '\$\{\{ matrix.shard \}\}', '-ShardCount', '3', '-Configuration', 'Release', '-NoBuild', '-EvidenceDirectory', 'artifacts\/browser-shard'\)/);
  assert.match(browserJob, /-TimeoutSeconds 1800/);
  for (const [name, phase, seconds, command, args] of [
    ['Prepare browser assets', 'preparation', 360, 'node', ['scripts/sync-assets.js']],
    ['Build browser lane', 'build', 900, 'dotnet', ['build', 'tests/ETL-SQL.Portal.BrowserTests/ETL-SQL.Portal.BrowserTests.csproj', '--configuration', 'Release']],
  ]) {
    const step = browserJob.split(/^      - /m).find(block => block.startsWith(`name: ${name}\n`) || block.startsWith(`name: ${name}\r\n`));
    assert.match(step, /^        shell: pwsh/m);
    assert.ok(Number(step.match(/^        timeout-minutes: (\d+)/m)?.[1]) * 60 > seconds);
    const harness = path.join(directory, 'browser-stage.ps1');
    writeFileSync(harness, `${step.match(/^        run: (.+)$/m)[1]}\nexit $LASTEXITCODE\n`);
    for (const exit of [0, 7]) {
      const receipt = path.join(directory, 'browser-receipt.json');
      const result = spawnSync('pwsh', ['-NoProfile', '-File', harness], {
        cwd: directory, encoding: 'utf8', windowsHide: true, timeout: 10000,
        env: { ...process.env, WORKFLOW_RECEIPT: receipt, FIXTURE_EXIT: String(exit) },
      });
      assert.equal(result.error, undefined);
      assert.equal(result.status, exit, `${name}: ${result.stdout} ${result.stderr}`);
      const actual = JSON.parse(readFileSync(receipt, 'utf8'));
      assert.equal(actual.Command, command);
      assert.deepEqual(actual.CommandArguments, args);
      assert.equal(actual.TimeoutSeconds, seconds);
      assert.equal(actual.EvidenceDirectory, `artifacts/browser-shard/${phase}`);
    }
  }
  for (const [name, args, log] of [
    ['Install browser compiler', 'ci --prefix scripts/typecheck', 'restore-compiler'],
    ['Restore VS Code UI dependencies', 'ci', 'restore-ui'],
    ['Build VS Code UI package', 'run build', 'build-ui'],
  ]) {
    const step = browserJob.split(/^      - /m).find(block => block.split(/\r?\n/)[0] === `name: ${name}`);
    assert.match(step, /^        shell: bash/m);
    assert.ok(Number(step.match(/^        timeout-minutes: (\d+)/m)?.[1]) < 50);
    const run = step.match(/^        run: (.+)$/m)[1];
    const commands = run === '|' ? step.split('        run: |')[1].replace(/^          /gm, '') : run;
    const cwd = path.join(directory, step.match(/^        working-directory: (.+)$/m)?.[1] ?? '');
    for (const exit of [0, 7]) {
      const result = spawnSync(fixtureBash, ['-euo', 'pipefail', '-c', `npm() { printf 'args=%s\\n' "$*"; printf 'browser-error\\n' >&2; return "$FIXTURE_EXIT"; }\n${commands}`], {
        cwd, encoding: 'utf8', windowsHide: true, timeout: 10000,
        env: { ...process.env, GITHUB_WORKSPACE: directory.replaceAll('\\', '/'), FIXTURE_EXIT: String(exit) },
      });
      assert.equal(result.error, undefined);
      assert.equal(result.status, exit, `${name}: ${result.stdout} ${result.stderr}`);
      const output = readFileSync(path.join(directory, `artifacts/browser-shard/${log}.log`), 'utf8');
      assert.ok(output.includes(`args=${args}`));
      assert.match(output, /browser-error/);
    }
  }
  console.log('Browser shard preparation: bounded commands retain original arguments, failure exits and both log channels inside shard artifacts.');
  const nativeSource = readFileSync(new URL('../../.github/workflows/native-packages.yml', import.meta.url), 'utf8');
  assert.match(nativeSource, /native-package:\r?\n    needs: package-impact\r?\n    if: needs\.package-impact\.outputs\.should_run == 'true'/);
  assert.match(nativeSource, /fetch-depth: 0/);
  assert.match(nativeSource, /cancel-in-progress: \$\{\{ github\.event_name == 'pull_request' \}\}/);
  assert.ok(nativeSource.includes("github.event_name == 'pull_request' && 'validation' || github.sha"));
  const groupTemplate = nativeSource.match(/^  group: (.+)$/m)[1];
  const groupFor = github => groupTemplate.replace(/\$\{\{([\s\S]*?)\}\}/g,
    (_, expression) => runInNewContext(expression, { github }, { timeout: 1000 }));
  const pr = { event_name: 'pull_request', ref: 'refs/pull/1/merge', sha: 'a'.repeat(40) };
  assert.equal(groupFor(pr), groupFor({ ...pr, sha: 'b'.repeat(40) }));
  assert.notEqual(groupFor(pr), groupFor({ ...pr, ref: 'refs/pull/2/merge' }));
  const manual = { ...pr, event_name: 'workflow_dispatch', ref: 'refs/heads/release/v0.22.0' };
  assert.notEqual(groupFor(manual), groupFor({ ...manual, sha: 'b'.repeat(40) }));
  assert.notEqual(groupFor(pr), groupFor(manual));
  const selectionSection = nativeSource.split('      - name: Select package consumers')[1];
  const selectionCommands = selectionSection.match(/^        run: \|\r?\n((?:          [^\r\n]*(?:\r?\n|$)|\r?\n)+)/m)[1].replace(/^          /gm, '');
  const harness = path.join(directory, 'native-selection.ps1');
  writeFileSync(harness, `
$ErrorActionPreference = 'Stop'
function node {
    $args | ConvertTo-Json -AsArray | Set-Content arguments.json
    New-Item -ItemType Directory -Force release-validation/preflight | Out-Null
    $result = @{}
    if ($env:MOCK_PACKAGES -ne 'missing') {
        $result.nativePackages = if ($env:MOCK_PACKAGES -eq 'true') { $true } elseif ($env:MOCK_PACKAGES -eq 'false') { $false } else { 'false' }
    }
    $result | ConvertTo-Json | Set-Content release-validation/preflight/native-impact.json
    $global:LASTEXITCODE = 0
}
${selectionCommands}
`);
  for (const [event, base, packages] of [['workflow_dispatch', '', 'true'], ['pull_request', 'b'.repeat(40), 'false'],
    ['pull_request', '', 'true'], ['pull_request', 'b'.repeat(40), 'missing'], ['pull_request', 'b'.repeat(40), 'string']]) {
    const output = path.join(directory, 'outputs.txt');
    writeFileSync(output, '');
    const result = spawnSync('pwsh', ['-NoProfile', '-File', harness], { cwd: directory, encoding: 'utf8', windowsHide: true, timeout: 10000,
      env: { ...process.env, GITHUB_EVENT_NAME: event, CANDIDATE_SHA: 'a'.repeat(40), IMPACT_BASE: base, MOCK_PACKAGES: packages, GITHUB_OUTPUT: output } });
    assert.equal(result.error, undefined);
    assert.equal(result.status, ['true', 'false'].includes(packages) ? 0 : 1, result.stdout + result.stderr);
    const args = JSON.parse(readFileSync(path.join(directory, 'arguments.json'), 'utf8'));
    assert.equal(args.includes('--full'), event === 'workflow_dispatch');
    assert.equal(args.includes('merge-base'), event === 'pull_request' && Boolean(base));
    if (result.status === 0) assert.equal(readFileSync(output, 'utf8').trim(), `should_run=${packages}`);
    else assert.equal(readFileSync(output, 'utf8').trim(), '');
  }
  const gate = nativeSource.split(/^  native-package-gate:\r?\n/m)[1];
  assert.match(gate, /^    needs: \[package-impact, native-package\]/m);
  assert.match(gate, /^    if: always\(\)/m);
  const commands = gate.split('        run: |')[1].replace(/^          /gm, '').replaceAll('\r\n', '\n');
  const bash = process.platform === 'win32' && existsSync('C:/Program Files/Git/bin/bash.exe') ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
  for (const impact of ['success', 'failure', 'cancelled', 'skipped']) {
    for (const required of ['true', 'false', '']) for (const outcome of ['success', 'failure', 'cancelled', 'skipped']) {
      const result = spawnSync(bash, ['-euo', 'pipefail', '-c', commands], { encoding: 'utf8', windowsHide: true, timeout: 10000,
        env: { ...process.env, IMPACT_RESULT: impact, PACKAGES_REQUIRED: required, PACKAGE_RESULT: outcome } });
      assert.equal(result.error, undefined);
      const accepted = impact === 'success' && ((required === 'true' && outcome === 'success') || (required === 'false' && outcome === 'skipped'));
      assert.equal(result.status === 0, accepted, `${impact}/${required}/${outcome}: ${result.stderr}`);
    }
  }
  console.log('Native package selection: full manual scope, PR comparison, typed impact, safe skip and failed/cancelled/missing gate outcomes passed.');
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
