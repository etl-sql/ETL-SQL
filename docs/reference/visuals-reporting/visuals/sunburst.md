Type: SUNBURST
A radial hierarchical chart where each ring represents a level of the hierarchy, sized proportionally by value. Useful for showing part-to-whole relationships across multiple levels simultaneously.

Two mapping modes:

Implicit hierarchy (level columns):
- **LEVEL1** - outermost ring category (required)
- **LEVEL2** - second ring (optional)
- **LEVEL3** - third ring (optional)
- **VALUE** - numeric measure (required)

Explicit parent-child:
- **LABEL** - node name; alias NAME accepted (required)
- **PARENT** - parent node name; empty or null marks a root node (required)
- **VALUE** - numeric measure (required)

Options:
  TITLE   = 'text'

Note: Level-column mode is simpler for flat GROUP BY results. Parent-child mode handles ragged hierarchies where branches have different depths.

```sql
-- Implicit mode: 3-level revenue breakdown (Category > Region > Salesperson)
SELECT Category, Region, Salesperson, SUM(Revenue) AS Revenue
  INTO #hier
  FROM dbo.Sales
  GROUP BY Category, Region, Salesperson;

CREATE VISUAL RevenueTree AS SUNBURST (
  SOURCE   = #hier,
  TITLE    = 'Revenue Breakdown',
  MAPPINGS (
    LEVEL1 = Category,
    LEVEL2 = Region,
    LEVEL3 = Salesperson,
    VALUE  = Revenue
  )
);
```

References:
- [Report SQL Guide](../../../guides/report-sql.md)
