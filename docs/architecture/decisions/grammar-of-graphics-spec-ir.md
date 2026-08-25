# Architecture Decision Record: Native Grammar-of-Graphics Contract and Pluggable Backends

**Status:** Accepted
**Date:** 2026-08-20
**Implementation:** Slices 0 and 1, Phase 7 native advanced authoring, and Phase 8 standard-catalog
migration/runtime retirement are shipped.
**Decision scope:** Reporting semantics and renderer boundaries. The accepted advanced language is
recorded in [NativeAdvancedChartAuthoring.md](native-advanced-chart-authoring.md).

## 1. Context

ETL-SQL historically built `VisualManifest` data and ECharts-shaped chart configuration for browser
and server consumers. `EChartsRenderer`, `EChartsSsrRenderer`, `PdfExporter`, `MarkdownRenderer`,
`SvgChartRenderer`, and `TerminalRenderer` then render or reinterpret that state. This makes a vendor
option schema the effective reporting contract and causes separate backends to repeat decisions about
ordering, scales, colors, nulls, and fallbacks.

That coupling limits ETL-SQL's product goals:

- Script-first, reviewable, lineage-aware report definitions.
- Renderer independence and the completed ECharts retirement.
- Consistent semantics across browser, terminal, PDF, email, and future presentation surfaces.
- Native static output without requiring server-side V8.
- Composite charts without adding a bespoke visual keyword for every combination.
- Smaller browser and standalone-report payloads where the report's visual catalog permits it.

ETL-SQL has no external compatibility obligation at the time of this decision. Existing reports and
rendered behavior remain valuable regression fixtures, but accidental syntax and ECharts-shaped
internal state are not contracts that must be preserved.

## 2. Decision

ETL-SQL will make its own typed, immutable, versioned Grammar-of-Graphics contract authoritative for
graphical report meaning. Rendering proceeds through three conceptual levels:

```text
.rptsql named visual sugar or native CUSTOM CHART grammar
                         |
                         v
                 Semantic ChartSpec
                         |
                         v
                  Resolved PlotPlan
               /          |          \
              v           v           v
        browser SVG    static SVG   terminal compiler
          runtime       compiler     and fallbacks
```

`ChartSpec` describes intent. `PlotPlan` resolves shared deterministic choices. Backends consume the
resolved plan and do not redefine report semantics. Phase 8 removed the temporary option compiler;
native SVG is now shared by browser and static output.

This ADR accepts the architecture and boundaries below. The Phase 7 `CUSTOM ... CHART` grammar is
specified separately so future language changes still require parser-tested examples, LSP parity,
lineage, cross-backend conformance, and Report Builder round-trip fidelity.

## 3. Semantic `ChartSpec`

`ChartSpec` answers what a chart means, independent of output technology. Its contract includes:

- A schema/version identifier and stable serialization rules.
- Typed data references and field bindings.
- Semantic mark layers and their z-order.
- Coordinate intent, including Cartesian, transposed, polar, and later geographic coordinates.
- Scale intent and shared/independent scale policies.
- Faceting and composition intent.
- Raw-value formatting and null-handling intent.
- Tooltip, selection, action, and interaction semantics.
- Theme tokens, accessibility labels, and semantic fallback metadata.

The initial semantic mark vocabulary covers `RECT`, `LINE`, `AREA`, `POINT`, `RULE`, `ARC`, and
`TEXT`. It may grow through a language decision when a new analytical capability cannot be expressed
coherently with those marks. Arbitrary SVG `PATH` is a resolved scene primitive, not an initial
author-facing semantic mark: exposing raw paths would couple authors to pixel geometry and weaken
portable rendering.

Named visual types such as `BAR`, `LINE`, `SCATTER`, `PIE`, `DONUT`, `COMBO`, `WATERFALL`, and
`CANDLESTICK` remain the normal authoring path. They lower into `ChartSpec` presets rather than into
renderer-specific configuration.

## 4. Resolved `PlotPlan`

`PlotPlan` is a deterministic, renderer-neutral result of combining `ChartSpec`, typed data, theme,
and requested output constraints. It owns decisions that must not drift between C#, JavaScript, and
terminal implementations:

