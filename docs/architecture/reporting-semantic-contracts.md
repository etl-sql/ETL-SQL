# Reporting Semantic Contracts

ETL-SQL owns a renderer-neutral, versioned reporting contract between Report-SQL authoring and every
graphical or semantic output backend. The contract implementation is
`src/ETL-SQL.Reporting.Contracts`; the accepted architectural decision remains
[`GrammarOfGraphicsSpecIR.md`](decisions/grammar-of-graphics-spec-ir.md).

## Project boundary

The reporting dependency direction is:

```text
ETL-SQL.Core                         ETL-SQL.Reporting.Contracts
language, parser, execution types    ChartSpec, ChartDataSet, PlotPlan
            \                         /
             \                       /
                    ETL-SQL.Reporting
          lowering, resolution, renderers, exporters
```

- **`ETL-SQL.Core`** does not reference reporting contracts, renderers, or pixel-emission libraries.
- **`ETL-SQL.Reporting.Contracts`** is BCL-only. It has no project or package references and cannot
  mention ECharts, SVG/Skia, PDF, terminal UI, or host types.
- **`ETL-SQL.Reporting`** references both lower layers. It owns named-visual lowering, deterministic
  plan resolution, renderer adapters, and export implementations.
- **Hosts and backends** consume resolved meaning; they do not choose category order, domains, ticks,
  palette identity, legend order, or null behavior independently.

Architecture tests enforce these boundaries against both project files and contract source.

## Contract levels

### `ChartSpec`

`ChartSpec` expresses author intent. It carries a schema URI and integer version plus typed field
bindings, ordered semantic mark layers, coordinate and scale intent, scale-resolution policy,
formatting, null behavior, interactions, themes, accessibility metadata, and typed presentation-only
conditions. Mark layers use the
portable vocabulary `Rect`, `Line`, `Area`, `Point`, `Rule`, `Arc`, and `Text`.

The native `CUSTOM ... CHART` language lowers directly into this intent. It adds no renderer-owned
JSON and no hidden aggregation, filtering, calculation, lookup, window, or statistical transforms.

The contract stores ordering-sensitive data in `ImmutableArray<T>`. Scale, layer, and binding IDs are
validated before serialization. A binding cannot reference an undeclared scale.

### `ChartDataSet`

`ChartDataSet` is columnar and explicitly typed. Each `ChartColumn` declares its physical
`ChartValueKind`, semantic intent (`Quantitative`, `Temporal`, `Nominal`, or `Ordinal`), raw typed
values, and a separate optional vector of formatted display values.

`ChartValue` distinguishes null, integer, floating point, decimal, text, date, time, local date-time,
offset date-time, and boolean values. Validation rejects mixed non-null physical types, non-finite
floating-point values, inconsistent row counts, and display vectors that do not match the raw vector.

### `PlotPlan`

`PlotPlan` is the deterministic, renderer-neutral resolved contract. It carries coordinates and
portable style tokens plus ordered scales and
ticks, category order, ordered series, palette assignments, legend entries, ordered resolved layers,
per-row resolved conditional values, deterministic facet panels with shared or independent scales,
row-level gaps and skips, an accessible summary, and a semantic fallback. Validation rejects
nondeterministic series, legend, or layer order and dangling palette/legend references.

Target-specific font measurement and viewport adaptation may affect physical layout, but a backend
must not alter the semantic fields represented by `PlotSemanticProjection`.

## Serialization and compatibility

`ChartContractSerializer` is the only supported JSON serializer for the three contracts. It:

- uses camel-case property and enum names;
- preserves array order;
- omits null properties but never conflates null chart values with absent rows;
- validates before serialization and after deserialization; and
- rejects unknown schema URIs and versions.

Golden SHA-256 fingerprints in `GrammarOfGraphicsContractTests` make an accidental wire change fail
CI. An intentional compatible or breaking change must introduce an explicit version decision and
update the fixtures and compatibility expectations together.

## Transposed point aspect ratios

Continuous primary-axis `POINT` and `TEXT` compositions accept `ASPECT_RATIO` with
`TRANSPOSED_CARTESIAN`. The ratio is physical semantic Y-unit size divided by X-unit size;
logarithmic spans use decades. The resolver fits the viewport using
`height / width = xSpan / (aspectRatio * ySpan)`, including facet panels and relayout.
The SVG adapter exchanges point channels and scale channels only in its local rendering copy.
Static export consumes the same SVG. Terminal and accessible output retain raw values and order;
they do not claim physical-distance fidelity.

This increment retains ChartSpec v2 and PlotPlan v3: it adds no serialized fields or enum values,
and existing valid specifications retain their behavior and fingerprints. Older ChartSpec validators
reject the newly allowed combination. Regenerate output with a supporting renderer; serialization
compatibility alone does not establish rendering support. Marks other than POINT/TEXT, secondary axes,
stacking, offset channels and DATA nudges remain rejected for transposed aspect ratios.

