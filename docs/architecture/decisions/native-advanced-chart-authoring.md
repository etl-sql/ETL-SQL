# Native Advanced Chart Authoring

**Status:** Accepted
**Date:** 2026-08-21
**Decision scope:** Phase 7 Report-SQL grammar and its lowering into the native Grammar-of-Graphics
contracts.

## 1. Decision

ETL-SQL will expose advanced, renderer-neutral chart authoring through `CREATE VISUAL ... AS CUSTOM`
and one nested `CHART` clause. The grammar describes mark layers, field encodings, scales,
coordinates, presentation conditions, facets, and scale resolution. It does not embed Vega-Lite JSON,
accept renderer option objects, or introduce a second transformation language.

The authoring path is:

```text
CUSTOM visual AST -> ChartSpec -> PlotPlan -> native SVG / terminal and semantic fallback
```

Named visual types remain the preferred concise syntax. `CUSTOM` is for compositions that cannot be
expressed clearly as one named preset.

## 2. Complete grammar

```sql
CREATE VISUAL visual_name AS CUSTOM (
  TITLE = 'Optional title',
  SOURCE = #prepared_rows,
  CHART (
    COORDINATE (
      TYPE = CARTESIAN | TRANSPOSED_CARTESIAN | POLAR | GEOGRAPHIC,
      START_ANGLE = number,
      END_ANGLE = number,
      INNER_RADIUS = number,
      PROJECTION = EQUIRECTANGULAR | MERCATOR,
      MAP_NAME = string,
      MAP_FILE = string,
      FEATURE_KEY = string
    ),
    SCALES (
      scale_name = LINEAR | LOGARITHMIC | TIME | BAND | POINT | ORDINAL | IDENTITY (
        CHANNEL = X | Y | Y2 | COLOR | THETA | RADIUS,
        INCLUDE_ZERO = ON | OFF,
        MIN = literal,
        MAX = literal,
        ORDER = SOURCE | ASCENDING | DESCENDING | (literal, ...)
      ), ...
    ),
    LAYERS (
      layer_name = RECT | LINE | AREA | POINT | RULE | ARC | TEXT (
        Z_INDEX = integer,
        ENCODINGS (
          channel = source_column (
            TYPE = QUANTITATIVE | TEMPORAL | NOMINAL | ORDINAL,
            SCALE = scale_name,
            AXIS = NONE | PRIMARY | SECONDARY,
            SORT = SOURCE | ASCENDING | DESCENDING,
            FORMAT = 'format'
          ), ...
        ),
        STYLE (portable_style = literal, ...),
        CONDITIONS (
          channel WHEN predicate THEN literal [ELSE literal], ...
        )
      ), ...
    ),
    FACET (ROW = source_column, COLUMN = source_column),
    RESOLVE (X = SHARED | INDEPENDENT, Y = SHARED | INDEPENDENT,
             COLOR = SHARED | INDEPENDENT)
  ),
  ACTIONS (...),
  INTERACTIONS (...),
  STYLE (...)
);
```

`COORDINATE`, `SCALES`, and `LAYERS` are required. `FACET`, `RESOLVE`, layer `STYLE`, and layer
`CONDITIONS` are optional. A `CUSTOM` visual uses `CHART` encodings instead of top-level `MAPPINGS` or
legacy `SERIES`.

Commas separate siblings at every nesting level. Layer and scale names are identifiers and must be
unique within the visual. Source-column names use the same quoted/bracketed identifier rules as
ordinary mappings.

## 3. Minimal working slices

### 3.1 Layering and dual axes

```sql
SELECT Month, Revenue, MarginPct
INTO #monthly
FROM sales.monthly;

CREATE VISUAL RevenueMargin AS CUSTOM (
  TITLE = 'Revenue and margin',
  SOURCE = #monthly,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
      months = BAND (CHANNEL = X, INCLUDE_ZERO = OFF, ORDER = SOURCE),
      revenue = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON, MIN = 0),
      margin = LINEAR (CHANNEL = Y2, INCLUDE_ZERO = OFF)
    ),
    LAYERS (
      revenue_bars = RECT (
        ENCODINGS (
          X = Month (TYPE = ORDINAL, SCALE = months),
          Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue, AXIS = PRIMARY)
        )
      ),
      margin_line = LINE (
        Z_INDEX = 1,
        ENCODINGS (
          X = Month (TYPE = ORDINAL, SCALE = months),
          Y2 = MarginPct (TYPE = QUANTITATIVE, SCALE = margin, AXIS = SECONDARY)
        )
      )
    )
  )
);
```

Layer order is source order unless `Z_INDEX` is supplied. Resolution is stable by `Z_INDEX`, then
layer name. A `Y2` binding must use `AXIS = SECONDARY`; a `Y` binding cannot use the secondary axis.

### 3.2 Presentation-only conditions

