# CHART

Defines renderer-neutral native mark layers, encodings, scales, coordinates, conditional presentation, and facet composition for a `CUSTOM` Report-SQL visual. Data preparation remains visible in ETL-SQL statements and `#temp` tables; `CHART` does not accept embedded Vega-Lite or hidden transforms.

## Syntax

```sql
CREATE VISUAL name AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = CARTESIAN, ASPECT_RATIO = positive_number) |
               (TYPE = GEOGRAPHIC, PROJECTION = EQUIRECTANGULAR | MERCATOR,
                MAP_NAME = 'WORLD' | MAP_FILE = 'allowed/path.geojson', FEATURE_KEY = 'name'),
    [SCALES (
      scale_name = LINEAR (
        CHANNEL = Y,
        INCLUDE_ZERO = ON,
        MIN = literal,
        MAX = literal,
        REVERSE = ON|OFF,
        MAJOR_TICK_COUNT = 2..100,
        TICK_INTERVAL = positive_number,
        MINOR_TICKS = ON|OFF,
        TIME_UNIT = AUTO|DAY|WEEK|MONTH|QUARTER|YEAR,
        TICK_FORMAT = 'format',
        LABEL_ROTATION = AUTO|0|45|90,
        LABEL_SKIP = AUTO|non_negative_integer,
        OUTER_PADDING = 0..1,
        ORDER = SOURCE | ASCENDING | DESCENDING | (literal, ...),
        RANGE = GRADIENT(LOW = '#RRGGBB', HIGH = '#RRGGBB') |
                DIVERGING(LOW = '#RRGGBB', MID = '#RRGGBB', HIGH = '#RRGGBB', MIDPOINT = number)
      )
    )],
    ENCODINGS (
      X = field (TYPE = NOMINAL),
      Y = DATUM(1500) (TYPE = QUANTITATIVE),
      COLOR = VALUE('#c62828') (TYPE = NOMINAL)
    ),
    LAYERS (
      layer_name = RECT | LINE | AREA | POINT | RULE | ARC | TEXT | TICK (
        Z_INDEX = number,
        INHERIT_ENCODINGS = ON | OFF,
        NULL_HANDLING = CONNECT | GAP | ZERO,
        AREA_BASELINE = ZERO | number,
        HOVER_FOCUS = NONE | SELF | SERIES,
        BAND_SIZE = fraction,
        THICKNESS = fraction,
        ORIENTATION = AUTO | HORIZONTAL | VERTICAL,
        POSITION = IDENTITY | JITTER(X = fraction, Y = fraction, KEY = field, SEED = integer) |
                   NUDGE(X = number, Y = number, UNIT = DATA | BAND | EM),
        ENCODINGS (
          X | X2 | X_START | X_END | X_OFFSET | Y | Y2 | Y_START | Y_END | Y_OFFSET |
          LOW | Q1 | MEDIAN | Q3 | HIGH | OPEN | CLOSE | ERROR_LOW | ERROR_HIGH |
          CONFIDENCE_LOW | CONFIDENCE_HIGH |
          COLOR | SIZE | SHAPE | THETA | RADIUS | LONGITUDE | LATITUDE | REGION | ROUTE |
          TEXT | TOOLTIP | DETAIL = field | DATUM(scalar) | VALUE(scalar) (
            TYPE = QUANTITATIVE | TEMPORAL | NOMINAL | ORDINAL,
            SCALE = scale_name,
            AXIS = NONE | PRIMARY | SECONDARY,
            SORT = SOURCE | ASCENDING | DESCENDING,
            FORMAT = 'format',
            STACK = NONE | ZERO | NORMALIZE
          )
        ),
        STYLE (property = literal, ...),
        CONDITIONS (
          COLOR | OPACITY | SIZE | SHAPE | TEXT WHEN predicate THEN literal [ELSE literal]
        )
      )
    ),
    [ANNOTATIONS (
      POINT (
        SERIES = 'series_name',
        TYPE = MAX | MIN | COORD(x, y),
        LABEL = 'label text',
        SYMBOL = 'pin' | 'arrow' | 'circle'
      ),
      ...
    )],
    FACET (ROW = field, COLUMN = field) | FACET (WRAP = field, COLUMNS = 3),
    RESOLVE (X = SHARED | INDEPENDENT, Y = SHARED | INDEPENDENT, COLOR = SHARED | INDEPENDENT)
  )
);
```

## Mappings

