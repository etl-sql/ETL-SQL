import assert from 'node:assert/strict';
import { _isSafeHtmlPreviewUrl, _validateHtmlPreviewCss } from '../src/ETL-SQL.ReportRuntime/Resources/Shared/designer/html-preview.js';

for (const url of ['https://example.test/image.png', 'HTTP://example.test', 'mailto:fixture@example.test',
    'tel:123', '#section', 'data:image/png;base64,AA==']) {
    assert.equal(_isSafeHtmlPreviewUrl(url), true);
}
for (const url of [null, undefined, '', '/relative', '//example.test', 'javascript:alert(1)',
    'java\nscript:alert(1)', 'https://example.test/\u0000', 'data:text/html,<p>fixture</p>']) {
    assert.equal(_isSafeHtmlPreviewUrl(url), false);
}
const svgUrl = svg => `data:image/svg+xml,${encodeURIComponent(svg)}`;
assert.equal(_isSafeHtmlPreviewUrl(svgUrl('<svg><rect width="2"/></svg>')), true);
assert.equal(_isSafeHtmlPreviewUrl('data:image/svg+xml;base64,' + btoa('<svg/>')), true);
for (const svg of ['<svg><script>alert(1)</script></svg>', '<svg onload="alert(1)"/>',
    '<svg><foreignObject/></svg>', '<svg><a href="javascript:alert(1)"/></svg>']) {
    assert.equal(_isSafeHtmlPreviewUrl(svgUrl(svg)), false);
    assert.equal(_isSafeHtmlPreviewUrl('data:image/svg+xml;base64,' + btoa(svg)), false);
}
assert.equal(_isSafeHtmlPreviewUrl('data:image/svg+xml,%invalid'), false);
assert.equal(_isSafeHtmlPreviewUrl('data:image/svg+xml;base64,%%%'), false);

for (const css of [null, '', '.card { color: var(--etl-text); }',
    '@media (max-width: 600px) { .card { display: block; } }',
    '@keyframes pulse { from { opacity: 0; } to { opacity: 1; } }',
    '/* @import "ignored.css" */ .card { color: red; }']) {
    assert.equal(_validateHtmlPreviewCss(css), null);
}
for (const css of ['@import "remote.css";', '@font-face { font-family: fixture; }',
    '.card { color: expression(alert(1)); }', '.card { background: url(https://example.test/a); }',
    '.card { background: url(data:text/html,fixture); }', '.card { color: var(--host-secret); }',
    '.card { color: \\72 ed; }', '@supports (display: grid) { .card { display: grid; } }']) {
    assert.equal(typeof _validateHtmlPreviewCss(css), 'string');
}
console.log('HTML preview: URL schemes, SVG payloads, malformed data, and scoped CSS checks passed.');
