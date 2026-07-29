# WAITFOR

Suspends script execution for a duration or until a specific wall-clock time. Use `WAIT UNTIL` for condition polling.

## Syntax
```sql
WAITFOR DELAY 'hh:mm:ss';
WAITFOR TIME  'hh:mm:ss';
WAIT UNTIL condition;
```

## Forms
- **DELAY** — wait for the specified duration (hours:minutes:seconds)
- **TIME** — wait until the wall-clock reaches the given time today
- **WAIT UNTIL** — condition polling; this is separate from `WAITFOR`

## Examples
```sql
-- Wait 30 seconds between retries
WAITFOR DELAY '00:00:30';

-- Wait until midnight before starting a batch
WAITFOR TIME '00:00:00';

-- Poll until a table has data
WAIT UNTIL (SELECT COUNT(*) FROM #Incoming) > 0;
```

> [!NOTE]
> `WAITFOR (<condition>)` has been retired. Use `WAIT UNTIL condition`.

References:
- [Control Flow](README.md)
- [WAIT UNTIL](wait-until.md)