- Validated data domains and category ordering.
- Series identity and stable palette assignments.
- Scale domains, zero policy, and shared/independent facet resolution.
- Tick locations and formatted labels.
- Null, gap, interpolation, and invalid-value handling.
- Legend entries and ordering.
- Resolved layers, annotations, and accessible summaries.
- Portable plot bounds and layout decisions where the target surface permits them.

The plan may contain scene-level paths and target-neutral geometry after semantic marks have been
resolved. It must retain enough semantic information for terminal and accessibility fallbacks rather
than collapsing immediately into SVG commands.

Layout choices that inherently depend on target font metrics or viewport constraints are expressed as
explicit backend inputs or bounded backend decisions. They are not allowed to change data ordering,
scale meaning, palette identity, or null semantics.

## 5. Typed Data Contract

Chart backends must not infer types independently from string-only rows. The reporting contract
preserves:

- Integer, floating-point, and decimal semantics.
- Dates, times, offsets, and time zones.
- Booleans.
- Null separately from zero and empty string.
- Nominal and ordinal category intent.
- Raw values separately from formatted display values.

Columnar JSON is the preferred first representation for ordinary chart vectors because it is native
to browsers, easy to inspect, and requires no chart-data decoding library. Arrow IPC remains useful
for dense `TABLE` and `MATRIX` data. The crossover between representations is selected from measured
payload, parse, memory, and interaction behavior; this ADR does not establish a permanent row-count
threshold.

### 5.1 Binding-source contract

An encoding has exactly one immutable, serialized source kind:

- **Field** reads a named source column and is written with the existing bare-column form. This is
  the canonical field syntax; ordinary reports do not require a `FIELD(...)` wrapper.
- **Datum** is written as `DATUM(scalar)` and supplies a typed value in the data domain. It may use a
  compatible scale, axis, format, and semantic type exactly as a field datum does.
- **Value** is written as `VALUE(scalar)` and supplies a typed value in the visual range. It bypasses
  scales and axes, and is rejected on positional channels.

The scalar is either a literal or a declared, non-secret variable. Column references, aggregates,
function calls, and arbitrary expressions are rejected so encoding cannot become a hidden
transformation language. A lowered parameter binding retains its parameter dependency and typed
resolved value; secret-bearing parameters fail before serialization. Field sources create column
lineage, literal sources create none, and parameter sources create parameter-dependency metadata.
No constant is represented as a synthetic source column.

Top-level `CHART ENCODINGS` are inherited by layers at compile time. A local channel replaces the
same global channel atomically, including its scale, axis, sort, format, and source. Other global
channels remain. `INHERIT_ENCODINGS = OFF` isolates a layer. Both scopes reject duplicate channels,
and mark/scale validation runs on the resulting effective set. `ChartSpec` always serializes the
complete effective bindings, so renderers never implement inheritance.

### 5.2 Resolved geometry and layout refinements

Placement dimensions remain orthogonal and serialized. `Z_INDEX` determines paint order, `STACK`
determines accumulation, nominal/ordinal offset channels determine dodge slots, and `BAND_SIZE`
determines relative mark thickness. Positive and negative stacks use separate baselines; normalized
stacks retain raw values while emitting resolved endpoints. Standard visual lowering writes these
semantics explicitly. Renderers do not inspect layer names or global `STACKED` flags.
The delivered stack geometry covers quantitative Y/Y2 in Cartesian and transposed Cartesian
coordinates; polar/radial stacking fails validation until a renderer-neutral radial endpoint
contract exists.

`Y_START`/`Y_END` and `X_START`/`X_END` are the general interval channels. AREA requires paired Y
endpoints for a floating ribbon; RULE consumes paired endpoints for a ranged segment. All interval
statistics are prepared in SQL. `TICK` is a separate category-local target/observation mark with a
relative band length and bounded em-relative thickness. It accepts nominal/ordinal X plus
quantitative Y; `AUTO` resolves to a horizontal category-local segment, while explicit horizontal
or vertical orientation remains portable. TICK never inherits RULE's plot-spanning meaning.

