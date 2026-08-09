# @@VERSION
Full engine version string including the build number, target framework, and runtime information.

```sql
PRINT @@VERSION;
-- Example: ETL-SQL 3.2.1 (build 20241105) | .NET 9.0.1 | win-x64

-- Log the version with each script run
INSERT INTO dbo.RunLog (run_at, engine_version)
VALUES (GETDATE(), @@VERSION);
```

Query `eng.version` for structured component/version rows. `@@VERSION` includes the full build metadata.

References:
- [Standard Library](../../guides/onboarding/getting-started.md)
