TRY/CATCH provides structured error handling. If any statement inside the TRY block throws, execution jumps to the CATCH block.

Syntax:
  BEGIN TRY
    ...
  END TRY
  BEGIN CATCH
    ...
  END CATCH;

Inside the CATCH block:
  @@ERROR          — integer error code
  ERROR_MESSAGE()  — the error message string

```sql
-- Basic error handling
BEGIN TRY
  SELECT * FROM dbo.MightNotExist INTO #data;
  PRINT 'Loaded ' + @@ROWCOUNT + ' rows.';
END TRY
BEGIN CATCH
  PRINT 'Load failed: ' + ERROR_MESSAGE();
  INSERT INTO #errors (msg, ts) VALUES (ERROR_MESSAGE(), GETDATE());
END CATCH;

-- Re-throw after logging
BEGIN TRY
  EXECUTE dbo.RiskyProcedure;
END TRY
BEGIN CATCH
  INSERT INTO dbo.ErrorLog (error, logged_at) VALUES (ERROR_MESSAGE(), GETDATE());
  THROW;
END CATCH;

-- Wrap a transaction
BEGIN TRY
  BEGIN TRANSACTION;
    DELETE FROM dbo.OldData WHERE age > 365;
    INSERT INTO dbo.Archive SELECT * FROM #export;
  COMMIT;
END TRY
BEGIN CATCH
  ROLLBACK;
  THROW;
END CATCH;
```

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
