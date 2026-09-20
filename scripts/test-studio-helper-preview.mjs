import { readFileSync as readSplitSource } from 'node:fs';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const studioAuthoringPipelineSource = readSplitSource('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring-pipeline.js', 'utf8');
const studioAuthoringDataSource = readSplitSource('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring-data.js', 'utf8');


const repo = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const queryWorkbenchSource = readFileSync(path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-query-workbench.js'), 'utf8');
const authoringSource = readFileSync(path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring.js'), 'utf8');
const uiSource = readFileSync(path.join(repo, 'src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-authoring-ui.js'), 'utf8');

// ── 1. Execution context fidelity: composeWorkbenchScript ─────────────────────
assert.match(queryWorkbenchSource, /export function composeWorkbenchScript/, 'studio-query-workbench must export composeWorkbenchScript');
assert.match(queryWorkbenchSource, /options\.context === 'remote' && options\.connection/, 'composeWorkbenchScript must check for remote context and connection');
assert.match(queryWorkbenchSource, /EXECUTE \$\{options\.connection\}[\s\S]*?BEGIN[\s\S]*?END;/, 'composeWorkbenchScript must wrap remote queries in EXECUTE <conn> BEGIN … END');

// ── 2. firstResultSet: accept flat empty rows ─────────────────────────────────
assert.match(queryWorkbenchSource, /export function firstResultSet/, 'studio-query-workbench must export firstResultSet');
assert.match(queryWorkbenchSource, /if \(Array\.isArray\(result\?\.rows\)\) \{/, 'firstResultSet must accept Array.isArray(result?.rows) without requiring non-zero length');
assert.doesNotMatch(queryWorkbenchSource, /Array\.isArray\(result\?\.rows\)\s*&&\s*result\.rows\.length/, 'firstResultSet must not exclude empty rows with result.rows.length check');

// ── 3. Educational empty result handling in createQueryWorkbench ──────────────
assert.doesNotMatch(queryWorkbenchSource, /if \(!sample\) throw new Error\('The query ran but returned no result set\.'\)/,
    'createQueryWorkbench must not throw an error when a query returns no result set');
assert.match(queryWorkbenchSource, /The query ran successfully but returned no result set\./,
    'createQueryWorkbench must display an informative note when a query succeeds with no result set');

// ── 4. Clarify engine vs remote context in Studio authoring surfaces ──────────
// Execution task editor passes context: 'remote' and clarifies remote execution
assert.match(studioAuthoringPipelineSource, /context:\s*'remote'/,
    'studio-authoring openPipelineTaskEditor must configure execution workbench with context: remote');
assert.match(studioAuthoringPipelineSource, /remote context[\s\S]*?EXECUTE[\s\S]*?BEGIN … END[\s\S]*?Preceding script variables and #temp staging tables are not executed/,
    'studio-authoring openPipelineTaskEditor must display note explaining remote context and bounded execution without predecessors');

// Dataset wizard passes context: 'engine'
assert.match(studioAuthoringDataSource, /context:\s*'engine'/,
    'studio-authoring mountQueryWorkbench must configure dataset wizard query workbench with context: engine');

// ── 5. studio-authoring-ui sampleGridMarkup teaches zero rows ─────────────────
assert.match(uiSource, /The query ran successfully but returned 0 rows\./,
    'studio-authoring-ui sampleGridMarkup must distinguish valid zero-row result from missing columns warning');

console.log('OK test-studio-helper-preview.mjs: helper preview context fidelity, firstResultSet zero-row handling, and engine vs remote context verified.');
