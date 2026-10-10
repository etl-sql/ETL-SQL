import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { readPortalPage, readPortalPageModule } from './lib/portal-page.mjs';

const portal = readPortalPage('orchestrator');
const story = await readFile(new URL('../tools/ui-sandbox/stories/orchestrator-run-overrides.story.js', import.meta.url), 'utf8');
const { parseJobRunOverrides } = await import('../src/ETL-SQL.Portal/wwwroot/js/orchestrator-job-actions.js');

assert.match(portal, /id="runJobModal"/);
assert.match(portal, /The saved job is not edited/);
assert.match(readPortalPageModule('orchestrator'), /createJobRunDialog\(/);
assert.match(readPortalPageModule('orchestrator'), /\/trigger`, \{ method: 'POST', body: \{ variables \}/);
assert.match(portal, /body: \{ variables \}/);
assert.match(portal, /Override names—not values—are written to the audit trail/);
assert.deepEqual({ ...parseJobRunOverrides([{ name: ' start_date ', value: '2026-10-01' },
    { name: '@region', value: ' North America ' }, { name: '', value: '' }]) },
{ '@start_date': '2026-10-01', '@region': ' North America ' });
for (const name of ['bad name', '1start', '@@start', 'start.date', ''])
    assert.throws(() => parseJobRunOverrides([{ name, value: 'entered value' }]), /not a variable name/);
assert.deepEqual({ ...parseJobRunOverrides([{ name: '@__proto__', value: 'literal' }]) }, { '@__proto__': 'literal' });
assert.match(story, /id: 'orchestrator-run-overrides'/);
assert.match(story, /@start_date/);

console.log('Orchestrator one-run override UI checks passed.');