Error bars on these points retain `ErrorLow`/`ErrorHigh` in the semantic Y domain.
Their endpoints participate in global and independent facet domains before viewport fitting.
The SVG adapter draws the interval on the physical horizontal axis using the transposed Y scale;
caps are vertical. Linear, logarithmic and reversed scales use the same mapping as the point.
No serialized contract changes are needed. Terminal and accessible output retain the interval text.

`NUDGE` with `UNIT = EM` is resolved for this transposed point composition before rendering.
The portable em remains 12 pixels. Semantic X moves vertically and semantic Y horizontally:
`DisplayOffsetX = Y * 12`, `DisplayOffsetY = -X * 12`. These are physical display offsets,
independent of scale reversal, domain kind and viewport size. Relayout recomputes the same offsets;
points and error intervals consume them together. No raw values, domains, fallback text or wire
fields change. Existing Cartesian and non-aspect transposed behavior stays unchanged.

`TEXT` layers share the transposed point mapping, collision placement, and EM displacement.
Conditional text overrides the text binding in SVG, terminal annotation rows, and fallback labels;
SVG also honors conditional color, opacity, and font size. Text that cannot fit the physical viewport
is retained in SVG descriptions. Terminal rows preserve semantic X/Y values and facet membership,
including text-only compositions. These behaviors are scoped to the newly accepted combination;
ChartSpec v2 and PlotPlan v3 remain unchanged, as do existing valid chart fingerprints.

`JITTER` is also accepted for this POINT/TEXT composition. With signed seeded hashes `hx` and
`hy`, resolution emits `DisplayOffsetX = hy * Y * (viewport.Width - 80)` and
`DisplayOffsetY = -hx * X * (viewport.Height - 100)`. The viewport is the fitted Cartesian
frame for the row's facet, or the global fitted frame; margins exclude axis chrome, and the
metric precedes renderer-specific legend layout. Relayout recomputes offsets from the same hashes.
The existing chart/layer/key/axis/seed hash identity is retained: row reorder and layer rename do
not change displacement, while changing the seed does. Layer identity includes mark, Z index and
channel sequence, so separate point/text layers may have different offsets. Keys must exist and
be unique and non-null. Raw channels, scale domains, fallback and terminal values stay unchanged.
This lifts a validation restriction without adding fields to ChartSpec v2 or PlotPlan v3; older
validators reject it. Existing valid Cartesian and transposed chart goldens remain unchanged.

`NUDGE UNIT BAND` uses the same fitted plot metric for continuous transposed POINT/TEXT:
`DisplayOffsetX = Y * (viewport.Width - 80)` and
`DisplayOffsetY = -X * (viewport.Height - 100)`. A continuous primary axis has one band.
The metric excludes fixed axis chrome and precedes renderer-specific legend layout, matching
jitter. Facets select the row's fitted viewport; relayout recalculates the physical displacement.
Positive semantic X moves up and positive Y moves right independently of scale reversal or kind.
Points, error intervals and text consume the same offsets. Raw values, domains, fallback and terminal
output remain unchanged. This extends validation without changing ChartSpec v2 or PlotPlan v3;
older validators reject the combination, and existing valid specifications retain their fingerprints.

## Cross-backend conformance

A backend implements `IPlotPlanSemanticBackend` and projects its effective interpretation into
`PlotSemanticProjection`. `PlotPlanConformanceHarness` compares that projection with the authoritative
plan and attributes drift to scales, series order, palette, legend, layers, nulls, accessibility, or
fallback behavior.

The standard catalog path is now:

```text
named standard visual
              -> ChartSpec + typed ChartDataSet
              -> deterministic PlotPlan
              -> native browser/static SVG | terminal | V8-free static PDF
```

Phase 7 adds the native authoring path:

```text
CUSTOM CHART layers / scales / coordinates / conditions / facets
              -> ChartSpec + typed ChartDataSet
              -> PlotPlan + resolved facet panels / conditional values
              -> native browser/static SVG | terminal | accessible fallback
```

`VisualManifest.NativeSvg` carries browser/static geometry; `ChartConfig` is an obsolete compatibility
slot and remains null on native manifests. Representative fixtures assert shared domains, source
ordering, series/palette/legend identity, dual axes, temporal values, stacking, gaps, overlays,
accessibility fallbacks, and backend consumption of the same plan. The capability matrix contains no
external chart-runtime dependency.

## References

- [Grammar-of-Graphics ADR](decisions/grammar-of-graphics-spec-ir.md)
- [Native Advanced Chart Authoring ADR](decisions/native-advanced-chart-authoring.md)
- [Source Boundary Standards](standards/source-boundary-standards.md)
- [Report-SQL Guide](../guides/feature-guides/report-sql.md)
