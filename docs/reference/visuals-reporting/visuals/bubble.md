# BUBBLE
A scatter chart where a third numeric column controls the radius of each circle, making it ideal for showing three-variable relationships simultaneously.

## Syntax

```sql
CREATE VISUAL VisualName AS BUBBLE (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  )
);
```

## Mappings

- **X** - horizontal numeric axis (required)
- **Y** - vertical numeric axis (required)
- **SIZE** - numeric column controlling circle radius (optional; uniform size if omitted)
- **LABEL** - column shown in the tooltip

## Options

  TITLE   = 'text'

Note: SIZE values are automatically scaled to a display range of 5 to 65 px. Use SCATTER if you do not need variable point sizes.

## Examples

```sql
-- Market analysis: price vs. margin, sized by revenue
SELECT
    segment,
    AVG(unit_price)   AS avg_price,
    AVG(margin_pct)   AS avg_margin,
    SUM(revenue)      AS total_rev
  INTO #market
  FROM dbo.Sales
  GROUP BY segment;

CREATE VISUAL MarketBubble AS BUBBLE (
  SOURCE   = #market,
  MAPPINGS (
    X     = avg_price,
    Y     = avg_margin,
    SIZE  = total_rev,
    LABEL = segment
  ),
  OPTIONS  (TITLE = 'Segment Market Map')
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
