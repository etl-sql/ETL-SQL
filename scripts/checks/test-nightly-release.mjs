import assert from 'node:assert/strict';
import { activeBranches, dispatch } from '../dispatch-nightly-release.mjs';

const sha = 'a'.repeat(40);
const ref = version => ({ ref: `refs/heads/release/v${version}`, object: { sha } });
const releases = [
  { tag_name: 'v0.21.0', draft: false, prerelease: false },
  { tag_name: 'v0.23.0', draft: true },
  { tag_name: 'v0.99.0-rc.1', prerelease: true },
];
const refs = [ref('0.20.0'), ref('0.21.0'), ref('0.22.0'), ref('0.100.0'), ref('nonsense')];
assert.deepEqual(activeBranches(refs, releases).map(r => r.branch), ['release/v0.22.0', 'release/v0.100.0']);
assert.throws(() => activeBranches(refs, []), /baseline/);
assert.throws(() => activeBranches([{ ...ref('0.22.0'), object: { sha: 'bad' } }], releases), /SHA/);
for (const status of ['in_progress', 'queued', 'completed']) {
  const writes = [];
  dispatch('owner/repo', args => {
    if (args[0] === 'workflow') { writes.push(args); return ''; }
    const endpoint = args.at(-1);
    if (endpoint.includes('matching-refs')) return JSON.stringify([[ref('0.22.0')]]);
    if (endpoint.includes('releases?')) return JSON.stringify([releases]);
    return JSON.stringify({ workflow_runs: [{ status }] });
  });
  assert.equal(writes.length, status === 'completed' ? 1 : 0);
  if (writes.length) assert.deepEqual(writes[0].slice(-4), ['--ref', 'release/v0.22.0', '-f', `candidate_sha=${sha}`]);
}
assert.throws(() => dispatch('owner/repo', () => { throw new Error('API unavailable'); }), /API unavailable/);
console.log('Nightly selection: version ordering, released/draft/prerelease boundaries, exact SHA, active-run suppression and API failure passed.');
