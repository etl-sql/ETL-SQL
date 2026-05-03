Type: TEXT
Renders a free-form HTML or Markdown block. Useful for report headers, commentary, disclaimers, or dynamic narrative driven by query results.

No SOURCE or MAPPINGS needed for static text. Use DEFAULT to supply the content directly.
For dynamic text built from data, use SOURCE with a MAPPINGS (CONTENT = col) mapping.

Options:
  MARKDOWN = ON|OFF   — render DEFAULT/CONTENT as Markdown (default OFF; raw HTML allowed)
  ALIGN    = 'left'|'center'|'right'  (default 'left')
  STYLE    — inline CSS string applied to the container div

Static:
```sql
CREATE VISUAL Disclaimer AS TEXT (
  DEFAULT = '<p style="color:#888;font-size:0.85em">Data as of last refresh. Figures are unaudited.</p>'
);

CREATE VISUAL Header AS TEXT (
  DEFAULT = '## Q1 2026 Executive Dashboard\nAll figures in USD thousands.',
  OPTIONS (MARKDOWN = ON)
);
```

Dynamic (narrative driven by a query):
```sql
SELECT 'Revenue grew ' + CAST(ROUND(growth_pct, 1) AS VARCHAR) + '% vs prior year.' AS summary
INTO #narrative FROM #metrics;

CREATE VISUAL Narrative AS TEXT (
  SOURCE   = #narrative,
  MAPPINGS (CONTENT = summary),
  OPTIONS  (MARKDOWN = OFF)
);
```
