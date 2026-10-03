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

Connected LINE/AREA output has a guarded PlotPlan v5 envelope. Its initial contract requires one
unstacked Cartesian LINE or AREA layer with linear X/Y scales, IDENTITY, GAP handling and LINEAR
interpolation. AREA with Y requires explicit AREA_BASELINE = ZERO and includes zero in its Y domain.
AREA ribbons bind X/Y_START/Y_END without a baseline. The optional AreaRibbon flag on resolved
layers preserves that distinction, including empty datasets; it is omitted for existing plans.
Ribbon domains include both bounds, and terminal/fallback output lists both authored endpoints.
AREA ribbon endpoints must share an effective Y scale. A shared scale other than the first primary
Y scale is preserved by `ResolvedMarkLayer.AreaRibbonScaleId`, with a guarded PlotPlan v6 envelope.
This applies to conditional and unconditional ribbons, including empty and grouped data. The field
is omitted for existing plans whose first Y scale already owns the bounds. Native/static SVG selects
the explicit scale, and semantic conformance includes that selection. Distinct endpoint scales fail
Core and ChartSpec validation with the compatibility correction documented in `BREAKING_CHANGES.md`.
Each `ResolvedMarkConnection` identifies adjacent array indices and source row
identities; its COLOR/OPACITY values must match the source datum. Validation requires every eligible
adjacent pair exactly once and rejects downgraded envelopes. SVG, terminal and fallback output
consume the same connections. This authoring form uses a guarded ChartSpec v3 envelope; existing
charts retain their prior versions. See the
[connected-mark implementation contract](decisions/connected-mark-conditions.md).

Conditional LINE/AREA/ribbons also accept CONNECT with guarded ChartSpec v4 and PlotPlan v7.
Their raw rows and domains remain intact; incomplete cross-sections are recorded as skipped
endpoints. Connections join adjacent complete rows in source order, with presentation supplied
only by the surviving source. The resolved contract validates eligible adjacency, numeric endpoint
completeness, skipped-row metadata and exact source presentation. Relayout preserves those pairs.
The semantic projection includes connection identities and presentation, so a backend that discards
connectivity fails conformance. Existing GAP output retains its earlier envelopes and fingerprints.

Connected compositions use guarded ChartSpec v6 and PlotPlan v9 when authoring has multiple layers,
categorical COLOR grouping on a conditioned layer, or facets. `ResolvedCartesianAxes` selects the
shared primary linear X/Y scales across every layer. Each conditioned layer uses its own GAP or
CONNECT policy and retains raw rows. `ConnectionSkippedRows` records that layer's incomplete
CONNECT endpoints; ordinary layers omit it. Connections carry `FacetId` and indices into the full
layer array. Validation rejects cross-panel endpoints, incomplete/skipped pairs, overlapping facet
membership, missing connections and downgraded envelopes. Null COLOR categories remain distinct
series, and sparse grids retain empty panels. SVG renders a panel-local copy after validating the
complete plan; terminal and fallback preserve layer/series/facet ownership and raw row identities.
Conformance checks connection facet identity, skipped rows and selected axes. Existing single-layer
wire shapes and goldens remain unchanged. `ConnectedCompositionTests` and Chromium
`ConnectedMarkSvgTests` cover this composition.

Conditioned ZERO uses guarded ChartSpec v7 and PlotPlan v10, including single-layer charts.
Shared primary linear axes and per-layer facet/series ownership remain explicit. A numeric X with
null scalar Y resolves to zero; missing X or either ribbon bound is a gap. Raw channels remain
unchanged and predicates evaluate raw values. Each complete ZERO datum carries
`ResolvedConnectionCoordinates`: X plus scalar Y or both YStart/YEnd bounds. Incomplete rows and
other policies omit this metadata. Validation compares it with raw channels, checks gap decisions,
requires exact eligible connections and rejects downgrades. Scalar Y domains contain zero, including
independent facets; explicit scalar bounds excluding zero are rejected. SVG/terminal consume the
resolved coordinates, while titles and fallback distinguish imputed nulls from observed zeros and
retain available raw bounds. Relayout preserves the metadata, and conformance compares it.
`ConnectedZeroPolicyTests`, LSP rename and Chromium checks cover ZERO and mixed-policy charts.