Layer position adjustments are typed. Jitter hashes the chart's semantic layer placement, stable
key, channel, and explicit seed with SHA-256, then resolves band-relative display offsets. It rejects
null or duplicate keys. Nudge declares `DATA`, `BAND`, or `EM` units. Both resolve after scales,
stacks, and offset slots and before rendering; raw values, domains, lineage, actions, tooltips,
fallbacks, and exports remain unchanged. Layer display names are deliberately excluded from jitter
identity so rename cannot move marks.

Scale inference is a lowering decision, never a renderer guess. Quantitative positions/sizes use
linear scales, temporal positions use time, categorical RECT/TICK positions use band, categorical
POINT/LINE positions use point, and categorical color/shape uses ordinal. Stable inferred IDs encode
coordinate, primary/secondary axis, and channel. Unsupported or conflicting combinations require an
explicit scale.

Quantitative color uses a linear/log data transform plus a typed sequential or diverging output
range. Portable colors are `#RRGGBB`; interpolation is deterministic sRGB with half-away-from-zero
component rounding, ratios clamp to the domain, nulls use the declared null color, and diverging
midpoints must resolve inside the domain. The plan carries colorbar ticks and an accessible range
description; terminal output uses ordered labeled bins.

One-dimensional `FACET WRAP` is mutually exclusive with the row/column grid. It retains first-seen
category order and resolves row-major panels with an incomplete final row aligned to the start.
Resolution rejects more than 100 panels, more than 1,000,000 panel-row work cells, columns outside
1–12, and panels below 120×110 logical units before allocating panel contracts. Graphical, terminal,
PDF/email, and accessibility consumers receive the same ordered panels.

Cartesian `ASPECT_RATIO` is the physical Y-unit/X-unit ratio after continuous primary domains are
resolved. The resolver subtracts fixed axis/title chrome, fits the maximal rectangle, centers it,
and serializes the viewport; renderers preserve domains and deliberate padding. Polar, transposed,
discrete, secondary-only, degenerate-domain, and undersized forms fail closed until they have a
portable meaning. Facets resolve the same rule independently inside each panel.

## 6. Authoring and Transformation Boundary

Existing named syntax remains the zero-friction path:

```sql
CREATE VISUAL Revenue AS BAR (
  SOURCE = #monthly_revenue,
  MAPPINGS (X = Month, Y = Revenue)
);
```

A later native advanced grammar may add mark layers, scales, coordinates, conditions, and faceting.
It must remain ETL-SQL syntax with normal linting, completion, lineage, formatting, and designer
support.

Heavy transformation does not move into the visual grammar. Aggregation, joins, calculated columns,
running totals, percentiles, moving averages, lookup, filtering, and statistical preparation remain
visible ETL-SQL operations, preferably staged in `#temp` tables for cross-source work. The visual
contract handles encoding, composition, and presentation rather than becoming a hidden data engine.

## 7. Vega-Lite Decision

ETL-SQL will not add first-class embedded Vega-Lite runtime syntax or persist Vega-Lite JSON as report
state. Vega-Lite is a design reference and competitive capability checklist, not a runtime dependency
or second language inside `.rptsql`.

Embedding it would create a second schema, quoting and diff problems, incomplete ETL-SQL linting and
lineage, a separate interaction model, hidden transformations, and an ongoing external compatibility
promise. Partial compatibility would be especially difficult to explain.

ETL-SQL will instead publish a conversion guide mapping common concepts to idiomatic ETL-SQL:

| Vega-Lite concept | ETL-SQL-native direction |
| :--- | :--- |
| `layer` | Native semantic mark layers |
| `facet` / `repeat` | Native facet and composition operators |
| `resolve.scale` | Shared or independent scale policies |
| Conditional encoding | Native encoding conditions |
| Selections and parameters | ETL-SQL variables, actions, and interactions |
| Calculate, aggregate, lookup, window, filter | Visible SQL projections, joins, windows, and `WHERE` |
| Configuration and themes | Report, page, and visual style contracts |

An agent can use that guide to translate a Vega-Lite specification into normal `.rptsql`, explain
where transforms moved, and call out constructs requiring judgment. Broadly useful missing concepts
should be added coherently to ETL-SQL rather than hidden in an importer.

## 8. Backend Strategy

### 8.1 Retired Migration Compiler

