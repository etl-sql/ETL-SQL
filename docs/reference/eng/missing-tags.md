# `eng.missing_tags`

Newest durable lineage targets missing required stewardship tags.

```sql
SELECT * FROM eng.missing_tags LIMIT 100;
```

Columns: `TargetTable`, `TargetColumn`, `MissingTags`, `PresentTags`, `RunAt`, `JobName`, `ScriptPath`.

## References

- [Lineage](../statements/session-control/lineage.md)
- [Engine Catalog](README.md)
