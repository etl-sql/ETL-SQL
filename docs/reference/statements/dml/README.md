# DML Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Data Quality Rules (@expect / @fail / ON FAILURE)](data-quality-rules.md) | Column-value validation declared inline on SELECT columns as governance tags, with pluggable |
| [DELETE](delete.md) | DELETE removes rows from a target table. Without WHERE, all rows are removed; prefer TRUNCATE in that case, as it is faster. |
| [EXECUTE TOOL](execute-tool.md) | Executes a previously registered custom executable tool. Data is streamed into the process's standard input in JSON Lines format and read from its ... |
| [insert](insert.md) | INSERT adds new rows to a target table from a SELECT result or a literal VALUES list. |
| [MERGE](merge.md) | Atomic, multi-action upsert statement. Evaluates source records against a target table using matching keys, conditionally updating existing rows, i... |
| [SELECT](select.md) | Retrieves, transforms, and projects rows from connections, in-memory `#temp` tables, subqueries, files, or scalar expressions. Supports inline outp... |
| [TRANSFORM](transform.md) | Applies a table-level transformation algorithm to a source table, writing the output to a target table. |
| [TRUNCATE](truncate.md) | TRUNCATE removes all rows from a table quickly by deallocating storage pages rather than issuing row-by-row deletes. It cannot be filtered with WHE... |
| [UNNEST / FLATTEN](unnest.md) | Table-valued functions that expand a list/array value into rows. Each element becomes one row in a single `Value` column. |
| [UPDATE](update.md) | UPDATE modifies column values in existing rows. Use WHERE to limit which rows are affected; omitting it updates every row. |
