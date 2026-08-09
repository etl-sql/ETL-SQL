import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const portal = await readFile(new URL('../src/ETL-SQL.Portal/wwwroot/orchestrator.html', import.meta.url), 'utf8');
const story = await readFile(new URL('../tools/ui-sandbox/stories/orchestrator-run-overrides.story.js', import.meta.url), 'utf8');

assert.match(portal, /id="runJobModal"/);
assert.match(portal, /The saved job is not edited/);
assert.match(portal, /api\.trigger\(runJobName, variables\)/);
assert.match(portal, /body: \{ variables \}/);
assert.match(portal, /Override names—not values—are written to the audit trail/);
assert.match(portal, /\^@\[A-Za-z_\]\[A-Za-z0-9_\]\*\$/);
assert.match(story, /id: 'orchestrator-run-overrides'/);
assert.match(story, /@start_date/);

console.log('Orchestrator one-run override UI checks passed.');
