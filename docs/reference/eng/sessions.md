# `eng.sessions`

Persisted engine sessions and their size, activity, and ownership metadata.

```sql
SELECT * FROM eng.sessions ORDER BY LastModified DESC;
```

Columns: `SessionId`, `Created`, `LastModified`, `Size_MB`, `TempTables`, `Variables`, `LastScript`, `User`, `Machine`.

## References

- [Engine Catalog](README.md)
