import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { readPortalPage } from './lib/portal-page.mjs';

const portal = readPortalPage('orchestrator');
const story = await readFile(
  new URL('../tools/ui-sandbox/stories/orchestrator-checkpoint-resume.story.js', import.meta.url),
  'utf8');

assert.match(portal, /\$\{BASE\}\/runs\/\$\{historyId\}\/resume/);
assert.match(portal, /Only failed or cancelled runs can resume/);
assert.match(portal, /was not persistent or never reached a top-level label/);
assert.match(portal, /history-resume-btn/);
assert.match(portal, /Resume from named checkpoint/);
assert.match(portal, /Work after that label may run again/);
assert.doesNotMatch(portal, /statement[- ]index/i);
assert.match(story, /id: 'orchestrator-checkpoint-resume'/);
assert.match(story, /Resume · load_complete/);
assert.match(story, /Only failed or cancelled runs can resume/);

console.log('Orchestrator named-checkpoint resume UI checks passed.');