The temporary Phase 3 compiler preserved browser behavior while the semantic contract stabilized.
Phase 8 deleted it after the standard-catalog conformance gate passed. No vendor option object is part
of the saved report, AST, manifest contract, or runtime path.

### 8.2 Native SVG Compiler

Native SVG is the canonical static graphical output. Scale and geometry code lives in the reporting
or rendering layer, not in Core. Text measurement may use the already-approved rendering stack where
needed, but the semantic contracts remain dependency-light.

The native compiler covers the standard Cartesian, circular, polar, statistical, financial,
timeline, layered, annotation, and faceted catalog. Focused managed modules cover specialized layouts.

### 8.3 Browser Renderer

The browser imports server-generated native SVG and binds row-indexed marks to actions,
cross-filtering, drill context, and tooltips. The capability matrix proves that every graphical type
has a native or approved focused implementation. Bundle results are recorded from bundled and
compressed artifacts using the declared representative harness.

### 8.4 Specialized Layout Modules

Maps, force networks, Sankey flows, treemaps, and sunbursts may use focused layout modules after the
native contract is stable. Gantt is evaluated separately because time/band scales, rectangles, rules,
text, and dependency paths may fit the native engine.

No D3 module or other package is accepted by this ADR. Each dependency must satisfy the third-party
policy, license inventory, maintenance, transitive-dependency, and necessity checks before adoption.
Any aggregate runtime-size goal is a measured budget, not an architectural promise.

## 9. Terminal and Accessibility Strategy

The terminal target is semantic parity, not pixel parity:

- Rectangles and bars lower to Unicode fractional blocks.
- Lines and areas lower to Braille or block canvases.
- Points lower to distinguishable glyphs.
- Rules lower to labeled box-drawing references.
- Arcs lower to proportional components and gauges.
- Facets lower to coordinated terminal panels where width permits.

Complex visual types receive useful semantic fallbacks:

- Maps become ranked regional breakdowns.
- Sankey diagrams become transition and drop-off tables.
- Treemaps and sunbursts become proportional hierarchy trees/tables.
- Networks become node-degree and connection summaries.

The fallback model is shared with screen-reader summaries, plain-text email, and other non-graphical
surfaces. Rich terminal form controls and keyboard navigation are worthwhile but form a distinct UI
initiative and do not block the GoG foundation.

Implementation status (Phase 5): `PlotPlanTerminalRenderer` lowers the representative native marks
using the forms above and composes first-seen row/column facets at 40-, 80-, and 120-column targets.
`SemanticFallback` is serialized on each visual manifest and carries ordered items plus optional
group, hierarchy level, detail, and summary metadata. Terminal output and Markdown/plain-text delivery
consume that same object; serialized report consumers use it for screen-reader descriptions. Legacy
maps, Sankey diagrams, treemaps/sunbursts, and networks build specialized fallbacks before rendering.

## 10. PDF and Email

Native SVG removes the server-side V8 requirement for supported charts, but delivery targets retain
different constraints:

- **PDF:** The current export path rasterizes SVG to PNG. Removing V8 does not by itself produce vector
  PDF. High-resolution raster output is an explicit acceptable interim state; a direct vector-to-PDF
  bridge can be evaluated separately.
- **Email:** SVG client support is inconsistent. Charts originate from the canonical SVG compiler and
  are delivered as PNG/CID or another broadly supported image form, with accessible text or table
  fallback.

These adapters preserve one semantic compiler without pretending every consumer accepts the same
physical format.

## 11. Custom HTML/SVG Boundary

GoG and custom presentation templates serve different purposes:

- GoG is the escape hatch for custom analytical graphics.
- HTML/SVG templates are the escape hatch for bespoke presentation components such as KPI cards,
  badges, narrative panels, repeaters, and status displays.

Repeated template construction of the same analytical chart indicates a missing mark, encoding,
coordinate, or composition feature. Arbitrary templates cannot be the normal chart path because they
cannot automatically provide semantic terminal output, accessible summaries, lineage-aware bindings,
or reliable PDF/email behavior.

Template support is a separate Zero-Trust boundary. It requires parsed HTML/CSS/SVG, scoped selectors,
element/attribute/property/URL allowlists, resource and recursion budgets, accessibility rules, and
deterministic fallbacks. Removing scripts and inline event handlers alone is insufficient.

