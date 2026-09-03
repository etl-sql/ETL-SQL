# PIE

A circular chart divided into slices proportional to each value. Use `DONUT` when the visual needs a hollow center.

## Syntax

```sql
CREATE VISUAL VisualName AS PIE (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  )
);
```

## Mappings

- **VALUE** - numeric metric that determines each slice's area (required)
- **NAME** - label for each slice (required)

## Options

- **ROSE_MODE = ON|OFF** - "nightingale" mode: radius also varies with value, not just angle (default OFF)
- **INNER_RADIUS = number** - DONUT hole as a fraction from `0` to `0.9`, or as a percentage; the default is `0.45`
- **SORT = SOURCE|VALUE_DESC|VALUE_ASC|ALPHA** - slice sort order: query order, largest first, smallest first, or alphabetical (default SOURCE)
- **MIN_SLICE_PCT = number** - minimum slice percentage threshold; smaller slices collapse into an "Other" segment
- **OTHER_LABEL = 'text'** - label for the collapsed "Other" slice (default 'Other')
- **EXPLODE = 'SliceName'** - category slice name to pull out radially from center
- **EXPLODE_ALL = number|ON** - pulls all slices radially outward by pixel distance (default 10)
- **EXPLODE_DISTANCE = number** - radial offset distance in pixels for exploded slices (default 10)
- **SLICE_BORDER_COLOR = '#rrggbb'** - separator stroke color between slices (default 'white')
- **SLICE_BORDER_WIDTH = number** - separator stroke width between slices (default 2; 0 removes lines)
- **START_ANGLE = number** - clockwise rotation in degrees from 12 o'clock position (default 0)
- **LEGEND = ON|OFF** - show colour legend (default ON)
- **DATA_LABELS = ON|OFF WITH (...)** — show slice labels and configure styling (default ON). Extended options:
  - **LEADER_LINE = ON|OFF WITH (COLOR = '#rrggbb', STYLE = SOLID|DASHED)** — connects slice outer arc to outside label (default OFF).
  - **LABEL_BACKGROUND = '#rrggbb'** — padded background rectangle drawn behind the label text.
  - **LABEL_BORDER = 'width style #rrggbb'** — border around the data label background (e.g., `'1px solid #334155'`).
- **CENTER_LABEL = 'text'** - text shown in the centre hole (DONUT only)
- **CENTER_VALUE = 'text'** - prominent value shown in the centre hole (DONUT only); `{total}` is replaced with the slice total

## Examples

```sql
SELECT channel, SUM(revenue) AS total
INTO #by_channel
FROM #sales GROUP BY channel;

-- Pie chart
CREATE VISUAL RevenueByChannel AS PIE (
  SOURCE   = #by_channel,
  MAPPINGS (VALUE = total, NAME = channel),
  OPTIONS  (LEGEND = ON, TITLE = 'Revenue Mix')
);

-- Donut with centre label
CREATE VISUAL RevenueDonut AS DONUT (
  SOURCE   = #by_channel,
  MAPPINGS (VALUE = total, NAME = channel),
  OPTIONS  (INNER_RADIUS = 0.55, CENTER_VALUE = '{total}', CENTER_LABEL = 'Revenue', TITLE = 'Revenue Mix')
);

-- Nightingale / rose chart
CREATE VISUAL SalesRose AS PIE (
  SOURCE   = #by_channel,
  MAPPINGS (VALUE = total, NAME = channel),
  OPTIONS  (ROSE_MODE = ON)
);

-- Pie chart with leader lines and styled data labels
CREATE VISUAL LeadSourcePie AS PIE (
  SOURCE   = #by_channel,
  MAPPINGS (VALUE = total, NAME = channel),
  OPTIONS  (
    LEGEND      = OFF,
    DATA_LABELS = ON WITH (
      LEADER_LINE      = ON WITH (COLOR = '#64748b', STYLE = DASHED),
      LABEL_BACKGROUND = '#ffffff',
      LABEL_BORDER     = '1px solid #cbd5e1'
    ),
    TITLE       = 'Revenue by Channel'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
