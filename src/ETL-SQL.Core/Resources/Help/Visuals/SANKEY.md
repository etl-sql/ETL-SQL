Type: SANKEY
A flow diagram where values travel between source and destination nodes through weighted links. Ideal for budget allocations, supply chains, conversion funnels, and category-to-category revenue flows.

Mappings:
  FROM    — source node name; alias SOURCE accepted (required)
  TO      — destination node name; alias TARGET accepted (required)
  VALUE   — numeric flow magnitude (required)

Options:
  TITLE   = 'text'

Note: Node names in FROM and TO are deduplicated automatically. A name may appear on both sides (e.g. a distribution hub). Circular flows (A → B → A) are supported but may reduce readability.

```sql
-- Revenue flow: region → product category
SELECT Region AS FromRegion, Category AS ToCategory, SUM(Revenue) AS FlowValue
  INTO #sankey_data
  FROM dbo.Sales
  GROUP BY Region, Category;

CREATE VISUAL RegionCategoryFlow AS SANKEY (
  SOURCE   = #sankey_data,
  TITLE    = 'Revenue Flow: Region → Category',
  MAPPINGS (
    FROM  = FromRegion,
    TO    = ToCategory,
    VALUE = FlowValue
  )
);
```
