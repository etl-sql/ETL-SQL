Type: TRELLIS
A small-multiples (faceted) chart that repeats the same chart type for each distinct value of a FACET column. All panels share layout and — by default — the same Y axis, making cross-facet comparisons honest.

Mappings:
  X     — category axis (BAR/LINE) or horizontal numeric axis (SCATTER) (required)
  Y     — measure axis (required)
  FACET — column whose unique values each produce one panel (required)

Options:
  TITLE       = 'text'
  CHART_TYPE  = BAR      -- BAR (default), LINE, or SCATTER
  COLUMNS     = 3        -- number of panels per row (1–6, default 3)
  SHARED_AXIS = ON       -- ON (default) locks the Y range across all panels; OFF lets each panel auto-scale

Note: With SHARED_AXIS = ON, panels with a narrow data range still show the global scale — this prevents misleading comparisons but may compress low-variance panels visually. SHARED_AXIS has no effect when CHART_TYPE = SCATTER (scatter panels always auto-scale independently).

```sql
-- Revenue by category, one bar chart per region
SELECT Region, Category, SUM(Revenue) AS Revenue
  INTO #trellis
  FROM dbo.Sales
  GROUP BY Region, Category;

CREATE VISUAL TrellisRevByRegion AS TRELLIS (
  SOURCE   = #trellis,
  TITLE    = 'Revenue by Category — Faceted by Region',
  MAPPINGS (
    X     = Category,
    Y     = Revenue,
    FACET = Region
  ),
  OPTIONS  (CHART_TYPE = BAR, COLUMNS = 2, SHARED_AXIS = ON)
);
```

References:
- [Report SQL Guide](../../../../../Docs/Report_SQL_Guide.md)