- **COORDINATE** — Selects `CARTESIAN`, `TRANSPOSED_CARTESIAN`, `POLAR`, or `GEOGRAPHIC`; polar coordinates may declare angles/radius. `ASPECT_RATIO` is the physical Y-unit/X-unit ratio and requires continuous quantitative primary X/Y scales. `TRANSPOSED_CARTESIAN` supports this ratio on `POINT` and `TEXT` layers with `IDENTITY`, `JITTER`, or `NUDGE(..., UNIT = EM|BAND|DATA)`, without stacking or secondary axes. LINE layers, RULE layers and RECT layers with both endpoint pairs are also supported under the restrictions below. Y becomes horizontal and X becomes vertical; logarithmic units are decades. Facets and resizing preserve the ratio. Terminal output preserves values and ordering, not physical distances.
- **SCALES** — Optionally declares named `LINEAR`, `LOGARITHMIC`, `TIME`, `BAND`, `POINT`, `ORDINAL`, or `IDENTITY` scales. Encoding `SCALE` references must name a declared scale; omission requests deterministic inference from the required `TYPE`, channel, mark, and coordinate.
- **RANGE** — Adds a dependency-free sRGB sequential or diverging output range to a quantitative `COLOR` scale. Colors use portable `#RRGGBB`; values clamp at the domain, nulls use `NULL_COLOR`, and a diverging midpoint must lie inside the resolved domain.
- **Scale axis controls** — `MIN`/`MAX` set the domain, `INCLUDE_ZERO` expands a quantitative domain to zero, `REVERSE` flips its display direction, `MAJOR_TICK_COUNT` or `TICK_INTERVAL` controls major ticks, `MINOR_TICKS` adds midpoint ticks, `TIME_UNIT` truncates/bins temporal scales by calendar unit (`AUTO`, `DAY`, `WEEK`, `MONTH`, `QUARTER`, `YEAR`), `TICK_FORMAT` applies a custom date/time or numeric format pattern, and `LABEL_ROTATION`/`LABEL_SKIP` control crowded tick labels. `OUTER_PADDING = 0..1` adds space before the first and after the last category on `BAND` scales only.
- **LAYERS** — Declares marks in deterministic `Z_INDEX` order. A layer consumes the visual's single `SOURCE`; stage differently prepared inputs into one visible `#temp` table before authoring the visual.
- **ENCODINGS** — At `CHART` scope, declares bindings inherited by layers. At layer scope, overrides individual channels. A layer defaults to `INHERIT_ENCODINGS = ON`; `OFF` makes its bindings isolated. Duplicate channels within either scope are errors.
- **SHAPE** — On `POINT` layers, accepts `CIRCLE`, `SQUARE`, `TRIANGLE`, `DIAMOND`, `CROSS`, or `STAR`. Bind a nominal/ordinal field containing those names, use `DATUM`/`VALUE` for one constant shape, or set the same vocabulary through a `SHAPE` condition. Values are case-insensitive. Unsupported runtime field values fall back to `CIRCLE`; invalid authored constants are rejected.
- **Binding sources** — A bare field reads a source column; `DATUM(literal-or-parameter)` supplies a typed data-domain constant that may use a scale; `VALUE(literal-or-parameter)` supplies a visual-range value and cannot use a scale or positional channel. Expressions, functions, aggregates, column references inside wrappers, null positional constants, and secret parameters are rejected.
- **STYLE** — Applies renderer-neutral literal style tokens to one layer. `POINT` layers accept `SYMBOL_STROKE_COLOR = '#RRGGBB'` and a non-negative `SYMBOL_STROKE_WIDTH = n`; a color without a width uses `1` pixel, while a width without a color draws no stroke. `LINE` layers accept `LINE_WIDTH = n` from `0.1` through `10` pixels. `LINE` and `AREA` layers also accept `INTERPOLATION = 'LINEAR'|'SMOOTH'|'STEP_BEFORE'|'STEP_AFTER'`, which selects how the layer connects its points, and `LINE_DASH = 'SOLID'|'DASHED'|'DOTTED'`, which sets its stroke pattern. Both are rejected on other marks. `THICKNESS` remains specific to `TICK` marks and measures a fraction of one em.
- **CONDITIONS** — Applies presentation-only values per row. Predicates accept fields, report parameters, literals, comparisons, `AND`, `OR`, `NOT`, and `IS [NOT] NULL`. Connected `LINE` and `AREA` marks reject row-level conditions; use separate staged series or layers.

## Options

