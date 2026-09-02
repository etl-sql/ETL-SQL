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
    REFERENCE_BAND (LOW = n, HIGH = n [, COLOR = '#rrggbb'] [, LABEL = 'text']),
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
- **OVERLAYS (...)** — adds horizontal primary-Y `REFERENCE_LINE` rules and shaded `REFERENCE_BAND(LOW = n, HIGH = n, ...)` intervals. Bounds must be finite and `LOW` must be less than `HIGH`. Both participate in automatic Y domain calculation; explicit axis `MIN`/`MAX` remain authoritative.

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
    REFERENCE_BAND (LOW = 20, HIGH = 30, COLOR = '#cbd5e1', LABEL = 'Expected margin'),
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
