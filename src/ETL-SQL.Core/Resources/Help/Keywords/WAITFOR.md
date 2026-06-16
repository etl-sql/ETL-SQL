WAITFOR suspends script execution for a duration, until a specific time, or until a condition becomes true.

Syntax:
  WAITFOR DELAY '<hh:mm:ss>';
  WAITFOR TIME  '<hh:mm:ss>';
  WAITFOR CONDITION (<expression>) [TIMEOUT '<hh:mm:ss>'];

Forms:
- **DELAY** — wait for the specified duration (hours:minutes:seconds)
- **TIME** — wait until the wall-clock reaches the given time today
- **CONDITION** — poll the expression; wait until it becomes TRUE, or until TIMEOUT elapses

```sql
-- Wait 30 seconds between retries
WAITFOR DELAY '00:00:30';

-- Wait until midnight before starting a batch
WAITFOR TIME '00:00:00';

-- Poll until a table has data (with 5-minute timeout)
WAITFOR CONDITION (EXISTS (SELECT 1 FROM dbo.Incoming)) TIMEOUT '00:05:00';
IF @@ERROR <> 0 BEGIN
  THROW 'Timed out waiting for data.';
END;
```

Always pair WAITFOR CONDITION with a TIMEOUT to avoid infinite waits.

References:
- [Grammar](../../../../../Docs/Reference/Grammar.md)
