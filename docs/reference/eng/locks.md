# `eng.locks`

Active engine and job-throttle lock records for concurrency diagnostics.

```sql
SELECT * FROM eng.locks ORDER BY AcquiredAt;
```

Columns: `Id`, `ProcessId`, `JobName`, `AcquiredAt`, `MachineName`.

## References

- [Engine Catalog](README.md)
