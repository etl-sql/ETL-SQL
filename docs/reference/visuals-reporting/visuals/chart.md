# CHART

Defines renderer-neutral native mark layers, encodings, scales, coordinates, conditional presentation, and facet composition for a `CUSTOM` Report-SQL visual. Data preparation remains visible in ETL-SQL statements and `#temp` tables; `CHART` does not accept embedded Vega-Lite or hidden transforms.

```sql
CREATE VISUAL name AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = CARTESIAN, ASPECT_RATIO = positive_number),
    [SCALES (
      scale_name = LINEAR (
        CHANNEL = Y,
        INCLUDE_ZERO = ON,
        MIN = literal,
        MAX = literal,
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
        BAND_SIZE = fraction,
        THICKNESS = fraction,
        ORIENTATION = AUTO | HORIZONTAL | VERTICAL,
        POSITION = IDENTITY | JITTER(X = fraction, Y = fraction, KEY = field, SEED = integer) |
                   NUDGE(X = number, Y = number, UNIT = DATA | BAND | EM),
        ENCODINGS (
          X | X2 | X_START | X_END | X_OFFSET | Y | Y2 | Y_START | Y_END | Y_OFFSET |
          COLOR | SIZE | SHAPE | THETA | RADIUS | TEXT | TOOLTIP | DETAIL = field | DATUM(scalar) | VALUE(scalar) (
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
    FACET (ROW = field, COLUMN = field) | FACET (WRAP = field, COLUMNS = 3),
    RESOLVE (X = SHARED | INDEPENDENT, Y = SHARED | INDEPENDENT, COLOR = SHARED | INDEPENDENT)
  )
);
```

- **COORDINATE** — Selects `CARTESIAN`, `TRANSPOSED_CARTESIAN`, or `POLAR`; polar coordinates may declare angles/radius. `ASPECT_RATIO` is the physical Y-unit/X-unit ratio and currently requires quantitative primary Cartesian X/Y scales.
- **SCALES** — Optionally declares named `LINEAR`, `LOGARITHMIC`, `TIME`, `BAND`, `POINT`, `ORDINAL`, or `IDENTITY` scales. Encoding `SCALE` references must name a declared scale; omission requests deterministic inference from the required `TYPE`, channel, mark, and coordinate.
- **RANGE** — Adds a dependency-free sRGB sequential or diverging output range to a quantitative `COLOR` scale. Colors use portable `#RRGGBB`; values clamp at the domain, nulls use `NULL_COLOR`, and a diverging midpoint must lie inside the resolved domain.
- **LAYERS** — Declares marks in deterministic `Z_INDEX` order. A layer consumes the visual's single `SOURCE`; stage differently prepared inputs into one visible `#temp` table before authoring the visual.
- **ENCODINGS** — At `CHART` scope, declares bindings inherited by layers. At layer scope, overrides individual channels. A layer defaults to `INHERIT_ENCODINGS = ON`; `OFF` makes its bindings isolated. Duplicate channels within either scope are errors.
- **Binding sources** — A bare field reads a source column; `DATUM(literal-or-parameter)` supplies a typed data-domain constant that may use a scale; `VALUE(literal-or-parameter)` supplies a visual-range value and cannot use a scale or positional channel. Expressions, functions, aggregates, column references inside wrappers, null positional constants, and secret parameters are rejected.
- **STYLE** — Applies renderer-neutral literal style tokens to one layer.
- **CONDITIONS** — Applies presentation-only values per row. Predicates accept fields, report parameters, literals, comparisons, `AND`, `OR`, `NOT`, and `IS [NOT] NULL`. Connected `LINE` and `AREA` marks reject row-level conditions; use separate staged series or layers.
- **Placement** — `STACK` accumulates quantitative Y/Y2 values for Cartesian and transposed Cartesian layouts; polar/radial stacking is rejected until it has portable geometry. Offset channels dodge categories, `BAND_SIZE` controls relative thickness, and `Z_INDEX` controls paint order. `JITTER` uses a stable key and deterministic hash; `NUDGE` is resolved after domains without changing raw values.
- **Intervals** — Paired `Y_START`/`Y_END` creates an AREA ribbon, a vertical RULE span, or a ranged RECT such as a qualitative band or a floating variance bar; `X_START`/`X_END` supplies the symmetric horizontal range, which on a RECT with a continuous X scale is an explicit-bin histogram. Both endpoints are required, must share a quantitative or temporal `TYPE`, and both take part in scale-domain resolution. A ranged RECT owns its extent on that axis, so it rejects `Y`/`Y2` alongside `Y_START`/`Y_END` and `X`/`X2` alongside `X_START`/`X_END`; `STACK` computes its own endpoints and is unaffected. Endpoint calculations stay in SQL.
- **TICK** — Draws a short category-local quantitative observation or target. It requires nominal/ordinal X and quantitative Y. `ORIENTATION = AUTO` resolves to a horizontal segment across the category band; `HORIZONTAL` and `VERTICAL` make that choice explicit. TICK is distinct from plot-spanning/ranged `RULE`; its `BAND_SIZE` is relative to the category band and `THICKNESS` is bounded to `(0, 1]` em.
- **FACET** — Creates a row/column grid or a mutually exclusive one-dimensional `WRAP`. Wrap uses stable first-seen row-major ordering, 1–12 columns, at most 100 panels, render-work limits, and minimum panel dimensions.
- **RESOLVE** — Selects shared or per-panel X, Y/Y2, and color scales. Independent resolution requires `FACET`.
- **Visible transformations** — Aggregation, filtering, calculation, lookup, windowing, and statistical preparation belong in preceding ETL-SQL/`#temp` statements, not in `CHART`.

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

## References

- [Report-SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Native Advanced Chart Authoring Decision](../../../architecture/decisions/native-advanced-chart-authoring.md)
- [Script Composition Standards](../../../architecture/standards/script-composition-standards.md)
