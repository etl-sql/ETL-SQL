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
    [COLOR = colorCol,]
    [LABEL = textCol]
  ),
  OPTIONS (
    [TITLE = 'text',]
    [SIZE_RANGE = (min_px, max_px),]
    [MIN_BUBBLE_SIZE = n,]
    [MAX_BUBBLE_SIZE = n,]
    [X_AXIS (SCALE = LINEAR|LOG|LOGARITHMIC, ...),]
    [Y_AXIS (SCALE = LINEAR|LOG|LOGARITHMIC, ...)]
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
- **COLOR** — column used to colour-code bubbles by category with an automatic legend
- **LABEL** — column shown in the tooltip

## Options

- **SIZE_RANGE = (min_px, max_px)** — sets explicit minimum and maximum bubble radius in pixels (defaults to `5` and `65`).
- **MIN_BUBBLE_SIZE = n** — sets the minimum circle radius in pixels (default `5`).
- **MAX_BUBBLE_SIZE = n** — sets the maximum circle radius in pixels (default `65`).
- **X_AXIS / Y_AXIS (SCALE = LINEAR|LOG|LOGARITHMIC)** — sets the axis scale type. Logarithmic scale requires positive values and domain bounds; cannot be combined with `INCLUDE_ZERO = ON`.
- **DATA_LABELS = ON|OFF WITH (...)** — shows mark labels with optional leader lines, background, and border.
  - **LABEL_BACKGROUND = '#rrggbb'** — background color for data label badges (e.g. `'#ffffff'`).
  - **LABEL_BORDER = 'width style color'** — border for data label badges (e.g. `'1px solid #cbd5e1'`).
  - **LEADER_LINE = ON|OFF WITH (COLOR = '#rrggbb', STYLE = SOLID|DASHED)** — shows leader lines connecting bubbles to displaced smart labels.
- **TITLE = 'text'** — visual title
- **OVERLAYS (...)** — adds horizontal primary-Y `REFERENCE_LINE` rules and shaded `REFERENCE_BAND(LOW = n, HIGH = n, ...)` intervals. Bounds must be finite and `LOW` must be less than `HIGH`. Both participate in automatic Y domain calculation; explicit axis `MIN`/`MAX` remain authoritative.
- **FORMATTING (...)** — conditional mark coloring based on predicate conditions (e.g. `FORMATTING (WHEN margin_pct < 0 THEN '#ef4444')`).

## Examples

```sql
-- Market analysis: price vs. margin, sized by revenue, colored by segment, with custom size range
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
    COLOR = segment,
    LABEL = segment
  ),
  OPTIONS (
    TITLE = 'Segment Market Map',
    SIZE_RANGE = (8, 48),
    X_AXIS (SCALE = LOG, LABEL = 'Avg Price (Log)'),
    Y_AXIS (LABEL = 'Avg Margin %')
  ),
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