- **Placement** — `STACK` accumulates quantitative Y/Y2 values for Cartesian and transposed Cartesian layouts; polar ARC layers accept STACK ZERO/NORMALIZE on RADIUS as described below. Offset channels dodge categories, `BAND_SIZE` controls relative thickness, and `Z_INDEX` controls paint order. `JITTER` uses a stable key and deterministic hash; `NUDGE` is resolved after domains without changing raw values. For transposed fixed-aspect points and text, `NUDGE` accepts `UNIT = EM`: one em is the portable 12-pixel unit, positive X moves up, and positive Y moves right. Negative values move in the opposite direction. This presentation displacement does not follow scale reversal; the point and its error bar move together.
- **Transposed fixed-aspect offset groups** — `X_OFFSET` and `Y_OFFSET` accept nominal/ordinal groups on POINT/TEXT charts. X groups move vertically and Y groups horizontally. Group centers divide one fitted plot band into equal slots, centered around the original position; the metric excludes fixed axis margins and precedes renderer-specific legend layout. `ORDER` sets category order and `REVERSE` on the offset scale reverses its slots. Reversing a primary X/Y scale does not reverse grouping displacement. Null, unmatched, and singleton groups have zero displacement. Facets retain the shared category slots but scale displacement to each fitted viewport. EM/BAND/DATA nudges and jitter add to these offsets. Error intervals move with points, and group names remain in terminal and accessible descriptions.
- **Transposed fixed-aspect DATA nudges** — `NUDGE(..., UNIT = DATA)` moves the anchor to where X + nudge X and Y + nudge Y would map on the original scales. This displacement follows scale reversal and logarithmic mapping, uses each facet's axes, and accounts for side-legend space. Raw values and domains do not change. Error bars translate with their point by the anchor displacement, keeping their original span; the endpoints are not independently shifted in data space. Numeric X/Y are required on every row, and a logarithmic anchor and its shifted target must both remain positive. Text consumes the same displacement before collision placement. Categorical offsets add to the DATA displacement.
- **Transposed fixed-aspect BAND nudges** — On continuous POINT/TEXT charts, `NUDGE(..., UNIT = BAND)` treats each primary axis as one band. X is a fraction of the fitted plot height and Y is a fraction of its width, excluding fixed axis margins and before renderer-specific legend layout. Positive X moves up, positive Y moves right, and negative values reverse those directions. Displacement is independent of scale reversal and raw values. Each facet uses its fitted viewport; resizing recomputes pixel offsets. Points, intervals, and text with the same nudge remain aligned.
- **Transposed fixed-aspect jitter** — `JITTER` supports POINT and TEXT with amplitudes from 0 to 1 and a unique, non-null `KEY` field. X amplitude is a fraction of the fitted plot height; Y amplitude is a fraction of its width, after removing the fixed axis margins and before renderer-specific legend layout. A stable seeded hash selects a signed displacement within each amplitude. X moves vertically and Y horizontally; reversal does not flip the displacement. Resizing recomputes pixel offsets from the same hashes, including independent facet viewports. Point error bars move with their point. Hash identity includes the layer's mark, order and channels, so independently jittered text and point layers need not coincide. Terminal and accessible values remain unchanged.
- **Transposed fixed-aspect text** — `TEXT` layers use the same primary X/Y scales and EM/BAND/DATA offsets as points, and can appear alone or alongside points and error bars. Labels use collision placement; labels that cannot fit remain in SVG descriptions and semantic fallback. Terminal output includes annotation text and raw X/Y values. Conditions support `TEXT`, `COLOR`, `SIZE` (font pixels, clamped to 1–100), and `OPACITY` (clamped to 0–1).
- **Intervals** — Paired `Y_START`/`Y_END` creates an AREA ribbon, a vertical RULE span, or a ranged RECT such as a qualitative band or a floating variance bar; `X_START`/`X_END` supplies the symmetric horizontal range, which on a RECT with a continuous X scale is an explicit-bin histogram. Both endpoints are required, must share a quantitative or temporal `TYPE`, and both take part in scale-domain resolution. A ranged RECT owns its extent on that axis, so it rejects `Y`/`Y2` alongside `Y_START`/`Y_END` and `X`/`X2` alongside `X_START`/`X_END`; `STACK` computes its own endpoints and is unaffected. Endpoint calculations stay in SQL.
- **TICK** — Draws a short category-local quantitative observation or target. It requires nominal/ordinal X and quantitative Y. `ORIENTATION = AUTO` resolves to a horizontal segment across the category band; `HORIZONTAL` and `VERTICAL` make that choice explicit. TICK is distinct from plot-spanning/ranged `RULE`; its `BAND_SIZE` is relative to the category band and `THICKNESS` is bounded to `(0, 1]` em.
- **Error bars** — `POINT` and `RECT` layers support paired `ERROR_LOW` and `ERROR_HIGH` encoding channels under Cartesian or transposed Cartesian coordinates. Both channels require quantitative type, share the primary Y scale, and expand the scale domain to encompass the whiskers. Absolute endpoints are pre-computed in SQL. Optional layer style `STYLE (ERROR_BAR_STYLE = 'CAPS')` or `STYLE (ERROR_BAR_STYLE = 'NO_CAPS')` controls whether endpoint caps are drawn (defaults to `'CAPS'`). On transposed `POINT` charts with `ASPECT_RATIO`, whiskers run horizontally and caps run vertically; endpoint values still use the semantic Y scale and expand its domain before the aspect viewport is fitted. On `RECT`, whiskers anchor to the category position and primary quantitative value, and error channels cannot be combined with ranged rectangle or boxplot/candlestick channels.
- **Confidence intervals** — `AREA` layers support paired `CONFIDENCE_LOW` and `CONFIDENCE_HIGH` encoding channels under Cartesian or transposed Cartesian coordinates. Both channels require quantitative type, share the primary Y scale, and expand the scale domain to encompass the ribbon. Values are absolute, pre-computed in SQL. Confidence channels cannot be combined with `Y`, `Y2`, `Y_START`, or `Y_END` on the same `AREA` layer.
- **Statistical and financial rectangles** — A `RECT` with `X`, `LOW`, `Q1`, `MEDIAN`, `Q3`, and `HIGH` renders a box-plot glyph. A `RECT` with `X`, `OPEN`, `CLOSE`, `LOW`, and `HIGH` renders a candlestick glyph. These channels are quantitative and share the primary Y scale. Keep derived summaries in SQL. Add ordinary layers to the same `CUSTOM` chart for combinations such as box plot plus mean `TICK` or candlestick plus volume on `Y2`.
- **Geographic composition** — `GEOGRAPHIC` requires an explicit `EQUIRECTANGULAR` or `MERCATOR` projection and exactly one geometry authority: a built-in `MAP_NAME` (`WORLD`, `US_STATES`, `US_COUNTIES`, `MN_COUNTIES`, `CANADA_PROVINCES`, or `EUROPE`) or a GeoJSON `MAP_FILE`. `MAP_FILE` is resolved through the engine path policy and is limited to 5 MiB, 10,000 features, 200,000 coordinates, and nesting depth 32. `FEATURE_KEY` names the GeoJSON property matched by `REGION`. Geographic `RECT` fills regions; `POINT` and `TEXT` require quantitative `LONGITUDE` and `LATITUDE`; `LINE` also requires nominal `ROUTE` and connects rows in source order. Rendering is bounded to 20,000 points/labels and 500 routes. Region and route fields are the default interaction keys. Terminal and assistive surfaces receive an ordered table/transition fallback, while browser and PDF use the same resolved SVG geometry. Resolved filesystem paths are never serialized.
- **FACET** — Creates a row/column grid or a mutually exclusive one-dimensional `WRAP`. Wrap uses stable first-seen row-major ordering, 1–12 columns, at most 100 panels, render-work limits, and minimum panel dimensions.
- **RESOLVE** — Selects shared or per-panel X, Y/Y2, and color scales. Independent resolution requires `FACET`.
- **NULL_HANDLING = CONNECT|GAP|ZERO** — On `LINE` layers, controls how null or missing values along the series path are handled. `CONNECT` (default) interpolates across missing data points, `GAP` introduces a visible break in the line, and `ZERO` clamps null observations to 0.
- **AREA_BASELINE = ZERO|n** — On `AREA` layers, sets the fill baseline level. Defaults to `ZERO` (fills to 0). An explicit numeric value fills to an arbitrary quantitative Y baseline.
- **HOVER_FOCUS = NONE|SELF|SERIES** — On mark layers, controls pointer hover emphasis. `NONE` (default) applies standard hover effects, `SELF` dims other marks in the plot, and `SERIES` highlights the active series across all categories while dimming unrelated series.
- **ANNOTATIONS (POINT (...))** — Attaches data point and coordinate callouts to chart series. `SERIES` specifies the target layer or series name, `TYPE` selects `MAX`, `MIN`, or `COORD(x, y)`, `LABEL` sets the callout text, and `SYMBOL` selects the marker style (`'pin'`, `'arrow'`, or `'circle'`).
- **Visible transformations** — Aggregation, filtering, calculation, lookup, windowing, and statistical preparation belong in preceding ETL-SQL/`#temp` statements, not in `CHART`.

