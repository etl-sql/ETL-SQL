# `eng.data_quality_rules`

Data-quality rules captured from `@expect` and `@fail` metadata in the current session.

```sql
SELECT * FROM eng.data_quality_rules WHERE Action = 'QUARANTINE';
```

Columns: `TargetTable`, `TargetColumn`, `RuleTag`, `Rule`, `Action`, `SourceFile`, `Line`.

## References

- [Data Quality Rules](../statements/dml/data-quality-rules.md)
- [Engine Catalog](README.md)
