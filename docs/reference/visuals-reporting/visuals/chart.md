# CHART

Defines renderer-neutral native mark layers, encodings, scales, coordinates, conditional presentation, and facet composition for a `CUSTOM` Report-SQL visual. Data preparation remains visible in ETL-SQL statements and `#temp` tables; `CHART` does not accept embedded Vega-Lite or hidden transforms.

```sql
CREATE VISUAL name AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
      scale_name = LINEAR (
        CHANNEL = Y,
        INCLUDE_ZERO = ON,
        MIN = literal,
        MAX = literal,
        ORDER = SOURCE | ASCENDING | DESCENDING | (literal, ...)
      )
    ),
    LAYERS (
      layer_name = RECT | LINE | AREA | POINT | RULE | ARC | TEXT (
        Z_INDEX = number,
        ENCODINGS (
          X | Y | Y2 | COLOR | SIZE | SHAPE | THETA | RADIUS | TEXT | TOOLTIP | DETAIL = field (
            TYPE = QUANTITATIVE | TEMPORAL | NOMINAL | ORDINAL,
            SCALE = scale_name,
            AXIS = NONE | PRIMARY | SECONDARY,
            SORT = SOURCE | ASCENDING | DESCENDING,
            FORMAT = 'format'
          )
        ),
        STYLE (property = literal, ...),
        CONDITIONS (
          COLOR | OPACITY | SIZE | SHAPE | TEXT WHEN predicate THEN literal [ELSE literal]
        )
      )
    ),
    FACET (ROW = field, COLUMN = field),
    RESOLVE (X = SHARED | INDEPENDENT, Y = SHARED | INDEPENDENT, COLOR = SHARED | INDEPENDENT)
  )
);
```

- **COORDINATE** — Selects `CARTESIAN`, `TRANSPOSED_CARTESIAN`, or `POLAR`; polar coordinates may also declare `START_ANGLE`, `END_ANGLE`, and `INNER_RADIUS`.
- **SCALES** — Declares named `LINEAR`, `LOGARITHMIC`, `TIME`, `BAND`, `POINT`, `ORDINAL`, or `IDENTITY` scales. Encoding `SCALE` references must name a declared scale.
- **LAYERS** — Declares marks in deterministic `Z_INDEX` order. A layer consumes the visual's single `SOURCE`; stage differently prepared inputs into one visible `#temp` table before authoring the visual.
- **ENCODINGS** — Binds simple source fields to visual channels. Use `Y2` with `AXIS = SECONDARY` for a second vertical axis.
- **STYLE** — Applies renderer-neutral literal style tokens to one layer.
- **CONDITIONS** — Applies presentation-only values per row. Predicates accept fields, report parameters, literals, comparisons, `AND`, `OR`, `NOT`, and `IS [NOT] NULL`. Connected `LINE` and `AREA` marks reject row-level conditions; use separate staged series or layers.
- **FACET** — Creates a row facet, column facet, or a two-dimensional row/column grid in first-seen data order.
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
- [Native Advanced Chart Authoring Decision](../../../architecture/decisions/NativeAdvancedChartAuthoring.md)
- [Script Composition Standards](../../../architecture/standards/Script_Composition_Standards.md)
