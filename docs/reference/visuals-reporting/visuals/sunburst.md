# SUNBURST

A radial hierarchical visual where concentric rings represent successive levels of a hierarchy, sized proportionally by value. Useful for displaying part-to-whole relationships, multi-level category decompositions, and organizational structures.

## Syntax

```sql
CREATE VISUAL VisualName AS SUNBURST (
  SOURCE = #tableName,
  MAPPINGS (
    LEVEL1 = CategoryColumn,
    LEVEL2 = SubcategoryColumn,
    VALUE = MeasureColumn,
    COLOR = MetricColumn
  ),
  OPTIONS (
    TITLE = 'Category Decomposition',
    SHOW_BREADCRUMB = ON
  )
);
```

## Mappings

Sunburst supports two hierarchy input modes:

### Multi-Level Column Mode
- **LEVEL1** — Outermost ring category or primary level (required).
- **LEVEL2** — Second concentric ring (optional).
- **LEVEL3** — Third concentric ring (optional).
- **VALUE** — Numeric measure determining segment angle.
- **COLOR** — Optional column containing custom color assignments or metric values used to color wedges independently of root segments.

### Parent-Child Mode
- **LABEL** — Node identifier or category name. Alias: `NAME`.
- **PARENT** — Parent node identifier; leave empty or null to mark root nodes.
- **VALUE** — Numeric measure determining segment angle.
- **COLOR** — Optional column containing custom color assignments or metric values used to color wedges independently of hierarchy levels.

## Options

- **SHOW_BREADCRUMB = ON|OFF** — Displays an interactive navigation path header at the top of the radial visual (default `OFF`).
- **COLORS** — Discrete category-to-color assignments mapping names to hex colors.
- **TITLE = 'text'** — Visual title displayed above the sunburst chart.

## Examples

### Multi-Level Hierarchy with Independent Color

```sql
SELECT 'Electronics' AS Cat, 'Phones' AS SubCat, 1200 AS Units, '#2563eb' AS ColorCode UNION ALL
SELECT 'Electronics', 'Laptops', 800, '#0284c7' UNION ALL
SELECT 'Apparel', 'Shirts', 1500, '#10b981' UNION ALL
SELECT 'Apparel', 'Pants', 900, '#16a34a'
INTO #product_hierarchy;

CREATE VISUAL ProductSunburst AS SUNBURST (
  SOURCE   = #product_hierarchy,
  MAPPINGS (
    LEVEL1 = Cat,
    LEVEL2 = SubCat,
    VALUE  = Units,
    COLOR  = ColorCode
  ),
  OPTIONS  (
    TITLE           = 'Product Units by Category',
    SHOW_BREADCRUMB = ON
  )
);
```

### Parent-Child Ragged Hierarchy

```sql
SELECT 'Executive' AS Role, '' AS ReportsTo, 1 AS Headcount UNION ALL
SELECT 'Engineering', 'Executive', 25, '' UNION ALL
SELECT 'Frontend', 'Engineering', 10, '' UNION ALL
SELECT 'Backend', 'Engineering', 15, '' UNION ALL
SELECT 'Sales', 'Executive', 18, ''
INTO #org_chart;

CREATE VISUAL OrgSunburst AS SUNBURST (
  SOURCE   = #org_chart,
  MAPPINGS (
    LABEL  = Role,
    PARENT = ReportsTo,
    VALUE  = Headcount
  ),
  OPTIONS  (
    TITLE           = 'Organization Headcount',
    SHOW_BREADCRUMB = ON
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Visual Reference](../README.md)
