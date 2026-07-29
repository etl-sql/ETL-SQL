# WAITFOR

Suspends script execution for a duration, until a specific time, or until a condition becomes true.

## Syntax
```sql
WAITFOR DELAY 'hh:mm:ss';
WAITFOR TIME  'hh:mm:ss';
WAITFOR (condition);
WAIT UNTIL condition;
```

## Forms
- **DELAY** — wait for the specified duration (hours:minutes:seconds)
- **TIME** — wait until the wall-clock reaches the given time today
- **(condition)** — poll the expression/subquery at 200ms intervals until it returns a truthy value
- **WAIT UNTIL** — preferred alias for condition polling

## Examples
```sql
-- Wait 30 seconds between retries
WAITFOR DELAY '00:00:30';

-- Wait until midnight before starting a batch
WAITFOR TIME '00:00:00';

-- Poll until a table has data
WAITFOR (SELECT COUNT(*) FROM dbo.Incoming) > 0;

-- Preferred condition-polling spelling
WAIT UNTIL (SELECT COUNT(*) FROM #Incoming) > 0;
```

> [!NOTE]
> `WAIT UNTIL condition` is the preferred alias for `WAITFOR (condition)`.

References:
- [Control Flow](README.md)
- [WAIT UNTIL](wait-until.md)
