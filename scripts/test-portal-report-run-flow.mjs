import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const scriptRoot = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptRoot, '..');
const source = fs.readFileSync(path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot', 'index.html'), 'utf8');

assert.match(source, /await reportsApi\.getParameters\(id\)/);
assert.match(source, /validateParamFields\('runParameterFields', params\)/);
assert.match(source, /runAndPoll\(id, report, validation\.values\)/);
assert.match(source, /reportsApi\.execute\(id, parameters\)/);
assert.doesNotMatch(source, /reportsApi\.refresh\(id\)/);
assert.match(source, /job\.status === 'Completed'/);
assert.match(source, /job\.status === 'Failed'/);
assert.match(source, /job\.status === 'Cancelled'/);
assert.match(source, /disabled title="Run the report before subscribing"/);
assert.match(source, /One run generates the first snapshot/);
assert.doesNotMatch(source, /<h2>Not run yet<\/h2>/);
console.log('Portal report preflight, single-run, terminal-status, and prerequisite flow passed.');