## Reference rules on transposed points

Use one quantitative field or `DATUM` binding on X or Y with `IDENTITY` (the default position).
Set `INHERIT_ENCODINGS = OFF` when shared encodings would add other channels.
Conditional rules and encodings beyond the supported single-axis or ranged forms are rejected in this combination.
A Y reference is vertical and an X reference is horizontal. Constants contribute to global and
independent facet domains; labels and accessible descriptions retain the semantic axis and value.
A field binding draws one rule per distinct non-null numeric threshold within each facet, in source
order. Repeated values share a rule. Terminal output follows the same rule selection; accessible
fallback lists distinct thresholds across the whole chart. Null-only layers have no rule geometry.

```sql
CREATE VISUAL Measurement AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
    SCALES (
      distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
      estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)
    ),
    LAYERS (
      observations = POINT (ENCODINGS (
        X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
        Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)
      )),
      threshold = RULE (
        Z_INDEX = 1,
        INHERIT_ENCODINGS = OFF,
        ENCODINGS (Y = DATUM(5) (TYPE = QUANTITATIVE, SCALE = estimates)),
        STYLE (LABEL = '<target>', COLOR = '#112233')
      )
    )
  )
);
```

To draw the distinct estimates as reference rules, replace the example's threshold encoding with:

```sql
ENCODINGS (Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates))
```

Reference rules also accept `NUDGE` with `UNIT = EM` or `UNIT = BAND`, along the bound axis only.
For a Y rule, X must be zero; for an X rule, Y must be zero. The rule and its label move together
while the line keeps spanning the plot. Positive X moves up and positive Y moves right, independently
of scale reversal. EM uses 12 pixels per unit; BAND uses the fitted plot height for X and width for Y,
excluding axis margins and before renderer-specific legend layout. Facets and resizing recompute BAND
displacement. Threshold values, domains, terminal text, and accessible values remain unchanged.
Reference rules also accept `JITTER` along their bound axis. The unbound amplitude must be zero;
amplitudes range from zero to one. X uses fitted plot height and Y uses fitted plot width, excluding
fixed axis margins and before legend layout. The same seeded displacement moves the rule and label,
while the line retains its full plot span. Reversal does not flip displacement; facets and resizing
rescale it. Raw thresholds, domains and terminal/accessible values remain unchanged.

The source must supply a unique, non-null stable key for every row, including null thresholds.
Repeated thresholds still draw one rule per facet using the first source row's key. A constant rule
uses the first row in each facet. Reordering rows preserves each key's offset, but reordering duplicate
thresholds can change which offset is shown. Use a deterministic source order when that matters.

For example, jitter a Y threshold by up to three percent of the fitted plot width:

```sql
POSITION = JITTER(X = 0, Y = 0.03, KEY = Id, SEED = 42)
```

For example, add this position to the Y threshold layer above to move it right by three pixels:

```sql
POSITION = NUDGE(X = 0, Y = 0.25, UNIT = EM)
```

Use `UNIT = BAND` with `Y = 0.03` to move it by three percent of the fitted plot width instead.

For DATA nudges, only the bound axis is required. The rule moves to where its threshold plus the
nudge would map on the original scale, including reversal, logarithmic mapping, side legends,
facets and resizing. The other amplitude must be zero. Raw thresholds and domains stay unchanged;
null thresholds draw no rule. On a logarithmic scale, both the original and shifted threshold must
be positive. For example, move the Y rule to the position of its threshold plus 0.5:

```sql
POSITION = NUDGE(X = 0, Y = 0.5, UNIT = DATA)
```

