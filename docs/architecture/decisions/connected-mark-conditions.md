# Connected mark condition semantics

Status: partially implemented. Cartesian and transposed Cartesian LINE, zero-baseline AREA and AREA ribbons accept
COLOR/OPACITY connections and SIZE/SHAPE/TEXT row decorations with GAP, CONNECT or ZERO, including
multiple layers, categorical COLOR series and facets. LINEAR, SMOOTH, STEP_BEFORE and STEP_AFTER are supported.
Other coordinate forms, placement, stacking and secondary axes remain rejected.
Remaining extensions are in the [roadmap backlog](../../../ROADMAP.md#reporting--presentation--grammar-of-graphics-semantic-extensions); no further combination is scheduled this sprint.

The internal `ConnectedMarkResolver.ResolveConnections` now resolves adjacency and source-owned
COLOR/OPACITY for an already partitioned layer with GAP or CONNECT handling. Its tests cover ownership, missing
source styles, gaps, coincident rows, transparent connections and isolated layers.
`ResolvedMarkConnection` lives in Reporting.Contracts and validates adjacency, endpoint identity,
gap exclusion and exact source-owned presentation. `ConnectedMarkResolver.Attach` produces a
guarded PlotPlan v5 with explicit connections for one unstacked Cartesian LINE or AREA layer, linear X/Y
scales, IDENTITY, GAP handling and LINEAR interpolation. SVG and terminal consume those connections;
fallback descriptions preserve source ownership. `ConnectedLinePlanTests` covers serialization,
geometry, descriptions, missing connections and rejected downgrades. Authoring validation and
lowering use guarded ChartSpec v3. AREA with Y requires explicit AREA_BASELINE = ZERO; the
resolver includes zero in the Y domain and SVG fills adjacent strips without interior strokes.
Ribbons instead bind X/Y_START/Y_END and omit the baseline. Their bounds remain in authored order,
including crossing bounds, and both endpoints contribute to Y domains. A resolved AreaRibbon flag
preserves geometry even for empty data; older readers reject the missing Y/baseline form.
Both ribbon bounds share one effective Y scale. When that scale differs from the first primary Y
scale, an explicit `AreaRibbonScaleId` is carried by guarded PlotPlan v6. Other connected output
retains v5. Core and ChartSpec reject separately scaled bounds; native/static SVG consumes the
selected shared scale. `RibbonScaleTests` pins the correction and existing ribbon goldens.
ConnectedAreaPlanTests covers signed/reversed geometry, gaps, relayout, authoring, PDF and goldens.

CONNECT uses guarded ChartSpec v4 and PlotPlan v7. Raw rows remain in the layer, with incomplete
X/Y or ribbon cross-sections listed in `Nulls.SkippedRows`. Connections join the adjacent complete
rows in source order; skipped rows cannot own connections or provide destination styles. The typed
contract computes eligible pairs and rejects skipped complete rows, incomplete endpoints, reordered
connections and downgraded envelopes. Existing GAP plans retain v5/v6. `ConnectedNullPolicyTests`
covers geometry, missing/transparent source styles, all-null/empty data, reversal, resize, authoring,
lineage, compatibility, terminal/fallback, PDF and deterministic fixtures.

## ZERO geometry and raw values

ZERO uses guarded ChartSpec v7 and PlotPlan v10. Scalar LINE and zero-baseline AREA resolve a
present null Y channel to numeric zero when X is numeric. Missing X, an absent Y channel, or either
missing ribbon bound remains a gap. Ribbons never invent an endpoint. Both scalar and ribbon ZERO
forms retain every raw row and channel; conditions still evaluate the source null, so `Y IS NULL`
can supply the outgoing connection's presentation.

`ResolvedDatum.ConnectionCoordinates` holds numeric X and scalar Y or both authored ribbon bounds
for each complete ZERO cross-section. Incomplete rows omit it and have `IsGap = true`. Other policies
omit it entirely. Validation recomputes the expected metadata from raw channels, checks the gap
decision, requires every eligible pair, and rejects invented geometry or downgraded envelopes.
Scalar Y domains include zero, including independent facets; explicit scalar Y bounds must contain
zero. Conditions do not affect those domains. ZERO uses the same shared primary axis, series and
facet ownership as compositions below, including the single-layer form.

SVG and terminal geometry consume the resolved coordinates. Titles and accessible descriptions
distinguish a raw null rendered at zero from an observed zero. Raw values and available ribbon bounds
remain in fallback. Relayout preserves both raw and resolved values, and conformance checks the
coordinate metadata. `ConnectedZeroPolicyTests` covers numeric source kinds, all-null/missing-X
inputs, signed/reversed geometry, mixed policies, resize, authoring/designer/contracts, lineage,
static exports, malformed metadata and deterministic fixtures. LSP rename and Chromium coverage
include ZERO. Existing workload budgets apply unchanged.

## Layer, series and facet composition

ChartSpec v6 and PlotPlan v9 guard compositions with more than one authored layer, a categorical
COLOR field on a conditioned layer, or facets. All positional bindings share one primary linear
quantitative X scale and one primary linear quantitative Y scale. `ResolvedCartesianAxes` names
those scales, including when unused declarations appear first. Independent facet domains use the
same scale identities. Unstacked ordinary Cartesian layers can share the chart; each conditioned
layer still requires IDENTITY, supported interpolation and explicit GAP, CONNECT or ZERO. A chart
containing a conditioned ZERO layer uses the v7/v10 envelope instead of v6/v9.
Series expansion retains the authored Z_INDEX. Resolved layers are sorted by that priority and
their identifier, so a later series cannot move above a layer with a higher authored priority.

Each layer groups only by its own nominal/ordinal COLOR field. Categories that differ only by case
remain distinct. Null categories retain their rows
in a distinct null series; text resembling an internal key remains a separate category. Conditions
do not affect membership. Connections are resolved inside each series and facet after membership
is known. Their indices address the full raw layer array, and `ResolvedMarkConnection.FacetId`
identifies the owning panel. Interleaved rows from another facet do not break a GAP run. A missing
coordinate within the same facet does. Sparse grids retain empty panels without inventing rows.

`ResolvedMarkLayer.ConnectionSkippedRows` lists incomplete CONNECT endpoints for that layer;
GAP and ZERO layers carry an empty list. This prevents one layer's policy from being attributed to another
when the same raw row is usable in one layer and incomplete in another. Validation requires every
eligible pair exactly once, correct facet membership, exact skipped-row metadata and source-owned
COLOR/OPACITY. Ordinary layers omit both connection fields. Older envelopes reject this metadata.

SVG selects the validated panel's connections and remaps indices only in its local rendering copy.
Terminal LINE/zero-baseline AREA consumes the typed pairs; ribbons list both raw bounds. SVG titles,
terminal descriptions and fallback identify the layer, series, facet and raw endpoint rows, including
transparent connections. Relayout preserves ownership and membership. The conformance projection
checks facet and skipped-row metadata. `ConnectedCompositionTests` covers geometry, mixed policies,
different grouping fields, empty/null/signed/reversed inputs, authoring/designer/contracts, lineage,
terminal/fallback, PDF and deterministic fixtures. LSP rename and `ConnectedMarkSvgTests` cover
authoring references and real Chromium paths. Earlier single-layer envelopes and goldens are unchanged.

## Transposed connection geometry

Conditioned TRANSPOSED_CARTESIAN charts use guarded ChartSpec v8 and PlotPlan v11, including
single-layer and empty charts. This envelope takes precedence over the Cartesian ZERO and
composition envelopes. The same source-order adjacency, series/facet ownership and GAP/CONNECT/ZERO
rules apply. Raw channels and resolved zero coordinates remain semantic X/Y; renderers map Y
horizontally and X vertically through the selected shared primary linear axes. Reversal changes
physical direction without changing ownership. ASPECT_RATIO is optional and preserves physical
Y/X unit sizes when present, including independent facets and relayout.

The transposed SVG adapter retains the full semantic data array on connected layers. LINE paths
consume typed pairs; AREA paths fill adjacent baseline or ribbon strips. Ordinary layers use the
same physical axes and placement viewport, including nudged points and their error bars.
Ordinary LINE/AREA layers require the supported GAP forms, including when
ASPECT_RATIO is omitted. With ASPECT_RATIO, ordinary LINE/AREA accepts JITTER, EM/BAND/DATA nudges
and SMOOTH/STEP_BEFORE/STEP_AFTER interpolation. Without it, these ordinary layers retain IDENTITY
and LINEAR. Ordinary confidence AREA also requires ASPECT_RATIO and its guarded v13/v16 contract. Core and contract validation reject arbitrary baselines and confidence on conditioned layers.
Terminal LINE and scalar AREA transpose the graphical axes; ribbons retain raw bound descriptions.
Ordinary rules retain every distinct threshold, and text retains its annotation table, even without
ASPECT_RATIO. The fallback uses the same semantic selection as those terminal descriptions.
Titles and fallback identify orientation and imputed nulls. Conformance checks coordinate kind
alongside selected axes, connections and zero geometry. Sampling cannot replace a connected
layer's endpoint array; `ConnectedSamplingTests` records that compatibility correction.
Connected compositions paint ordinary points in authored Z_INDEX order; they no longer promote
every point layer above connected paths. `ConnectedRenderOrderTests` checks both priorities in
Cartesian/transposed charts and facets, with the correction recorded in `BREAKING_CHANGES.md`.

`TransposedConnectedTests` covers all three policies, optional aspect, series/facets, reversed
axes, mixed policies and ordinary point placement, raw-null/all-null/singleton inputs, resize,
authoring/designer/contracts, lineage, static exports, malformed envelopes and deterministic
plan/SVG fixtures. Rename, Chromium and existing workload budgets cover both orientations.
Connected placement, stacking, secondary axes and other coordinate
families remain separate roadmap extensions; the implemented supported forms are complete within their declared scope.

## Row symbols and annotations

SIZE, SHAPE and TEXT conditions on LINEAR LINE/AREA forms use guarded ChartSpec v9 and
PlotPlan v12. This envelope takes precedence over other connection envelopes, including empty and
all-null data. `ResolvedMarkLayer.ConnectionDecorations` preserves the declared intent when no
condition matches. Every complete row carries `ResolvedDatum.ConnectionDecoration` with semantic
X/Y, symbol radius in pixels, a normalized portable shape and optional typed text. Ribbons anchor
on the authored Y_END, including crossing bounds. No midpoint or sorted bound is substituted.
Scalar ZERO anchors a raw null Y at zero; missing X or a required ribbon bound has no decoration.
Final and isolated complete rows retain their symbols and annotations without inventing a connection.

SIZE accepts numeric or null results, including signed numeric literals, and clamps radius to 2–30
pixels before floating-point-to-decimal conversion. Null or unmatched SIZE uses SYMBOL_SIZE or the
3-pixel default. SHAPE accepts CIRCLE, SQUARE, TRIANGLE, DIAMOND, CROSS and STAR, case-insensitively;
null or unmatched SHAPE uses the layer/global symbol shape or CIRCLE. Invalid authored or resolved
SIZE/SHAPE kinds are rejected, including on incomplete rows.
TEXT uses the existing typed display format. Absent TEXT permits ordinary DATA_LABELS; explicit
null, empty or whitespace TEXT suppresses the annotation. Supplied text renders even with
DATA_LABELS off. SYMBOLS off hides symbols while preserving explicit annotations. SIZE controls
symbol radius, not path width or annotation font. Row COLOR/OPACITY apply to its decoration;
connection COLOR/OPACITY still belong exclusively to the outgoing source row.

Native/static SVG shares connection ownership text in an accessible layer group and keeps row
titles on paths. It omits redundant default attributes to stay within existing byte budgets.
Symbols retain raw coordinate titles, normalized shape/size and text or suppression descriptions.
Collision-occluded annotations remain in SVG descriptions. Terminal and fallback retain every raw
row, including missing anchors and requested conditional values. Validation recomputes decorations
from raw coordinates, row encodings and defaults, rejects lost/invented metadata and downgrades,
and conformance compares declared intent and every decoration. Relayout changes physical mapping
without changing anchors or presentation. `ConnectedDecorationTests`, LSP rename and Chromium
cover these behaviors; the 600-row/12-panel workload budgets remain unchanged.

## Resolved interpolation

Conditioned SMOOTH, STEP_BEFORE and STEP_AFTER use guarded ChartSpec v10 and PlotPlan v13,
including empty/all-null charts. This envelope takes precedence over other connection envelopes.
`ResolvedMarkLayer.ConnectionInterpolation` records non-linear intent; each connection carries
`ResolvedConnectionGeometry` with an ordered Upper boundary, optional Lower boundary and a Cubic
flag. Points remain in semantic X/Y space. LINE omits Lower; scalar AREA uses zero; ribbons use
authored Y_START/Y_END without sorting crossing bounds. LINEAR retains its prior wire shape.

STEP_BEFORE changes semantic Y at the source X, then moves to the destination X. STEP_AFTER moves
along semantic X at the source Y, then changes Y. Transposition and reversal map these same corners;
they do not reinterpret before/after in screen coordinates. Each ribbon boundary uses the same
policy and source order; its lower boundary is traced backward only when closing the rendered strip.

SMOOTH uses the existing Catmull-Rom cubic construction with one-sixth neighbor differences.
For a source P and destination Q, controls are P + (Q - previous)/6 and
Q - (next - P)/6, independently for semantic X/Y and both AREA boundaries. Run endpoints repeat
themselves for missing outer neighbors. Runs with two rows remain straight; isolated rows have no
connection. Neighbors are selected after null handling within the same layer, series and facet.
GAP resets the run; CONNECT uses surviving neighbors; ZERO uses resolved scalar zeros and keeps
missing X/ribbon bounds as gaps. Presentation changes never restart a curve or change its tangents.

Raw rows, endpoint-derived domains, decorations and condition values remain unchanged. Curves can
overshoot the endpoint range; controls do not expand scale domains. Independent numeric facets
retain each explicit MIN/MAX and infer only unspecified bounds, with existing INCLUDE_ZERO rules.
That correction has behavioral SVG tests and a breaking-change entry.

Validation recomputes the geometry and rejects lost/invented boundaries, mismatched interpolation,
wrong run neighbors and downgraded envelopes. Conformance compares the layer flag and point arrays
by value, including after serialization. Fallback conformance also compares ordered content by value.
Relayout changes physical mapping only. Native/static SVG maps the stored controls and corners,
preserves one source-owned path per connection, and shares interpolation labels and line width/dash
in accessible groups. Terminal LINE/scalar AREA samples stored curves or traces steps; ribbons
retain their raw bound table. All descriptions name interpolation and source ownership.

`ConnectedInterpolationTests` covers geometric controls, all policies, orientations, optional aspect,
series/facets, reversal, mixed layers, raw values, empty/two-row inputs, authoring/designer/contracts,
lineage, PDF/Markdown, conformance and deterministic plan/SVG/terminal fixtures. LSP and Chromium
cover the same vocabulary; the decorated 600-row/12-panel allocation, time and byte budgets pass.
Ordinary transposed aspect LINE interpolation uses separate display-space intent in ChartSpec v11
and PlotPlan v14; mixed conditioned/ordinary charts select that newer envelope. Ordinary transposed
aspect AREA interpolation uses separately interpolated display-space boundaries in ChartSpec v12 and PlotPlan v15.

## Segment ownership

A row owns the outgoing connection to the next eligible row in its resolved layer and facet.
The condition on that source row determines the entire connection's presentation. The destination
row never supplies a missing conditional value for the incoming connection. The final row has no
outgoing connection, but retains its own point, label, raw values and accessible description.

For example, rows A, B and C with conditional colors red, blue and green produce a red A-to-B
connection and a blue B-to-C connection. C's point is green when point symbols are enabled.
This rule is independent of ascending or descending X, axis reversal and transposition. Ownership
follows resolved row order, not screen direction or the numeric order of endpoints.

For an AREA, a connection is the strip between two adjacent cross-sections: the source and
destination upper boundaries, followed by their lower boundaries in reverse order. Ordinary
areas use the resolved baseline; ribbons use their resolved lower and upper endpoints. A row
does not own the whole polygon. Adjacent strips share their cross-section without an extra stroke
along the internal edge. Opacity applies once to each strip, including at changes of style.

Conditions remain presentation-only. They cannot filter, reorder, aggregate, split series, move
vertices, alter stacking totals, or change domains. A transparent connection is still present in
the semantic plan and accessible output.

## Conditions and connectivity

Keep the existing predicate evaluation and per-channel precedence in
`PlotPlanResolver.ResolveConditions`: evaluate conditions in source order; the first condition
that supplies a THEN or ELSE value wins. No supplied value means the ordinary layer/series
presentation applies. Do not borrow a value from a neighboring row.

Resolve connectivity after series and facet partitioning and null handling. GAP breaks the run;
CONNECT joins the surviving neighbors. ZERO uses the separately resolved numeric geometry described
above. An absent required X or ribbon endpoint remains a gap. An isolated row has no connection.
Duplicate coordinates retain row identity and ordering, even when the resulting connection has
zero length. Never connect different layers, series or facets.

COLOR and OPACITY apply to connections. SIZE, SHAPE and TEXT describe row decorations, not
connection width, interpolation or area boundaries. Support for a channel must include its
visible decoration and accessible meaning; reject any unsupported channel explicitly.

## Neutral representation and compatibility

Add a typed, immutable connection representation to the resolved contract. Each connection must
identify both endpoint rows within its resolved layer, its owning source row, and its resolved
presentation. Renderers consume these connections; they do not evaluate predicates or choose
endpoint precedence. Relayout may change physical coordinates but must preserve connectivity,
ownership and presentation.

Do not reuse `ResolvedDatum.SegmentColor` as the new contract. The current native LINE path reads
the destination value first and falls back to the source; `VisualBuilder` uses those fields for
existing segment styles. Reinterpreting them would silently change existing output. Retain that
behavior for existing plans unless a separately documented compatibility change is intended.

Guard plans carrying the new semantics with a versioned envelope that older readers reject.
The current radial v4 envelope permits only stacked polar ARC layers, so it cannot silently be
repurposed for connected Cartesian marks. Connected plans use v5. Existing plans retain their wire
shape and golden output. Test both accepted and rejected envelopes. ChartSpec validation must
guard new intent as well as PlotPlan output before authoring is enabled.

## Implementation order and evidence

The supported Cartesian/transposed LINE, zero-baseline AREA and ribbon combinations described
above are implemented. Further placement, stacking, axis and coordinate forms are separately bounded
roadmap increments. Select one named form, move only that task into TODO.md and keep neighboring
combinations rejected. Completing a selected increment does not expand its scope or create a
requirement to implement every remaining combination.

Each increment requires:

- Matching Core and ChartSpec validation, positioned diagnostics, and explicit negative cases.
- Parser/formatter and designer round trips; predicate-field lineage and LSP rename.
- Deterministic connection and SVG goldens. Assert endpoint ownership directly, including a
  conditional value absent at one endpoint, three alternating styles, gaps, duplicates and a final
  row with a different style.
- SVG geometry and point/label behavior, static PDF export, terminal descriptions and accessible
  fallback that identify both endpoints and the source-owned style without relying on color alone.
- Facet/series isolation and relayout invariance where supported. Conditions must leave raw values,
  scales, stacking totals and ordinary row fallback intact.
- Version compatibility and unchanged existing chart goldens, payload budgets, render-time budgets
  and browser bundle budgets. Update the capability matrix and focused authoring reference only
  when the combination is accepted by the parser and implemented by the backends.

## References

- [Native advanced chart authoring](native-advanced-chart-authoring.md)
- [Reporting semantic contracts](../reporting-semantic-contracts.md)
- [Chart reference](../../reference/visuals-reporting/visuals/chart.md)
- [Remaining extension backlog](../../../ROADMAP.md#reporting--presentation--grammar-of-graphics-semantic-extensions)
