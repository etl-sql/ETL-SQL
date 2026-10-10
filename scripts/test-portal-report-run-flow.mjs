import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { readPortalPage, readPortalPageModule } from './lib/portal-page.mjs';

const scriptRoot = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptRoot, '..');
// The run flow lives in the report viewer owner and its view module; the page only mounts them.
// ReportViewerSandboxTests drives the behaviour; this keeps the single-run contract and wording in step.
const source = readPortalPage('index')
  + fs.readFileSync(path.join(repoRoot, 'src/ETL-SQL.Portal/wwwroot/js/report-viewer.js'), 'utf8')
  + fs.readFileSync(path.join(repoRoot, 'src/ETL-SQL.Portal/wwwroot/js/report-viewer-view.js'), 'utf8');

assert.match(source, /\/api\/reports\/\$\{current\.id\}\/parameters/);
assert.match(source, /readParameterDraft\(runInputs\)/);
assert.match(source, /reportsApi\.execute\(current\.id, parameters\)/);
assert.doesNotMatch(source, /reportsApi\.refresh\(/);
assert.match(source, /value\.status === 'Completed'/);
assert.match(source, /value\.status === 'Failed'/);
assert.match(source, /value\.status === 'Cancelled'/);
assert.match(source, /disabled title="Run the report before subscribing"/);
assert.match(source, /One run generates the first snapshot/);
assert.doesNotMatch(source, /<h2>Not run yet<\/h2>/);
console.log('Portal report preflight, single-run, terminal-status, and prerequisite flow passed.');
