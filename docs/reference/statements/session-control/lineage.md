# LINEAGE

Tracks column-level data provenance across all `SELECT`, `INSERT`, `UPDATE`, and `MERGE` operations in a script. Records how each output column was derived, including which source tables and columns it came from, what transformation was applied, and any governance tags attached along the way.

## Syntax

```sql
-- Query lineage for all tables in the session
SELECT * FROM eng.lineage;

-- Query lineage for a specific table
SELECT * FROM eng.lineage WHERE target_table = '#target_table';

-- Query lineage for a specific column
SELECT * FROM eng.lineage
WHERE target_table = '#target_table' AND target_column = 'revenue';

-- Store lineage rows in a temp table
SELECT * INTO #lineage FROM eng.lineage;
```

## Semantics

The engine automatically records provenance edges during script evaluation:
- **Engine context** — Transformations across `#temp` tables and file sources capture fine-grained column lineage and applied expressions.
- **Node prefixes** — Tables appear as `#temp` or `db.table`, datasets as `dataset:&name`, and visuals as `report:Name`.
- **Immutability** — Lineage generated during script execution is append-only and cannot be altered or deleted.

## Columns

The `eng.lineage` virtual table exposes recorded provenance events:

| Column | Description |
| :--- | :--- |
| `timestamp` | When the lineage event was recorded |
| `operation` | SELECT, UPDATE COLUMN, MERGE UPDATE, MERGE INSERT, BULK INSERT, etc. |
| `target_table` | Destination table or temp table |
| `target_column` | Destination column (null for table-level entries) |
| `source_tables` | Comma-separated list of source tables |
| `source_columns` | Comma-separated list of source columns |
| `description` | Value of the `@d` tag if set |
| `metadata` | JSON of all tags on the column |
| `derived_from_descriptions` | `@d` values inherited from source columns |
| `source_file` | Script file name |
| `line` / `column` | Source location |
| `transformation_kind` | Classification of the expression (PassThrough, Cast, Aggregation, etc.) |
| `transformation_expression` | Raw SQL of the expression (omitted for PassThrough) |
| `functions_applied` | Comma-separated list of function names in the expression |

## Transformation Kinds

| Value | Meaning |
| :--- | :--- |
| `PassThrough` | Direct column reference with no transformation |
| `Literal` | Constant value |
| `Cast` | CAST, CONVERT, TRY_CAST, TO_DATE, etc. |
| `FunctionCall` | Any scalar function |
| `Aggregation` | SUM, COUNT, AVG, MIN, MAX, STRING_AGG, etc. |
| `WindowFunction` | ROW_NUMBER, RANK, LAG, LEAD, etc. (OVER clause) |
| `CaseExpression` | CASE WHEN ... END |
| `Conditional` | COALESCE, ISNULL, IIF, NVL, NULLIF, etc. |
| `Arithmetic` | +, -, *, /, % operators |
| `StringOperation` | String concatenation (+ or \|\|) |
| `Subquery` | Scalar subquery |
| `Unknown` | Expression type not classified |

## Examples

```sql
-- Find all PII-carrying columns across the session
SELECT target_table, target_column, metadata
FROM eng.lineage
WHERE JSON_VALUE(metadata, '$.pii') = 'true';

-- Find all aggregations applied
SELECT target_table, target_column, transformation_kind, functions_applied
FROM eng.lineage
WHERE transformation_kind = 'Aggregation';
```

## References

- [EXPORT LINEAGE](export-lineage.md)
- [IMPORT LINEAGE](import-lineage.md)
- [Governance Tags](governance-tags.md)
- [Configuration Settings](config.md)
- [`eng.lineage` Table](../../eng/lineage.md)
- [Statement Reference](../README.md)
