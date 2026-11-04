// Synthetic transport/structure data. Report semantics are tested separately with real validators.
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { operatorTemplate } from '../../operator-release-evidence.mjs';

export function operatorFixture(directory, { sha = 'a'.repeat(40), version = '1.2.3', now = Date.now(), comparisonBase = 'b'.repeat(40) } = {}) {
  const checklist = operatorTemplate(sha, version, { comparisonBase, changedPathsSha256: 'c'.repeat(64), previousStableTag: 'v1.2.2' });
  checklist.reviewedBy = 'fixture-reviewer'; checklist.reviewedAt = new Date(now - 2000).toISOString(); checklist.generatedAt = new Date(now - 1000).toISOString();
  for (const row of checklist.reviews) { row.status = 'Passed'; row.reason = 'Synthetic review for transport and completeness tests.'; }
  for (const row of checklist.certifications) { row.status = 'NotApplicable'; row.reason = 'Synthetic scope decision bound to the declared source diff.'; row.command = 'Fixture certification command'; row.topology = 'Fixture topology'; }
  const write = (name, bytes) => { mkdirSync(path.dirname(path.join(directory, name)), { recursive: true }); writeFileSync(path.join(directory, name), bytes); };
  for (const row of checklist.reports) {
    write(row.path, '{"fixture":"not execution evidence"}');
    if (row.id === 'recovery') { // The real CLI prints its security-policy start-up lines before the version.
      write(row.cliVersionLog, `Security: Path Protection Mode set to 1.
Security: Loaded 1 allowed hosts.
${version}+${sha}
`); write(row.transcript, 'Synthetic validation transcript'); }
  }
  for (const row of checklist.reviews) for (const name of row.evidence) write(name, 'Synthetic operator review; not release evidence.');
  write('checklist.json', JSON.stringify(checklist));
  return checklist;
}
