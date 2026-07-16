# @@VERSION
Full engine version string including the build number, target framework, and runtime information.

```sql
PRINT @@VERSION;
-- Example: ETL-SQL 3.2.1 (build 20241105) | .NET 9.0.1 | win-x64

-- Log the version with each script run
INSERT INTO dbo.RunLog (run_at, engine_version)
VALUES (GETDATE(), @@VERSION);
```

Use SHOW VERSION for a compact one-line version string. @@VERSION includes the full build metadata.

References:
- [Standard Library](../../../../../Docs/Reference/Standard_Library.md)
