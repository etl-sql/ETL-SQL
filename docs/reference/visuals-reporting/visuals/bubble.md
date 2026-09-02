# BUBBLE
A scatter chart where a third numeric column controls the radius of each circle, making it ideal for showing three-variable relationships simultaneously.

## Syntax

```sql
CREATE VISUAL VisualName AS BUBBLE (
  SOURCE = #tableName,
  MAPPINGS (
    X = numCol,
    Y = numCol,
    [SIZE = numCol,]
    [LABEL = textCol]
  ),
  OPTIONS (
    [TITLE = 'text']
  ),
  [OVERLAYS (
    REFERENCE_LINE (VALUE = n [, LABEL = 'text'] [, STYLE = SOLID|DASHED|DOTTED] [, COLOR = '#rrggbb']),
    ...
  )]
);
```

## Mappings

- **X** — horizontal numeric axis (required)
- **Y** — vertical numeric axis (required)
- **SIZE** — numeric column controlling circle radius (optional; uniform size if omitted)
- **LABEL** — column shown in the tooltip

## Options

- **DATA_LABELS = ON|OFF WITH (...)** — Shows mark labels with optional leader lines, background, and border.
  - **LABEL_BACKGROUND = '#rrggbb'** — Background color for data label badges (e.g. `'#ffffff'`).
  - **LABEL_BORDER = 'width style color'** — Border for data label badges (e.g. `'1px solid #cbd5e1'`).
  - **LEADER_LINE = ON|OFF WITH (COLOR = '#rrggbb', STYLE = SOLID|DASHED)** — Shows leader lines connecting bubbles to displaced smart labels.
- **TITLE = 'text'** — visual title
- **OVERLAYS (...)** — visual overlays including `REFERENCE_LINE(VALUE = n, ...)`. Renders an author-specified constant reference line targeting the primary vertical quantitative Y axis as a horizontal plot-spanning rule. `VALUE` is required and accepts finite signed numeric literals (including zero, decimals, and negative values). `LABEL` is optional; an omitted or empty `LABEL` does not paint a visible browser badge label, leader, or background. `STYLE` accepts `SOLID`, `DASHED` (default), or `DOTTED`. `COLOR` defaults to the standard overlay neutral `#888888`. No SQL calculation is performed; `REFERENCE_LINE` acts as a general author annotation distinct from `GOAL`. Reference values participate in automatic Y domain calculation, while explicit axis `MIN`/`MAX` remain authoritative and may clip an out-of-range line.

Note: SIZE values are automatically scaled to a display range of 5 to 65 px. Use SCATTER if you do not need variable point sizes.

## Examples

```sql
-- Market analysis: price vs. margin, sized by revenue, with a target margin reference line
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
  OPTIONS  (TITLE = 'Segment Market Map'),
  OVERLAYS (
    REFERENCE_LINE (
      VALUE = 25.0,
      LABEL = 'Target Margin',
      STYLE = DASHED,
      COLOR = '#10b981'
    )
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
