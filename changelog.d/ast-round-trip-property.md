### Added

- **`AstRoundTripPropertyTests` — no clause may disappear when a statement is serialized back to
  SQL.** The round-trip tests that existed were written per feature, by whoever added the feature,
  so a clause added later had none. That is how `ON FAILURE` came to be dropped entirely by
  `ToSql()`: the script still parsed and routed its `@fail: 'QUARANTINE'` rows nowhere.

  Rather than compare ASTs — which differ in source positions and would need a bespoke comparer per
  node — it asserts the weaker but broadly applicable property that every keyword in the input
  survives serialization, since a dropped clause always loses its keyword. Sixteen statement forms
  are covered, and reintroducing the original defect makes it report
  `ToSql() dropped ON, SCRIPT, THROW, TO, WITH`.

  Keywords the serializer legitimately normalizes away (`AS`, `INNER`, `OUTER`, `ROWS`) are listed
  explicitly with a reason each, so the list cannot quietly grow to silence a real failure. Running
  it over the sixteen forms found no further dropped clauses.
