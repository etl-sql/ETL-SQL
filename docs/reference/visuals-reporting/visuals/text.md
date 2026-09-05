# TEXT

Renders Markdown, plain text, or template-interpolated commentary with formatting and typography controls.

## Syntax

```sql
CREATE VISUAL VisualName AS TEXT (
  [SOURCE = #tableName,]
  [MAPPINGS (
    [CONTENT = <column_name>]
  ),]
  [CONTENT = '<template_text_or_markdown>',]
  [OPTIONS (
    [MARKDOWN = ON|OFF,]
    [ALIGN = 'left'|'center'|'right',]
    [MAX_LINES = <int_lines>,]
    [OVERFLOW = CLIP|SCROLL|ELLIPSIS,]
    [FONT_SIZE = '<css_size>',]
    [FONT_COLOR = '<color_hex>',]
    [FONT_WEIGHT = NORMAL|BOLD|<number>]
  )]
  [, ACTIONS (ON_CLICK = <action>)]
);
```

## Mappings

- **CONTENT** — Column containing dynamic narrative text or Markdown.

## Options

- **MARKDOWN = ON|OFF** — Renders content as formatted Markdown (default `ON`; set `OFF` for escaped text).
- **ALIGN = 'left'|'center'|'right'** — Text horizontal alignment (default `'left'`).
- **MAX_LINES = n** — Limits visible text block to a maximum number of rendered lines.
- **OVERFLOW = CLIP|SCROLL|ELLIPSIS** — Overflow handling when content exceeds visual area or `MAX_LINES` limit (default `CLIP`).
- **FONT_SIZE = 'size'** — Typography font size string (e.g. `'14px'`, `'1.1rem'`).
- **FONT_COLOR = 'color'** — Text typography foreground color hex code or CSS color string.
- **FONT_WEIGHT = NORMAL|BOLD|number** — Font weight styling (e.g. `BOLD`, `600`).

## Examples

```sql
SELECT 'Widget Pro' AS ProductName, 0.325 AS ProfitRate INTO #summary_stats;

CREATE VISUAL ProductSummary AS TEXT (
  SOURCE = #summary_stats,
  CONTENT = 'Top performing product **{ProductName}** earned margin **{ProfitRate FORMAT ''0.0%''}**.',
  OPTIONS (
    MAX_LINES = 2,
    OVERFLOW = ELLIPSIS,
    FONT_SIZE = '16px',
    FONT_COLOR = '#1e3a8a',
    FONT_WEIGHT = BOLD
  ),
  ACTIONS (
    ON_CLICK = SHOW_MODAL('DetailModal')
  )
);
```

```sql
CREATE VISUAL ReportDisclaimer AS TEXT (
  CONTENT = '*Financial figures shown are preliminary and subject to final audit reconciliations.*',
  OPTIONS (
    ALIGN = 'center',
    MARKDOWN = ON,
    FONT_COLOR = '#6b7280'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