Conditioned transposed LINE/zero-baseline AREA/ribbons use ChartSpec v8 and PlotPlan v11,
with or without ASPECT_RATIO. This guard takes precedence over the Cartesian connection envelopes,
including empty datasets. `ResolvedCartesianAxes` retains semantic scale ownership; raw channels
and optional zero coordinates retain X/Y meaning. Physical Y is horizontal and X vertical.
Connections, null policies, source presentation, series and facets use the same validated metadata
as Cartesian compositions. Independent facets and relayout preserve physical unit ratios when
ASPECT_RATIO is set. Ordinary layers share the selected axes and physical placement frame.
`PlotSemanticProjection.ConnectedCoordinate` checks orientation alongside endpoint metadata.
`TransposedConnectedTests`, LSP and Chromium cover geometry, raw values, malformed downgrades
and static exports. Connected layers retain the full endpoint array even when SAMPLING is enabled;
`ConnectedSamplingTests` pins that correction without changing ordinary sampled layers.

Connected SIZE/SHAPE/TEXT decorations use ChartSpec v9 and PlotPlan v12, taking precedence over
the other connection envelopes. A declared `ResolvedMarkLayer.ConnectionDecorations` flag remains
present even on empty or all-null data. `ResolvedDatum.ConnectionDecoration` records semantic X/Y,
radius in pixels, normalized portable shape and optional typed text at complete rows. Ribbons use
the authored Y_END. Scalar ZERO uses resolved zero while retaining raw null Y. Missing anchors omit
the decoration. Absent Text differs from `ChartValue.Null()`, which explicitly suppresses a label.
Validation recomputes metadata from raw anchors, resolved row encodings and defaults, rejects
unknown/duplicate row channels and malformed/downgraded envelopes, and semantic projection compares
the layer flag and decoration array. `ResolvedMarkConnection.Encodings` remains COLOR/OPACITY only.
SVG shares ownership text in accessible groups while preserving row titles; terminal and fallback
describe raw values, normalized decorations and incomplete rows. `ConnectedDecorationTests`, rename
and Chromium cover geometry, contracts and exports. Existing workload budgets and old goldens pass.

