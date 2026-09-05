# SLIDER

An interactive numeric slider or two-handle range slider. Slider values can increment by uniform steps or snap directly to breakpoints from a data source.

## Syntax

```sql
CREATE VISUAL VisualName AS SLIDER (
  SOURCE   = #breakpoints,
  MAPPINGS (
    VALUE = column
  ),
  OPTIONS (
    MODE        = SINGLE|RANGE,
    MIN         = n,
    MAX         = n,
    STEP        = n,
    DEFAULT     = n,
    FORMAT      = 'format-pattern',
    SHOW_TICKS  = ON|OFF,
    TICK_LABELS = ON|OFF,
    FIRE_ON     = RELEASE|CHANGE
  ),
  ACTIONS (
    ON_CHANGE = SET_PARAMETER(@low, @high, value)
  )
);
```

## Mappings

- **VALUE** — Optional column supplying discrete numeric breakpoints to snap slider handles to.

## Options

- **MODE = SINGLE|RANGE** — Single slider or dual-handle range slider (default SINGLE).
- **MIN = n** — Minimum slider value (default 0).
- **MAX = n** — Maximum slider value (default 100).
- **STEP = n** — Incremental step between tick positions (default 1).
- **DEFAULT = n** — Initial value or default range.
- **FORMAT = 'format-pattern'** — Display formatting string (e.g. `'C0'`, `'N2'`, `'P1'`).
- **SHOW_TICKS = ON|OFF** — Displays tick marks along the slider track (default OFF).
- **TICK_LABELS = ON|OFF** — Displays numeric labels under the tick marks (default OFF).
- **FIRE_ON = RELEASE|CHANGE** — Fires parameter updates on drag release or on every increment (default RELEASE).

## Actions

- **ON_CHANGE = SET_PARAMETER(@variable, value)** — Fires when the value changes in SINGLE mode.
- **ON_CHANGE = SET_PARAMETER(@low, @high, value)** — Fires when either handle moves in RANGE mode, binding low and high parameters.

## Examples

```sql
DECLARE @min_price DECIMAL = 50;
DECLARE @max_price DECIMAL = 500;

SELECT DISTINCT price INTO #price_tiers FROM #catalog;

CREATE VISUAL PriceFilter AS SLIDER (
  SOURCE   = #price_tiers,
  MAPPINGS (VALUE = price),
  OPTIONS  (
    MODE        = RANGE,
    FORMAT      = 'C0',
    SHOW_TICKS  = ON,
    FIRE_ON     = RELEASE
  ),
  ACTIONS  (ON_CHANGE = SET_PARAMETER(@min_price, @max_price, value))
);

CREATE VISUAL ProductGrid AS TABLE (
  SOURCE   = (SELECT product, price FROM #catalog
              WHERE price BETWEEN @min_price AND @max_price),
  MAPPINGS (product, price)
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
