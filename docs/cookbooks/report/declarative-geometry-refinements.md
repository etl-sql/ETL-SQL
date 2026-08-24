# Declarative Geometry Refinements

[« Back to Report Cookbook](README.md)

Use this pattern when a named visual cannot express a composite cleanly but the report must remain script-first and portable. The complete, copy-pasteable lifecycle script is [`declarative_geometry_refinements.rptsql`](../../../samples/08_Reporting/declarative_geometry_refinements.rptsql).

The sample prepares every derived value in visible SQL before creating visuals. Its two pages cover:

- normalized and grouped bars with explicit stack, offset, band-size, and paint-order semantics;
- forecast ribbons and error ranges using paired interval endpoints;
- deterministic jitter and nudge without changing raw values;
- `DATUM` and `VALUE` constants, inherited encodings, and parameter dependencies;
- sequential/diverging continuous color ranges, wrapped facets, and fixed Cartesian aspect;
- category-local `TICK` targets, rules, conditions, tooltip/detail fields, and highlight interactions;
- titles and semantic fallbacks shared by browser SVG, terminal, PDF/email, Markdown, plain text, and assistive technology.

Run it directly:

```powershell
etl-sql run samples/08_Reporting/declarative_geometry_refinements.rptsql
```

Keep aggregation, lookups, windows, normalization inputs, confidence bounds, and statistical calculations in the SQL preparation section. `CHART` should contain encoding and presentation semantics only.

## References

- [CHART Reference](../../reference/visuals-reporting/visuals/chart.md)
- [Vega-Lite Conversion Guide](../../guides/reporting/vega-lite-to-etl-sql.md)
- [Report-SQL Guide](../../guides/feature-guides/report-sql.md)
