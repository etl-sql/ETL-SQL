// Tag Release (tag-release.yml) is the release button: it tags the certified commit from Actions.
// Tags are immutable, so every gate must pass before the tag exists, and a rerun must reuse a tag
// already on the candidate. The Marketplace publish must use the packaging vsce and be rerunnable.
// Static guarantees on the real workflows plus an execution of the real tag commands with mocks.
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import path from 'node:path';

const read = name => readFileSync(new URL(`../../${name}`, import.meta.url), 'utf8').replaceAll('\r\n', '\n');
function jobsOf(workflow) {
  return Object.fromEntries(workflow.split(/^jobs:\n/m)[1].split(/^(?=  [\w-]+:\n)/m)
    .filter(block => /^  [\w-]+:\n/.test(block)).map(block => [block.match(/^  ([\w-]+):/)[1], block]));
}
function stepOf(jobs, job, name) {
  const block = jobs[job]?.split(/^      - /m).find(candidate => candidate.split('\n')[0] === `name: ${name}`);
  assert.ok(block, `${job}: missing step ${name}`);
  return block;
}
const runOf = step => step.match(/^        run: \|\n((?:          [^\n]*\n|\n)+)/m)[1].replace(/^          /gm, '');

const workflow = read('.github/workflows/tag-release.yml');
const jobs = jobsOf(workflow);

// Only a person starts it, on a release branch, for one named commit that is that branch's HEAD.
assert.match(workflow, /^on:\n  workflow_dispatch:\n    inputs:\n      candidate_sha:\n/m);
assert.doesNotMatch(workflow, /^  (?:push|schedule|workflow_run|pull_request\w*):/m);
assert.match(workflow, /^permissions:\n  contents: read\n/m);
const identity = stepOf(jobs, 'verify-candidate', 'Verify candidate is the release branch HEAD');
assert.match(identity, /GITHUB_REF -notmatch '\^refs\/heads\/release\/v\\d\+\\\.\\d\+\\\.\\d\+\$'/);
assert.match(identity, /CANDIDATE_SHA -cne \$env:GITHUB_SHA/);

// Every gate the Release run checks after the tag runs here first, against the same commit.
for (const [job, name, script] of [
  ['verify-candidate', 'Require final release metadata', 'check-release-metadata.mjs'],
  ['verify-candidate', 'Require the version tag to be free or already on the candidate', 'check-release-tag.mjs'],
  ['verify-candidate', 'Require exact-candidate CI and CodeQL evidence', 'collect-hosted-release-evidence.mjs'],
  ['verify-candidate', 'Require certified pre-tag Windows packages', 'collect-certified-windows.mjs'],
  ['verify-operator', 'Require candidate operator submission', 'collect-operator-release-evidence.mjs'],
]) {
  const step = stepOf(jobs, job, name);
  assert.match(step, new RegExp(`scripts/${script.replaceAll('.', '\\.')}`), name);
  assert.doesNotMatch(step, /continue-on-error|^        if:/m, `${name} can never be skipped`);
}
const release = read('.github/workflows/release.yml');
for (const script of ['check-release-metadata.mjs', 'collect-hosted-release-evidence.mjs', 'collect-certified-windows.mjs', 'collect-operator-release-evidence.mjs']) {
  assert.ok(release.includes(`scripts/${script}`), `Release no longer runs ${script}; update Tag Release to match`);
}

// Only the tag job can write a ref or start a run, and only after both verification jobs passed.
assert.match(jobs.tag, /^    needs: \[verify-candidate, verify-operator\]$/m);
assert.match(jobs.tag, /^    permissions:\n      contents: write\n      actions: write\n/m);
for (const job of ['verify-candidate', 'verify-operator']) assert.doesNotMatch(jobs[job], /git (?:tag|push)|gh workflow run|actions: write/, job);
const start = runOf(stepOf(jobs, 'tag', 'Start Release on the tag'));
assert.match(start, /gh workflow run release\.yml --repo "\$GITHUB_REPOSITORY" --ref "\$TAG_NAME" -f tag="\$TAG_NAME"/);

// Execute the real tag commands with git and node mocked: a new tag is created on the candidate and
// pushed by its full ref; a tag already on the candidate is reused, never recreated or moved.
const tagCommands = runOf(stepOf(jobs, 'tag', 'Create and push the release tag'));
const bash = process.platform === 'win32' && existsSync('C:/Program Files/Git/bin/bash.exe') ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
const root = mkdtempSync(path.join(tmpdir(), 'release-tag-workflow-'));
function execute({ remoteTag, tagCheck = 0 }) {
  const log = path.join(root, 'calls.log').replaceAll('\\', '/');
  rmSync(log, { force: true });
  const mock = `node() { printf 'node %s\\n' "$*" >> "${log}"; return ${tagCheck}; }
git() {
  printf 'git %s\\n' "$*" >> "${log}"
  if [ "$1" = ls-remote ]; then [ "$REMOTE_TAG" = true ]; return; fi
}
`;
  const result = spawnSync(bash, ['-c', mock + tagCommands], { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 15000,
    env: { ...process.env, TAG_NAME: 'v1.2.3', CANDIDATE_SHA: 'a'.repeat(40), REMOTE_TAG: String(remoteTag) } });
  assert.equal(result.error, undefined);
  return { status: result.status, calls: existsSync(log) ? readFileSync(log, 'utf8').trim().split('\n') : [], output: result.stdout + result.stderr };
}
try {
  let run = execute({ remoteTag: false });
  assert.equal(run.status, 0, run.output);
  assert.equal(run.calls[0], `node scripts/check-release-tag.mjs v1.2.3 ${'a'.repeat(40)} origin`);
  assert.ok(run.calls.includes(`git tag -a v1.2.3 -m ETL-SQL v1.2.3 ${'a'.repeat(40)}`), run.calls.join('\n'));
  assert.equal(run.calls.at(-1), 'git push origin refs/tags/v1.2.3');

  run = execute({ remoteTag: true });
  assert.equal(run.status, 0, run.output);
  assert.ok(!run.calls.some(call => /^git (?:tag|push)/.test(call)), 'An existing tag on the candidate is reused.');

  run = execute({ remoteTag: false, tagCheck: 1 });
  assert.notEqual(run.status, 0, 'A tag elsewhere stops the run.');
  assert.ok(!run.calls.some(call => /^git (?:tag|push)/.test(call)));
} finally {
  rmSync(root, { recursive: true, force: true });
}

// Marketplace: the vsce that packaged the VSIXs publishes them, and a rerun skips what is published.
const vsce = JSON.parse(read('scripts/vsix/package.json')).devDependencies?.['@vscode/vsce']
  ?? JSON.parse(read('scripts/vsix/package.json')).dependencies?.['@vscode/vsce'];
assert.match(vsce, /^\d+\.\d+\.\d+$/, 'scripts/vsix pins an exact vsce');
const marketplace = jobsOf(release)['publish-marketplace'];
const publishes = marketplace.match(/@vscode\/vsce@\S+ publish[^\n]*/g);
assert.deepEqual(publishes, [`@vscode/vsce@${vsce} publish --oidc --skip-duplicate --packagePath "$vsix"`]);

console.log('Tag Release: dispatch-only on a release-branch HEAD, every Release gate before the tag, tag reused never moved, Release started on the tag; Marketplace publish pinned and rerunnable.');
