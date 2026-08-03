import assert from 'node:assert/strict';
import { readdir, readFile } from 'node:fs/promises';
import path from 'node:path';

const roots = [
  'src/ETL-SQL.ReportRuntime/Resources/Shared',
  'src/ETL-SQL.Portal/wwwroot',
  'tools/ui-sandbox',
];
const embeddedHostSources = [
  'src/ETL-SQL.ReportPlayer/Program.cs',
  'src/ETL-SQL.WorkstationEditor/EditorShell.cs',
  'src/ETL-SQL.WorkstationEditor/WorkstationEditorApp.cs',
  'src/etl-sql-vscode/src/reportDesignerPanel.ts',
  'src/etl-sql-vscode/src/reportPreviewPanel.ts',
  'src/etl-sql-vscode/src/visualFlowPanel.ts',
];
const extensions = new Set(['.js', '.html']);
const nativeDialog = /(^|[^.A-Za-z0-9_$])(alert|prompt|confirm)\s*\(/gm;

async function filesUnder(root) {
  const entries = await readdir(root, { withFileTypes: true });
  const files = [];
  for (const entry of entries) {
    const full = path.join(root, entry.name);
    if (entry.isDirectory()) files.push(...await filesUnder(full));
    else if (extensions.has(path.extname(entry.name)) && !entry.name.endsWith('.min.js')) files.push(full);
  }
  return files;
}

const violations = [];
for (const root of roots) {
  for (const file of await filesUnder(root)) {
    const source = await readFile(file, 'utf8');
    for (const match of source.matchAll(nativeDialog)) {
      const line = source.slice(0, match.index).split('\n').length;
      violations.push(`${file}:${line} uses native ${match[2]}()`);
    }
    if (/window\.(?:alert|prompt|confirm)\b/.test(source)) violations.push(`${file} references a native window dialog`);
  }
}
for (const file of embeddedHostSources) {
  const source = await readFile(file, 'utf8');
  for (const match of source.matchAll(nativeDialog)) {
    const line = source.slice(0, match.index).split('\n').length;
    violations.push(`${file}:${line} uses native ${match[2]}()`);
  }
  if (/window\.(?:alert|prompt|confirm)\b/.test(source)) violations.push(`${file} references a native window dialog`);
}
assert.deepEqual(violations, [], violations.join('\n'));

const feedback = await readFile('src/ETL-SQL.ReportRuntime/Resources/Shared/feedback.js', 'utf8');
assert.match(feedback, /aria-modal/);
assert.match(feedback, /aria-live/);
assert.match(feedback, /event\.key === 'Escape'/);
assert.match(feedback, /event\.key !== 'Tab'/);
assert.match(feedback, /requiredMessage/);
assert.match(feedback, /auditAction/);
assert.match(feedback, /textContent = String\(message/);

const hosts = {
  'Portal Admin': ['src/ETL-SQL.Portal/wwwroot/admin.html', '/js/feedback.js'],
  'Portal Reports': ['src/ETL-SQL.Portal/wwwroot/index.html', '/js/feedback.js'],
  'Portal Designer': ['src/ETL-SQL.Portal/wwwroot/designer.html', '/js/feedback.js'],
  'Portal Orchestrator': ['src/ETL-SQL.Portal/wwwroot/orchestrator.html', '/js/feedback.js'],
  'ReportPlayer': ['src/ETL-SQL.ReportPlayer/Program.cs', '/feedback.js'],
  'Workstation editor': ['src/ETL-SQL.WorkstationEditor/EditorShell.cs', '/feedback.js'],
  'Workstation preview': ['src/ETL-SQL.WorkstationEditor/WorkstationEditorApp.cs', '/runtime/feedback.js'],
  'VS Code report preview': ['src/etl-sql-vscode/src/reportPreviewPanel.ts', 'feedback.js'],
  'VS Code designer': ['src/etl-sql-vscode/src/reportDesignerPanel.ts', 'feedback.js'],
  'VS Code visual flow': ['src/etl-sql-vscode/src/visualFlowPanel.ts', 'feedback.js'],
};
for (const [label, [file, marker]] of Object.entries(hosts)) {
  assert.ok((await readFile(file, 'utf8')).includes(marker), `${label} does not load shared feedback.js`);
}

console.log(`Shared feedback contract passed across ${Object.keys(hosts).length} hosts.`);
