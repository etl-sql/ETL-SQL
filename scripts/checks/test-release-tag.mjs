import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { checkReleaseTag } from '../check-release-tag.mjs';
import { fileURLToPath } from 'node:url';

const root = mkdtempSync(path.join(os.tmpdir(), 'release-tag-'));
const query = args => {
  const result = spawnSync('git', args, { cwd: root, encoding: 'utf8' });
  if (result.error) throw result.error;
  return result;
};
function git(...args) {
  const result = query(args);
  assert.equal(result.status, 0, result.stderr);
  return result.stdout.trim();
}
try {
  git('init', '-q');
  git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'core.hooksPath=/dev/null', 'commit', '--allow-empty', '--no-gpg-sign', '-qm', 'first');
  const first = git('rev-parse', 'HEAD');
  const options = { tag: 'v1.2.3', sha: first, remote: root };
  git('branch', 'v1.2.3');
  assert.deepEqual(checkReleaseTag(options, query), { localExists: false, remoteExists: false });
  git('-c', 'tag.gpgSign=false', 'tag', 'v1.2.3');
  assert.deepEqual(checkReleaseTag(options, query), { localExists: true, remoteExists: true });
  git('tag', '-d', 'v1.2.3');
  git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'tag.gpgSign=false', 'tag', '-a', 'v1.2.3', '-m', 'annotated');
  assert.notEqual(git('rev-parse', 'refs/tags/v1.2.3'), first);
  assert.deepEqual(checkReleaseTag(options, query), { localExists: true, remoteExists: true });
  git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'core.hooksPath=/dev/null', 'commit', '--allow-empty', '--no-gpg-sign', '-qm', 'second');
  git('branch', '-f', 'v1.2.3', 'HEAD');
  const branchCommit = git('rev-parse', 'refs/heads/v1.2.3');
  assert.notEqual(branchCommit, first);
  assert.deepEqual(checkReleaseTag(options, query), { localExists: true, remoteExists: true });
  assert.equal(git('rev-parse', 'refs/heads/v1.2.3'), branchCommit);
  // Execute each driver's actual local tagging block with a same-named branch at another commit.
  const scripts = fileURLToPath(new URL('../', import.meta.url));
  const ps = readFileSync(path.join(scripts, 'Invoke-Release.ps1'), 'utf8');
  const sh = readFileSync(path.join(scripts, 'invoke-release.sh'), 'utf8');
  const psBlock = ps.slice(ps.indexOf('$existingTagSha ='), ps.indexOf('Invoke-Native { git push $Remote "refs/tags/$Tag"'));
  const shBlock = sh.slice(sh.indexOf('if EXIST_SHA='), sh.indexOf('mut "git push $REMOTE refs/tags/$TAG"'));
  assert.ok(psBlock.includes('git tag') && shBlock.includes('git tag'));
  const psHarness = [
    '$ErrorActionPreference = "Stop"', '$Tag = "v1.2.4"', `$localSha = '${first}'`,
    'function Write-Info($message) {}', 'function Fail($message) { throw $message }',
    'function Invoke-Native { param([scriptblock]$Action, [string]$Describe, [switch]$Mutating) & $Action; if ($LASTEXITCODE) { throw $Describe } }',
    psBlock,
  ].join('\n');
  const shHarness = ['set -euo pipefail', 'TAG=v1.2.5', `LOCAL_SHA=${first}`, 'SIGN_TAG=0',
    'info() { :; }', 'die() { echo "$*" >&2; exit 1; }', 'mut() { shift; "$@"; }', shBlock].join('\n').replace(/\r\n/g, '\n');
  git('config', 'user.name', 'Fixture');
  git('config', 'user.email', 'fixture@example.invalid');
  git('config', 'tag.gpgSign', 'false');
  const bash = process.platform === 'win32' && existsSync('C:/Program Files/Git/bin/bash.exe') ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
  for (const [tag, name, content, command, args] of [
    ['v1.2.4', 'tag.ps1', psHarness, 'pwsh', ['-NoProfile', '-File', 'tag.ps1']],
    ['v1.2.5', 'tag.sh', shHarness, bash, ['tag.sh']],
  ]) {
    git('branch', tag);
    writeFileSync(path.join(root, name), content);
    const result = spawnSync(command, args, { cwd: root, encoding: 'utf8' });
    assert.equal(result.status, 0, result.stdout + result.stderr);
    assert.equal(git('rev-parse', `refs/tags/${tag}^{commit}`), first);
    assert.equal(git('rev-parse', `refs/heads/${tag}`), branchCommit);
  }
  assert.throws(() => checkReleaseTag({ ...options, sha: git('rev-parse', 'HEAD') }, query), /Remote tag.*elsewhere/);
  assert.throws(() => checkReleaseTag({ ...options, remote: path.join(root, 'missing') }, query), /Cannot inspect remote/);
  // No remote record must not hide a conflicting local tag.
  const noRemote = args => args[0] === 'ls-remote' ? { status: 0, stdout: '' } : query(args);
  assert.throws(() => checkReleaseTag({ ...options, sha: git('rev-parse', 'HEAD') }, noRemote), /Local tag/);
  assert.throws(() => checkReleaseTag(options, () => ({ status: 0, stdout: 'invalid response' })), /Invalid remote/);
  assert.throws(() => checkReleaseTag(options, args => args[0] === 'ls-remote' ? { status: 0, stdout: '' } : { status: 128, stdout: '' }), /Cannot inspect local/);
  console.log('Tag resume: absent, lightweight and annotated tags pass; conflicting tags and unavailable/invalid lookups fail.');
} finally {
  // Only remove the private test repository created above.
  rmSync(root, { recursive: true, force: true });
}
