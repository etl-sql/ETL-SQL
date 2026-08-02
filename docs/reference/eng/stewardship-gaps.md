# eng.stewardship_gaps

`eng.stewardship_gaps` explains every unmet requirement counted by `eng.stewardship_score`. It is read-only and available through local and remote Orchestrator catalog queries.

```sql
SELECT scope_type, scope_name, component, target_table, target_column, requirement,
       source_file, line
FROM eng.stewardship_gaps
WHERE scope_type = 'TABLE';
```

## Columns

| Column | Description |
| :--- | :--- |
| `scope_type` | `GLOBAL`, `JOB`, or `TABLE`. |
| `scope_name` | Scope identifier. |
| `component` | Score component that owns the unmet requirement. |
| `target_table` | Affected target table or asset. |
| `target_column` | Affected column, when applicable. |
| `requirement` | Missing tag or rule, such as `@owner|@steward|@contact`, `@classification`, or `@expect`. |
| `source_file` | Script or scanned schema source when known. |
| `line` | One-based source line when known; legacy catalog entries may report zero. |
| `evaluated_at_utc` | UTC evaluation timestamp shared with the score calculation. |
| `definition_version` | Calculation contract version. |

The table contains metadata only. It never stores failed row samples, protected values, connection strings, or credentials.

## References

- [`eng.stewardship_score`](stewardship-score.md)
- [Engine Catalog](README.md)
- [PII schema scanner](../cli/scan.md)

