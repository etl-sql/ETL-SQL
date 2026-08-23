# Troubleshooting: Syntax and Dialect Awareness

This guide addresses common dialect mismatches, keyword errors, and query pitfalls encountered when migrating from traditional single-engine databases to ETL-SQL.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS).

## 1. Dialect Keyword Mismatches (`TOP` vs `LIMIT` vs `ROWNUM`)

### Problem
A query like `SELECT TOP 10 * FROM pg_conn.customers` works in SQL Server (SSMS) but fails linting in ETL-SQL.

### Cause
ETL-SQL is **dialect-aware**. The linter validates queries against the target connection type. `TOP` is a T-SQL keyword that is invalid in PostgreSQL and Oracle.

### Solution
- When querying a remote database directly, use its native syntax:
  ```sql
  -- PostgreSQL
  SELECT * FROM pg_conn.customers LIMIT 10;
  ```
- When working with `#temp` tables (ETL-SQL engine context), standard ETL-SQL functions and keywords are always supported:
  ```sql
  SELECT TOP 10 * FROM #staged_data ORDER BY Amount DESC;
  ```
- Push complex native queries via `EXECUTE ... BEGIN ... END` blocks:
  ```sql
  EXECUTE pg_conn BEGIN
      SELECT * FROM customers LIMIT 10;
  END;
  ```

---

## 2. Date and Timestamp Functions (`GETDATE()` vs `NOW()` vs `SYSDATE`)

### Problem
Calling `GETDATE()` against a PostgreSQL or Oracle connection fails.

### Solution
- Use `NOW()` for PostgreSQL connections.
- Use `SYSDATE` for Oracle connections.
- When transforming data engine-side in `#temp` tables, ETL-SQL's built-in `GETDATE()` and `DATEADD()` functions are always available:
  ```sql
  SELECT id, GETDATE() AS StagedAt INTO #staged FROM pg_conn.users;
  ```

---

## 3. String Concatenation and Null Handling (`ISNULL` vs `COALESCE`)

### Problem
`ISNULL(val, default)` fails against PostgreSQL or MySQL connections.

### Solution
Use standard ANSI `COALESCE(val, default)`, which is universally supported across all database connectors and the ETL-SQL engine.

---

## 4. Polling with `WAIT UNTIL`

### Problem
How do I poll a database table until an upstream task marks it 'Ready'?

### Solution
Use `WAIT UNTIL (condition)`:

```sql
-- Polls every 200ms until condition returns truthy
WAIT UNTIL (SELECT COUNT(*) FROM control_db.JobStatus WHERE Status = 'Ready') > 0;
PRINT 'Upstream job ready — continuing pipeline.';
```

For custom polling intervals, combine a `WHILE` loop with `WAITFOR DELAY`:

```sql
DECLARE @ready INT = 0;
WHILE @ready = 0
BEGIN
    SET @ready = (SELECT COUNT(*) FROM control_db.JobStatus WHERE Status = 'Ready');
    IF @ready = 0 WAITFOR DELAY '00:01:00'; -- Poll every 60 seconds
END
```

---

## Related Topics

- [Thinking in Pipelines](../onboarding/getting-started.md) — Engine vs. remote context.
- [Statement Reference](../../reference/statements/README.md) — Statement syntax index.
