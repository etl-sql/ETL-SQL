import assert from 'node:assert/strict';
import {
    completionKind,
    diagnosticSeverity,
    markdownToTooltipHtml,
    createScriptEditor,
} from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/script-editor.js';

// ── 1. completionKind ────────────────────────────────────────────────────────
assert.equal(completionKind('keyword'), 'keyword');
assert.equal(completionKind('KEYWORD'), 'keyword');
assert.equal(completionKind('function'), 'function');
assert.equal(completionKind('table'), 'class');
assert.equal(completionKind('column'), 'property');
assert.equal(completionKind('variable'), 'variable');
assert.equal(completionKind('alias'), 'variable');
assert.equal(completionKind('connection'), 'namespace');
assert.equal(completionKind('connector'), 'namespace');
assert.equal(completionKind('path'), 'file');
assert.equal(completionKind('optionname'), 'property');
assert.equal(completionKind('optionvalue'), 'constant');
assert.equal(completionKind('snippet'), 'text');
assert.equal(completionKind('unknown'), 'text');
assert.equal(completionKind(''), 'text');
assert.equal(completionKind(null), 'text');
assert.equal(completionKind(undefined), 'text');

// ── 2. diagnosticSeverity ───────────────────────────────────────────────────
assert.equal(diagnosticSeverity(undefined), 'warning');
assert.equal(diagnosticSeverity({}), 'warning');
assert.equal(diagnosticSeverity({ severity: 'error' }), 'error');
assert.equal(diagnosticSeverity({ severity: 'Error' }), 'error');
assert.equal(diagnosticSeverity({ severity: 'SYNTAX_ERROR' }), 'error');
assert.equal(diagnosticSeverity({ severity: 0 }), 'error');
assert.equal(diagnosticSeverity({ severity: 'info' }), 'info');
assert.equal(diagnosticSeverity({ severity: 'Information' }), 'info');
assert.equal(diagnosticSeverity({ severity: 'hint' }), 'info');
assert.equal(diagnosticSeverity({ severity: 'warning' }), 'warning');
assert.equal(diagnosticSeverity({ severity: 'WARN' }), 'warning');
assert.equal(diagnosticSeverity({ severity: 1 }), 'warning');

// ── 3. markdownToTooltipHtml ────────────────────────────────────────────────
// Basic lines and empty line gap
const gapHtml = markdownToTooltipHtml('\n');
assert.ok(gapHtml.includes('etlsql-editor-hover-gap'));

// Headings
const h1Html = markdownToTooltipHtml('# Heading One');
assert.equal(h1Html, '<div class="etlsql-editor-hover-heading etlsql-editor-hover-heading-1">Heading One</div>');

const h3Html = markdownToTooltipHtml('### Sub Section');
assert.equal(h3Html, '<div class="etlsql-editor-hover-heading etlsql-editor-hover-heading-3">Sub Section</div>');

// Heading level 6
const h6Html = markdownToTooltipHtml('###### Deep Level');
assert.equal(h6Html, '<div class="etlsql-editor-hover-heading etlsql-editor-hover-heading-6">Deep Level</div>');

// Bullets
const bulletDash = markdownToTooltipHtml('- List item 1');
assert.equal(bulletDash, '<div class="etlsql-editor-hover-bullet">List item 1</div>');

const bulletStar = markdownToTooltipHtml('* List item 2');
assert.equal(bulletStar, '<div class="etlsql-editor-hover-bullet">List item 2</div>');

// Inline formatting: code and bold
const inlineFormatted = markdownToTooltipHtml('Use `SELECT` with **BOLD** text');
assert.equal(inlineFormatted, '<div class="etlsql-editor-hover-line">Use <code>SELECT</code> with <strong>BOLD</strong> text</div>');

// Code block
const codeBlock = markdownToTooltipHtml('```sql\nSELECT * FROM #temp;\n```');
assert.equal(codeBlock, '<pre><code>SELECT * FROM #temp;</code></pre>');

// Unclosed code block flushes at end
const unclosedCode = markdownToTooltipHtml('```\nlet a = 1;');
assert.equal(unclosedCode, '<pre><code>let a = 1;</code></pre>');

// HTML escaping inside markdown
const escapedText = markdownToTooltipHtml('<script>alert("xss")</script>');
assert.ok(escapedText.includes('&lt;script&gt;'));
assert.ok(!escapedText.includes('<script>'));

// Mixed markdown document
const doc = [
    '# Function: COUNT',
    '',
    'Returns the count of items in a group.',
    '- Parameter: `expression`',
    '',
    '```sql',
    'SELECT COUNT(*) FROM users;',
    '```',
].join('\n');
const renderedDoc = markdownToTooltipHtml(doc);
assert.ok(renderedDoc.includes('etlsql-editor-hover-heading-1'));
assert.ok(renderedDoc.includes('etlsql-editor-hover-bullet'));
assert.ok(renderedDoc.includes('<code>expression</code>'));
assert.ok(renderedDoc.includes('<pre><code>SELECT COUNT(*) FROM users;</code></pre>'));

// ── 4. createScriptEditor export & signature ────────────────────────────────
assert.equal(typeof createScriptEditor, 'function');

console.log('OK test-script-editor.mjs: all checks passed.');
