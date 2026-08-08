# `eng.protected_data_suggestions`

Non-authoritative classifier findings for lineage fields that may need protected-data tags.

```sql
SELECT * FROM eng.protected_data_suggestions WHERE confidence >= 0.8;
```

Columns include run and target identity, suggested tag/value, confidence, evidence, reason, existing tags, and source location.

## References

- [Lineage](../statements/session-control/lineage.md)
- [Engine Catalog](README.md)
