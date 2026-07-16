Type: TEXT
Renders a free-form Markdown or plain-text block. Ideal for report headers, narrative commentary, research-paper-style paragraphs, disclaimers, and dynamic text driven by query results.

Use CONTENT to supply the markdown directly. DEFAULT is accepted as an alias for backward compatibility.
For dynamic text built from a query, use SOURCE with MAPPINGS (CONTENT = col). The first row's value is rendered.

Options:
- **MARKDOWN = ON|OFF** - render CONTENT as Markdown (default ON; set OFF for plain escaped text)
  ALIGN    = 'left'|'center'|'right'  (default 'left')

Markdown features supported: headers (#/##/###), **bold**, *italic*, `inline code`,
fenced code blocks (```), `[links](http://example.com)`, unordered lists (- item), ordered lists (1. item),
blockquotes (> text), tables (|col|col|), horizontal rules (---).

Static (most common):
```sql
CREATE VISUAL Summary AS TEXT (
  CONTENT = '## Q1 2026 Executive Summary

Revenue grew **12% YoY** driven by enterprise deals in the West region.

Key themes:
- Strong ARR expansion from existing customers
- New logo wins up 18% vs Q4
- EMEA pipeline rebuilt after restructure

> All figures are preliminary and subject to audit.'
);
```

Static with alignment:
```sql
CREATE VISUAL Disclaimer AS TEXT (
  CONTENT = '*Data as of last refresh. Figures are unaudited.*',
  OPTIONS (ALIGN = 'center', MARKDOWN = ON)
);
```

Plain HTML (MARKDOWN = OFF):
```sql
CREATE VISUAL RawHtml AS TEXT (
  CONTENT = '<p style="color:#888;font-size:0.85em">Data as of last refresh.</p>',
  OPTIONS (MARKDOWN = OFF)
);
```

Dynamic (narrative driven by a query):
```sql
SELECT 'Revenue grew ' + CAST(ROUND(growth_pct, 1) AS VARCHAR) + '% vs prior year.' AS content
INTO #narrative FROM #metrics;

CREATE VISUAL Narrative AS TEXT (
  SOURCE   = #narrative,
  MAPPINGS (CONTENT = content)
);
```

References:
- [Report SQL Guide](../../../guides/report-sql.md)
