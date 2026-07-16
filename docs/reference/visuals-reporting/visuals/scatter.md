Type: SCATTER / BUBBLE
A scatter plot for exploring correlations between two numeric dimensions. Add a SIZE mapping to render a bubble chart where point area encodes a third variable.

Mappings:
- **X** — horizontal numeric axis (required)
- **Y** — vertical numeric axis (required)
- **SIZE** — numeric column controlling bubble radius (BUBBLE form)
- **COLOR** — column used to colour-code points by category
- **LABEL** — column shown as a tooltip or annotation label

Options:
- **SHOW_REGRESSION = ON|OFF** — overlay a linear regression line (default OFF)
  TITLE           = 'text'

```sql
-- Basic scatter: price vs. quantity
SELECT unit_price, quantity_sold, product_name
  INTO #scatter_data
  FROM dbo.Products;

CREATE VISUAL PriceVsQty AS SCATTER (
  SOURCE   = #scatter_data,
  MAPPINGS (X = unit_price, Y = quantity_sold, LABEL = product_name),
  OPTIONS  (SHOW_REGRESSION = ON, TITLE = 'Price vs. Quantity')
);

-- Bubble chart: revenue × margin × volume
SELECT
    region,
    AVG(unit_price)     AS avg_price,
    AVG(margin_pct)     AS avg_margin,
    SUM(quantity_sold)  AS total_qty
  INTO #bubble_data
  FROM dbo.Sales
  GROUP BY region;

CREATE VISUAL MarketBubble AS SCATTER (
  SOURCE   = #bubble_data,
  MAPPINGS (
    X     = avg_price,
    Y     = avg_margin,
    SIZE  = total_qty,
    COLOR = region,
    LABEL = region
  ),
  OPTIONS  (TITLE = 'Regional Market Overview')
);
```

References:
- [Report SQL Guide](../../../guides/report-sql.md)
