// Keep only the latest attempt of each artifact downloaded from one workflow run. "Re-run failed
// jobs" does not rerun the jobs that passed, so their artifacts carry an earlier attempt suffix;
// a consumer that downloads only the current attempt would miss them. Download every attempt
// (pattern ...-<run_id>-*), then call this to drop superseded copies.
// Usage: node scripts/latest-attempt-artifacts.mjs <directory> <run_id> <current_attempt>
import { readdirSync, rmSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export function supersededArtifacts(names, runId, currentAttempt) {
  if (!/^\d+$/.test(String(runId)) || !Number.isSafeInteger(currentAttempt) || currentAttempt < 1) throw new Error('Invalid run identity.');
  // A fixed pattern; the run id is compared as text, never compiled into a RegExp.
  const latest = new Map();
  for (const name of names) {
    const match = name.match(/^(.+)-(\d+)-(\d+)$/);
    if (!match || match[2] !== String(runId)) throw new Error(`Artifact ${name} does not belong to run ${runId}.`);
    const attempt = Number(match[3]);
    if (attempt < 1 || attempt > currentAttempt) throw new Error(`Artifact ${name} names an attempt outside 1..${currentAttempt}.`);
    const kept = latest.get(match[1]);
    if (!kept || attempt > kept.attempt) latest.set(match[1], { name, attempt });
  }
  const keep = new Set([...latest.values()].map(item => item.name));
  return names.filter(name => !keep.has(name));
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [directory, runId, attempt] = process.argv.slice(2);
    if (process.argv.length !== 5) throw new Error('Usage: latest-attempt-artifacts.mjs <directory> <run_id> <current_attempt>');
    const names = readdirSync(directory).filter(name => statSync(path.join(directory, name)).isDirectory());
    const superseded = supersededArtifacts(names, runId, Number(attempt));
    for (const name of superseded) rmSync(path.join(directory, name), { recursive: true });
    console.log(`Kept ${names.length - superseded.length} artifacts; removed ${superseded.length} superseded by a later attempt.`);
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
