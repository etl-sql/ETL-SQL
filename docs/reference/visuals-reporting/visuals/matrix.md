Type: MATRIX
A pivot (cross-tab) table with expandable row and column headers. Row dimensions come from one or more ROW columns; columns are driven by one or more COL columns; each cell contains an aggregated VALUE. Rendered as an HTML table, not a chart.

Mappings:
- **ROW** — primary row-dimension column (required); use ROW1/ROW2/ROW3 for multiple row dimensions
- **COL** — primary pivot column; use COL1/COL2/COL3 for multiple column dimensions
- **VALUE** — numeric measure to aggregate into each cell (required)
- **VALUE2, VALUE3, VALUE4, VALUE5** — additional measure columns; each gets its own sub-column per pivot column

Options:
  TITLE       = 'text'
  AGGREGATE   = SUM      -- SUM (default), AVG, COUNT, MIN, MAX
  GRAND_TOTAL = OFF      -- ON appends a grand-total row summing all row groups
  SUBTOTALS   = OFF      -- ON appends a subtotal row after each collapsed row group
  AXIS_SORT   = ALPHA    -- ALPHA (default) sorts column keys alphabetically; DESC sorts by aggregate value descending

Note: Cells are blank when no source row matches the row+column combination. Multiple row or column dimensions render grouped headers that can be expanded and collapsed in the viewer. AXIS_SORT = DESC is useful for "top N categories" pivots where you want the highest-value columns first.

```sql
-- Revenue + Units pivot: Category rows × Region columns
SELECT Category, Region, SUM(Revenue) AS Revenue, SUM(Units) AS Units
  INTO #pivot
  FROM dbo.Sales
  GROUP BY Category, Region;

CREATE VISUAL RevenuePivot AS MATRIX (
  SOURCE   = #pivot,
  TITLE    = 'Revenue & Units by Category × Region',
  MAPPINGS (
    ROW    = Category,
    COL    = Region,
    VALUE  = Revenue,
    VALUE2 = Units
  ),
  OPTIONS  (AGGREGATE = SUM, GRAND_TOTAL = ON, SUBTOTALS = ON, AXIS_SORT = DESC)
);
```

References:
- [Report SQL Guide](../../../guides/report-sql.md)