Isolated single-axis rules do not inherit color groups from other layers. A color-grouped POINT
layer therefore does not duplicate reference rules or their accessible values.

## Ranged rules on transposed fixed-aspect charts

A ranged RULE accepts exactly `X`, `Y_START`, `Y_END`; exactly `Y`, `X_START`, `X_END`;
or all four endpoint channels `X_START`, `X_END`, `Y_START`, `Y_END` for a diagonal segment.
All bindings must be quantitative fields or `DATUM` constants. Use `IDENTITY` placement
(the default), `JITTER`, or `NUDGE` with `UNIT = EM`, `BAND`, or `DATA`, without conditions or additional encodings. A semantic Y interval becomes horizontal;
a semantic X interval becomes vertical. Endpoints expand global and independent facet domains.
One segment is retained per source row, including coincident segments. Rows with a missing anchor
or endpoint draw no segment and contribute no interval to terminal or accessible output.
Endpoint order is preserved, including descending intervals. Terminal and accessible descriptions
include every raw endpoint and any fixed coordinate. A diagonal joins `(X_START, Y_START)` to
`(X_END, Y_END)` after transposition. Coincident endpoints retain a zero-length segment and its
accessible values. EM/BAND nudges translate both endpoints and the label together: positive X moves
up and positive Y moves right, independently of scale reversal. EM uses 12 pixels per unit; BAND
uses the fitted plot height for X and width for Y, excluding fixed axis margins and before legend
layout. Facets and resizing recompute BAND displacement. Raw endpoints and domains stay unchanged.
DATA nudges use the authored start as the anchor: the fixed coordinate plus range start for a
ranged rule, or `(X_START, Y_START)` for a diagonal. The anchor moves to where its X/Y values plus
the nudge map on the original scales. Both endpoints and the label translate by that same physical
displacement, preserving the drawn segment's length and direction even on logarithmic axes.
This displacement follows scale reversal and accounts for side legends, facets and resizing.
The end values are not independently nudged in data space. Start values and shifted targets must
be positive on logarithmic axes; incomplete rows are skipped before displacement is evaluated.
JITTER applies one seeded displacement to each complete source segment and its label. X amplitude
is a fraction of fitted plot height; Y amplitude is a fraction of fitted plot width, excluding fixed
axis margins and before legend layout. Amplitudes must be between zero and one. The KEY field must
exist and be unique and non-null across the entire source, including incomplete rows. Reordering
rows or renaming the layer preserves displacement by key; changing SEED changes it. Scale reversal
does not flip jitter, and facets and resizing scale the same seeded offsets to their fitted viewports.
The hash identity includes the chart, mark, Z index and channel sequence; separate layer shapes need
not share offsets. Raw intervals, domains, terminal output and accessible values remain unchanged.
Single-axis reference rules restrict JITTER to their bound axis and retain first-row threshold selection.

For example, spread segments by up to two percent of plot height and three percent of plot width:

```sql
POSITION = JITTER(X = 0.02, Y = 0.03, KEY = StableId, SEED = 42)
```

The source must supply a stable, unique `StableId` for each row.

Add this position to a ranged or diagonal RULE layer to move the segment up 0.24 pixels and left
0.36 pixels:

```sql
POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = EM)
```

Use `UNIT = BAND` with the same amplitudes for two percent of fitted plot height and three percent
of fitted plot width. To move the start to X - 0.5 and Y + 0.5 on the original scales:

```sql
POSITION = NUDGE(X = -0.5, Y = 0.5, UNIT = DATA)
```

```sql
CREATE VISUAL Measurement AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
    SCALES (
      distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
      estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)
    ),
    LAYERS (
      observations = POINT (ENCODINGS (
        X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
        Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)
      )),
      threshold = RULE (
        Z_INDEX = 1,
        INHERIT_ENCODINGS = OFF,
        ENCODINGS (X = Distance (TYPE = QUANTITATIVE, SCALE = distances), Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)),
        STYLE (LABEL = '<target>', COLOR = '#112233')
      )
    )
  )
);
```

## Diagonal rules on transposed fixed-aspect charts

Stage each segment's four endpoints in the source. Both X endpoints contribute to the X domain;
both Y endpoints contribute to the Y domain, including independent facets. Endpoint order and
source rows remain intact. A row missing any endpoint contributes no segment or interval output.

```sql
CREATE VISUAL Measurement AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
    SCALES (
      distances = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
      estimates = LINEAR (CHANNEL = Y, MIN = 0, MAX = 10)
    ),
    LAYERS (
      observations = POINT (ENCODINGS (
        X = Distance (TYPE = QUANTITATIVE, SCALE = distances),
        Y = Estimate (TYPE = QUANTITATIVE, SCALE = estimates)
      )),
      threshold = RULE (
        Z_INDEX = 1,
        INHERIT_ENCODINGS = OFF,
        ENCODINGS (X_START = StartX (TYPE = QUANTITATIVE, SCALE = distances), X_END = EndX (TYPE = QUANTITATIVE, SCALE = distances), Y_START = LowerBound (TYPE = QUANTITATIVE, SCALE = estimates), Y_END = UpperBound (TYPE = QUANTITATIVE, SCALE = estimates)),
        STYLE (LABEL = '<target>', COLOR = '#112233')
      )
    )
  )
);
```

## Stacked radial bars

