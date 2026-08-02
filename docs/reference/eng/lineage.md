# `eng.lineage`

Current-session table and column lineage events, including sources, transformations, locations, and metadata.

```sql
SELECT * FROM eng.lineage WHERE TargetTable = '#orders';
```

Columns: `Timestamp`, `Operation`, `TargetTable`, `TargetColumn`, `SourceTables`, `SourceColumns`, `Description`, `Metadata`, `DerivedFromDescriptions`, `SourceFile`, `Line`, `Column`, `TransformationKind`, `TransformationExpression`, `FunctionsApplied`.

## References

- [Lineage](../statements/session-control/lineage.md)
- [Engine Catalog](README.md)
