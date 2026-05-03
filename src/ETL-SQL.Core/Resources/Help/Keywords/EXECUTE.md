# EXECUTE
Sends a raw command block to an external connection, or runs administrative/portal operations.

## Syntax
```sql
-- Pass-through SQL block to a connection
EXECUTE MyDB BEGIN
  CREATE INDEX idx_tmp ON staging (id);
  UPDATE staging SET active = 1 WHERE loaded_at >= GETDATE();
END;

-- Single-statement shorthand
EXECUTE MyDB 'TRUNCATE TABLE staging';
```

## Notes
- The body between `BEGIN` and `END` is forwarded verbatim to the target connection — ETL-SQL does not parse or validate it.
- Use for DDL, admin commands, or vendor-specific SQL that ETL-SQL does not natively support.
- Output from the remote statement is not captured; use `SELECT ... INTO #table` for data retrieval.
- `EXECUTE` does not open a new transaction — it runs inside any active transaction on that connection.
- For running another ETL-SQL script, see: RUN SCRIPT
- See: RUN, TRANSACTION