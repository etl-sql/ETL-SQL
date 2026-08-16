# IF...ELSE

Conditionally executes a statement block based on the evaluation of a boolean predicate. Supports single-statement blocks, compound `BEGIN...END` blocks, `ELSE IF` chaining, subquery tests (`EXISTS`), and system file predicates (`FILE_EXISTS`).

---

## Syntax

```sql
IF <boolean_condition>
BEGIN
  -- Statements executed when condition is TRUE
END
[ELSE IF <alternate_condition>
BEGIN
  -- Statements executed when alternate condition is TRUE
END]
[ELSE
BEGIN
  -- Fallback statements executed when no preceding conditions match
END];
```

---

## Supported Predicates & Test Expressions

- **Scalar Comparisons**: `=`, `<>`, `<`, `>`, `<=`, `>=`, `!=`
- **Subquery Existence**: `IF EXISTS (SELECT 1 FROM #table WHERE ...)`
- **File System Existence**: `IF FILE_EXISTS('C:\data\incoming.csv')`
- **Nullity & Matching**: `IS NULL`, `IS NOT NULL`, `LIKE`, `ILIKE`, `IN (...)`, `BETWEEN ... AND ...`
- **Logical Connectives**: `AND`, `OR`, `NOT`

---

## Examples

### 1. Dynamic Mode Selection: Full Refresh vs. Incremental Delta

Branch script execution based on input parameters or execution flags:

```sql
DECLARE @execution_mode VARCHAR = 'INCREMENTAL';
DECLARE @last_sync_date DATE = '2026-08-01';

IF @execution_mode = 'FULL'
BEGIN
  PRINT 'Executing full table refresh...';
  TRUNCATE TABLE dest_db.dbo.DimCustomers;
  INSERT INTO dest_db.dbo.DimCustomers SELECT * FROM source_crm.customers;
END
ELSE IF @execution_mode = 'INCREMENTAL'
BEGIN
  PRINT 'Executing incremental delta synchronization...';
  SELECT * INTO #deltas FROM source_crm.customers WHERE updated_at >= @last_sync_date;
  
  MERGE INTO dest_db.dbo.DimCustomers AS T
  USING #deltas AS S ON T.id = S.id
  WHEN MATCHED THEN UPDATE SET T.name = S.name, T.email = S.email
  WHEN NOT MATCHED THEN INSERT (id, name, email) VALUES (S.id, S.name, S.email);
END
ELSE
BEGIN
  THROW 50003, 'Invalid @execution_mode specified. Expected FULL or INCREMENTAL.', 1;
END;
```

### 2. Production Guard: File Invariant & Preflight Verification

Verify that an expected vendor file drop exists and is non-empty before initiating warehouse staging:

```sql
DECLARE @drop_path VARCHAR = 'C:\ftp\inbound\vendor_feed.csv';

-- 1. Check physical file existence
IF NOT FILE_EXISTS(@drop_path)
BEGIN
  PRINT 'WARNING: Vendor file drop not found at ' + @drop_path + '. Skipping ingestion cycle.';
  RETURN;
END;

-- 2. Stage and check row count invariant
CREATE CONNECTION feed AS FLATFILE(PATH=@drop_path);
SELECT * INTO #staged_feed FROM feed.data;

IF (SELECT COUNT(*) FROM #staged_feed) = 0
BEGIN
  PRINT 'Vendor file exists but contains 0 data rows. Logging notification.';
  INSERT INTO alerts_db.dbo.AuditLog (EventType, Message, LoggedAt)
  VALUES ('EMPTY_FILE_DROP', @drop_path, GETDATE());
  RETURN;
END;

PRINT 'Validation passed. Ingesting ' + CAST(@@ROWCOUNT AS VARCHAR) + ' records.';
```

---

## References & Related Recipes

- [Control Flow Reference](README.md)
- [WHILE Loop](while.md)
- [FOREACH Loop](foreach.md)
- [TRY...CATCH Error Handling](try-catch.md)
- [ETL Cookbook: Full Refresh](../../cookbooks/etl/full-refresh.md)
- [ETL Cookbook: Incremental Load With High-Water Mark](../../cookbooks/etl/incremental-load-with-high-water-mark.md)
- [Syntax Index](../../syntax-index.md)
