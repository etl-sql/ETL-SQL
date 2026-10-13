#!/usr/bin/env node
/**
 * Runs every `scripts/checks/test-*.mjs` consumer-contract check.
 *
 * These are the checks that read the shipped browser sources, Portal pages and controllers and
 * assert that a contract still holds — that a formatting picker still exists, that a governance
 * view is still role-gated, that data-quality polling still points at the namespace that answers.
 * None of them ran in pre-push or CI, which is exactly why eight of them were red at once and
 * nobody knew: three had drifted behind code that moved, three were asserting the product as it
 * used to be, and two were reporting real defects.
 *
 * A check that nothing runs is a comment. This is the gate that makes them checks again.
 *
 * Usage:
 *   node scripts/check-consumer-contracts.mjs            # all of them
 *   node scripts/check-consumer-contracts.mjs --only dq  # just the ones whose name contains "dq"
 *   node scripts/check-consumer-contracts.mjs --list     # names only, run nothing
 */
import { spawnSync } from 'node:child_process';
import { readdirSync } from 'node:fs';
import path from 'node:path';
import process from 'node:process';
import { fileURLToPath } from 'node:url';

const scriptRoot = path.dirname(fileURLToPath(import.meta.url));
const checkRoot = path.join(scriptRoot, 'checks');

/**
 * Scripts outside `checks/` that the gate still runs, with the arguments that make them a check.
 *
 * `test-service-capacity.mjs` is the only entry: it is not a check, it is the load driver that
 * `checks/test-service-capacity-smoke.mjs` exercises end to end against a stub server, and it stays
 * in `scripts/` because runbooks and the HA soak service name it there. Run bare it dials a live
 * Portal and fails with a login error on any machine that has not got one. `--validate-only` asks
 * it the question a gate can answer — is the workload configuration still valid — and leaves the
 * load test to the smoke script.
 */
const OUTSIDE_CHECKS = {
  'test-service-capacity.mjs': ['--validate-only'],
};

const args = process.argv.slice(2);
const only = args.includes('--only') ? args[args.indexOf('--only') + 1] : null;
const listOnly = args.includes('--list');

const checks = [
  ...readdirSync(checkRoot)
    .filter(name => name.startsWith('test-') && name.endsWith('.mjs'))
    .map(name => ({ name, file: path.join(checkRoot, name), label: `scripts/checks/${name}`, extra: [] })),
  ...Object.entries(OUTSIDE_CHECKS)
    .map(([name, extra]) => ({ name, file: path.join(scriptRoot, name), label: `scripts/${name}`, extra })),
]
  .filter(check => !only || check.name.includes(only))
  .sort((left, right) => left.name.localeCompare(right.name));

if (checks.length === 0) {
  console.error(only ? `No consumer checks match "${only}".` : 'No consumer checks found.');
  process.exit(1);
}

if (listOnly) {
  for (const check of checks) console.log(check.name);
  process.exit(0);
}

const started = Date.now();
const failures = [];

for (const { name, file, label, extra } of checks) {
  const result = spawnSync(process.execPath, [file, ...extra], {
    cwd: path.resolve(scriptRoot, '..'),
    encoding: 'utf8',
  });

  if (result.status === 0) continue;

  failures.push({
    name,
    label,
    extra,
    // A check that cannot start reports nothing on stdout, so both streams are kept.
    output: [result.stdout, result.stderr].filter(Boolean).join('\n').trimEnd(),
    status: result.status,
  });
}

const seconds = ((Date.now() - started) / 1000).toFixed(1);

if (failures.length === 0) {
  console.log(`consumer contract gate: ${checks.length} checks passed in ${seconds}s`);
  process.exit(0);
}

console.error(`consumer contract gate: ${failures.length} of ${checks.length} checks failed in ${seconds}s\n`);
for (const failure of failures) {
  const invocation = ['node', failure.label, ...failure.extra].join(' ');
  console.error(`── ${failure.name} (exit ${failure.status}) — reproduce with: ${invocation}`);
  console.error(failure.output);
  console.error('');
}
process.exit(1);
