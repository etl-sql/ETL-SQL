import { readFileSync as readSplitSource } from 'node:fs';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';

const studioContextSource = readSplitSource('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-context.js', 'utf8');
const studioReportWorkflowSource = readSplitSource('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-report-workflow.js', 'utf8');
const studioSyntaxBridgeSource = readSplitSource('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio-syntax-bridge.js', 'utf8');


const root = new URL('../', import.meta.url);
const read = path => readFile(new URL(path, root), 'utf8');

const [studioJs, scriptEditorJs, feedbackJs, designerCss] = await Promise.all([
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/studio.js'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/script-editor.js'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/feedback.js'),
  read('src/ETL-SQL.ReportRuntime/Resources/Shared/designer/designer.css'),
]);

// ── 1. Learned Preference for Code View ───────────────────────────────────────
assert.match(
  studioContextSource,
  /etlsql-studio-projection-preference/,
  'Studio must define a localStorage key for learned projection preference.'
);
assert.match(
  studioContextSource,
  /function getStoredProjectionPreference\(\)/,
  'Studio must provide getStoredProjectionPreference to check learned projection preference.'
);
assert.match(
  studioContextSource,
  /function storeProjectionPreference\(/,
  'Studio must provide storeProjectionPreference to record user projection choice.'
);

// Verify createNewFile and openCatalogReport respect projection preference
assert.match(
  studioJs,
  /getStoredProjectionPreference\(\)/,
  'createNewFile or openCatalogReport must query getStoredProjectionPreference.'
);

// ── 2. Dashboard Workflow Syntax Instruction ──────────────────────────────────
assert.match(
  studioReportWorkflowSource,
  /class="etlsql-workflow-syntax-tag"[^>]*>CREATE CONNECTION<\/code>/,
  'Dashboard workflow step 1 must introduce CREATE CONNECTION syntax.'
);
assert.match(
  studioReportWorkflowSource,
  /class="etlsql-workflow-syntax-tag"[^>]*>CREATE VISUAL<\/code>/,
  'Dashboard workflow step 2 must introduce CREATE VISUAL syntax.'
);
assert.match(
  studioReportWorkflowSource,
  /class="etlsql-workflow-syntax-tag"[^>]*>FILTER \/ SLICER<\/code>/,
  'Dashboard workflow step 3 must introduce FILTER / SLICER syntax.'
);
assert.match(
  studioReportWorkflowSource,
  /class="etlsql-workflow-syntax-tag"[^>]*>LAYOUT \(\.\.\.\)<\/code>/,
  'Dashboard workflow step 4 must introduce LAYOUT (...) syntax.'
);
assert.match(
  studioReportWorkflowSource,
  /class="etlsql-workflow-syntax-tag"[^>]*>OPTIONS \/ MAPPINGS<\/code>/,
  'Dashboard workflow step 5 must introduce OPTIONS / MAPPINGS syntax.'
);

// ── 3. Exact Changed-Range Selection in script-editor ────────────────────────
assert.match(
  scriptEditorJs,
  /revealRange:\s*\(from,\s*to,\s*select\s*=\s*false\)/,
  'Script editor revealRange must support an optional select parameter.'
);
assert.match(
  scriptEditorJs,
  /selection\s*[:=]\s*\{\s*anchor:\s*start,\s*head:\s*end\s*\}/,
  'Script editor revealRange must dispatch selection across [from, to] when select is true.'
);

// ── 4. Feedback Toast Multiple Actions ───────────────────────────────────────
assert.match(
  feedbackJs,
  /rawActions\s*=\s*Array\.isArray\(options\.actions\)/,
  'Feedback notify must accept options.actions array.'
);
assert.match(
  feedbackJs,
  /etlsql-feedback-actions-inline/,
  'Feedback notify must render actions container with etlsql-feedback-actions-inline class.'
);
assert.match(
  feedbackJs,
  /etlsql-feedback-action-primary/,
  'Feedback notify must support primary action styling.'
);

// ── 5. offerUndo "Show What Changed" & Syntax Bridge ─────────────────────────
assert.match(
  studioSyntaxBridgeSource,
  /label:\s*['"]Show what changed['"]/,
  'offerUndo toast must include a "Show what changed" action alongside Undo.'
);
assert.match(
  studioContextSource,
  /function analyzeSyntaxDiff\(/,
  'Studio must analyze syntax diff from visual mutation before and after scripts.'
);
assert.match(
  studioSyntaxBridgeSource,
  /function openSyntaxBridge\(/,
  'Studio must implement openSyntaxBridge to switch to split view, highlight range, and display helper.'
);
assert.match(
  studioJs,
  /data-action="syntax-bridge"/,
  'Studio toolbar must expose a syntax-bridge helper button to reopen helper without losing context.'
);
assert.match(
  studioJs,
  /data-syntax-bridge/,
  'Studio code stage must include a syntax bridge panel container.'
);

// ── 6. CSS Styling for Syntax Bridge ─────────────────────────────────────────
assert.match(
  designerCss,
  /\.etlsql-workflow-syntax-tag/,
  'designer.css must style .etlsql-workflow-syntax-tag.'
);
assert.match(
  designerCss,
  /\.etlsql-studio-syntax-bridge/,
  'designer.css must style .etlsql-studio-syntax-bridge.'
);

console.log('test-visual-script-bridge: all consumer contract assertions passed.');
