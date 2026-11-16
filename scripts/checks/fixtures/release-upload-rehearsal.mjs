import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';

export function uploadCommands(jobId, stepName) {
  const workflow = readFileSync(new URL('../../../.github/workflows/release.yml', import.meta.url), 'utf8');
  const job = workflow.split(new RegExp(`^  ${jobId}:\\r?\\n`, 'm'))[1]?.split(/^  [\w-]+:\r?\n/m)[0];
  assert.ok(job, `Missing workflow job ${jobId}`);
  const step = job.split(/^      - /m).find(block => block.split(/\r?\n/)[0] === `name: ${stepName}`);
  assert.ok(step, `Missing workflow step ${stepName}`);
  const body = step.match(/^        run: \|\r?\n((?:          [^\r\n]*(?:\r?\n|$)|\r?\n)+)/m)?.[1];
  assert.ok(body, `Missing commands for ${stepName}`);
  return body.replace(/^          /gm, '').replaceAll('\r\n', '\n');
}

export function runUpload(commands, cwd, env) {
  const bash = process.platform === 'win32' && existsSync('C:/Program Files/Git/bin/bash.exe') ? 'C:/Program Files/Git/bin/bash.exe' : 'bash';
  const mock = `gh() {
    printf 'called\\n' >> uploads.log
    mkdir -p uploaded
    for arg in "$@"; do
      if [ -f "$arg" ]; then
        cp "$arg" uploaded/
        if [ -f fail-next-upload ]; then rm fail-next-upload; return 23; fi
      fi
    done
  }
`;
  const result = spawnSync(bash, ['-euo', 'pipefail', '-c', mock + commands], {
    cwd, env, encoding: 'utf8', windowsHide: true, timeout: 15000,
  });
  assert.equal(result.error, undefined);
  return result;
}