## 12. Delivery Plan

### Slice 0 — Authoring Safety

Complete lossless Report Builder mutation behavior for current and future nested presentation syntax.
No new GoG grammar is considered usable until the LSP and embedded authoring paths preserve unrelated
text, trivia, and line endings.

### Slice 1 — Representative End-to-End Contract

This slice originally proved a versioned `ChartSpec`, typed columnar data, deterministic `PlotPlan`,
named visual lowering, native SVG, and terminal output with a deliberately varied set:

- `BAR` for band and linear scales.
- `LINE` for temporal or ordinal values, gaps, and multiple series.
- `SCATTER` for two quantitative axes and point encoding.
- `PIE` or `DONUT` for polar coordinates.
- `COMBO` for layering and dual axes.
- `RULE` for targets and annotations.

At least one PDF or email export path for the representative set must work without server-side V8.

### Slice 2 — Native Micro-Charts and Static Export

Use the same contracts for card/table sparklines and progress indicators. Expand native SVG coverage
only with geometry goldens, typed-data conformance, and portable fallbacks.

### Slice 3 — Native Advanced Authoring

Specify and parser-test the ETL-SQL-native advanced grammar for layering, scales, coordinates,
conditions, and facets. Update LSP, formatter, documentation, samples, and Report Builder mutation
support together. This slice does not add embedded Vega-Lite.

### Slice 4 — Catalog Expansion and ECharts Retirement (Complete)

The remaining standard visuals migrated in independently testable groups. Focused native layout
modules cover the complex charts, Gantt was classified from evidence, and ECharts/ClearScript were
removed after capability, export, interaction, and regression gates passed.

### Slice 5 — Advanced Samples and Conversion Guidance

Add production-grade composite examples and a Vega-Lite-to-ETL-SQL concept guide. Samples keep data
transformation in visible SQL and demonstrate layering, annotations, conditions, facets, interactions,
accessibility, and cross-surface fallbacks.

## 13. Acceptance Evidence

The architecture is complete only when evidence covers:

- Serialization/version compatibility for `ChartSpec` and `PlotPlan`.
- Typed numbers, decimals, temporal values, booleans, categories, nulls, and raw/formatted separation.
- Shared domain, ordering, tick, palette, legend, and null decisions across backends.
- Cross-backend semantic conformance plus visual goldens where pixel output matters.
- Browser, terminal, PDF, email, accessibility, and plain-text fallback fixtures.
- Report Builder and LSP round-trip preservation for every added grammar form.
- Measured bundle size, cold start, export time, output size, and memory on declared workloads.
- A maintained capability matrix classifying native, semantic-fallback, and unsupported paths.
- Third-party license and inventory compliance for any specialized modules.

ECharts retirement followed from passing this evidence; it was not a date or size promise embedded in
the architecture.

Phase 12 resolver, allocation, serialized-plan, and native-SVG measurements are recorded in
[Reporting Phase 12 Refinement Measurements](../../benchmarks/reporting-phase12-refinements.md).
The complete requirement-to-test and measurement index is recorded in
[Reporting Phase 13 Closure Evidence](../../benchmarks/reporting-phase13-closure.md).

## 14. Consequences

### Positive

- ETL-SQL owns its semantic reporting contract and can evolve it with its language, lineage, and
  governance model.
- All renderers share deterministic decisions instead of reverse-engineering vendor configuration.
- Native SVG, terminal reporting, micro-charts, and accessibility summaries become coherent consumers
  of one plan.
- Existing named visuals stay simple while advanced composition can grow in native syntax.
- External chart runtimes can be kept retired with measurable capability gates.

### Costs and risks

- `ChartSpec` and `PlotPlan` introduce contracts that require versioning and conformance discipline.
- Cross-backend layout still needs carefully bounded target-specific behavior.
- Typed report data changes manifest and serialization assumptions.
- Advanced grammar expands parser, LSP, formatter, documentation, and designer responsibilities.
- Native geometry and specialized layouts require sustained testing that a single vendor renderer
  previously absorbed.

These costs are accepted because a vendor-shaped semantic core would continue to undermine
portability, script-first authoring, and multi-surface reporting.
