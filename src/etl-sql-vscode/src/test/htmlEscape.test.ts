import { describe, it, expect } from 'vitest';
import { escapeHtml, formatNotebookTable } from '../htmlEscape';

describe('formatNotebookTable', () => {
    it('escapes cell values so query results cannot inject markup', () => {
        const html = formatNotebookTable(['col'], [{ col: '<img src=x onerror="alert(1)">' }]);

        expect(html).not.toContain('<img');
        expect(html).toContain('&lt;img src=x onerror=&quot;alert(1)&quot;&gt;');
    });

    it('escapes column names', () => {
        const html = formatNotebookTable(['<script>alert(1)</script>'], [{ '<script>alert(1)</script>': 1 }]);

        expect(html).not.toContain('<script>');
        expect(html).toContain('&lt;script&gt;alert(1)&lt;/script&gt;');
        expect(html).toContain('>1</td>');
    });

    it('renders null and missing values as empty cells', () => {
        const html = formatNotebookTable(['a', 'b'], [{ a: null }]);

        expect(html).toContain('<td style="border: 1px solid #444; padding: 8px;"></td><td style="border: 1px solid #444; padding: 8px;"></td>');
    });
});

describe('escapeHtml', () => {
    it('escapes every character that can end text or an attribute', () => {
        expect(escapeHtml(`&<>"'`)).toBe('&amp;&lt;&gt;&quot;&#39;');
    });
});
