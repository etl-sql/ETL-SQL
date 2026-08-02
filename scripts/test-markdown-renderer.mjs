import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { pathToFileURL } from 'node:url';
import path from 'node:path';

const { renderMarkdown } = await import(pathToFileURL(path.resolve('src/ETL-SQL.Portal/wwwroot/js/markdown-renderer.js')).href);
const html = renderMarkdown(`# Topic

> [!WARNING]
> Be careful.

| Name | Value |
| --- | --- |
| A | **bold** |

\`\`\`sql
SELECT '<script>alert(1)</script>';
\`\`\`

[safe](https://example.test) [unsafe](javascript:alert(1))`);

assert.match(html, /class="md-admonition md-warning"/);
assert.match(html, /class="md-table"/);
assert.match(html, /data-md-copy=/);
assert.match(html, /&lt;script&gt;alert\(1\)&lt;\/script&gt;/);
assert.doesNotMatch(html, /<script>/);
assert.doesNotMatch(html, /href="javascript:/);
assert.match(html, /href="#"/);

const root = new URL('../', import.meta.url);
const [docs, connections] = await Promise.all([
  readFile(new URL('src/ETL-SQL.Portal/wwwroot/js/docs.js', root), 'utf8'),
  readFile(new URL('src/ETL-SQL.Portal/wwwroot/js/connections-admin.js', root), 'utf8'),
]);
assert.match(docs, /from '\/js\/markdown-renderer\.js/);
assert.match(connections, /from '\.\/markdown-renderer\.js'/);
assert.doesNotMatch(connections, /function renderMarkdown/);

console.log('Shared sanitized Markdown renderer contract passed.');
