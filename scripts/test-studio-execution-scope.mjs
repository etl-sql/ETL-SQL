import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const studioSource = readFileSync(path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js'), 'utf8');
const workbenchSource = readFileSync(path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer/script-workbench.js'), 'utf8');
const cssSource = readFileSync(path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.css'), 'utf8');

// ── 1. Stop controls wired alongside Run ──────────────────────────────────────
assert.match(studioSource, /data-action="stop"/, 'Studio topbar must include stop button');
assert.match(studioSource, /data-action="code-stop"/, 'Studio code toolbar must include code-stop button');
assert.match(studioSource, /stop:\s*'<rect/, '_STUDIO_ICONS must define stop icon');
assert.match(studioSource, /updateRunControls\(\)/, 'Studio must coordinate Run and Stop visibility via updateRunControls');
assert.match(studioSource, /handleStopRun/, 'Studio must wire Stop button to handleStopRun');
assert.match(cssSource, /\.etlsql-studio-btn\.btn-danger/, 'designer.css must define .btn-danger styling for studio buttons');

// ── 2. Run correlation ID & cancellation distinction ─────────────────────────
assert.match(studioSource, /clientRunId\s*=\s*'run_'/, 'executeRun must generate client correlation ID');
assert.match(studioSource, /clientRunId,/, 'executeRun must pass clientRunId to /api/designer/run payload');
assert.match(studioSource, /Cancelled \(Unconfirmed\)/, 'executeRun must distinguish unconfirmed client abort from server confirmation');
assert.match(studioSource, /server-confirmed/, 'executeRun must report server-confirmed cancellation on 499 status');
assert.match(workbenchSource, /clientRunId\s*=\s*'run_'/, 'script-workbench must generate client correlation ID');
assert.match(workbenchSource, /Cancelled \(Unconfirmed\)/, 'script-workbench must mark client abort as unconfirmed');

// ── 3. Explicit run-selected scope (no silent fallback to whole script) ──────
// In studio.js, clicking run-selected must test selectedText then currentStatement, not fall back to script
assert.match(studioSource, /data-action="run-selected"[\s\S]*?selectedText[\s\S]*?currentStatement[\s\S]*?No query or statement selected to run/,
    'Studio run-selected must require explicit selection or statement at cursor, refusing to silently fall back to entire script');
assert.doesNotMatch(studioSource, /getCurrentStatement\?\.\(\)\s*\|\|\s*script/,
    'Studio run-selected must not contain silent fallback to full script');

assert.match(workbenchSource, /scope === 'selection'[\s\S]*?editor\.getSelection[\s\S]*?editor\.getCurrentStatement[\s\S]*?No query or statement selected to run/,
    'script-workbench run(selection) must require explicit selection or statement at cursor');
assert.doesNotMatch(workbenchSource, /getCurrentStatement\?\.\(\)\s*\|\|\s*script/,
    'script-workbench run(selection) must not contain silent fallback to full script');

// ── 4. Guard explainStatementAtCursor against mutating prefix statements ─────
assert.match(studioSource, /prefixEffects/, 'Studio must inspect prefixEffects from scope');
assert.match(studioSource, /data-allow-mutating-prefix/, 'Studio must provide checkbox to explicitly allow mutating prefix execution');
assert.match(studioSource, /Refused:\s*Mutating prefix statements/, 'explainStatementAtCursor must refuse execution if mutating prefix is present without explicit checkbox confirmation');

console.log('OK test-studio-execution-scope.mjs: explicit execution scope, stop controls, correlation ID, and mutating prefix guards verified.');
