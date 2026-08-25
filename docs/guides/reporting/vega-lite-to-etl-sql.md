# Vega-Lite to ETL-SQL Conversion

[« Back to Report-SQL Guides](README.md)

ETL-SQL provides a native declarative chart grammar, but it does not execute Vega-Lite JSON or its transformation pipeline. Convert data transformations to visible SQL or `#temp` staging first, then express only visual meaning in `CHART`. This keeps the report script diffable, reviewable, portable across renderers, and inside ETL-SQL's lineage and zero-trust boundaries.

## Encoding Sources

| Vega-Lite concept | ETL-SQL authoring |
| :--- | :--- |
| `field` | Bare field binding: `X = Revenue (TYPE = QUANTITATIVE)` |
| `datum` | Scaled data-domain constant: `Y = DATUM(@Target) (TYPE = QUANTITATIVE)` |
| `value` | Unscaled visual-range constant: `COLOR = VALUE('#c62828') (TYPE = NOMINAL)` |
| shared encoding | Top-level `ENCODINGS (...)`; layers inherit by default |
| layer override | Repeat the channel in the layer `ENCODINGS (...)` block |
| no inheritance | `INHERIT_ENCODINGS = OFF` on the layer |

Only scalar literals and declared, non-secret parameters are valid inside `DATUM` and `VALUE`. Calculations, aggregates, and function calls belong in SQL.

## Geometry and Position

| Vega-Lite concept | ETL-SQL authoring |
| :--- | :--- |
| `stack: null` / zero / normalize | `STACK = NONE | ZERO | NORMALIZE` on a quantitative Y/Y2 encoding |
| `xOffset`, `yOffset` | `X_OFFSET` or `Y_OFFSET` with a nominal/ordinal field |
| `bandSize`-like relative width | Layer `BAND_SIZE = 0.65` |
| tick mark | `TICK`, with `BAND_SIZE`, `THICKNESS`, and `ORIENTATION` |
| ranged area | `AREA` with paired `Y_START` and `Y_END` |
| ranged bar / `x`,`x2` bin rect | `RECT` with paired `Y_START`/`Y_END` or `X_START`/`X_END` |
| ranged rule | `RULE` with paired start/end or X/X2 and Y/Y2 channels |
| jitter / nudge | Layer `POSITION = JITTER(...)` or `POSITION = NUDGE(...)` |

Offsets express dodging; stack expresses accumulation; `BAND_SIZE` expresses relative thickness; `Z_INDEX` expresses paint order. ETL-SQL rejects ambiguous combinations rather than choosing a renderer-specific heuristic. Polar stacking is deliberately rejected until radial endpoints have one portable meaning across all output surfaces.

## Layers, Conditions, and Composition

Map a Vega-Lite `layer` array to the ordered entries in `LAYERS (...)`. Put common channels in the chart-level `ENCODINGS (...)` block and override only the channels that differ on a layer. A Vega-Lite conditional encoding becomes a typed layer `CONDITIONS (...)` clause; calculations used by the condition remain visible SQL columns.

```sql
SELECT Month, Revenue, MarginPct,
       CASE WHEN MarginPct < 0 THEN 1 ELSE 0 END AS IsNegative
INTO #performance
FROM finance.MonthlyPerformance;

CREATE VISUAL Performance AS CUSTOM (
  SOURCE = #performance,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    ENCODINGS (X = Month (TYPE = ORDINAL)),
    LAYERS (
      revenue = RECT (ENCODINGS (Y = Revenue (TYPE = QUANTITATIVE))),
      margin = POINT (
        ENCODINGS (Y2 = MarginPct (TYPE = QUANTITATIVE, AXIS = SECONDARY)),
        CONDITIONS (COLOR WHEN IsNegative = 1 THEN '#c62828' ELSE '#2563eb')
      )
    )
  )
);
```

Vega-Lite `concat`, `hconcat`, and `vconcat` map to Report-SQL pages and containers with explicit `STRUCTURE` and `MAP` slots. There is no implicit nested visualization tree.

## Scales, Color, Facets, and Aspect

Encoding `TYPE` is mandatory. Ordinary scales may be omitted: ETL-SQL deterministically infers linear scales for quantitative positions, time scales for temporal positions, band scales for categorical RECT/TICK positions, point scales for categorical POINT/LINE positions, ordinal scales for categorical color/shape, and linear scales for quantitative size. Declare `SCALES (...)` for log/identity behavior, explicit domains or order, named sharing, or continuous color ranges.

