# ggplot2 to ETL-SQL Concepts

[« Back to Report-SQL Guides](README.md)

ETL-SQL and ggplot2 both separate data bindings from marks, but ETL-SQL is a script-first orchestration language rather than an in-process statistical runtime. Prepare data in SQL, then lower visual semantics into versioned `ChartSpec` and deterministic `PlotPlan` contracts shared by browser, SVG, terminal, PDF, email, accessibility, and snapshot replay.

## Concept Map

| ggplot2 concept | ETL-SQL concept |
| :--- | :--- |
| global `aes(...)` | Top-level `ENCODINGS (...)` |
| layer-local `aes(...)` override | Layer `ENCODINGS (...)` binding for the same channel |
| `inherit.aes = FALSE` | `INHERIT_ENCODINGS = OFF` |
| `geom_*` | Typed layer mark such as `RECT`, `LINE`, `AREA`, `RULE`, `POINT`, `TEXT`, or `TICK` |
| `position_identity()` | `POSITION = IDENTITY` |
| `position_jitter()` | `POSITION = JITTER(X = ..., Y = ..., KEY = StableId, SEED = 42)` |
| nudge arguments | `POSITION = NUDGE(X = ..., Y = ..., UNIT = DATA | BAND | EM)` |
| dodge | Nominal/ordinal `X_OFFSET` or `Y_OFFSET` encoding |
| stack / fill | `STACK = ZERO` / `STACK = NORMALIZE` |
| `facet_grid()` | `FACET (ROW = ..., COLUMN = ...)` |
| `facet_wrap()` | `FACET (WRAP = ..., COLUMNS = n)` |
| `coord_fixed()` | Cartesian `ASPECT_RATIO = n` |
| continuous/diverging scale | Quantitative COLOR scale with `RANGE = GRADIENT(...)` or `DIVERGING(...)` |

Global bindings are expanded into each effective layer during lowering. The serialized chart contract therefore contains no implicit inheritance. Layer overrides replace the entire binding for that channel, and isolated layers retain nothing from global scope.

## Intervals and Reference Geometry

Use `AREA` with `Y_START`/`Y_END` for a precomputed ribbon, `RECT` with `Y_START`/`Y_END` for a `geom_rect`-style band or floating bar (and `X_START`/`X_END` for precomputed histogram bins), and `RULE` for plot-spanning thresholds or explicit ranged segments. Use `TICK` for a short category-local observation or target. Confidence intervals, forecast quantiles, error bounds, smoothing, interpolation, and model estimates must already be columns produced by SQL.

```sql
SELECT Period,
       MIN(Forecast) AS Lower,
       MAX(Forecast) AS Upper
INTO #forecast_bounds
FROM #forecast_samples
GROUP BY Period;

CREATE VISUAL ForecastBand AS CUSTOM (
  SOURCE = #forecast_bounds,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    ENCODINGS (X = Period (TYPE = ORDINAL)),
    LAYERS (band = AREA (ENCODINGS (
      Y_START = Lower (TYPE = QUANTITATIVE),
      Y_END = Upper (TYPE = QUANTITATIVE)
    )))
  )
);
```

## Determinism and Portability

Jitter requires a unique, non-null stable key and uses a documented deterministic hash; it never calls a random generator or changes raw values. Nudge uses data, band-relative, or em-relative units—not device pixels. Facet order and bounds, scale inference, stack endpoints, offset slots, continuous colors, and fixed-aspect viewports are resolved once in `PlotPlan`, so backends do not reinterpret layer names or inspect data to invent geometry.

## `stat_*` Remains SQL

There is no hidden equivalent of `stat_bin`, `stat_smooth`, `stat_density`, `stat_summary`, or `after_stat`. Express those calculations in a visible query or `#temp` stage. This restores the transformation step to the pipeline: calculations are diffable, testable, governed, and recorded by lineage instead of being embedded in an opaque chart object.

## References

- [Report-SQL Guide](../feature-guides/report-sql.md)
- [Native Grammar ADR](../../architecture/decisions/GrammarOfGraphicsSpecIR.md)
- [Declarative Geometry Sample](../../../samples/08_Reporting/declarative_geometry_refinements.rptsql)
