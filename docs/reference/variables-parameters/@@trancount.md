# @@TRANCOUNT
Current transaction nesting depth. 0 means no active transaction; 1 means one open transaction; values greater than 1 mean nested transactions.

```sql
PRINT 'Before: ' + @@TRANCOUNT;   -- 0

BEGIN TRANSACTION;
PRINT 'Open: ' + @@TRANCOUNT;     -- 1

BEGIN TRANSACTION;
PRINT 'Nested: ' + @@TRANCOUNT;   -- 2

COMMIT;
PRINT 'After inner commit: ' + @@TRANCOUNT;  -- 1

ROLLBACK;
PRINT 'After rollback: ' + @@TRANCOUNT;      -- 0
```

ROLLBACK always rolls back to the outermost transaction regardless of nesting depth. COMMIT only decrements the nesting level; only the outermost COMMIT makes changes durable.

References:
- [Standard Library](../../guides/onboarding/getting-started.md)
