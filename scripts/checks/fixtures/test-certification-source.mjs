import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawnSync } from 'node:child_process';
import { appendFileSync, copyFileSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../../', import.meta.url));
assert.ok(process.argv.length === 2 || (process.argv.length === 4 && process.argv[2] === '--legacy-producer'
  && /^[a-f0-9]{7,40}$/.test(process.argv[3])), 'Expected no arguments or --legacy-producer <commit>.');
const temporary = mkdtempSync(path.join(os.tmpdir(), 'certification-source-'));
const legacy = process.argv[2] === '--legacy-producer' ? process.argv[3] : null;
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
  for (const name of ['Release.Helpers.ps1', 'ProductionCanaryEvidence.Helpers.ps1', 'Test-ProductionCanaryCertification.ps1']) {
    copyFileSync(path.join(root, 'scripts', name), path.join(repo, 'scripts', name));
  }
  if (legacy) {
    const old = spawnSync('git', ['-C', root, 'show', `${legacy}:scripts/Test-ProductionCanaryCertification.ps1`], { encoding: 'utf8', windowsHide: true });
    assert.equal(old.status, 0, old.stderr);
    writeFileSync(path.join(repo, 'scripts/Test-ProductionCanaryCertification.ps1'), old.stdout);
  }
  copyFileSync(path.join(root, 'tests/fixtures/production-canary-plan.json'), path.join(repo, 'tests/fixtures/production-canary-plan.json'));
  writeFileSync(path.join(repo, '.gitignore'), 'out/\n');
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
  const modes = legacy ? ['tracked'] : ['stable', 'dirty-stable', 'tracked', 'untracked', 'commit', 'initial-status-error', 'final-status-error'];
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
      if (legacy) assert.notEqual(git(repo, ['status', '--porcelain']), '', 'Legacy producer should falsely describe this changed source as clean.');
    } else {
      assert.notEqual(run.status, 0, `Accepted ${mode}\n${run.stdout}\n${run.stderr}`);
      assert.match(run.stderr, /Candidate source changed|Cannot establish certification candidate cleanliness/);
      const output = path.join(repo, 'out');
      assert.ok(!existsSync(output) || readdirSync(output).every(name => !existsSync(path.join(output, name, 'certification.json'))),
        'Source drift or failed identity lookup must not leave a completed certification report.');
    }
    console.log(`${legacy ? 'Reproduced unsafe legacy acceptance' : 'Verified certification driver'}: ${mode}`);
  }
} finally {
  rmSync(temporary, { recursive: true, force: true });
}
