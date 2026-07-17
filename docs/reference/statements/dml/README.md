# Statements: DML

Reference pages for Dml in the ETL-SQL engine.

| Name | Description |
| :--- | :--- |
| [DELETE](delete.md) | DELETE removes rows from a target table. Without WHERE, all rows are removed; prefer TRUNCATE in that case, as it is ... |
| [insert](insert.md) | INSERT adds new rows to a target table from a SELECT result or a literal VALUES list. |
| [MERGE](merge.md) | MERGE performs an upsert. Matching rows are updated; unmatched rows are inserted. Optionally, rows present in the tar... |
| [SELECT](select.md) | SELECT retrieves rows from a connection, `#temp` table, subquery, or inline expression. Use `INTO` to write results t... |
| [TRUNCATE](truncate.md) | TRUNCATE removes all rows from a table quickly by deallocating storage pages rather than issuing row-by-row deletes. ... |
| [UNNEST / FLATTEN](unnest.md) | Table-valued functions that expand a list/array value into rows. Each element becomes one row in a single `Value` col... |
| [UPDATE](update.md) | UPDATE modifies column values in existing rows. Use WHERE to limit which rows are affected; omitting it updates every... |

## References

- [Statements Reference](../README.md)
- [Syntax Index](../../../syntax-index.md)

