# TRANSACTION

Transactions group multiple DML operations into an atomic unit. If any statement fails the entire group can be rolled back.

## Syntax

```sql
BEGIN TRANSACTION;
  ...
COMMIT;     -- or ROLLBACK;
```

## Nesting

Transactions can be nested. Each `BEGIN TRANSACTION` increments `@@TRANCOUNT`; `COMMIT` decrements it. `ROLLBACK` always rolls back the outermost transaction regardless of nesting depth.

```sql
BEGIN TRANSACTION;

  INSERT INTO dbo.Orders (id, amount)
    SELECT id, amount FROM #staged;

  UPDATE dbo.Accounts SET balance = balance - @total WHERE id = @account_id;

  IF @@ERROR <> 0 BEGIN
    ROLLBACK;
    THROW 'Transaction failed; rolled back.';
  END;

COMMIT;
PRINT 'Committed ' + @@ROWCOUNT + ' rows.';
```

Use TRY/CATCH to handle errors inside transactions:

```sql
BEGIN TRY
  BEGIN TRANSACTION;
    DELETE FROM dbo.Temp WHERE expired = 1;
    INSERT INTO dbo.Archive SELECT * FROM dbo.Temp WHERE exported = 1;
  COMMIT;
END TRY
BEGIN CATCH
  ROLLBACK;
  THROW;
END CATCH;
```

References:
- [Grammar](../../../guides/getting-started.md)
