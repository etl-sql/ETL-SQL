import assert from 'node:assert/strict';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { uploadCommands } from './checks/fixtures/release-upload-rehearsal.mjs';
import { windowsPackages } from './windows-release-packages.mjs';

// Diagnostic branch only. Production verification and upload commands stay unchanged;
// the destination is an isolated draft, and attempt one fails after its first real upload.
const env = process.env;
assert.match(env.REHEARSAL_TAG ?? '', /^rehearsal-upload-\d+$/);
assert.ok(['1', '2'].includes(env.GITHUB_RUN_ATTEMPT));
mkdirSync('evidence', { recursive: true });
const certification = JSON.parse(readFileSync('candidate/artifacts/native-packages/win-x64/certification.json'));
assert.equal(certification.commit, env.SOURCE_SHA);
const manifestHash = certification.packageManifestSha256;
assert.match(manifestHash, /^[a-f0-9]{64}$/);
const command = uploadCommands('upload-windows', 'Verify and upload Windows release assets');
writeFileSync('evidence/production-command.sh', command);
const wrapper = `gh() {
  [[ "$1" == release && "$2" == upload && "$3" == "$TAG_NAME" ]] || return 90
  local args=("$@")
  args[2]="$REHEARSAL_TAG"
  if [[ "$GITHUB_RUN_ATTEMPT" == 1 ]]; then
    for item in "$@"; do
      if [[ -f "$item" ]]; then
        command gh release upload "$REHEARSAL_TAG" --repo "$GITHUB_REPOSITORY" "$item" --clobber || return $?
        echo 'Controlled failure after first real package upload' >&2
        return 23
      fi
    done
    return 91
  fi
  command gh "\u0024{args[@]}"
}
`;
const result = spawnSync('bash', ['-euo', 'pipefail', '-c', wrapper + command], {
  encoding: 'utf8', timeout: 12 * 60 * 1000, maxBuffer: 1024 * 1024,
  env: { ...env, MANIFEST_SHA256: manifestHash },
});
writeFileSync('evidence/stdout.log', result.stdout ?? '');
writeFileSync('evidence/stderr.log', result.stderr ?? '');
writeFileSync('evidence/outcome.json', JSON.stringify({
  sourceSha: env.SOURCE_SHA, sourceRun: env.SOURCE_RUN, sourceArtifact: env.SOURCE_ARTIFACT,
  sourceDigest: env.SOURCE_DIGEST, manifestHash, rehearsalSha: env.GITHUB_SHA,
  runId: env.GITHUB_RUN_ID, attempt: env.GITHUB_RUN_ATTEMPT, exitCode: result.status,
  error: result.error?.message, completedAt: new Date().toISOString(),
}, null, 2));
console.log(result.stdout); console.error(result.stderr);
if (result.error || result.status !== 0) process.exit(result.status ?? 1);
const view = spawnSync('gh', ['release', 'view', env.REHEARSAL_TAG, '--repo', env.GITHUB_REPOSITORY, '--json', 'isDraft,assets'], { encoding: 'utf8' });
assert.equal(view.status, 0, view.stderr);
const release = JSON.parse(view.stdout);
assert.equal(release.isDraft, true);
assert.equal(release.assets.length, 3);
writeFileSync('evidence/draft.json', view.stdout);
const downloaded = spawnSync('gh', ['release', 'download', env.REHEARSAL_TAG, '--repo', env.GITHUB_REPOSITORY, '--dir', 'downloaded'], { encoding: 'utf8', timeout: 10 * 60 * 1000 });
assert.equal(downloaded.status, 0, downloaded.stderr);
await windowsPackages('verify', 'downloaded', env.SOURCE_SHA, env.TAG_NAME.slice(1), manifestHash);
writeFileSync('evidence/acceptance.json', JSON.stringify({ passed: true, draftOnly: true, sourceArtifact: env.SOURCE_ARTIFACT, manifestHash }));
console.log('Hosted upload recovery passed with original packages and verified downloaded draft bytes.');
