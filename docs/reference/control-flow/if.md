IF evaluates a condition and executes the matching branch. The ELSE branch is optional.

Syntax:
  IF <condition> BEGIN
    ...
  END;

  IF <condition> BEGIN
    ...
  END ELSE BEGIN
    ...
  END;

Conditions support standard comparison operators (=, <>, <, >, <=, >=), IS NULL / IS NOT NULL, IN (...), BETWEEN, LIKE, EXISTS (...), and boolean operators AND, OR, NOT.

```sql
-- Simple guard
IF @@ROWCOUNT = 0 BEGIN
  PRINT 'No rows loaded — check source.';
  RETURN;
END;

-- Branch on variable
IF @mode = 'full' BEGIN
  SELECT * FROM dbo.FullSnapshot INTO #data;
END ELSE BEGIN
  SELECT * FROM dbo.DeltaView WHERE changed_at > @last_run INTO #data;
END;

-- Nested condition with EXISTS
IF EXISTS (SELECT 1 FROM #errors) BEGIN
  THROW 'Errors found — aborting.';
END;
```

References:
- [Grammar](../../guides/getting-started.md)
