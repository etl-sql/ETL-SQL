# @@ERROR
Integer error code from the most recently executed statement. 0 means the statement succeeded; any other value indicates an error.

Set by: every statement execution.
Scope:  reset after each statement — read it immediately after the statement you want to check.

```sql
INSERT INTO dbo.Audit (event) VALUES ('load started');
IF @@ERROR <> 0 BEGIN
  THROW 'Failed to write audit record.';
END;

-- In a TRY/CATCH block
BEGIN TRY
  DELETE FROM dbo.Temp WHERE expired = 1;
END TRY
BEGIN CATCH
  PRINT 'Error code: ' + @@ERROR;
END CATCH;
```

References:
- [Standard Library](../../guides/onboarding/getting-started.md)
