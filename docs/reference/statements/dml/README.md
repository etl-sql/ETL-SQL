# DML Reference

[« Back to parent](../README.md)

| Page | Description |
| :--- | :--- |
| [Data Quality Rules (@expect / @fail / ON FAILURE)](data-quality-rules.md) | Column-value validation declared inline on SELECT columns as governance tags, with pluggable |
| [DELETE](delete.md) | DELETE removes rows from a target table. Without WHERE, all rows are removed; prefer TRUNCATE in that case, as it is faster. |
| [insert](insert.md) | INSERT adds new rows to a target table from a SELECT result or a literal VALUES list. |
| [MERGE](merge.md) | MERGE performs an upsert. Matching rows are updated; unmatched rows are inserted. Optionally, rows present in the target but absent from the source... |
| [SELECT](select.md) | SELECT retrieves rows from a connection, `#temp` table, subquery, or inline expression. Use `INTO` to write results to a `#temp` table or variable ... |
| [TRUNCATE](truncate.md) | TRUNCATE removes all rows from a table quickly by deallocating storage pages rather than issuing row-by-row deletes. It cannot be filtered with WHE... |
| [UNNEST / FLATTEN](unnest.md) | Table-valued functions that expand a list/array value into rows. Each element becomes one row in a single `Value` column. |
| [TRANSFORM](transform.md) | TRANSFORM applies a table-level transformation algorithm (e.g. FILL_DATES) to a source table. |
| [UPDATE](update.md) | UPDATE modifies column values in existing rows. Use WHERE to limit which rows are affected; omitting it updates every row. |