POLAR ARC layers accept categorical THETA and a quantitative RADIUS with STACK = ZERO or
STACK = NORMALIZE. Categories occupy equal angular sectors. Repeated category rows are retained;
each series contributes a radial interval within that sector. COLOR may be nominal/ordinal,
or layers may use a literal STYLE color. All layers must use the same stack mode.

ZERO accumulates non-negative values from zero and uses the largest category total as the outer
radius. NORMALIZE divides each contribution by its category total, filling each nonzero category
to one. Radial length represents the value; sector area is not proportional to the value.
Null coordinates are gaps, zero values retain accessible values without visible segments, and
negative values fail. Stacking follows layer order, then resolved series order (labels sorted without case), then source rows.
The default category order is first occurrence; THETA scale ordering and reversal are honored.

INNER_RADIUS reserves a central hole. START_ANGLE/END_ANGLE select a positive clockwise sweep
of at most 360 degrees. Radius scales must be linear with automatic zero-based bounds and no
reversal. IDENTITY placement is required; facets and row-level conditions are not supported.
Titles, terminal rows and accessible fallback include raw values and cumulative radial intervals.
Existing unstacked polar charts retain their pie/donut behavior.

```sql
CREATE VISUAL Totals AS CUSTOM (SOURCE = #prepared, CHART (
  COORDINATE (TYPE = POLAR, INNER_RADIUS = 0.2),
  LAYERS (rings = ARC (ENCODINGS (
    THETA = Category (TYPE = NOMINAL),
    RADIUS = Amount (TYPE = QUANTITATIVE, STACK = NORMALIZE),
    COLOR = Series (TYPE = NOMINAL)
  )))
));
```

## Lines on transposed fixed-aspect charts

LINE supports exactly quantitative field or DATUM X/Y bindings with IDENTITY placement,
explicit NULL_HANDLING = GAP and explicit STYLE (INTERPOLATION = 'LINEAR'). Extra encodings,
row-level conditions, nudges, jitter, stacking and secondary axes are not supported for this form.
Use a literal STYLE color for each line; unrelated POINT color groups do not split it.

Vertices follow source row order within each facet, including descending or repeated X values.
A missing X or Y breaks the path. Coincident rows remain distinct. Semantic Y maps horizontally
and X vertically through linear/logarithmic and reversed scales. Facets and resizing retain the
physical aspect ratio. Symbols and optional data labels report semantic Y. Terminal output and
accessible fallback preserve semantic values; they do not reproduce physical distances.

```sql
CREATE VISUAL Route AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
    LAYERS (route = LINE (
      INHERIT_ENCODINGS = OFF,
      NULL_HANDLING = GAP,
      ENCODINGS (
        X = Distance (TYPE = QUANTITATIVE),
        Y = Estimate (TYPE = QUANTITATIVE)
      ),
      STYLE (INTERPOLATION = 'LINEAR', COLOR = '#112233')
    ))
  )
);
```

## Rectangles on transposed fixed-aspect charts

RECT supports exactly four quantitative field or DATUM bindings: `X_START`, `X_END`,
`Y_START` and `Y_END`. Use IDENTITY, JITTER or `NUDGE(..., UNIT = EM|BAND|DATA)` placement without conditions, stacking, secondary axes
or additional encodings. Each row defines a rectangle between its paired corners. Semantic Y
maps horizontally and X vertically, including reversed/logarithmic scales, independent facets
and resizing. Every endpoint contributes to its scale domain.

The rectangle uses the exact mapped width and height. Minimum bar dimensions do not apply:
subpixel ranges retain their size, and a zero-width or zero-height interval remains a zero-area
rectangle with accessible values. Descending endpoints draw the same extent but retain their
original order in SVG titles, optional data labels, terminal output and accessible fallback.
A missing endpoint draws no rectangle and is reported as a gap. Coincident rows remain distinct.
These isolated rectangles do not inherit color groups from another layer. Use STYLE for a fixed color.

EM nudges translate the entire rectangle: positive X moves upward and positive Y moves right,
with one em equal to 12 SVG units. Reversed axes do not reverse this display displacement.
Dimensions, domains and reported endpoint values stay unchanged through facets and resizing.
For example, add `POSITION = NUDGE(X = -0.5, Y = 1, UNIT = EM)` to move down 6 units
and right 12 units.

BAND nudges move the rectangle by fractions of the fitted plot area before legend layout.
Positive X moves up by its fraction of plot height; positive Y moves right by its fraction of
plot width. Each continuous axis has one band. The displacement scales with resizing and
each facet viewport; dimensions, domains and reported values remain unchanged.
DATA nudges use the authored `X_START`/`Y_START` corner as the anchor, even for descending
intervals. The anchor moves by the given amounts through the original scales, and that physical
displacement translates the whole rectangle. Width and height stay unchanged, including on
logarithmic axes. Reversed scales reverse the displacement; facets use their own scales and
plot area after legend layout. Raw intervals and domains stay unchanged. A missing endpoint
skips placement and remains a gap. Logarithmic anchor targets must stay positive.
JITTER moves each whole rectangle by a deterministic offset using its stable KEY and SEED.
X and Y amplitudes must be between zero and one; they bound fractions of fitted plot height
and width respectively, before legend layout. Reversed axes do not reverse these display offsets.
Row reorder and layer rename preserve the offsets; a new seed changes them. Resizing scales
the offsets with the viewport. Keys must exist, be non-null and be unique across all source rows,
including gaps. Coincident rectangles remain distinct. Dimensions, domains and reported
intervals stay unchanged.

