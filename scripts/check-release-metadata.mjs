import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
export function metadataFindings(tag, read = name => readFileSync(path.join(root, name), 'utf8')) {
  if (!/^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$/.test(tag)) return ['Expected a stable release tag vMajor.Minor.Patch.'];
  const version = tag.slice(1);
  const escaped = version.replaceAll('.', '\\.');
  const findings = [];
  function content(name) {
    try { return read(name).replace(/\r\n/g, '\n'); }
    catch { findings.push(`Cannot read ${name}.`); return ''; }
  }
  function validDate(value) {
    if (!/^\d{4}-\d{2}-\d{2}$/.test(value ?? '')) return false;
    const date = new Date(`${value}T00:00:00Z`);
    return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value;
  }
  const versions = [...content('Directory.Build.props').matchAll(/<VersionPrefix>([^<]*)<\/VersionPrefix>/g)];
  if (versions.length !== 1 || versions[0][1] !== version) findings.push('Release tag must match the single VersionPrefix.');
  const changelog = content('CHANGELOG.md');
  const headings = [...changelog.matchAll(new RegExp(`^## \\[${escaped}\\][^\\n]*$`, 'gm'))];
  const changelogDate = headings.length === 1 ? headings[0][0].match(/ — (\d{4}-\d{2}-\d{2})\s*$/)?.[1] : undefined;
  if (!validDate(changelogDate)) findings.push(`CHANGELOG.md needs one dated ## [${version}] — YYYY-MM-DD heading.`);
  const notes = content(`docs/releases/${tag}.md`);
  if (!new RegExp(`^# ETL-SQL v${escaped}\\s*$`, 'm').test(notes)) findings.push('Release notes need the exact version title.');
  const dates = [...notes.matchAll(/^\*\*Released:\*\*\s*([^\n]+)$/gm)];
  const notesDate = dates.length === 1 ? dates[0][1].trim() : undefined;
  if (!validDate(notesDate)) findings.push('Release notes need one finalized **Released:** YYYY-MM-DD date.');
  if (validDate(notesDate) && validDate(changelogDate) && notesDate !== changelogDate) findings.push('Release notes and changelog dates differ.');
  if (/^\*\*Status:\*\*\s*(draft|unreleased|planned)\b/im.test(notes)) findings.push('Release notes are still marked draft, unreleased or planned.');
  const indexRows = content('docs/releases/README.md').split('\n').filter(line => line.includes(`](${tag}.md)`));
  if (indexRows.length !== 1) findings.push('Release index needs exactly one link to the version notes.');
  else {
    if (/\[[^\]]*\b(draft|unreleased|planned)\b/i.test(indexRows[0]) || /\*\*Status:\*\*\s*(draft|unreleased|planned)\b/i.test(indexRows[0])) findings.push('Release index still marks the version as unfinished.');
    const indexDate = indexRows[0].match(/\*\*Released:\*\*\s*(\d{4}-\d{2}-\d{2})/)?.[1];
    if (indexRows[0].includes('**Released:**') && (!validDate(indexDate) || indexDate !== notesDate)) findings.push('Release index date differs from the release notes or is unfinished.');
  }
  return findings;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const findings = metadataFindings(process.argv.length === 3 ? process.argv[2] : '');
  for (const finding of findings) console.error(`FAIL ${finding}`);
  if (!findings.length) console.log('Final release metadata agrees: version, changelog date, notes date and index.');
  process.exitCode = findings.length ? 1 : 0;
}
