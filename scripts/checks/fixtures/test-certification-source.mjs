import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { appendFileSync, copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../', import.meta.url));
assert.ok(process.argv.length === 2 || (process.argv.length === 4 && ['--legacy-producer', '--legacy-boundaries', '--legacy-enterprise'].includes(process.argv[2])
  && /^[a-f0-9]{7,40}$/.test(process.argv[3])), 'Expected no arguments or --legacy-producer/--legacy-boundaries/--legacy-enterprise <commit>.');
const temporary = mkdtempSync(path.join(os.tmpdir(), 'certification-source-'));
const legacy = process.argv[3] ?? null;
const templates = path.join(root, 'certification-results/production-canaries/20260906-173002-120/journey-evidence');
function git(repo, args) {
  const result = spawnSync('git', ['-C', repo, ...args], { encoding: 'utf8', windowsHide: true, timeout: 10000 });
  assert.equal(result.status, 0, result.stdout + result.stderr);
  return result.stdout.trim();
}
let next = 0;
function repository() {
  const repo = path.join(temporary, `repo-${next++}`);
  mkdirSync(path.join(repo, 'scripts'), { recursive: true });
  mkdirSync(path.join(repo, 'tests/fixtures'), { recursive: true });
  for (const name of ['Release.Helpers.ps1', 'ProductionCanaryEvidence.Helpers.ps1', 'Test-ProductionCanaryCertification.ps1', 'Test-EnterpriseHardeningCertification.ps1']) {
    copyFileSync(path.join(root, 'scripts', name), path.join(repo, 'scripts', name));
  }
  if (legacy) {
    const name = process.argv[2] === '--legacy-enterprise' ? 'Test-EnterpriseHardeningCertification.ps1' : 'Test-ProductionCanaryCertification.ps1';
    const old = spawnSync('git', ['-C', root, 'show', `${legacy}:scripts/${name}`], { encoding: 'utf8', windowsHide: true });
    assert.equal(old.status, 0, old.stderr);
    writeFileSync(path.join(repo, 'scripts', name), old.stdout);
  }
  copyFileSync(path.join(root, 'tests/fixtures/production-canary-plan.json'), path.join(repo, 'tests/fixtures/production-canary-plan.json'));
  writeFileSync(path.join(repo, '.gitignore'), 'out/\ncache/\n');
  writeFileSync(path.join(repo, 'tracked.txt'), 'baseline\n');
  git(repo, ['init', '--quiet']);
  for (const [key, value] of [['user.name', 'Certification fixture'], ['user.email', 'fixture@example.invalid'], ['commit.gpgsign', 'false'], ['core.autocrlf', 'false'], ['core.hooksPath', path.join(temporary, 'empty-hooks')]]) {
    git(repo, ['config', key, value]);
  }
  git(repo, ['add', '.']);
  git(repo, ['commit', '--quiet', '-m', 'Initial fixture source']);
  return repo;
}
function powershell(script, args) {
  return spawnSync('pwsh', ['-NoProfile', '-File', fileURLToPath(new URL(script, import.meta.url)), ...args], {
    encoding: 'utf8', windowsHide: true, timeout: 20000,
  });
}
try {
  const helper = powershell('./test-certification-source-helpers.ps1', ['-RepoRoot', repository()]);
  assert.equal(helper.status, 0, helper.stdout + helper.stderr);
  console.log(helper.stdout.trim());
  const boundaryModes = ['provisioning-tenant', 'provisioning-cost', 'credential-identity', 'credential-reason'];
  const modes = legacy ? (process.argv[2] === '--legacy-enterprise' ? [] : process.argv[2] === '--legacy-boundaries' ? boundaryModes : ['tracked']) : ['stable', 'dirty-stable', 'tracked', 'untracked', 'commit', 'initial-status-error', 'final-status-error', ...boundaryModes];
  for (const mode of modes) {
    const repo = repository();
    if (mode === 'dirty-stable') appendFileSync(path.join(repo, 'tracked.txt'), 'development change');
    const sha = git(repo, ['rev-parse', 'HEAD']);
    const run = powershell('./run-canary-source-probe.ps1', ['-RepoRoot', repo, '-TemplateRoot', templates, '-Mutation', mode]);
    if (legacy || mode === 'stable' || mode === 'dirty-stable') {
      assert.equal(run.status, 0, run.stdout + run.stderr);
      const outputs = readdirSync(path.join(repo, 'out'));
      assert.equal(outputs.length, 1);
      const report = JSON.parse(readFileSync(path.join(repo, 'out', outputs[0], 'certification.json'), 'utf8').replace(/^\uFEFF/, ''));
      assert.equal(report.commit, sha);
      assert.equal(report.result, 'Passed');
      assert.equal(report.dirty, mode === 'dirty-stable');
      if (!legacy) {
        assert.match(report.sourceFingerprint, /^[a-f0-9]{64}$/);
        if (mode === 'stable') assert.equal(report.sourceFingerprint, createHash('sha256').update(sha).digest('hex'));
      }
      if (legacy && mode === 'tracked') assert.notEqual(git(repo, ['status', '--porcelain']), '', 'Legacy producer should falsely describe this changed source as clean.');
    } else {
      assert.notEqual(run.status, 0, `Accepted ${mode}\n${run.stdout}\n${run.stderr}`);
      if (boundaryModes.includes(mode)) {
        const outputs = readdirSync(path.join(repo, 'out'));
        const report = JSON.parse(readFileSync(path.join(repo, 'out', outputs[0], 'certification.json'), 'utf8').replace(/^\uFEFF/, ''));
        assert.equal(report.result, 'Failed');
        assert.ok(report.boundaryFindingCount > 0);
        console.log(`Verified certification driver rejects boundary evidence: ${mode}`);
        continue;
      }
      assert.match(run.stderr, /Candidate source changed|Cannot establish certification candidate cleanliness/);
      const output = path.join(repo, 'out');
      assert.ok(!existsSync(output) || readdirSync(output).every(name => !existsSync(path.join(output, name, 'certification.json'))),
        'Source drift or failed identity lookup must not leave a completed certification report.');
    }
    console.log(`${legacy ? 'Reproduced unsafe legacy acceptance' : 'Verified certification driver'}: ${mode}`);
  }
  if (!legacy) {
    for (const configuration of ['default', 'Release', 'Debug', 'release']) {
      const repo = repository();
      const run = powershell('./run-enterprise-configuration-probe.ps1', ['-RepoRoot', repo, '-Configuration', configuration]);
      assert.equal(run.status, 0, run.stdout + run.stderr);
      const expected = configuration === 'default' || configuration.toLowerCase() === 'release' ? 'Release' : 'Debug';
      const report = JSON.parse(readFileSync(path.join(repo, 'out/enterprise-hardening-summary.json'), 'utf8').replace(/^\uFEFF/, ''));
      assert.equal(report.status, 'Passed');
      assert.equal(report.configuration, expected);
      assert.equal(report.releaseEligible, expected === 'Release');
      const commands = readFileSync(path.join(repo, 'out/arguments.jsonl'), 'utf8').trim().split(/\r?\n/).map(line => JSON.parse(line));
      assert.equal(commands.length, 2);
      assert.deepEqual(commands.map(args => args[args.indexOf('--configuration') + 1]), [expected, expected]);
      assert.ok(commands.every(args => args.includes('--no-build')));
      console.log(`Verified enterprise configuration and eligibility: ${configuration}`);
    }
    const invalid = powershell('./run-enterprise-configuration-probe.ps1', ['-RepoRoot', repository(), '-Configuration', 'Unknown']);
    assert.notEqual(invalid.status, 0, 'Unknown enterprise configurations must fail before running tests.');
  }
  if (!legacy || process.argv[2] === '--legacy-enterprise') {
    for (const mode of ['NoBuild', 'NoRestore', 'Build', 'FailedBuild', 'ExistingEvidence', ...(!legacy ? ['CleanupEscape', 'LinkedBuild', 'ConcurrentClaim'] : [])]) {
      const repo = repository();
      const run = powershell('./run-enterprise-preservation-probe.ps1', ['-RepoRoot', repo, '-Mode', mode]);
      assert.equal(run.status === 0, !['FailedBuild', 'CleanupEscape', 'LinkedBuild', 'ConcurrentClaim'].includes(mode) && (!!legacy || mode !== 'ExistingEvidence'), run.stdout + run.stderr);
      for (const part of ['engine', 'portal']) {
        const sentinel = path.join(repo, 'cache', part, 'keep.txt');
        if (legacy) assert.equal(existsSync(sentinel), false, 'Legacy cleanup must reproduce deleting the prior artifact.');
        else assert.equal(readFileSync(sentinel, 'utf8'), `Original ${part} artifact`);
      }
      if (!legacy) {
        if (['CleanupEscape', 'LinkedBuild'].includes(mode)) {
          assert.match(run.stderr, mode === 'CleanupEscape' ? /Refusing cleanup outside/ : /Refusing enterprise build cleanup through a link/);
        } else {
          assert.deepEqual(readdirSync(path.join(repo, 'cache')).sort(), ['engine', 'portal'], 'Owned temporary build roots must be cleaned without touching cached builds.');
        }
        if (mode === 'ExistingEvidence') {
          assert.equal(readFileSync(path.join(repo, 'out/enterprise-hardening-tests.log'), 'utf8'), 'Original failed-attempt log');
          assert.equal(readFileSync(path.join(repo, 'out/enterprise-hardening-summary.json'), 'utf8'), 'Original attempt summary');
          assert.equal(existsSync(path.join(repo, 'out/mock-invocations.jsonl')), false, 'Refuse evidence reuse before tests start.');
        }
        if (mode === 'ConcurrentClaim') {
          assert.match(run.stderr, /Cannot exclusively claim enterprise evidence directory/);
          assert.equal(readFileSync(path.join(repo, 'out/enterprise-attempt.json'), 'utf8'), 'Competing attempt claim');
          assert.equal(existsSync(path.join(repo, 'out/mock-invocations.jsonl')), false, 'Do not test in another attempt\'s directory.');
        }
      }
      console.log(`${legacy ? 'Reproduced destructive enterprise cleanup' : 'Verified enterprise artifact/evidence preservation'}: ${mode}`);
    }
  }
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