```sql
CREATE VISUAL Regions AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
    LAYERS (regions = RECT (
      INHERIT_ENCODINGS = OFF,
      POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Distance, SEED = 42),
      ENCODINGS (
        X_START = StartX (TYPE = QUANTITATIVE),
        X_END = EndX (TYPE = QUANTITATIVE),
        Y_START = LowerBound (TYPE = QUANTITATIVE),
        Y_END = UpperBound (TYPE = QUANTITATIVE)
      ),
      STYLE (COLOR = '#112233')
    ))
  )
);
```

## Fixed physical units on transposed points

```sql
CREATE VISUAL PhysicalScatter AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
    SCALES (
      horizontal = LINEAR (CHANNEL = X, MIN = 0, MAX = 10),
      vertical = LINEAR (CHANNEL = Y, MIN = 0, MAX = 20)
    ),
    LAYERS (observations = POINT (
      POSITION = NUDGE(X = 1, Y = -0.5, UNIT = EM),
      ENCODINGS (
        X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
        Y = Elevation (TYPE = QUANTITATIVE, SCALE = vertical),
        ERROR_LOW = LowerBound (TYPE = QUANTITATIVE, SCALE = vertical),
        ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE, SCALE = vertical)
      )
    ), labels = TEXT (
      POSITION = NUDGE(X = 1, Y = -0.5, UNIT = EM),
      ENCODINGS (
        X = Distance (TYPE = QUANTITATIVE, SCALE = horizontal),
        Y = Elevation (TYPE = QUANTITATIVE, SCALE = vertical),
        TEXT = Caption (TYPE = NOMINAL)
      )
    ))
  )
);
```

- **Source** — `#prepared` supplies quantitative `Distance`, `Elevation`, `LowerBound`, and `UpperBound` columns, plus nominal `Caption` labels.
- **ASPECT_RATIO = 2** — One Elevation unit spans twice the physical distance of one Distance unit, including after transposition.
- **NUDGE** — Moves each point and its interval 12 pixels up and 6 pixels left without changing the reported values. Omit `POSITION` for no displacement.

## Grouped displacement on transposed points

```sql
CREATE VISUAL GroupedMeasurements AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
    SCALES (cohorts = BAND (CHANNEL = X_OFFSET, ORDER = ('control', 'treated'))),
    LAYERS (observations = POINT (
      ENCODINGS (
        X = Distance (TYPE = QUANTITATIVE),
        Y = Elevation (TYPE = QUANTITATIVE),
        X_OFFSET = Cohort (TYPE = NOMINAL, SCALE = cohorts)
      )
    ))
  )
);
```

- **Source** — `#prepared` supplies quantitative `Distance` and `Elevation`, plus nominal `Cohort`.
- **X_OFFSET** — With two groups, control moves down by one-quarter of fitted plot height and treated moves up by one-quarter. Raw measurements remain unchanged.

## Data-unit displacement on transposed points

```sql
CREATE VISUAL AdjustedLabels AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
    LAYERS (labels = TEXT (
      POSITION = NUDGE(X = 0.5, Y = -0.5, UNIT = DATA),
      ENCODINGS (
        X = Distance (TYPE = QUANTITATIVE),
        Y = Elevation (TYPE = QUANTITATIVE),
        TEXT = Caption (TYPE = NOMINAL)
      )
    ))
  )
);
```

- **Source** — `#prepared` supplies non-null quantitative `Distance` and `Elevation`, plus nominal `Caption`.
- **NUDGE** — Places label anchors at the display position of Distance + 0.5 and Elevation - 0.5, keeping the reported measurements intact.

## Relative displacement on transposed points

```sql
CREATE VISUAL ShiftedMeasurements AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
    LAYERS (observations = POINT (
      POSITION = NUDGE(X = 0.02, Y = -0.03, UNIT = BAND),
      ENCODINGS (
        X = Distance (TYPE = QUANTITATIVE),
        Y = Elevation (TYPE = QUANTITATIVE)
      )
    ))
  )
);
```

- **Source** — `#prepared` supplies quantitative `Distance` and `Elevation` columns.
- **NUDGE** — Moves observations up by 2% of fitted plot height and left by 3% of its width. Terminal and accessible values retain the original measurements.

## Deterministic jitter on transposed points

```sql
CREATE VISUAL RepeatedMeasurements AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = TRANSPOSED_CARTESIAN, ASPECT_RATIO = 2),
    LAYERS (observations = POINT (
      POSITION = JITTER(X = 0.02, Y = 0.03, KEY = Id, SEED = 42),
      ENCODINGS (
        X = Distance (TYPE = QUANTITATIVE),
        Y = Elevation (TYPE = QUANTITATIVE)
      )
    ))
  )
);
```

- **Source** — `#prepared` supplies quantitative `Distance` and `Elevation`, plus a unique, non-null `Id`.
- **JITTER** — Spreads observations by up to 2% of the fitted plot height and 3% of its width without changing the underlying measurements. Reordering source rows preserves offsets by key.

## Examples

