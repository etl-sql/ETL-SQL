import { spawnSync } from 'node:child_process';
import { executable, lock } from './setup-actionlint.mjs';

const version = spawnSync(executable, ['-version'], { encoding: 'utf8', windowsHide: true, timeout: 10000 });
if (version.error || version.status !== 0 || version.stdout.split(/\r?\n/)[0] !== lock.version) {
  throw new Error('Prepare the pinned workflow checker first: node scripts/setup-actionlint.mjs');
}
// External shell/Python analyzers are separate tools, not prerequisites of this source gate.
const result = spawnSync(executable, ['-shellcheck=', '-pyflakes=', ...process.argv.slice(2)], {
  stdio: 'inherit', windowsHide: true, timeout: 45000,
});
if (result.error) throw result.error;
process.exitCode = result.status ?? 1;
