# MATRIX
A pivot (cross-tab) table with expandable row and column headers, conditional formatting, magnitude data bars, and configurable totals. Row dimensions come from one or more ROW columns; columns are driven by one or more COL columns; each cell contains an aggregated VALUE measure. Rendered as an interactive HTML table.

## Syntax

```sql
CREATE VISUAL VisualName AS MATRIX (
  SOURCE     = #tableName,
  TITLE      = 'Title',
  MAPPINGS (
    ROW      = categoryColumn,
    COL      = periodColumn,
    VALUE    = measureColumn DATA_BAR COLOR '#4472C4'
  ),
  OPTIONS (
    AGGREGATE      = SUM,
    COLUMN_TOTAL   = ON,
    ROW_TOTAL      = ON,
    DEFAULT_EXPAND = LEVEL_1
  ),
  FORMATTING (
    WHEN value > 50000 THEN '#10b981' FONT '#ffffff',
    WHEN value < 20000 THEN '#fee2e2' FONT '#991b1b'
  )
);
```

## Mappings

- **ROW** — Primary row-dimension column (required); use ROW1, ROW2, ROW3 for multi-level row grouping hierarchies.
- **COL** — Primary pivot column (required); use COL1, COL2, COL3 for multi-level column grouping hierarchies.
- **VALUE** — Numeric measure to aggregate into each cell (required). Add `DATA_BAR [COLOR '#RRGGBB']` or `VALUE DATA_BAR = column` to show proportional fill bars.
- **VALUE2, VALUE3, VALUE4, VALUE5** — Additional measure columns; each gets its own sub-column per pivot column.

## Options

- **TITLE = 'text'** — Visual title displayed above the matrix.
- **AGGREGATE = SUM** — Aggregation method for cell values: SUM (default), AVG, COUNT, MIN, MAX.
- **COLUMN_TOTAL = ON|OFF** — Appends a bottom-margin total row summing each column across all row groups (default OFF).
- **ROW_TOTAL = ON|OFF** — Appends a right-hand total column summing each row across all pivot columns (default OFF).
- **GRAND_TOTAL = ON|OFF** — Enables the bottom-margin column total row (and corner intersection total) for backward compatibility (default OFF).
- **SUBTOTALS = ON|OFF** — Appends a subtotal row after each expanded row group (default OFF).
- **AXIS_SORT = ALPHA|DESC** — Sorts column dimension keys alphabetically (ALPHA, default) or by aggregate measure sum descending (DESC).
- **DEFAULT_EXPAND = ALL|NONE|LEVEL_1|LEVEL_2** — Initial expansion depth for hierarchical row groups: ALL (default, everything open), NONE (all groups collapsed), LEVEL_1 (level 0 expanded, children collapsed), or LEVEL_2 (levels 0 and 1 expanded).
- **DATA_BAR = ON|OFF** — Enables proportional fill bars behind cell values across the entire matrix (default OFF).
- **DATA_BAR_COLOR = '#RRGGBB'** — Custom color for cell magnitude bars (default '#4472C4').

## Formatting

The optional `FORMATTING` clause applies conditional styles to numeric cells in both the interactive viewer and SVG export:

```sql
FORMATTING (
  WHEN value > 50000 THEN '#d1fae5' FONT '#065f46',
  WHEN value < 20000 THEN '#fee2e2' FONT '#991b1b'
)
```

Rules support conditions comparing `value` (or measure column name) using `>`, `>=`, `<`, `<=`, `=`, `!=`, and `BETWEEN low AND high`.

## Examples

```sql
-- Revenue + Units pivot: Category rows by Region columns with totals and data bars
SELECT Category, Region, SUM(Revenue) AS Revenue, SUM(Units) AS Units
  INTO #pivot
  FROM dbo.Sales
  GROUP BY Category, Region;

CREATE VISUAL RevenuePivot AS MATRIX (
  SOURCE   = #pivot,
  TITLE    = 'Revenue & Units by Category and Region',
  MAPPINGS (
    ROW    = Category,
    COL    = Region,
    VALUE  = Revenue DATA_BAR COLOR '#3b82f6',
    VALUE2 = Units
  ),
  OPTIONS (
    AGGREGATE      = SUM,
    COLUMN_TOTAL   = ON,
    ROW_TOTAL      = ON,
    DEFAULT_EXPAND = ALL,
    AXIS_SORT      = DESC
  ),
  FORMATTING (
    WHEN value > 50000 THEN '#10b981' FONT '#ffffff'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
