// A push from a git worktree hands the pre-push hook GIT_DIR and friends. On 2026-11-04 fixtures
// that `git init` a temp repo inherited them and rewrote the shared repository's config and refs.
// This runs the real hook's preamble in a worktree push, then a fixture that writes git config, and
// checks the shared repository is untouched. A control run without the preamble must still leak,
// so the check cannot pass just because Git stopped exporting the variables.
import assert from 'node:assert/strict';
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const hookSource = readFileSync(fileURLToPath(new URL('../pre-push', import.meta.url)), 'utf8').replace(/\r\n/g, '\n');
const cut = hookSource.indexOf('\n# Git passes one');
assert.ok(cut > 0, 'scripts/pre-push no longer has the marker this check cuts at; update the check');
const preamble = hookSource.slice(0, cut);
assert.match(preamble, /unset \$\(git rev-parse --local-env-vars\)/, 'scripts/pre-push must clear git local env vars before running fixtures');

const fixture = `
fixture_dir=$(mktemp -d)
cd "$fixture_dir"
git init -q fixture
cd fixture
git config core.bare true
git config user.name LeakedFixture
exit 0
`;

function run(cwd, args, env = {}) {
  const result = spawnSync('git', args, { cwd, encoding: 'utf8', env: { ...process.env, ...env } });
  if (result.error) throw result.error;
  assert.equal(result.status, 0, `git ${args.join(' ')}\n${result.stderr}`);
  return result.stdout.trim();
}

function pushFromWorktree(hookBody) {
  const root = mkdtempSync(path.join(os.tmpdir(), 'pre-push-git-env-'));
  try {
    const identity = ['-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', '-c', 'commit.gpgSign=false'];
    run(root, ['init', '-q', '--bare', 'remote.git']);
    run(root, ['init', '-q', 'main']);
    const main = path.join(root, 'main');
    run(main, [...identity, '-c', 'core.hooksPath=/dev/null', 'commit', '-q', '--allow-empty', '-m', 'init']);
    const hooks = path.join(root, 'hooks');
    mkdirSync(hooks);
    const hook = path.join(hooks, 'pre-push');
    writeFileSync(hook, `${hookBody}\n${fixture}`);
    chmodSync(hook, 0o755);
    run(main, ['config', 'core.hooksPath', hooks.replace(/\\/g, '/')]);
    run(main, ['worktree', 'add', '-q', '-b', 'feature', path.join(root, 'wt')]);
    const worktree = path.join(root, 'wt');
    run(worktree, [...identity, 'commit', '-q', '--allow-empty', '-m', 'work']);
    run(worktree, ['push', '-q', path.join(root, 'remote.git').replace(/\\/g, '/'), 'feature']);
    const local = spawnSync('git', ['config', '--local', '--list'], { cwd: main, encoding: 'utf8' }).stdout;
    return { bare: /^core\.bare=true$/m.test(local), leakedUser: /^user\.name=LeakedFixture$/m.test(local) };
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

const control = pushFromWorktree('#!/bin/sh\nset -eu');
assert.ok(control.bare && control.leakedUser,
  'control push without the preamble no longer leaks; this check would pass vacuously, so revisit it');

const guarded = pushFromWorktree(preamble);
assert.equal(guarded.bare, false, 'a pre-push fixture set core.bare=true on the shared repository');
assert.equal(guarded.leakedUser, false, 'a pre-push fixture wrote user.name into the shared repository');

console.log('pre-push clears inherited git environment before fixtures run');