```sql
SELECT Product, MarginPct, IsForecast
INTO #margin_points
FROM sales.product_margin;

CREATE VISUAL MarginStatus AS CUSTOM (
  SOURCE = #margin_points,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
      products = BAND (CHANNEL = X, INCLUDE_ZERO = OFF),
      margin = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
    ),
    LAYERS (
      margin_points = POINT (
        ENCODINGS (
          X = Product (TYPE = NOMINAL, SCALE = products),
          Y = MarginPct (TYPE = QUANTITATIVE, SCALE = margin)
        ),
        CONDITIONS (
          COLOR WHEN MarginPct < 0 THEN '#C0392B' ELSE '#2E86C1',
          OPACITY WHEN IsForecast = TRUE THEN 0.45 ELSE 1
        )
      )
    )
  )
);
```

Conditions may set only the portable presentation channels `COLOR`, `OPACITY`, `SIZE`, `SHAPE`, and
`TEXT`. They do not add, remove, aggregate, calculate, join, rank, or reorder rows. Predicates may use
source columns, literals, report parameters, comparison operators, `AND`, `OR`, `NOT`, `IS NULL`, and
parentheses. Function calls, subqueries, aggregates, windows, and arithmetic are rejected by the
Analysis tier. Connected `LINE` and `AREA` marks cannot use row-varying presentation conditions in
this slice because a segment spans multiple rows; authors can prepare a series column in SQL and use a
`COLOR` encoding instead.

### 3.3 One- and two-dimensional facets

```sql
SELECT Region, Segment, Month, Revenue
INTO #regional_revenue
FROM sales.regional_monthly;

CREATE VISUAL RegionalRevenue AS CUSTOM (
  SOURCE = #regional_revenue,
  CHART (
    COORDINATE (TYPE = CARTESIAN),
    SCALES (
      months = BAND (CHANNEL = X, INCLUDE_ZERO = OFF),
      revenue = LINEAR (CHANNEL = Y, INCLUDE_ZERO = ON)
    ),
    LAYERS (
      bars = RECT (
        ENCODINGS (
          X = Month (TYPE = ORDINAL, SCALE = months),
          Y = Revenue (TYPE = QUANTITATIVE, SCALE = revenue)
        )
      )
    ),
    FACET (ROW = Region, COLUMN = Segment),
    RESOLVE (X = SHARED, Y = INDEPENDENT, COLOR = SHARED)
  )
);
```

Supplying only `ROW` or only `COLUMN` creates a one-dimensional facet. Supplying both creates the
Phase 7 two-dimensional composition. Facet values and panels use first-seen source order. `SHARED`
uses one resolved domain for all panels; `INDEPENDENT` resolves a domain per panel without changing
row, category, series, palette, or panel order.

## 4. Semantic lowering

The Core AST contains immutable records for the chart, layers, encodings, scales, coordinate, facet,
resolution, styles, and conditions. Reporting lowers those records directly into `ChartSpec`:

- Layer names, mark kinds, z-indexes, field bindings, and styles become `MarkLayerSpec` values.
- Encodings become typed `FieldBinding` values. No renderer infers a different semantic type.
- Declared scales become `ScaleSpec` values; undeclared scale references are authoring errors.
- `FACET` becomes `FacetSpec` and explicit row/column bindings.
- `RESOLVE` becomes `ScaleResolutionSpec` and then resolved per-facet scale sets in `PlotPlan`.
- Conditions become typed `EncodingConditionSpec` values and resolve into per-datum portable
  presentation values before any renderer runs.

`ChartSpec` and `PlotPlan` advance together when these additive contracts are serialized. Native SVG
remains derived output and never appears in the AST, `ChartSpec`, saved designer state, or neutral
manifest as authoritative meaning.

## 5. Validation and diagnostics

The parser validates structure and normalized enum spelling and stamps a source span on every chart
node — definition, coordinate, scales, encodings, binding sources, layers, styles, conditions, position
adjustments, color ranges, facet, and resolution.

Semantic validation has one owner: `AdvancedChartSemanticValidator` in `ETL-SQL.Core`. The
`AdvancedChartAuthoring` Analysis rule and `AdvancedChartLowerer` both run it, so an editor diagnostic
and a report preview failure cannot disagree. It reports `RPT-CHART` errors for semantic violations,
including:

- `CHART` on a non-`CUSTOM` visual or a `CUSTOM` visual without `CHART`;
- top-level `MAPPINGS`, `SERIES`, `OVERLAYS`, or table formatting on `CUSTOM`;
- missing/duplicate layers or scales and negative/duplicate z-indexes;
- missing required channels for a mark;
- unknown columns when the source projection is statically known;
- missing scales, incompatible scale/channel/type combinations, or invalid dual-axis bindings;
- invalid polar/transposed channel combinations;
- unsupported conditional predicates, values, channels, or connected-mark conditions;
- empty facets, duplicate row/column fields, or independent resolution without a facet.

