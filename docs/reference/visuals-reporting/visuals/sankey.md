Type: SANKEY
A flow diagram where values travel between source and destination nodes through weighted links. Ideal for budget allocations, supply chains, conversion funnels, and category-to-category revenue flows.

Mappings:
- **SOURCE** - source node name; alias FROM accepted (required)
- **TARGET** - destination node name; alias TO accepted (required)
- **VALUE** - numeric flow magnitude (required)

Options:
  TITLE   = 'text'

Note: Node names in SOURCE/FROM and TARGET/TO are deduplicated automatically. A name may appear on both sides, such as a distribution hub. Circular flows are supported but may reduce readability.

```sql
-- Revenue flow from region to product category
SELECT Region AS FromRegion, Category AS ToCategory, SUM(Revenue) AS FlowValue
  INTO #sankey_data
  FROM dbo.Sales
  GROUP BY Region, Category;

CREATE VISUAL RegionCategoryFlow AS SANKEY (
  SOURCE   = #sankey_data,
  TITLE    = 'Revenue Flow: Region to Category',
  MAPPINGS (
    FROM  = FromRegion,
    TO    = ToCategory,
    VALUE = FlowValue
  )
);
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
