# `eng.lineage_history`

Durable lineage events captured across orchestrated runs. Qualify the schema with an Orchestrator connection to query a remote catalog.

```sql
SELECT * FROM prod_orch.eng.lineage_history WHERE TargetTable = 'Orders';
```

Columns: `Id`, `RunAt`, `JobName`, `TargetTable`, `TargetColumn`, `SourceTables`, `Operation`, `Tags`, `SourceFile`, `Line`.

## References

- [Lineage](../statements/session-control/lineage.md)
- [Engine Catalog](README.md)