```sql
SELECT Month, Revenue, MarginPct, Region, FiscalYear
INTO #chart_data
FROM #monthly_metrics;

CREATE VISUAL RevenueAndMargin AS CUSTOM (
  SOURCE = #chart_data,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
      months = BAND (CHANNEL = X, ORDER = SOURCE),
      revenue = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON),
      margin = LINEAR (CHANNEL = Y2, INCLUDE_ZERO = OFF)
    ),
    LAYERS (
      bars = RECT (
        Z_INDEX = 0,
        ENCODINGS (
          X = Month (TYPE = ORDINAL, SCALE = months),
          Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue, AXIS = PRIMARY)
        ),
        CONDITIONS (COLOR WHEN Revenue < 0 THEN '#b91c1c' ELSE '#2563eb')
      ),
      margin_line = LINE (
        Z_INDEX = 1,
        ENCODINGS (
          X = Month (TYPE = ORDINAL, SCALE = months),
          Y2 = MarginPct (TYPE = QUANTITATIVE, SCALE = margin, AXIS = SECONDARY)
        )
      )
    ),
    FACET (ROW = Region, COLUMN = FiscalYear),
    RESOLVE (X = SHARED, Y = INDEPENDENT, COLOR = SHARED)
  )
);
```

```sql
CREATE VISUAL PriceAndVolume AS CUSTOM (
  SOURCE = #daily_market,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
      days = BAND (CHANNEL = X, ORDER = SOURCE),
      price = LINEAR (CHANNEL = Y, INCLUDE_ZERO = OFF),
      volume = LINEAR (CHANNEL = Y2, INCLUDE_ZERO = ON)
    ),
    LAYERS (
      volume_bars = RECT (
        Z_INDEX = 0,
        BAND_SIZE = 0.35,
        ENCODINGS (
          X = TradingDay (TYPE = ORDINAL, SCALE = days),
          Y2 = Volume (TYPE = QUANTITATIVE, SCALE = volume, AXIS = SECONDARY)
        )
      ),
      candles = RECT (
        Z_INDEX = 1,
        ENCODINGS (
          X = TradingDay (TYPE = ORDINAL, SCALE = days),
          OPEN = OpenPrice (TYPE = QUANTITATIVE, SCALE = price),
          CLOSE = ClosePrice (TYPE = QUANTITATIVE, SCALE = price),
          LOW = LowPrice (TYPE = QUANTITATIVE, SCALE = price),
          HIGH = HighPrice (TYPE = QUANTITATIVE, SCALE = price)
        )
      )
    )
  )
);
```

```sql
CREATE VISUAL ServiceMap AS CUSTOM (
  SOURCE = #service_locations,
  CHART (
    COORDINATE (TYPE = GEOGRAPHIC, PROJECTION = EQUIRECTANGULAR,
      MAP_NAME = 'WORLD', FEATURE_KEY = 'name'),
    LAYERS (
      regions = RECT (ENCODINGS (
        REGION = Country (TYPE = NOMINAL), COLOR = Orders (TYPE = QUANTITATIVE)
      )),
      routes = LINE (Z_INDEX = 1, ENCODINGS (
        LONGITUDE = Longitude (TYPE = QUANTITATIVE), LATITUDE = Latitude (TYPE = QUANTITATIVE),
        ROUTE = RouteId (TYPE = NOMINAL)
      )),
      locations = POINT (Z_INDEX = 2, ENCODINGS (
        LONGITUDE = Longitude (TYPE = QUANTITATIVE), LATITUDE = Latitude (TYPE = QUANTITATIVE),
        TEXT = Location (TYPE = NOMINAL)
      ))
    )
  )
);
```

```sql
CREATE VISUAL TrialIntervals AS CUSTOM (
  SOURCE = #trials,
  TITLE = 'Trial Estimates with Error Bars',
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    LAYERS (
      points = POINT (
        STYLE (ERROR_BAR_STYLE = 'CAPS'),
        ENCODINGS (
          X = Trial (TYPE = NOMINAL),
          Y = Estimate (TYPE = QUANTITATIVE),
          SHAPE = VALUE('DIAMOND') (TYPE = NOMINAL),
          ERROR_LOW = LowerBound (TYPE = QUANTITATIVE),
          ERROR_HIGH = UpperBound (TYPE = QUANTITATIVE)
        ),
        STYLE (SYMBOL_STROKE_COLOR = '#1e3a8a', SYMBOL_STROKE_WIDTH = 1.5)
      )
    )
  )
);
```

```sql
CREATE VISUAL TrendWithAnnotations AS CUSTOM (
  SOURCE = #sensor_metrics,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    LAYERS (
      sensor_line = LINE (
        Z_INDEX = 0,
        NULL_HANDLING = GAP,
        ENCODINGS (
          X = Timestamp (TYPE = TEMPORAL),
          Y = Temperature (TYPE = QUANTITATIVE)
        )
      )
    ),
    ANNOTATIONS (
      POINT (
        SERIES = 'sensor_line',
        TYPE = MAX,
        LABEL = 'Peak Temperature',
        SYMBOL = 'pin'
      ),
      POINT (
        SERIES = 'sensor_line',
        TYPE = COORD(10, 85.5),
        LABEL = 'Calibration Offset',
        SYMBOL = 'circle'
      )
    )
  )
);
```

## References

- [Report-SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Native Advanced Chart Authoring Decision](../../../architecture/decisions/native-advanced-chart-authoring.md)
- [Script Composition Standards](../../../architecture/standards/script-composition-standards.md)
- [Statistical and Financial CUSTOM Sample](../../../../samples/08_Reporting/custom_statistical_financial_layers.rptsql)
- [Error Bars Sample](../../../../samples/08_Reporting/error_bars_statistical_intervals.rptsql)
