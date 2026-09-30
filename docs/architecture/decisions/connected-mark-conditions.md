# Connected mark condition semantics

Status: partially implemented. Single-layer straight Cartesian LINE, zero-baseline AREA and AREA ribbons accept
COLOR/OPACITY conditions. The remaining combinations are still rejected. TODO item 7 remains open.

The internal `ConnectedMarkResolver.ResolveGapConnections` now resolves adjacency and source-owned
COLOR/OPACITY for an already partitioned layer with GAP handling. Its tests cover ownership, missing
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
ConnectedAreaPlanTests covers signed/reversed geometry, gaps, relayout, authoring, PDF and goldens.

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
SKIP connects the surviving neighbors; ZERO uses the resolved zero geometry. An absent required
X or ribbon endpoint must not become an invented coordinate. An isolated row has no connection.
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

Implement one complete combination at a time. Start with straight, unstacked Cartesian LINE,
quantitative X/Y and GAP handling, covering COLOR and OPACITY. Keep other combinations rejected
until their semantics are implemented. Then cover AREA baseline strips and ribbons, row
decorations, the other null policies, interpolation, stacking, secondary axes, transposition and
facets wherever the underlying geometry is supported. These increments do not redefine completion
of the parent TODO item.

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
- [Implementation ledger](../../../TODO.md#7-grammar-of-graphics-semantic-extensions-candidate)
