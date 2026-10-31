import { spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';

export function operatorComparison(sha, base, { execute = args => spawnSync('git', args, { windowsHide: true, timeout: 60000, maxBuffer: 32 * 1024 * 1024 }) } = {}) {
  if (!/^[a-f0-9]{40}$/.test(sha ?? '') || typeof base !== 'string' || !/^(?:[a-f0-9]{40}|v(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*))$/.test(base)) throw new Error('Invalid operator comparison identity.');
  function git(args) {
    const result = execute(args);
    if (result.error || result.status !== 0) throw new Error('Cannot establish operator comparison from Git.');
    return Buffer.from(result.stdout);
  }
  const comparisonBase = git(['rev-parse', '--verify', `${base}^{commit}`]).toString().trim();
  if (!/^[a-f0-9]{40}$/.test(comparisonBase) || comparisonBase === sha) throw new Error('Invalid operator comparison base.');
  git(['merge-base', '--is-ancestor', comparisonBase, sha]);
  return { comparisonBase, ...(base.startsWith('v') ? { previousStableTag: base } : {}),
    changedPathsSha256: createHash('sha256').update(git(['diff', '--name-status', '-z', '--no-renames', comparisonBase, sha])).digest('hex') };
}

export function previousOperatorRelease(releases, version) {
  const parse = value => {
    const match = typeof value === 'string' && value.length <= 64 ? /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.exec(value) : null;
    return match ? match.slice(1).map(BigInt) : null;
  };
  const current = parse(version);
  if (!current || !Array.isArray(releases)) throw new Error('Invalid release comparison inventory.');
  const compare = (a, b) => { for (let i = 0; i < 3; i++) if (a[i] !== b[i]) return a[i] > b[i] ? 1 : -1; return 0; };
  const eligible = releases.filter(row => row.draft === false && row.prerelease === false && row.tag_name?.startsWith('v'))
    .map(row => ({ tag: row.tag_name, value: parse(row.tag_name.slice(1)) }))
    .filter(row => row.value && compare(row.value, current) < 0).sort((a, b) => compare(b.value, a.value));
  if (!eligible.length || eligible.length > 1 && eligible[0].tag === eligible[1].tag) throw new Error('Missing or ambiguous previous stable release.');
  return eligible[0].tag;
}
