# TREEMAP

Displays flat or hierarchical data as squarified nested rectangles sized by value.

## Syntax

```sql
CREATE VISUAL VisualName AS TREEMAP (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  )
);
```

## Mappings

- **NAME** - category label for each tile
- **VALUE** - determines tile area (must be positive)
- **PARENT** - optional; enables hierarchy (parent category name, or empty for root)
- **COLOR** - optional; column used to colour tiles independently of size

## Options

- **COLORS** - discrete category-to-color assignments such as `COLORS ('Hardware' = '#4e79a7')`
- **SHOW_VALUES = ON|OFF** - show value inside tile (default ON)
- **SHOW_PERCENT = ON|OFF** - show % of total inside tile (default OFF)

## Examples

```sql
-- Flat treemap (no hierarchy)
SELECT product_category AS name, SUM(revenue) AS value
INTO #market_share
FROM #sales GROUP BY product_category;

CREATE VISUAL MarketShare AS TREEMAP (
  SOURCE   = #market_share,
  MAPPINGS (NAME = name, VALUE = value),
  OPTIONS  (SHOW_PERCENT = ON, TITLE = 'Revenue by Category')
);

-- Hierarchical treemap
SELECT category AS name, subcategory AS parent, SUM(revenue) AS value
INTO #hier FROM #sales GROUP BY category, subcategory;

CREATE VISUAL HierRevenue AS TREEMAP (
  SOURCE   = #hier,
  MAPPINGS (NAME = name, PARENT = parent, VALUE = value)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
