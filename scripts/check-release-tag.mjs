import { spawnSync } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function checkReleaseTag({ tag, sha, remote }, query = args => {
  const result = spawnSync('git', args, { encoding: 'utf8', timeout: 30000, windowsHide: true });
  if (result.error) throw new Error('Git tag lookup failed or timed out.');
  return result;
}) {
  if (!/^v\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(tag) || !/^[a-f0-9]{40}$/i.test(sha) || !remote || remote.startsWith('-')) throw new Error('Invalid release tag, SHA or remote.');
  const ref = `refs/tags/${tag}`;
  const remoteResult = query(['ls-remote', '--tags', remote, ref, `${ref}^{}`]);
  if (remoteResult.status !== 0) throw new Error('Cannot inspect remote tag; refusing to treat an unavailable remote as an absent tag.');
  const records = new Map();
  for (const line of remoteResult.stdout.trim().split(/\r?\n/).filter(Boolean)) {
    const [hash, name, extra] = line.split(/\s+/);
    if (!/^[a-f0-9]{40}$/i.test(hash) || ![ref, `${ref}^{}`].includes(name) || extra || records.has(name)) throw new Error('Invalid remote tag response.');
    records.set(name, hash);
  }
  if (records.has(`${ref}^{}`) && !records.has(ref)) throw new Error('Remote tag response is missing its tag object.');
  const remoteSha = records.get(`${ref}^{}`) ?? records.get(ref);
  if (remoteSha && remoteSha !== sha) throw new Error(`Remote tag ${tag} points elsewhere. Refusing to move or reuse it.`);
  const local = query(['show-ref', '--verify', '--quiet', ref]);
  if (![0, 1].includes(local.status)) throw new Error('Cannot inspect local tag.');
  if (local.status === 0) {
    const peeled = query(['rev-parse', '--verify', `${ref}^{commit}`]);
    if (peeled.status !== 0 || peeled.stdout.trim() !== sha) throw new Error(`Local tag ${tag} does not resolve to the candidate commit. Refusing to move or reuse it.`);
  }
  return { localExists: local.status === 0, remoteExists: Boolean(remoteSha) };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    if (process.argv.length !== 5) throw new Error('Usage: node scripts/check-release-tag.mjs <tag> <SHA> <remote>');
    const [, , tag, sha, remote] = process.argv;
    const result = checkReleaseTag({ tag, sha, remote });
    console.log(result.localExists || result.remoteExists ? 'Existing release tags match the candidate; safe to resume without Force.' : 'Release tag is available.');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
