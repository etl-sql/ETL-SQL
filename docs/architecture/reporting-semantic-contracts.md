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
compatibility alone does not establish rendering support. Marks other than POINT/TEXT and supported RULE, secondary axes,
stacking remain rejected for transposed aspect ratios.

Single-axis RULE layers require exactly one quantitative field or DATUM binding on X or Y, IDENTITY or EM/BAND/DATA NUDGE along the bound axis,
and no conditions or other encodings. Y rules are vertical; X rules are horizontal.
Constants participate in global and independent facet domains. SVG labels and titles, terminal
output, and accessible fallback retain the semantic axis and value. Field-backed rules select one
representative row per distinct numeric threshold, in first-seen order. SVG and terminal select after
facet filtering; fallback selects across the chart. Null/gap rows do not draw rules. The shared
`PlotPlanResolver.ReferenceRuleData` owns selection; raw plan rows remain intact. Conditional
and jittered rules remain rejected in this composition. All nudges must have zero
amplitude on the unbound axis. The resolver uses the point/text displacement contract; SVG consumes
the perpendicular offset for both the rule and label while preserving its plot-spanning extent.
Raw values, domains, terminal/fallback content and rule deduplication remain unchanged. Contract
versions remain unchanged.

DATA rule nudges map the bound threshold plus its displacement on the original scale, using shared
plot sizing including legend space. Null/gap thresholds are skipped; positive logarithmic anchors
and targets are required. No unbound anchor is required. Single-axis rules do not inherit another
layer's color series; this correction is recorded in `BREAKING_CHANGES.md`.

Ranged RULE accepts X + Y_START/Y_END, Y + X_START/X_END, or both endpoint pairs for diagonal segments, with quantitative field/DATUM bindings
and IDENTITY only. The SVG adapter transposes both endpoint channels, while the authoritative plan
retains semantic channels. Complete source rows remain distinct; missing anchors/endpoints are
skipped. Shared interval descriptions preserve fixed coordinates and both endpoints in SVG titles,
terminal text and accessible fallback. Endpoint order is preserved. Both endpoints participate in
global/facet domains before aspect fitting. Diagonal segments join the paired start coordinates
to the paired end coordinates; no primary X/Y anchor is required. All four endpoint values remain
visible in terminal/fallback even for zero-length segments. Missing any endpoint skips the row.
No wire fields or versions change.

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

`X_OFFSET` and `Y_OFFSET` are accepted on the same continuous transposed POINT/TEXT composition.
For category index `i` in `n` ordered groups, the centered slot fraction is `(i + 0.5) / n - 0.5`.
X_OFFSET contributes the negative fraction of fitted plot height to DisplayOffsetY; Y_OFFSET
contributes the fraction of fitted plot width to DisplayOffsetX. The metric matches BAND nudges,
including facets and relayout. Offset-scale reversal reverses category indices; primary-scale
reversal does not change displacement. Null, unmatched and singleton groups contribute zero.
Offsets compose additively with EM/BAND/DATA nudges and jitter. Terminal rows and fallback details carry
the original group labels, including null groups, while raw measures and domains remain intact.
The extension retains ChartSpec v2 and PlotPlan v3 and existing valid chart fingerprints; older
validators reject the newly allowed combination.

`NUDGE UNIT DATA` maps each original anchor and its shifted X/Y values through the same
continuous scales, then stores their physical-coordinate difference as display offsets.
Semantic Y maps horizontally and X vertically, including reversal and logarithmic mapping.
`TransposedAspectLayout` shares fitted plot sizing between resolution and SVG, accounting for
side legends and overlay gutters; facets use their own scales and fitted frame. The existing
EM/BAND/jitter/group metrics stay unchanged. DATA nudges require numeric X/Y on every row;
nonpositive logarithmic anchors or targets fail before rendering. Raw values, domains and
fallback remain unchanged. Error bars translate rigidly by their point's anchor displacement,
including on log scales. Text consumes the displacement before label collision placement.
This adds no wire fields or enum values: ChartSpec v2 and PlotPlan v3 remain current, older
validators reject the new combination, and existing valid-chart fingerprints stay unchanged.

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
