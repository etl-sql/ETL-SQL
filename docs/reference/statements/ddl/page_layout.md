# PAGE_LAYOUT

Defines the physical page layout (size, margins, orientation, scale) for a `CREATE PAGE` statement.

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

- **SIZE** — The paper size format (e.g., `'Letter'`, `'A4'`).
- **ORIENTATION** — `'Portrait'` or `'Landscape'`.
- **MARGINS** — A 4-tuple of margins `(Top, Right, Bottom, Left)` in the configured `UNITS`.
- **UNITS** — The unit of measurement (`'in'`, `'cm'`, `'mm'`, `'px'`).
- **OVERFLOW** — Controls how content that exceeds page bounds is handled (`'Scale'` down to fit, `'Split'` across pages, or `'Clip'`).

## References
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
