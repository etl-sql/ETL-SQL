import { spawn } from 'node:child_process';
import { copyFileSync, existsSync, mkdirSync, readFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';

// Keep native process isolation and private files for every case. Bound concurrency so startup
// latency does not serialize the entire matrix or overwhelm the source-preflight worker.
export async function runPrepushCases(fixture, cases, { concurrency = Math.min(4, os.availableParallelism()) } = {}) {
  if (!Number.isSafeInteger(concurrency) || concurrency < 1 || concurrency > 4) throw new Error('Invalid fixture concurrency.');
  const results = new Array(cases.length);
  let next = 0;
  async function worker() {
    while (next < cases.length) {
      const index = next++, directory = path.join(fixture, 'routing-cases', String(index));
      mkdirSync(path.join(directory, 'scripts'), { recursive: true });
      copyFileSync(path.join(fixture, 'scripts/Test-PrePush.ps1'), path.join(directory, 'scripts/Test-PrePush.ps1'));
      copyFileSync(path.join(fixture, 'prepush-fixture.ps1'), path.join(directory, 'prepush-fixture.ps1'));
      const started = Date.now();
      const result = await new Promise(resolve => {
        const child = spawn('pwsh', ['-NoProfile', '-File', path.join(directory, 'prepush-fixture.ps1')], {
          cwd: directory, windowsHide: true,
          env: { ...process.env, ...cases[index], IMPACT_FIXTURE_ROOT: directory },
        });
        let stdout = '', stderr = '', error;
        const timer = setTimeout(() => { error = new Error('Routing fixture exceeded its original 15-second deadline.'); child.kill(); }, 15000);
        child.stdout.setEncoding('utf8'); child.stderr.setEncoding('utf8');
        const append = (value, stream) => {
          if (stream === 'stdout') stdout += value; else stderr += value;
          if (stdout.length + stderr.length > 4 * 1024 * 1024) { error = new Error('Routing fixture exceeded its output limit.'); child.kill(); }
        };
        child.stdout.on('data', value => append(value, 'stdout'));
        child.stderr.on('data', value => append(value, 'stderr'));
        child.on('error', failure => { error = failure; });
        child.on('close', status => { clearTimeout(timer); resolve({ status, stdout, stderr, error }); });
      });
      const read = name => existsSync(path.join(directory, name)) ? readFileSync(path.join(directory, name), 'utf8') : '';
      results[index] = { ...result, commands: read('commands.txt'), restoredPerformance: read('restored-performance-output.txt') };
      console.log(`Pre-push routing case ${index + 1}/${cases.length}: exit ${result.status}, ${Date.now() - started} ms.`);
    }
  }
  const workers = await Promise.allSettled(Array.from({ length: Math.min(concurrency, cases.length) }, worker));
  const failed = workers.filter(result => result.status === 'rejected');
  if (failed.length) throw new AggregateError(failed.map(result => result.reason), 'Pre-push fixture workers failed.');
  return results;
}