Conditioned non-linear interpolation uses ChartSpec v10 and PlotPlan v13. Each resolved layer
declares `ConnectionInterpolation` (Smooth, StepBefore or StepAfter), and each connection carries
`ResolvedConnectionGeometry`: Upper semantic points, optional Lower points and Cubic. Validation
recomputes controls/corners from the eligible run within each series/facet and compares by value.
LINE omits Lower; scalar AREA uses zero; ribbons retain authored bound order. Two-row SMOOTH runs
remain straight. GAP breaks neighbors, CONNECT skips incomplete rows and ZERO uses its resolved
geometry. Raw values, domains and decorations remain intact. The guard survives empty data and
takes precedence over other connection envelopes; LINEAR omits the new fields. Semantic projection
checks interpolation and geometry, including wire round trips. SVG/static export maps stored points;
terminal scalar paths consume them and ribbon fallback keeps raw bounds. See
[resolved interpolation](decisions/connected-mark-conditions.md#resolved-interpolation) for controls,
step direction and backend evidence. Independent numeric facets now retain authored MIN/MAX;
fallback conformance compares ordered content, with both corrections recorded in `BREAKING_CHANGES.md`.

`CartesianInterpolation` owns the shared step-corner, Catmull-Rom control and cubic-evaluation
arithmetic. It has no scale, placement or run policy. Conditioned connections call it in semantic
coordinates; ordinary SVG paths call it after mapping and placement; terminal paths sample the
stored semantic cubic. Callers retain the two-row straight-line rule and gap boundaries. Transposing
or reversing a step boundary swaps STEP_BEFORE/STEP_AFTER; reversing a cubic swaps its controls.
Arithmetic tests cover these identities and affine mapping without changing serialized geometry.

Ordinary transposed fixed-aspect LINE interpolation uses ChartSpec v11 and PlotPlan v14.
`PathInterpolation` declares Smooth, StepBefore or StepAfter on an ordinary layer; it is separate
from semantic `ConnectionInterpolation` geometry. It requires GAP, quantitative X/Y, shared linear
or logarithmic primary scales, no extra encodings and supported IDENTITY/JITTER/EM/BAND/DATA placement.
SVG constructs controls after mapping and placement; step direction follows the original semantic
axes. Terminal rendering samples the same display-space curve at character resolution and retains
raw rows. Neighbors never cross gaps or facets; two-point SMOOTH runs remain straight. This envelope
takes precedence in mixed conditioned charts and survives empty inputs. LINEAR keeps its existing
envelope.

Ordinary transposed fixed-aspect AREA interpolation uses ChartSpec v12 and PlotPlan v15, taking
precedence over LINE and conditioned envelopes. It extends `PathInterpolation` to zero-baseline
AREA and authored Y_START/Y_END ribbons with GAP and existing placement modes. Both boundaries
interpolate mapped, displaced vertices within one uninterrupted facet run. The lower boundary closes
in reverse order: cubic controls reverse with it, and step direction swaps. Crossing bounds remain
unsorted. Empty/singleton runs draw nothing; two-point SMOOTH stays straight. SVG/static output uses
the closed path, while terminal and accessible fallback preserve raw intervals and expose the mode.
Scalar AREA still requires a linear Y scale that includes zero; ribbons may use positive log scales.

Transposed fixed-aspect confidence AREA uses ChartSpec v13 and PlotPlan v16. `AreaConfidence`
declares that the ribbon's raw endpoints are ConfidenceLow/ConfidenceHigh, not YStart/YEnd. It
requires X, both quantitative bounds on one primary Y scale, GAP, no baseline or conditions, and
supported placement/interpolation. Bounds remain unsorted. DATA nudges anchor at ConfidenceLow;
both endpoints translate together. Only the local SVG adapter maps confidence channels to physical
boundary channels; serialized data retains the authored channels. The native path class is
`plot-confidence-band`, and titles, terminal intervals and accessible fallback identify confidence.
The guard survives empty data, takes precedence in mixed charts, and participates in conformance.

`ChartContractSerializer` is the only supported JSON serializer for the three contracts. It:

- uses camel-case property and enum names;
- preserves array order;
- omits null properties but never conflates null chart values with absent rows;
- validates before serialization and after deserialization; and
- rejects unknown schema URIs and versions.

Version-one migration reads exact root schema/version fields, including compact JSON. It preserves
all payload strings; text containing a schema URI does not trigger migration. Mismatched legacy
envelopes remain rejected. `EnvelopeMigrationTests` covers this boundary, and ZERO round trips
cover the v10 schema without mistaking it for v1.

Golden SHA-256 fingerprints in `GrammarOfGraphicsContractTests` make an accidental wire change fail
CI. An intentional compatible or breaking change must introduce an explicit version decision and
update the fixtures and compatibility expectations together.

## Radial stacking envelope

ChartSpec v2 now accepts ZERO/NORMALIZE stacks on polar RADIUS with categorical THETA and
optional categorical COLOR. ARC layers use one stack mode, IDENTITY, no conditions and no facets.
The resolver preserves repeated source rows, groups by category, accumulates non-negative values
in resolved layer/series order, and records a ResolvedRadialInterval on each complete datum.
Intervals carry start/end angles in degrees, cumulative start/end values and their common maximum.
NORMALIZE divides cumulative raw totals, avoiding cumulative rounding drift. Null coordinates
remain gaps. Radius scale domains and ticks use the stacked extent. Negative values fail.

These plans alone emit schema plot-plan/v4 and version 4. Existing plans continue emitting v3,
and both envelopes are validated. A v3 envelope cannot contain radial intervals; stacked arcs
require v4 and complete non-gap intervals. This prevents older readers from interpreting a radial
stack as a pie. ChartSpec has no new fields. SVG draws annular sectors from the resolved intervals;
full-circle sectors use two arcs. Terminal and fallback consume the same intervals. Relayout
changes physical radii without recalculating stack values. Radial length, not sector area, carries
the value. Unstacked polar behavior and its fingerprints are unchanged.

## Transposed physical aspect ratios

Continuous primary-axis `POINT` and `TEXT` compositions, with supported LINE/AREA/RULE/RECT layers, accept `ASPECT_RATIO` with
`TRANSPOSED_CARTESIAN`. The ratio is physical semantic Y-unit size divided by X-unit size;
logarithmic spans use decades. The resolver fits the viewport using
`height / width = xSpan / (aspectRatio * ySpan)`, including facet panels and relayout.
The SVG adapter exchanges point channels and scale channels only in its local rendering copy.
Static export consumes the same SVG. Terminal and accessible output retain raw values and order;
they do not claim physical-distance fidelity.

This increment retains ChartSpec v2 and PlotPlan v3: it adds no serialized fields or enum values,
and existing valid specifications retain their plan and SVG fingerprints. The terminal row-selection
correction described below changes output for previously omitted series. Older ChartSpec validators
reject the newly allowed combination. Regenerate output with a supporting renderer; serialization
compatibility alone does not establish rendering support. Marks other than POINT/TEXT and supported LINE/AREA/RULE/RECT, secondary axes,
stacking remain rejected for transposed aspect ratios.

Single-axis RULE layers require exactly one quantitative field or DATUM binding on X or Y, IDENTITY, JITTER or EM/BAND/DATA NUDGE along the bound axis,
and no conditions or other encodings. Y rules are vertical; X rules are horizontal.
Constants participate in global and independent facet domains. SVG labels and titles, terminal
output, and accessible fallback retain the semantic axis and value. Field-backed rules select one
representative row per distinct numeric threshold, in first-seen order. SVG and terminal select after
facet filtering; fallback selects across the chart. Null/gap rows do not draw rules. The shared
`PlotPlanResolver.ReferenceRuleData` owns selection; raw plan rows remain intact. Conditional
rules remain rejected in this composition. All nudges and jitter must have zero
amplitude on the unbound axis. The resolver uses the point/text displacement contract; SVG consumes
the perpendicular offset for both the rule and label while preserving its plot-spanning extent.
Raw values, domains, terminal/fallback content and rule deduplication remain unchanged. Contract
versions remain unchanged.

Single-axis rule JITTER uses the same stable-key hash and fitted viewport metric as segment jitter.
Only the bound axis can have a non-zero amplitude. Every source key must exist and be unique and
non-null, including rows with null thresholds. Reference selection stays first-seen per distinct
numeric threshold within each facet; a constant rule selects its first source row per facet.
That selected row supplies the physical offset. Per-key offsets survive row reorder and layer
rename, but changing the first row for a duplicate threshold can change its displayed displacement.
Seed changes alter offsets; scale reversal does not. Labels consume the rule's perpendicular offset
and the line continues to span the plot. Null thresholds remain absent from geometry and fallback.

DATA rule nudges map the bound threshold plus its displacement on the original scale, using shared
plot sizing including legend space. Null/gap thresholds are skipped; positive logarithmic anchors
and targets are required. No unbound anchor is required. Single-axis rules do not inherit another
layer's color series; this correction is recorded in `BREAKING_CHANGES.md`.

Ranged RULE accepts X + Y_START/Y_END, Y + X_START/X_END, or both endpoint pairs for diagonal segments, with quantitative field/DATUM bindings
and IDENTITY, JITTER or EM/BAND/DATA NUDGE. Both endpoints and the label consume the same physical offsets;
for EM/BAND, positive X moves up and positive Y moves right. EM uses 12 pixels per unit; BAND uses the fitted
viewport minus fixed axis margins, including facets and relayout. Raw intervals and domains remain
unchanged. The SVG adapter transposes both endpoint channels, while the authoritative plan
retains semantic channels. Complete source rows remain distinct; missing anchors/endpoints are
skipped. Shared interval descriptions preserve fixed coordinates and both endpoints in SVG titles,
terminal text and accessible fallback. Endpoint order is preserved. Both endpoints participate in
global/facet domains before aspect fitting. Diagonal segments join the paired start coordinates
to the paired end coordinates; no primary X/Y anchor is required. All four endpoint values remain
visible in terminal/fallback even for zero-length segments. Missing any endpoint skips the row.
DATA displacement uses the authored start: X_START or fixed X, and Y_START or fixed Y. The resolver
maps that anchor plus the nudge on the original scales and translates both endpoints by the resulting
physical displacement. It preserves the screen-space segment vector, including descending and
zero-length segments; endpoint values are not independently shifted. Reversed/logarithmic scales,
side-legend space, independent facets and relayout use the shared DATA plot sizing contract.
Incomplete rows receive zero offsets and remain absent from rendered/fallback segments. Logarithmic
anchors and shifted anchor targets must be positive. Raw intervals, domains, terminal and accessible
output remain unchanged. No wire fields or versions change.

Segment JITTER uses the same seeded stable-key displacement as POINT/TEXT. Semantic X scales by
the fitted viewport height minus 100 pixels and semantic Y by its width minus 80 pixels; each facet
uses its own fitted viewport. Both endpoints and the label consume one physical displacement per
source row. Reversal does not flip it; relayout rescales the same hashes. Keys must exist and be
unique/non-null across all source rows, including incomplete segments. Row reorder and layer rename
preserve offsets; seed changes do not. Existing hash identity includes chart ID, mark, Z index and
channel sequence. Missing endpoints still skip rendering and fallback. Single-axis reference rules
accept JITTER only along the bound axis. ChartSpec v2 and PlotPlan v3 retain their existing wire shapes.

LINE accepts exactly quantitative field/DATUM X/Y bindings, IDENTITY placement, explicit
NULL_HANDLING = GAP and explicit interpolation (LINEAR, or SMOOTH/STEP_BEFORE/STEP_AFTER with ASPECT_RATIO). With ASPECT_RATIO, EM nudges translate
the full LINE path, symbols and labels through existing physical DisplayOffsetX/Y metadata.
Positive semantic X moves up and Y right by 12 pixels per em; reversal does not change that offset.
LINE also accepts BAND nudges: semantic X uses the fitted viewport height minus 100 pixels and
Y its width minus 80 pixels before legend layout. Every path vertex and symbol/label anchor uses
the same physical offset. Each independent facet and relayout recomputes the displacement from its
fitted dimensions; reversal does not flip it. DATA nudges instead map each complete vertex's
X + nudge X and Y + nudge Y through its original scales and final plot area, including side legends.
Symbols and labels share that vertex's displacement. Reversal follows the scales, and logarithmic
targets must remain positive; per-vertex pixel offsets can differ on log axes. Missing X/Y remains
a gap with zero displacement. JITTER moves each vertex, symbol and label by its stable-key hash:
semantic X uses fitted viewport height minus 100 pixels and Y its width minus 80 pixels, before
legend layout. Reversal does not flip the offset; facets and relayout scale the same signed hashes.
All source keys, including gap rows, must be unique and non-null. Source reordering preserves
key-owned displacement while changing path order. Raw channels, domains, gaps and semantic
terminal/fallback remain unchanged through facets and resize.
No envelope fields change. No additional encodings or conditions
are accepted. The physical-axis adapter preserves source row order and splits paths at missing
coordinates. Coincident rows remain distinct. Native symbols and data labels read semantic Y
after the adapter exchanges channels. Terminal and accessible output keep semantic coordinates.
Independent facets, reversed/log scales and relayout use the same point geometry. Isolated lines
ignore other layers' color groups. Mixed LINE/RECT terminal charts render the line and rectangle
ranges separately, retaining both rectangle intervals. ChartSpec v2 and PlotPlan v3 are unchanged.

Ordinary fixed-aspect AREA accepts IDENTITY, JITTER and EM/BAND/DATA nudges with explicit GAP and
LINEAR/SMOOTH/STEP_BEFORE/STEP_AFTER interpolation. Both bounds of each cross-section move together, including the scalar zero baseline.
EM uses the physical offset `(Y * 12, -X * 12)`; BAND uses fitted viewport width minus 80 pixels
and height minus 100 pixels before legend layout. Reversal preserves displacement direction.
Each facet and resize recomputes BAND metrics. Domains, authored crossings, raw interval descriptions
and gaps remain unchanged. DATA uses the scalar Y vertex or ribbon's authored Y_START anchor:
map X + nudge X and anchor Y + nudge Y on the original scales and final plot area, including legends,
then translate both bounds by that displacement. The cross-section retains its displayed span.
Reversal follows the scales and log anchors/targets must remain positive. Missing coordinates
remain gaps with zero displacement. JITTER translates both bounds of each cross-section by one
stable-key displacement, using the fitted height/width metrics and seeded hashes before legend layout.
Reversal preserves display direction; facets and relayout scale the same hashes. All source keys,
including gap rows, must be unique and non-null. Reordering preserves key-owned offsets and polygons
follow the new source order. Raw intervals/domains and displayed spans remain intact. No fields or versions change:
ChartSpec v5 and PlotPlan v8 retain resolved primary-axis ownership.

RECT accepts exactly four quantitative field/DATUM endpoints (X_START/X_END/Y_START/Y_END) with
IDENTITY, JITTER or EM/BAND/DATA NUDGE placement and no conditions or additional encodings. The physical-axis adapter exchanges
both endpoint pairs locally; authoritative plan channels stay semantic. Each endpoint contributes
to global and independent facet domains. SVG uses the minimum mapped corner and absolute mapped
span on each physical axis, without minimum bar dimensions. Zero-area and subpixel rectangles
therefore preserve their physical extent. Isolated rectangles ignore unrelated layer color groups.

The shared `CartesianRangeDescription` preserves both intervals and endpoint order in SVG titles,
optional labels, terminal rows and accessible fallback. Incomplete rows draw no rectangle and are
reported as gaps. Complete coincident rows remain distinct. The resolved extent is `None` because
these rectangles have no value baseline. EM nudges apply the shared physical offset `(Y * 12, -X * 12)`
to both corners; dimensions, domains and interval text are unchanged. BAND nudges apply
`(Y * (viewport.Width - 80), -X * (viewport.Height - 100))` using each fitted viewport
before legend layout. Direction stays physical under reversed scales. DATA nudges map the authored
X_START/Y_START corner and its shifted value through the original per-panel scales and the
plot area after legend layout. Their difference translates both corners, preserving screen dimensions
even on log axes and descending intervals. Incomplete rectangles skip placement; nonpositive
logarithmic anchor targets fail. Domain values and fallback text stay unchanged. Rectangle JITTER
uses the shared stable-key hash and fitted pre-legend viewport fractions, translating the entire
rectangle without changing dimensions. All source keys are validated, including gaps. Row reorder
and layer rename preserve displacement; seeds change it. Coincident rows remain separate. Native/static SVG share the geometry; terminal output
preserves values rather than physical distance. ChartSpec v2 and PlotPlan v3 require no new fields.
Unfaceted terminal output now selects rows from every layer, fixing omission of rows outside the
first color group; that existing-behavior correction is recorded in `BREAKING_CHANGES.md`.

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

### Transposed AREA axis ownership

The straight transposed physical-aspect AREA form uses ChartSpec v5 and PlotPlan v8. It requires
quantitative field/DATUM X/Y and an explicit ZERO baseline on linear Y, or X/Y_START/Y_END without
a baseline. Both forms require GAP, no conditions, stacking or secondary axes, and accept
IDENTITY/JITTER/EM/BAND/DATA placement. LINEAR retains the v5/v8 envelope; SMOOTH and both step
modes use v12/v15. Ribbons may use linear/log axes; logarithmic anchors must be positive.

`ChartSpec.ResolveTransposedAreaAxes()` resolves one shared primary scale per semantic axis across
all positional bindings. The plan stores `ResolvedCartesianAxes(XScaleId, YScaleId)` in
`PlotPlan.CartesianAxes`. Selected scales own viewport fitting, independent facet layout, geometry,
axis labels and mixed-mark display offsets. Unused scale declarations do not change that selection.
Downgraded envelopes, missing axes, absent endpoints and unsupported baseline/policy combinations
fail contract validation. Older chart shapes omit the new property and retain their envelopes.

The resolver includes zero for baseline AREA and both raw bounds for ribbons. SVG adapts a local
copy to physical axes, builds gap-separated polygons in source order and retains raw semantic
intervals in titles. Terminal and fallback use the same raw interval descriptions. Signed,
crossing, coincident and empty inputs, reversal, logarithmic axes, facets, resize, mixed marks,
authoring/designer/LSP/lineage and static PDF are covered by `TransposedAspectAreaTests`.
The conformance projection includes axis ownership and diagnoses a backend that drops it.

The same renderer corrects previously accepted non-aspect transposed Y_START/Y_END ribbons that
emitted no geometry. This is a v0.20.0 compatibility change recorded in `BREAKING_CHANGES.md`.
Continuous X uses the existing top-to-bottom transposed category convention; categorical X uses
resolved categories and reversal. Shared ribbon Y scales own horizontal geometry and grid lines.
Confidence bands and prior fixed-aspect POINT/TEXT/LINE/RULE/RECT shapes retain existing output.

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