Every diagnostic is anchored to the offending node — the layer, scale, encoding, condition, facet, or
coordinate that carries the mistake — not to the `CREATE VISUAL` header, and every duplicate name or
binding is reported in one pass rather than only the first.

Lowering never fails with a bare message. Semantic failures leave `AdvancedChartLowerer` as an
`AdvancedChartSemanticException` carrying the same positioned diagnostics, including the two classes the
AST cannot express — parameter resolution (undeclared or secret-bearing `@variable`, anchored at the
offending node) and the `ChartSpec.Validate()` contract backstop (anchored at the `CHART` clause).
`VisualBuilder` still produces a safe visual error state, and additionally publishes those diagnostics on
`VisualManifest.diagnostics`, so a semantic authoring failure never exists only as unpositioned text
painted inside a rendered report.

The AST enums in Core and the contract enums in `Reporting.Contracts` stay separate families, bridged by
the explicit arm-per-member mappings in `AdvancedChartEnumBridge`; parity tests keep the families
aligned. Runtime validation repeats safety-critical contract checks because scripts can execute without
an editor.

## 6. Lineage, rename, actions, themes, and accessibility

Every encoding field, facet field, and condition predicate column contributes column lineage to the
`report:<visual>` target. Scale and layer names are presentation identifiers and do not create data
lineage. Conditions add read lineage but never transformation lineage because they do not change data.

LSP rename is scoped by symbol kind:

- Renaming a layer or scale updates declarations and references inside the owning `CHART`.
- Renaming a chart field updates encoding, facet, and condition references in that visual. It does not
  silently rewrite upstream SQL; the editor reports the source projection as the definition boundary.

Existing `ACTIONS`, `INTERACTIONS`, report/page/visual themes, and accessibility summaries remain
outside `CHART` and work unchanged. `ChartSpec.Accessibility` and the resolved semantic fallback must
describe layered and faceted meaning without relying on color alone. Condition values supplement the
raw value and do not remove accessible data.

## 7. Report Builder contract

The designer stores the canonical formatted `CHART (...)` clause as an opaque advanced-authoring
option until a dedicated visual editor exists. Parsing and no-op edits preserve the original clause
byte-for-byte. Creating or semantically replacing a custom chart emits the canonical formatter form.
Surgical changes to title, layout, actions, interactions, or outer styles do not rewrite comments,
whitespace, field expressions, or nested chart trivia.

## 8. Transformation boundary

The following remain visible ETL-SQL operations before `CREATE VISUAL`:

- aggregation and binning;
- joins and lookups;
- calculated columns and arithmetic;
- filtering and top-N selection;
- windows, ranks, moving calculations, and statistics;
- imputation, sampling, and reshaping.

`CHART` selects fields, layers marks, declares scales, arranges facets, and changes portable
presentation channels. It cannot contain `SELECT`, `WHERE`, `GROUP BY`, aggregate functions, lookup
definitions, window clauses, or renderer-native escape hatches.

## 9. Deliberate exclusions

- Embedded Vega-Lite/Vega JSON and compatibility shims.
- ECharts option fragments, JavaScript callbacks, arbitrary SVG paths, or CSS selectors.
- Hidden visual transforms or calculated fields.
- Arbitrary geographic projections, remote map URLs, renderer-native paths, and client-side geometry
  loading. The shipped geographic slice is bounded to equirectangular/Mercator projection, an
  allow-listed built-in map or execution-context-resolved GeoJSON file, and region/point/label/route
  layers resolved server-side.
- Arbitrary repeat, concatenation, or nested dashboard layout inside one visual. Phase 7 composition
  is the explicit row/column facet grid; page/container layout composes independent visuals.
- Row-varying conditions on connected marks until portable segment semantics are accepted.

## 10. Acceptance evidence

Every grammar form requires:

- parser acceptance and rejection tests plus canonical formatter round trips;
- `RPT-CHART` diagnostics and LSP completion, hover, and scoped rename coverage;
- Report Builder parse/generate and trivia-preserving surgical mutation fixtures;
- column-lineage assertions for encodings, facets, and condition predicates;
- deterministic `ChartSpec` and `PlotPlan` serialization;
- equivalent semantic ordering, domains, layers, facet panels, conditions, and fallbacks across the
  browser/native SVG, terminal, and accessibility/plain-text surfaces;
- documentation and snippets containing parser-tested minimal examples.

## References

- [Grammar-of-Graphics Contract ADR](grammar-of-graphics-spec-ir.md)
- [Reporting Semantic Contracts](../reporting-semantic-contracts.md)
- [Language Syntax Standards](../standards/language-syntax-standards.md)
- [Report Runtime Asset Standards](../standards/report-runtime-asset-standards.md)
