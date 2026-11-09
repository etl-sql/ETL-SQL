import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { download } from '../setup-actionlint.mjs';

const body = Buffer.from('actionlint archive bytes');
const sha256 = createHash('sha256').update(body).digest('hex');
const ok = bytes => ({ ok: true, status: 200, arrayBuffer: async () => bytes });
const options = (responses, messages = []) => {
  const calls = [];
  return { calls, messages, fetchImpl: async (url, init) => {
    calls.push(init.signal);
    const next = responses.shift();
    if (next instanceof Error) throw next;
    return next;
  }, pauseMs: 1, report: message => messages.push(message) };
};

// A stalled first attempt (CI 42476400399) and a truncated second one are retried.
const recovered = options([new Error('The operation was aborted due to timeout'), ok(body.subarray(0, 5)), ok(body)]);
assert.deepEqual(await download('https://example.invalid/a', sha256, recovered), body);
assert.equal(recovered.calls.length, 3);
assert.ok(recovered.calls.every(signal => signal instanceof AbortSignal), 'Every attempt has its own deadline.');
assert.equal(recovered.messages.length, 2);

// Exhausted attempts report every cause.
const failed = options([{ ok: false, status: 503 }, new Error('reset'), { ok: false, status: 502 }]);
await assert.rejects(download('https://example.invalid/a', sha256, failed),
  /attempt 1: HTTP 503; attempt 2: reset; attempt 3: HTTP 502/);
console.log('actionlint download: stalled, truncated and refused attempts are retried; exhausted attempts name every cause.');
