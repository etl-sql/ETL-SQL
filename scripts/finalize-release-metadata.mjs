import { readFileSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { metadataFindings } from './check-release-metadata.mjs';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export function planReleaseMetadata(tag, date, read) {
  if (!/^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/.test(tag) ||
      !/^\d{4}-\d{2}-\d{2}$/.test(date) || !Number.isFinite(Date.parse(`${date}T00:00:00Z`)) ||
      new Date(`${date}T00:00:00Z`).toISOString().slice(0, 10) !== date) throw new Error('Supply a stable version tag and a valid YYYY-MM-DD date.');
  const files = ['Directory.Build.props', 'CHANGELOG.md', `docs/releases/${tag}.md`, 'docs/releases/README.md'];
  const original = Object.fromEntries(files.map(name => [name, read(name)]));
  const proposed = Object.fromEntries(files.map(name => [name, original[name].replace(/\r\n/g, '\n')]));
  const version = tag.slice(1);
  const escaped = version.replaceAll('.', '\\.');
  const changelog = 'CHANGELOG.md';
  const heading = new RegExp(`^## \\[${escaped}\\][^\\n]*$`, 'gm');
  const matches = [...proposed[changelog].matchAll(heading)];
  if (matches.length > 1) throw new Error('Duplicate version headings in CHANGELOG.md.');
  function protectDate(text) {
    const existing = text.match(/\b\d{4}-\d{2}-\d{2}\b/)?.[0];
    if (existing && existing !== date) throw new Error(`Refusing to change finalized release date ${existing}.`);
  }
  if (matches.length === 1) {
    protectDate(matches[0][0]);
    proposed[changelog] = proposed[changelog].replace(heading, () => `## [${version}] — ${date}`);
  } else {
    const unreleased = [...proposed[changelog].matchAll(/^## \[Unreleased\]\s*$/gm)];
    if (unreleased.length !== 1) throw new Error('Need one Unreleased heading or one existing version heading in CHANGELOG.md.');
    proposed[changelog] = proposed[changelog].replace(/^## \[Unreleased\][ \t]*$/m, () => `## [Unreleased]\n\n## [${version}] — ${date}`);
  }
  const notes = `docs/releases/${tag}.md`;
  const statusPattern = /^\*\*(Released|Status):\*\*[^\n]*$/gm;
  const statuses = [...proposed[notes].matchAll(statusPattern)];
  if (statuses.length !== 1) throw new Error('Notes must have exactly one Released or Status field.');
  protectDate(statuses[0][0]);
  if (!/^\*\*(?:Released|Status):\*\*\s*(?:\d{4}-\d{2}-\d{2}|draft|unreleased|planned|YYYY-MM-DD)\s*$/i.test(statuses[0][0])) throw new Error('Unrecognized notes status; finalize it explicitly.');
  proposed[notes] = proposed[notes].replace(statusPattern, () => `**Released:** ${date}`);
  const index = 'docs/releases/README.md';
  const rows = proposed[index].split('\n').filter(line => line.includes(`](${tag}.md)`));
  if (rows.length > 1) throw new Error('Duplicate release-index entries.');
  const row = `| [ETL-SQL ${tag}](${tag}.md) | **Released:** ${date} |`;
  if (rows.length) {
    protectDate(rows[0]);
    // Preserve an authored description. Replace only a draft/date status or label.
    let updated = rows[0].replace(/\[[^\]]+\]/, `[ETL-SQL ${tag}]`);
    updated = updated.replace(/\*\*(?:Released|Status):\*\*[^|]*/g, () => `**Released:** ${date} `);
    proposed[index] = proposed[index].replace(rows[0], () => updated);
  } else {
    const separator = /^\|\s*:?-+:?\s*\|\s*:?-+:?\s*\|\s*$/m;
    if (!separator.test(proposed[index])) throw new Error('Release index is missing its two-column table.');
    proposed[index] = proposed[index].replace(separator, match => `${match}\n${row}`);
  }
  const errors = metadataFindings(tag, name => proposed[name]);
  if (errors.length) throw new Error(errors.join('\n'));
  return files.filter(name => name !== 'Directory.Build.props').map(name => ({ name, before: original[name],
    after: original[name].includes('\r\n') ? proposed[name].replace(/\n/g, '\r\n') : proposed[name] }))
    .filter(change => change.before !== change.after);
}

export function applyMetadataPlan(changes, read, write) {
  // Reject edits made since planning before touching any file.
  for (const change of changes) if (read(change.name) !== change.before) throw new Error(`Metadata changed during preparation: ${change.name}`);
  const attempted = [];
  try {
    for (const change of changes) { attempted.push(change); write(change.name, change.after); }
  } catch (error) {
    const failures = [];
    for (const change of attempted.reverse()) {
      try { write(change.name, change.before); } catch { failures.push(change.name); }
    }
    throw new Error(`Metadata write failed: ${error.message}.${failures.length ? ` Restore these files from your diff: ${failures.join(', ')}` : ' Original contents restored.'}`);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [, , tag, date, mode] = process.argv;
    if (process.argv.length < 4 || process.argv.length > 5 || (mode && mode !== '--write')) throw new Error('Usage: node scripts/finalize-release-metadata.mjs vx.y.z YYYY-MM-DD [--write]');
    const read = name => readFileSync(path.join(root, name), 'utf8');
    const changes = planReleaseMetadata(tag, date, read);
    if (mode === '--write') applyMetadataPlan(changes, read, (name, text) => writeFileSync(path.join(root, name), text));
    for (const change of changes) console.log(`${mode === '--write' ? 'Updated' : 'Would update'} ${change.name}`);
    console.log(changes.length ? `${tag}: ${date}. ${mode === '--write' ? 'Review and commit metadata before freezing the candidate.' : 'Validated preview; pass --write to apply.'}` : 'Metadata already finalized; no changes.');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