```sql
SCALES (
  variance = LINEAR (
    CHANNEL = COLOR,
    RANGE = DIVERGING(
      LOW = '#2166ac', MID = '#f7f7f7', HIGH = '#b2182b', MIDPOINT = 0
    )
  )
)
```

Use `FACET (WRAP = Region, COLUMNS = 3)` for one-dimensional wrapping and ROW/COLUMN for a grid. `COORDINATE (TYPE = CARTESIAN, ASPECT_RATIO = 1)` fixes the physical Y-unit/X-unit ratio when both primary axes are continuous.

Vega-Lite `repeat` has no hidden chart expander. For a single field, use `FACET (WRAP = field, COLUMNS = n)`. For different measures or structurally different charts, declare reviewable visuals explicitly and compose them on a page. Map `resolve.scale.x`, `resolve.scale.y`, and `resolve.scale.color` to `RESOLVE (X = SHARED | INDEPENDENT, Y = SHARED | INDEPENDENT, COLOR = SHARED | INDEPENDENT)` inside a faceted chart.

## Selections and Parameters

Vega-Lite selections map to Report-SQL interaction contracts, not to an embedded selection object:

- Point or interval emphasis maps to `INTERACTIONS (ON_SELECT = HIGHLIGHT)`.
- Cross-filtering maps to `INTERACTIONS (ON_SELECT = FILTER)`.
- A mark click that sets report state maps to `ACTIONS (ON_CLICK = SET_PARAMETER(@name, column))`.
- User controls use `SLICER`, `MULTISELECT`, `SLIDER`, or another input visual with `SET_PARAMETER`.
- Constant visual references use declared parameters in `DATUM(@name)` or `VALUE(@name)`; secret parameters are rejected.

Parameters are report state and can trigger normal Report-SQL refresh behavior. They do not authorize arbitrary expressions inside encodings.

## Keep Transformations in SQL

Map Vega-Lite `calculate`, `aggregate`, `joinaggregate`, `lookup`, `window`, `filter`, `fold`, `flatten`, `bin`, density, regression, and other transform/statistical operations to a visible `SELECT ... INTO #prepared` stage. In particular, use a normal `JOIN` for `lookup`, a window function with an explicit `OVER (...)` clause for `window`/`joinaggregate`, and `WHERE` for `filter`. The chart reads the resulting columns; it never hides row generation or statistical computation.

```sql
SELECT Region,
       SUM(Revenue) AS Revenue,
       SUM(Cost) AS Cost,
       SUM(Revenue) - SUM(Cost) AS Variance
INTO #prepared
FROM sales.Orders
GROUP BY Region;

CREATE VISUAL Variance AS CUSTOM (
  SOURCE = #prepared,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    LAYERS (bars = RECT (ENCODINGS (
      X = Region (TYPE = NOMINAL),
      Y = Variance (TYPE = QUANTITATIVE)
    )))
  )
);
```

## Themes and Accessibility

Map Vega-Lite `config` and named theme values to Report-SQL `CREATE THEME`, `CREATE STYLE`, and page/visual `STYLE` clauses. Keep data-dependent color decisions in encodings or conditions; keep reusable typography, palette, background, and spacing in themes/styles.

Give every production visual a meaningful `TITLE`, bind useful `TOOLTIP`/`DETAIL` fields, and preserve the report description. ETL-SQL derives the accessible summary and terminal/Markdown/plain-text fallback from the same resolved `PlotPlan`; do not encode essential meaning only in color or hover. Interactive-only behavior should be described as enhancement, never as the sole way to read a value.

## Conversion Checklist

1. Move every transform, lookup, window, aggregate, and filter into visible SQL stages.
2. Declare parameters and controls, then map selection behavior to `INTERACTIONS` or `ACTIONS`.
3. Translate layers, bindings, scales, conditions, and facets into one native `CHART` contract.
4. Replace `repeat`/concatenation with a facet or explicit page/container composition.
5. Apply themes/styles and add titles, tooltips, and non-color meaning.
6. Verify browser SVG, terminal, PDF/email, and Markdown/plain-text behavior from the same script.

## References

- [Report-SQL Guide](../feature-guides/report-sql.md)
- [Native Grammar ADR](../../architecture/decisions/grammar-of-graphics-spec-ir.md)
- [Declarative Geometry Sample](../../../samples/08_Reporting/declarative_geometry_refinements.rptsql)
