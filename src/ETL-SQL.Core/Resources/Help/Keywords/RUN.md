# RUN SCRIPT
Executes another ETL-SQL script file, optionally passing parameters in or out.

## Syntax
```sql
-- Basic call
RUN SCRIPT 'path/to/subscript.etlsql';

-- Pass input parameters
RUN SCRIPT 'loaders/load_sales.etlsql' WITH (
  @start = @reportStart,
  @end   = @reportEnd
);

-- Capture an output parameter
RUN SCRIPT 'utils/get_row_count.etlsql' WITH (
  @table  = '#orders',
  @count  = @out
);
```

## Notes
- Paths are resolved relative to the calling script's location unless absolute.
- Parameters marked `DECLARE @x TYPE INPUT` in the subscript can be supplied via `WITH (...)`.
- Parameters marked `DECLARE @x TYPE OUTPUT` write back to the caller's variable when listed in `WITH (... @param = @out)`.
- From the CLI: `etlsql run script.etlsql --var @start=D-1` overrides any INPUT variable.
- Scripts can be nested; each level gets its own variable scope.
- See: DECLARE, RETURN, EXECUTE