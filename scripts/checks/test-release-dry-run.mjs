// The release dry run (release.yml with dry_run: true) must never tag, publish, attest or touch a
// real release draft, and must delete only its own draft. These are static guarantees on the real
// workflow plus executions of its actual prepare and delete commands against a mocked gh.
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { uploadCommands } from './fixtures/release-upload-rehearsal.mjs';

const workflow = readFileSync(new URL('../../.github/workflows/release.yml', import.meta.url), 'utf8').replaceAll('\r\n', '\n');
const jobs = Object.fromEntries(workflow.split(/^jobs:\n/m)[1].split(/^(?=  [\w-]+:\n)/m)
  .filter(block => /^  [\w-]+:\n/.test(block)).map(block => [block.match(/^  ([\w-]+):/)[1], block]));
const step = (job, name) => {
  const block = jobs[job].split(/^      - /m).find(candidate => candidate.split('\n')[0] === `name: ${name}`);
  assert.ok(block, `${job}: missing step ${name}`);
  return block;
};

// Identity: a dry-run draft can never carry a version tag name, and never shares a real run's lock.
assert.match(workflow, /DRAFT_TAG: \$\{\{ inputs\.dry_run && format\('dry-run-\{0\}-\{1\}', inputs\.tag, github\.run_id\) \|\| inputs\.tag \|\| github\.ref_name \}\}/);
assert.match(workflow, /group: release-\$\{\{ inputs\.dry_run && 'dry-run-' \|\| '' \}\}/);

// Every release read and write names DRAFT_TAG; asset file names may still use TAG_NAME.
const releaseCommands = workflow.match(/gh release (?:view|edit|create|upload|download|delete) \S+/g);
assert.ok(releaseCommands.length >= 20);
for (const command of releaseCommands) assert.match(command, /^gh release \w+ (?:"\$DRAFT_TAG"|\$env:DRAFT_TAG)$/, command);
assert.doesNotMatch(workflow, /git (?:tag|push)\b/, 'Release never creates or pushes tags.');

// Nothing public: no publication, no attestation, no Marketplace in a dry run.
assert.match(step('publish-release', 'Publish draft release'), /^        if: env\.DRY_RUN != 'true'$/m);
assert.match(step('attest-provenance', 'Attest draft artifacts'), /^        if: env\.DRY_RUN != 'true'/m);
assert.match(jobs['publish-marketplace'], /needs\.publish-release\.result == 'success' && !inputs\.dry_run/);

// The dry run validates the exact release-branch HEAD instead of a tag.
assert.match(step('validate', 'Checkout'), /ref: \$\{\{ inputs\.dry_run && inputs\.candidate_sha \|\| env\.TAG_NAME \}\}/);
assert.match(step('validate', 'Verify release tag is on a supported release branch'), /candidate_sha equal to the release branch HEAD/);

// The delete job runs whatever happened, after every job that writes to the draft.
const discard = jobs['discard-dry-run-draft'];
assert.match(discard, /if: \$\{\{ always\(\) && inputs\.dry_run \}\}/);
const needs = discard.match(/needs: \[([^\]]+)\]/)[1].split(/,\s*/);
const writers = Object.entries(jobs).filter(([, block]) => /gh release (?:create|edit|upload)/.test(block)).map(([id]) => id);
for (const writer of writers.filter(id => id !== 'publish-release')) assert.ok(needs.includes(writer), `delete must wait for ${writer}`);
assert.ok(needs.includes('publish-release'));

// Execute the real prepare and delete commands with a mocked gh.
const bash = process.platform === 'win32' && existsSync('C:/Program Files/Git/bin/bash.exe') ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
const root = mkdtempSync(path.join(tmpdir(), 'release-dry-run-'));
function execute(commands, { draft, existing = false, state = 'true dry-run-v1.2.3-77', dryRun = 'true' }) {
  const log = path.join(root, 'gh.log');
  rmSync(log, { force: true });
  const mock = `gh() {
    printf '%s\\n' "$*" >> "${log.replaceAll('\\', '/')}"
    case "$1 $2" in
      "release view") [ "$EXISTING" = true ] || return 1; if [[ "$*" == *--jq* ]]; then printf '%s' "$STATE"; fi ;;
    esac
  }
`;
  const result = spawnSync(bash, ['-euo', 'pipefail', '-c', mock + commands], { cwd: root, encoding: 'utf8', windowsHide: true, timeout: 15000,
    env: { ...process.env, DRY_RUN: dryRun, DRAFT_TAG: draft, TAG_NAME: 'v1.2.3', SOURCE_SHA: 'a'.repeat(40), GITHUB_RUN_ID: '77',
      GITHUB_REPOSITORY: 'owner/repo', PRERELEASE: 'false', EXISTING: String(existing), STATE: state } });
  assert.equal(result.error, undefined);
  return { status: result.status, calls: existsSync(log) ? readFileSync(log, 'utf8').trim().split('\n') : [], output: result.stdout + result.stderr };
}
try {
  const prepare = uploadCommands('prepare-release', 'Create or update draft release');
  let run = execute(prepare, { draft: 'dry-run-v1.2.3-77' });
  assert.equal(run.status, 0, run.output);
  assert.match(run.calls.at(-1), /^release create dry-run-v1\.2\.3-77 --draft --target a{40} /);
  run = execute(prepare, { draft: 'dry-run-v1.2.3-77', existing: true });
  assert.notEqual(run.status, 0, 'A dry run never reuses an existing draft.');
  assert.ok(!run.calls.some(call => /release (?:edit|create)/.test(call)));
  run = execute(prepare, { draft: 'v1.2.3', existing: true, dryRun: 'false' });
  assert.equal(run.status, 0, run.output);
  assert.match(run.calls.at(-1), /^release edit v1\.2\.3 --draft/, 'A real release keeps reusing its draft.');

  const remove = uploadCommands('discard-dry-run-draft', 'Delete the draft this run created');
  run = execute(remove, { draft: 'dry-run-v1.2.3-77', existing: true });
  assert.equal(run.status, 0, run.output);
  assert.match(run.calls.at(-1), /^release delete dry-run-v1\.2\.3-77 --repo owner\/repo --yes$/);
  run = execute(remove, { draft: 'dry-run-v1.2.3-77' });
  assert.equal(run.status, 0, 'No draft (prepare failed) is not an error.');
  assert.ok(!run.calls.some(call => call.startsWith('release delete')));
  for (const [draft, state] of [['v1.2.3', 'true v1.2.3'], ['dry-run-v1.2.3-77', 'false dry-run-v1.2.3-77'], ['dry-run-v1.2.3-77', 'true v1.2.3']]) {
    run = execute(remove, { draft, existing: true, state });
    assert.notEqual(run.status, 0, `${draft} ${state}`);
    assert.ok(!run.calls.some(call => call.startsWith('release delete')), `${draft} ${state} must not be deleted`);
  }
} finally {
  rmSync(root, { recursive: true, force: true });
}
console.log('Release dry run: unique draft identity, every release command on the draft, no publication/attestation/Marketplace, exact-candidate validation, and draft-only deletion after every writer passed.');
