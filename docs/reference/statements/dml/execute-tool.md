# EXECUTE TOOL

Executes a previously registered custom executable tool. Data is streamed into the process's standard input in JSON Lines format and read from its standard output in JSON Lines format, ensuring safe processing of large datasets without exhausting memory.

## Syntax
```sql
EXECUTE TOOL '<ToolAlias>'
[FROM SourceTable]
[INTO TargetTable]
[WITH (Param1 = Value1, Param2 = Value2)]
[EXPECT SCHEMA (Col1 Type, Col2 Type)];
```

## Options
- **ToolAlias** — the name of the tool registered via `CREATE TOOL`.
- **WITH ( ... )** — provides substitution variables for `{ParamName}` tokens present in the tool's `ARGS` option.
- **FROM SourceTable** — the table to stream rows from. Each row is serialized as a JSON object and sent to the tool's standard input.
- **INTO TargetTable** — the table to store output rows in. Output is parsed from the tool's standard output. If this is a new temporary table, it is dynamically created.
- **EXPECT SCHEMA ( ... )** — declares the output schema expected from the tool. Essential when writing to a new `#temp` table so the engine can construct the columns and types correctly.

## Examples
```sql
-- Execute a tool with parameters and data streaming
EXECUTE TOOL 'DataSummarizer'
FROM #raw_logs
INTO #summaries
WITH (batch_size = 500)
EXPECT SCHEMA (category STRING, total_count INT, last_seen DATETIME);

-- Execute a tool that requires no input/output, just runs a process
EXECUTE TOOL 'EnvironmentCleanup';
```

## References
- [Script Composition Standards](../../../architecture/standards/script-composition-standards.md)
- [Statement Reference](../README.md)
