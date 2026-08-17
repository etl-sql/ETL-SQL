# PRINT_LAYOUT
Defines physical page dimensions, print margins, and pagination break rules for paginated reports and high-fidelity PDF exports. Used at the page level in `CREATE PAGE ... AS PAGINATED` or at the visual level in `CREATE VISUAL`.

## Syntax

```sql
-- Page-level syntax (defines physical sheet dimensions and margins)
CREATE PAGE <name> AS PAGINATED (
  LAYOUT (STRUCTURE = '<grid>', MAP ('<slot>' = <visual>)),
  PRINT_LAYOUT (
    [PAGE_SIZE = 'Letter' | 'A4' | 'Legal' | 'Executive' | 'Tabloid' | 'A3' | 'A5' | 'Custom',]
    [ORIENTATION = 'PORTRAIT' | 'LANDSCAPE',]
    [CUSTOM_WIDTH = <width_number>,]
    [CUSTOM_HEIGHT = <height_number>,]
    [MARGINS = (<top>, <right>, <bottom>, <left>),]
    [UNITS = 'in' | 'cm' | 'mm' | 'pt' | 'px',]
    [OVERFLOW = 'AUTO' | 'CLIP' | 'SPLIT' | 'SCROLL']
  )
);

-- Visual-level syntax (defines page break and keep-together behavior)
CREATE VISUAL <name> AS <TYPE> (
  SOURCE = #data,
  PRINT_LAYOUT (
    [PAGE_BREAK_BEFORE = ON | OFF,]
    [PAGE_BREAK_AFTER = ON | OFF,]
    [KEEP_TOGETHER = ON | OFF,]
    [EXCLUDE_FROM_PRINT = ON | OFF]
  )
);
```

## Page-Level Options

- **PAGE_SIZE = '<size>'** — Standard physical page size. Supported values include `'Letter'` (8.5 x 11 in, default), `'A4'` (210 x 297 mm), `'Legal'` (8.5 x 14 in), `'Executive'`, `'Tabloid'`, `'A3'`, `'A5'`, and `'Custom'`. (Alias: `SIZE`).
- **ORIENTATION = '<orientation>'** — Sheet orientation: `'PORTRAIT'` (default) or `'LANDSCAPE'`.
- **CUSTOM_WIDTH = <n>** — Width when `PAGE_SIZE = 'Custom'`, in the specified `UNITS`.
- **CUSTOM_HEIGHT = <n>** — Height when `PAGE_SIZE = 'Custom'`, in the specified `UNITS`.
- **MARGINS = (top, right, bottom, left)** — Four-tuple defining printable page margins (default `(1.0, 1.0, 1.0, 1.0)` in inches). (Alias: `MARGIN`).
- **UNITS = '<unit>'** — Measurement units: `'in'` (inches, default), `'cm'`, `'mm'`, `'pt'` (points), or `'px'`.
- **OVERFLOW = '<mode>'** — How oversized visual content is handled across physical boundaries: `'AUTO'` (split tables across pages, default), `'CLIP'`, `'SPLIT'`, or `'SCROLL'`.

## Visual-Level Options

- **PAGE_BREAK_BEFORE = ON | OFF** — Forces a new physical page before rendering this visual.
- **PAGE_BREAK_AFTER = ON | OFF** — Forces a new physical page immediately after this visual completes.
- **KEEP_TOGETHER = ON | OFF** — Prevents splitting this visual across physical page boundaries when possible.
- **EXCLUDE_FROM_PRINT = ON | OFF** — Omits interactive-only visuals (e.g., prompt controls or buttons) from printed sheets and PDF exports. (Alias: `EXCLUDE`).

## Examples

```sql
-- Define a paginated landscape invoice report with 0.5in margins
CREATE PAGE InvoiceReport AS PAGINATED (
  LAYOUT (
    STRUCTURE = 'H / T / S',
    MAP (
      'H' = CompanyHeader,
      'T' = LineItemsTable,
      'S' = SummaryFooter
    )
  ),
  PRINT_LAYOUT (
    PAGE_SIZE = 'Letter',
    ORIENTATION = 'LANDSCAPE',
    MARGINS = (0.5, 0.5, 0.5, 0.5),
    UNITS = 'in'
  )
);

-- Break page before the summary and keep totals intact
CREATE VISUAL SummaryFooter AS CARD (
  SOURCE = #order_totals,
  MAPPINGS (VALUE = TotalAmount, LABEL = 'Grand Total'),
  PRINT_LAYOUT (
    PAGE_BREAK_BEFORE = ON,
    KEEP_TOGETHER = ON
  )
);
```

## Physical Page Compilation & PDF Export

When a report containing `PAGINATED` pages or `PRINT_LAYOUT` clauses is compiled, the engine's `PhysicalPageCompiler` calculates exact page geometry and partitions long tables into physical page slices (`startRowIndex` to `endRowIndex`).

- **CLI Export:** `etl-sql-report build report.rptsql --format pdf` compiles the report into physical pages and produces `<script>.report.pdf`.
- **PDF Modes:** Automatic selection routes paginated reports through `STATIC` deterministic layout or `BROWSER` high-fidelity rendering.
- **Portal & Report Player:** Render physical sheets with realistic page borders and shadows matching print specifications.

References:
- [PAGE Reference](page.md)
- [VISUAL Reference](visual.md)
- [Report-SQL Scripting Guide](../../../guides/feature-guides/report-sql.md)
- [Report CLI and PDF Export](../report-cli.md)
