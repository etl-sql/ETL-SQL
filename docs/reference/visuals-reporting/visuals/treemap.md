# TREEMAP

Displays flat or hierarchical data as squarified nested rectangles sized by value. Useful for part-to-whole comparisons, budget breakdowns, and disk or asset inventory visualizations.

## Syntax

```sql
CREATE VISUAL VisualName AS TREEMAP (
  SOURCE = #tableName,
  MAPPINGS (
    NAME = CategoryColumn,
    VALUE = SizeColumn,
    PARENT = ParentColumn,
    COLOR = MetricColumn
  ),
  OPTIONS (
    TITLE = 'Revenue Treemap',
    SHOW_BREADCRUMB = ON,
    LABEL_MIN_SIZE = 42,
    LABEL_OVERFLOW = CLIP
  )
);
```

## Mappings

- **NAME** — Category label for each tile. Alias: `LABEL`.
- **VALUE** — Numeric measure determining tile area (must be positive).
- **PARENT** — Optional parent node identifier enabling multi-level nested hierarchies. Leave empty or null for root nodes.
- **COLOR** — Optional column containing custom color assignments or metric values used to color tiles independently of area/size.

## Options

- **SHOW_BREADCRUMB = ON|OFF** — Displays an interactive navigation path header at the top of the hierarchy (default `OFF`).
- **LABEL_MIN_SIZE = n** — Minimum tile dimension in pixels required to render text labels (default `42`).
- **LABEL_OVERFLOW = CLIP|WRAP|HIDDEN** — Handling for labels exceeding tile boundaries. `CLIP` truncates with ellipsis (default), `WRAP` breaks into multi-line text when vertical space allows, and `HIDDEN` suppresses the label entirely if it does not fit.
- **COLORS** — Discrete category-to-color assignments mapping names to hex colors.
- **TITLE = 'text'** — Visual title displayed above the treemap.

## Examples

### Flat Treemap with Independent Color Encoding

```sql
SELECT 'Software' AS Category, 450000 AS Revenue, '#2563eb' AS ColorCode UNION ALL
SELECT 'Hardware', 320000, '#0284c7' UNION ALL
SELECT 'Cloud Hosting', 680000, '#10b981' UNION ALL
SELECT 'Consulting', 180000, '#f59e0b'
INTO #sales_by_dept;

CREATE VISUAL DeptTreemap AS TREEMAP (
  SOURCE   = #sales_by_dept,
  MAPPINGS (
    NAME  = Category,
    VALUE = Revenue,
    COLOR = ColorCode
  ),
  OPTIONS  (
    TITLE          = 'Department Revenue & Status',
    LABEL_MIN_SIZE = 30,
    LABEL_OVERFLOW = WRAP
  )
);
```

### Hierarchical Treemap with Breadcrumbs

```sql
SELECT 'Global' AS Region, '' AS ParentRegion, 1200000 AS Sales UNION ALL
SELECT 'North America', 'Global', 700000, '' UNION ALL
SELECT 'Europe', 'Global', 500000, '' UNION ALL
SELECT 'US East', 'North America', 450000, '' UNION ALL
SELECT 'US West', 'North America', 250000, ''
INTO #regional_sales;

CREATE VISUAL RegionalTreemap AS TREEMAP (
  SOURCE   = #regional_sales,
  MAPPINGS (
    NAME   = Region,
    PARENT = ParentRegion,
    VALUE  = Sales
  ),
  OPTIONS  (
    TITLE           = 'Regional Hierarchy',
    SHOW_BREADCRUMB = ON,
    LABEL_MIN_SIZE  = 25,
    LABEL_OVERFLOW  = CLIP
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Visual Reference](../README.md)
