# Reporting Semantic Contracts

ETL-SQL owns a renderer-neutral, versioned reporting contract between Report-SQL authoring and every
graphical or semantic output backend. The contract implementation is
`src/ETL-SQL.Reporting.Contracts`; the accepted architectural decision remains
[`GrammarOfGraphicsSpecIR.md`](decisions/GrammarOfGraphicsSpecIR.md).

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
- **`ETL-SQL.Reporting`** references both lower layers. It will own named-visual lowering, deterministic
  plan resolution, renderer adapters, and export implementations.
- **Hosts and backends** consume resolved meaning; they do not choose category order, domains, ticks,
  palette identity, legend order, or null behavior independently.

Architecture tests enforce these boundaries against both project files and contract source.

## Contract levels

### `ChartSpec`

`ChartSpec` expresses author intent. It carries a schema URI and integer version plus typed field
bindings, ordered semantic mark layers, coordinate and scale intent, scale-resolution policy,
formatting, null behavior, interactions, themes, and accessibility metadata. Mark layers use the
portable vocabulary `Rect`, `Line`, `Area`, `Point`, `Rule`, `Arc`, and `Text`.

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

`PlotPlan` is the deterministic, renderer-neutral resolved contract. It carries ordered scales and
ticks, category order, ordered series, palette assignments, legend entries, ordered resolved layers,
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

## Cross-backend conformance

A backend implements `IPlotPlanSemanticBackend` and projects its effective interpretation into
`PlotSemanticProjection`. `PlotPlanConformanceHarness` compares that projection with the authoritative
plan and attributes drift to scales, series order, palette, legend, layers, nulls, accessibility, or
fallback behavior. Phase 3 renderer adapters will plug into this harness; Phase 2 proves the harness
itself with conforming ECharts, native-SVG, and terminal probes plus intentional-drift tests.

## References

- [Grammar-of-Graphics ADR](decisions/GrammarOfGraphicsSpecIR.md)
- [Source Boundary Standards](standards/Source_Boundary_Standards.md)
- [Report-SQL Guide](../guides/feature-guides/report-sql.md)
