# PAGE_LAYOUT

Defines the physical page layout (size, margins, orientation, scale) for a `CREATE PAGE` statement.

## Syntax

```sql
CREATE PAGE Revenue AS PAGINATED (
    STRUCTURE = 'A',
    MAP ('A' = MonthlyRevenueTable),
    PAGE_LAYOUT (
        SIZE = 'Letter',             -- 'Letter', 'A4', or 'Custom'
        ORIENTATION = 'Landscape',   -- 'Portrait' or 'Landscape'
        MARGINS = (0.5, 0.5, 0.5, 0.5), -- Top, Right, Bottom, Left
        UNITS = 'in',                -- 'in', 'cm', 'mm', 'px'
        OVERFLOW = 'Scale'           -- 'Scale', 'Split', 'Clip'
    )
);
```

- **SIZE** — The paper size format (e.g., `'Letter'`, `'A4'`, `'Custom'`).
- **CUSTOM_WIDTH** — The custom width of the page. Only valid when `SIZE = 'Custom'`.
- **CUSTOM_HEIGHT** — The custom height of the page. Only valid when `SIZE = 'Custom'`.
- **ORIENTATION** — `'Portrait'` or `'Landscape'`.
- **MARGINS** — A 4-tuple of margins `(Top, Right, Bottom, Left)` in the configured `UNITS`.
- **UNITS** — The unit of measurement (`'in'`, `'cm'`, `'mm'`, `'px'`).
- **OVERFLOW** — Controls how content that exceeds page bounds is handled (`'Scale'` down to fit, `'Split'` across pages, or `'Clip'`).

## References
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)

## Examples

```sql
PAGE_LAYOUT (COLUMNS = 12, PADDING = 16);
```
