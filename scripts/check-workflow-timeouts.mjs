import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Reusable calls inherit their constituent jobs' limits; every runner job needs its own bound.
export function workflowTimeoutFindings(source, label = 'workflow') {
  const lines = source.replaceAll('\r\n', '\n').split('\n');
  const start = lines.findIndex(line => /^jobs:[ \t]*(?:#.*)?$/.test(line));
  if (start < 0) return [`${label}: missing or unsupported jobs declaration`];
  let end = start + 1;
  while (end < lines.length && !/^[^\s#]/.test(lines[end])) end++;
  const body = lines.slice(start + 1, end);
  const headers = [], findings = [];
  for (let index = 0; index < body.length; index++) {
    const line = body[index];
    if (!line.trim() || /^\s*#/.test(line)) continue;
    const header = /^  ([A-Za-z_][A-Za-z0-9_-]*):[ \t]*(?:#.*)?$/.exec(line);
    if (header) headers.push({ id: header[1], index });
    else if (!/^ {4}\S|^ {5}/.test(line)) findings.push(`${label}: unsupported job layout at line ${start + index + 2}`);
  }
  if (!headers.length) findings.push(`${label}: no inspectable jobs`);
  for (let index = 0; index < headers.length; index++) {
    const header = headers[index];
    const job = body.slice(header.index + 1, headers[index + 1]?.index ?? body.length).join('\n');
    if (!/^    runs-on:/m.test(job)) {
      if (!/^    uses:/m.test(job)) findings.push(`${label}/${header.id}: missing direct runner or reusable workflow`);
      continue;
    }
    const limits = [...job.matchAll(/^    timeout-minutes:[ \t]*([^#\n]*?)[ \t]*(?:#.*)?$/gm)];
    if (limits.length !== 1 || !/^[1-9]\d*$/.test(limits[0][1]) || Number(limits[0][1]) > 180) {
      findings.push(`${label}/${header.id}: set one literal timeout-minutes between 1 and 180`);
    }
    const jobMinutes = limits.length === 1 ? Number(limits[0][1]) : NaN;
    const jobLines = job.split('\n');
    const stepsStart = jobLines.findIndex(line => /^    steps:[ \t]*(?:#.*)?$/.test(line));
    if (stepsStart < 0) { findings.push(`${label}/${header.id}: missing or unsupported steps declaration`); continue; }
    let stepsEnd = stepsStart + 1;
    while (stepsEnd < jobLines.length && !/^    [^\s#-]/.test(jobLines[stepsEnd])) stepsEnd++;
    const stepLines = jobLines.slice(stepsStart + 1, stepsEnd), stepHeaders = [];
    for (let line = 0; line < stepLines.length; line++) {
      const match = /^( {4}| {6})- [\w-]+:/.exec(stepLines[line]);
      if (match) stepHeaders.push({ line, indent: match[1].length });
      else if (/^ {4}(?:  )?-\s/.test(stepLines[line])) findings.push(`${label}/${header.id}: unsupported step layout`);
    }
    if (!stepHeaders.length) findings.push(`${label}/${header.id}: no inspectable steps`);
    for (let stepIndex = 0; stepIndex < stepHeaders.length; stepIndex++) {
      const selected = stepHeaders[stepIndex];
      const step = stepLines.slice(selected.line, stepHeaders[stepIndex + 1]?.line ?? stepLines.length).join('\n');
      const property = ' '.repeat(selected.indent + 2);
      const first = ' '.repeat(selected.indent) + '- ';
      const command = new RegExp(`^(?:${first}|${property})run:`, 'm').test(step);
      const checkout = new RegExp(`^(?:${first}|${property})uses: actions/checkout@`, 'm').test(step);
      const stepLimits = [...step.matchAll(new RegExp(`^(?:${first}|${property})timeout-minutes:[ \\t]*([^#\\n]*?)[ \\t]*(?:#.*)?$`, 'gm'))];
      if (!command && !checkout && !stepLimits.length) continue;
      if (stepLimits.length !== 1 || !/^[1-9]\d*$/.test(stepLimits[0][1]) ||
          Number(stepLimits[0][1]) > 180 || Number.isFinite(jobMinutes) && Number(stepLimits[0][1]) > jobMinutes) {
        findings.push(`${label}/${header.id}/step-${stepIndex + 1}: set one literal step deadline within the job limit`);
      }
    }
  }
  return findings;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  if (process.argv.length !== 2) throw new Error('This check always audits every repository workflow.');
  const root = fileURLToPath(new URL('../.github/workflows/', import.meta.url));
  const names = readdirSync(root).filter(name => /\.ya?ml$/.test(name)).sort();
  const findings = names.flatMap(name => workflowTimeoutFindings(readFileSync(path.join(root, name), 'utf8'), name));
  if (!names.length) findings.push('No workflows found.');
  for (const finding of findings) console.error(finding);
  console.log(`Workflow job/command/checkout deadlines: ${names.length} files, ${findings.length} findings.`);
  process.exitCode = findings.length ? 1 : 0;
}
