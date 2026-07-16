PRINT writes a message to the console output or execution log.

Syntax:
  PRINT <expression> [, TIMESTAMP = TRUE | FALSE];

The expression can be a string literal, a variable, or a string expression. Non-string values are converted automatically.

```sql
-- Simple message
PRINT 'Starting load...';

-- With variable interpolation
DECLARE @count INT = 0;
SELECT COUNT(*) INTO @count FROM #staging;
PRINT 'Rows loaded: ' + @count;

-- With timestamp prefix
PRINT 'Checkpoint reached', TIMESTAMP = TRUE;

-- Progress inside a loop
FOR @i = 1 TO 5 BEGIN
  PRINT 'Step ' + @i + ' of 5', TIMESTAMP = TRUE;
END;
```

In headless mode PRINT writes to stdout. In the TUI editor messages appear in the output panel. In scheduled jobs they are captured in the execution log.

References:
- [Grammar](../../../guides/getting-started.md)
