# Architecture Decision Record: Native Grammar-of-Graphics Contract and Pluggable Backends

**Status:** Accepted
**Date:** 2026-08-20
**Implementation:** Slices 0 and 1 shipped on 2026-08-21; later slices remain planned.
**Decision scope:** Reporting semantics and renderer boundaries; advanced authoring syntax remains a
separate language-design decision.

## 1. Context

ETL-SQL historically built `VisualManifest` data and ECharts-shaped chart configuration for browser
and server consumers. `EChartsRenderer`, `EChartsSsrRenderer`, `PdfExporter`, `MarkdownRenderer`,
`SvgChartRenderer`, and `TerminalRenderer` then render or reinterpret that state. This makes a vendor
option schema the effective reporting contract and causes separate backends to repeat decisions about
ordering, scales, colors, nulls, and fallbacks.

That coupling limits ETL-SQL's product goals:

- Script-first, reviewable, lineage-aware report definitions.
- Renderer independence and eventual ECharts retirement.
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
.rptsql named visual sugar or future native advanced grammar
                         |
                         v
                 Semantic ChartSpec
                         |
                         v
                  Resolved PlotPlan
               /          |          \
              v           v           v
      ECharts compiler  native SVG  terminal compiler
        (temporary)      compiler     and fallbacks
```

`ChartSpec` describes intent. `PlotPlan` resolves shared deterministic choices. Backends consume the
resolved plan and do not redefine report semantics. ECharts configuration is generated transiently
during migration and is never stored as the canonical semantic report state.

This ADR accepts the architecture and boundaries below. It does not freeze unimplemented `.rptsql`
grammar. Any new `CUSTOM`, layer, scale, coordinate, or facet syntax must follow the language syntax
standards, include minimal parser-accepted examples, and preserve Report Builder round-trip fidelity
before it is documented as supported syntax.

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

### 8.1 Temporary ECharts Compiler

The first migration backend compiles `PlotPlan` to an ECharts option object. This preserves broad
browser behavior while severing the semantic dependency on ECharts. No ECharts option becomes part of
the saved report, AST, or neutral manifest contract.

### 8.2 Native SVG Compiler

Native SVG is the canonical static graphical output. Scale and geometry code lives in the reporting
or rendering layer, not in Core. Text measurement may use the already-approved rendering stack where
needed, but the semantic contracts remain dependency-light.

The native compiler initially covers representative Cartesian, polar, layered, and annotation cases.
Standard visual coverage expands from conformance evidence rather than from an all-at-once rewrite.

### 8.3 Browser Renderer

The browser may render standard `PlotPlan` output through a small SVG/DOM implementation. ECharts can
be omitted only when the report capability matrix proves that every contained visual and interaction
has a native implementation. Bundle-size targets are measured from bundled, minified, and compressed
artifacts using a declared fixture.

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

Prove a versioned `ChartSpec`, typed columnar data, deterministic `PlotPlan`, named visual lowering,
transient ECharts compilation, native SVG, and terminal output with a deliberately varied set:

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

### Slice 4 — Catalog Expansion and ECharts Retirement

Migrate remaining standard visuals in independently testable groups. Evaluate specialized layout
modules for complex charts, classify Gantt from evidence, and remove ECharts/ClearScript only after
the capability matrix, exports, interactions, and regression fixtures no longer require them.

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
- A maintained capability matrix classifying native, semantic-fallback, and temporary ECharts paths.
- Third-party license and inventory compliance for any specialized modules.

ECharts retirement is an outcome of passing this evidence, not a date or size promise embedded in the
architecture.

## 14. Consequences

### Positive

- ETL-SQL owns its semantic reporting contract and can evolve it with its language, lineage, and
  governance model.
- All renderers share deterministic decisions instead of reverse-engineering vendor configuration.
- Native SVG, terminal reporting, micro-charts, and accessibility summaries become coherent consumers
  of one plan.
- Existing named visuals stay simple while advanced composition can grow in native syntax.
- ECharts can be retired incrementally with measurable capability gates.

### Costs and risks

- `ChartSpec` and `PlotPlan` introduce contracts that require versioning and conformance discipline.
- Cross-backend layout still needs carefully bounded target-specific behavior.
- Typed report data changes manifest and serialization assumptions.
- Advanced grammar expands parser, LSP, formatter, documentation, and designer responsibilities.
- Native geometry and specialized layouts require sustained testing that a single vendor renderer
  previously absorbed.

These costs are accepted because a vendor-shaped semantic core would continue to undermine
portability, script-first authoring, and multi-surface reporting.
