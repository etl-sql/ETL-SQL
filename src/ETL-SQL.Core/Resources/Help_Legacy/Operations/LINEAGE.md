# LINEAGE

Tracks column-level data provenance across all SELECT, INSERT, UPDATE, and MERGE operations in a script. Records how each output column was derived — which source tables and columns it came from, what transformation was applied, and any governance tags attached along the way.

## Viewing Lineage

```sql
-- Show lineage for all tables in the session
SHOW LINEAGE;

-- Show lineage for a specific table
SHOW LINEAGE FOR #target_table;

-- Show lineage for a specific column
SHOW LINEAGE FOR #target_table COLUMN revenue;

-- Store lineage rows in a temp table
SHOW LINEAGE INTO #lineage;
```

### Report Nodes

`CREATE DATASET` and `CREATE VISUAL` statements are automatically tracked. Their nodes appear with distinct prefixes and rendering:

| Node type | Prefix in lineage | Text header | Mermaid shape |
| :--- | :--- | :--- | :--- |
| Temp table / DB table | `#temp` / `db.table` | `[Table: ...]` | Rectangle `[]` |
| Dataset | `dataset:&name` | `[Dataset: ...]` | Cylinder `[()]` |
| Report visual | `report:Name` | `[Visual: ...]` | Rounded rect `()` |

This enables end-to-end tracing: `CRM.dbo.Orders → #orders → dataset:&daily_sales → report:SalesChart`.

## Querying the LINEAGE Virtual Table

The `LINEAGE` virtual table exposes every recorded lineage event as rows:

```sql
SELECT * FROM LINEAGE;

-- Filter to a specific target
SELECT * FROM LINEAGE WHERE TargetTable = '#summary';

-- Find all PII-carrying columns across the session
SELECT TargetTable, TargetColumn, Metadata
FROM LINEAGE
WHERE JSON_VALUE(Metadata, '$.pii') = 'true';

-- Find all aggregations applied
SELECT TargetTable, TargetColumn, TransformationKind, FunctionsApplied
FROM LINEAGE
WHERE TransformationKind = 'Aggregation';
```

### LINEAGE Table Columns

| Column | Description |
| :--- | :--- |
| `Timestamp` | When the lineage event was recorded |
| `Operation` | SELECT, UPDATE COLUMN, MERGE UPDATE, MERGE INSERT, BULK INSERT, etc. |
| `TargetTable` | Destination table or temp table |
| `TargetColumn` | Destination column (null for table-level entries) |
| `SourceTables` | Comma-separated list of source tables |
| `SourceColumns` | Comma-separated list of source columns |
| `Description` | Value of the `@d` tag if set |
| `Metadata` | JSON of all tags on the column |
| `DerivedFromDescriptions` | `@d` values inherited from source columns |
| `SourceFile` | Script file name |
| `Line` / `Column` | Source location |
| `TransformationKind` | Classification of the expression (see below) |
| `TransformationExpression` | Raw SQL of the expression (omitted for PassThrough) |
| `FunctionsApplied` | Comma-separated list of function names in the expression |

### TransformationKind Values

| Value | Meaning |
| :--- | :--- |
| `PassThrough` | Direct column reference — no transformation |
| `Literal` | Constant value |
| `Cast` | CAST, CONVERT, TRY_CAST, TO_DATE, etc. |
| `FunctionCall` | Any scalar function |
| `Aggregation` | SUM, COUNT, AVG, MIN, MAX, STRING_AGG, etc. |
| `WindowFunction` | ROW_NUMBER, RANK, LAG, LEAD, etc. (OVER clause) |
| `CaseExpression` | CASE WHEN … END |
| `Conditional` | COALESCE, ISNULL, IIF, NVL, NULLIF, etc. |
| `Arithmetic` | +, -, *, /, % operators |
| `StringOperation` | String concatenation (+ or \|\|) |
| `Subquery` | Scalar subquery |
| `Unknown` | Expression type not classified |

## Governance Tags

Tags are attached to columns and tables using inline SQL comments with the `/* @tagname: value; */` syntax. They are automatically inherited by downstream columns through the lineage graph.

### Standard Tag Library

#### Security & Privacy

| Tag | Type | Values | Purpose |
| :--- | :--- | :--- | :--- |
| `@pii` | boolean | `true` / `false` | Personal Identifiable Information — inherits as `true` if any source is `true` |
| `@phi` | boolean | `true` / `false` | Protected Health Information (HIPAA) |
| `@pci` | boolean | `true` / `false` | Payment Card data (PCI-DSS) |
| `@sensitive` | boolean | `true` / `false` | Sensitive data requiring access controls |
| `@classification` | string | `Public` / `Internal` / `Confidential` / `Restricted` | Data classification tier |
| `@encrypted_at_rest` | boolean | `true` / `false` | Column is stored encrypted |

#### Ownership

| Tag | Type | Values | Purpose |
| :--- | :--- | :--- | :--- |
| `@owner` | string | team or person name | Accountable owner of this data |
| `@domain` | string | e.g. `Finance`, `HR`, `Sales` | Business domain |
| `@steward` | string | data steward name | Person responsible for data quality |
| `@contact` | string | email or Slack handle | Point of contact for questions |

#### Quality

| Tag | Type | Values | Purpose |
| :--- | :--- | :--- | :--- |
| `@freshness` | string | e.g. `daily`, `hourly`, `real-time` | How often this data is refreshed |
| `@sla` | string | e.g. `4h`, `T+1` | Delivery SLA |
| `@quality` | string | `high` / `medium` / `low` / `unverified` | Confidence in data quality |
| `@nullable` | boolean | `true` / `false` | Whether this column can contain NULLs |

#### Documentation

| Tag | Type | Values | Purpose |
| :--- | :--- | :--- | :--- |
| `@d` | string | free text | Human-readable description (core tag) |
| `@example` | string | sample value | Representative example value |
| `@unit` | string | e.g. `USD`, `ms`, `rows` | Unit of measurement |
| `@format` | string | e.g. `YYYY-MM-DD`, `E.164` | Expected format or pattern |

#### Source

| Tag | Type | Values | Purpose |
| :--- | :--- | :--- | :--- |
| `@source_system` | string | e.g. `Salesforce`, `SAP` | Originating system |
| `@source_table` | string | e.g. `dbo.Orders` | Originating table |
| `@source_column` | string | e.g. `cust_id` | Original column name in the source system |
| `@load_pattern` | string | `full_load` / `incremental` / `streaming` | How data is loaded |

### Tag Syntax

Tags go in SQL block comments on the column expression they annotate, using semicolons to separate multiple tags:

```sql
SELECT
    customer_id     /* @d: Unique customer identifier; @pii: true; @owner: CRM Team */,
    email           /* @pii: true; @classification: Confidential; @format: RFC 5321 */,
    SUM(revenue)    /* @d: Total revenue; @unit: USD; @quality: high */ AS total_revenue
INTO #summary
FROM #orders;
```

### Tag Inheritance Rules

1. **Column-level overrides table-level** — column tags win when both exist.
2. **@pii: true wins** — if any upstream column carries `@pii: true`, the derived column inherits `true` regardless of what the expression sets.
3. **@d is not overwritten** — if a column has its own `@d` tag, inherited descriptions are stored in `DerivedFromDescriptions` instead.
4. **Tags accumulate through chains** — tags on `#raw.email` flow to `#cleaned.email` which flows to `#report.email`.

## Script-Level Metadata

Add script metadata in the file header with structured comments. The engine records those tags with lineage entries:

```sql
/* @author: Data Engineering; @pipeline: Daily Sales ETL; @version: 0.7.0; */
```

## Example: Complete Tagged Pipeline

```sql
-- Source table annotation
SELECT
    order_id    /* @d: Unique order identifier; @nullable: false */,
    customer_id /* @pii: true; @d: Customer FK; @owner: CRM */,
    amount      /* @d: Order total; @unit: USD; @quality: high */,
    order_date  /* @d: Order placement date; @format: YYYY-MM-DD */
INTO #orders_raw
FROM CRM.dbo.Orders /* @source_system: Salesforce; @load_pattern: incremental */;

-- Transformation step — pii inherited automatically
SELECT
    customer_id,
    SUM(amount) /* @d: Monthly revenue per customer; @unit: USD */ AS monthly_revenue,
    COUNT(*)    /* @d: Number of orders */ AS order_count,
    MAX(order_date) AS last_order_date
INTO #customer_summary
FROM #orders_raw
GROUP BY customer_id;

-- View the lineage graph
SHOW LINEAGE FOR #customer_summary;

-- Query which columns carry PII
SELECT TargetTable, TargetColumn
FROM LINEAGE
WHERE JSON_VALUE(Metadata, '$.pii') = 'true';

-- Find what transformations were applied
SELECT TargetColumn, TransformationKind, FunctionsApplied
FROM LINEAGE
WHERE TargetTable = '#customer_summary'
  AND TransformationKind <> 'PassThrough';
```

## LINEAGE_TAGS Virtual Table

`LINEAGE_TAGS` exposes every tag as a flat row — one row per tag per lineage entry. This eliminates the need for `JSON_VALUE` gymnastics on the `Metadata` column of the `LINEAGE` table.

```sql
-- Find all PII columns in the session
SELECT TargetTable, TargetColumn
INTO #pii_columns
FROM LINEAGE_TAGS
WHERE TagName = 'pii' AND TagValue = 'true';

-- Audit all classification levels
SELECT DISTINCT TagValue AS classification
INTO #levels
FROM LINEAGE_TAGS
WHERE TagName = 'classification'
ORDER BY classification;

-- Find columns without an owner tag
SELECT DISTINCT l.TargetTable, l.TargetColumn
INTO #unowned
FROM LINEAGE l
WHERE l.TargetColumn IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM LINEAGE_TAGS t
      WHERE t.TargetTable = l.TargetTable
        AND t.TargetColumn = l.TargetColumn
        AND t.TagName = 'owner'
  );
```

### LINEAGE_TAGS Columns

| Column | Description |
| :--- | :--- |
| `TargetTable` | Table the tag is on |
| `TargetColumn` | Column the tag is on (null for table-level tags) |
| `Operation` | SELECT, TABLE_TAGS, UPDATE COLUMN, etc. |
| `TagName` | Tag name (e.g. `pii`, `owner`, `classification`) |
| `TagValue` | Tag value |
| `Scope` | `column` or `table` |
| `Line` | Source line number |
| `SourceFile` | Script file name |

## HAS_TAG() Function

`HAS_TAG(table, column, tag_name [, expected_value])` — predicate that returns 1 if the tag exists (optionally matching a specific value), 0 otherwise. Useful in WHERE clauses against the LINEAGE table.

```sql
-- Filter lineage to PII-tagged columns
SELECT TargetTable, TargetColumn, TransformationKind
INTO #pii_transforms
FROM LINEAGE
WHERE HAS_TAG(TargetTable, TargetColumn, 'pii', 'true') = 1
  AND TransformationKind <> 'PassThrough';

-- Check if a specific column has a tag
SELECT HAS_TAG('#orders', 'email', 'pii') AS is_pii;  -- returns 1 or 0
SELECT HAS_TAG('#orders', 'amount', 'unit', 'USD') AS is_usd;
```

## SHOW TAGS

```sql
-- List all tag events in the session
SHOW TAGS;

-- Capture to a temp table
SHOW TAGS INTO #all_tags;
SELECT * FROM #all_tags WHERE tag_name = 'pii' AND tag_value = 'true';
```

## Cross-Run Lineage Catalog

`SHOW LINEAGE` is scoped to the current session. The catalog stores lineage across all orchestrated runs so you can answer stewardship questions that span many executions.

### SHOW LINEAGE HISTORY FOR TABLE

```sql
-- Local catalog
SHOW LINEAGE HISTORY FOR TABLE Orders;
SHOW LINEAGE HISTORY FOR TABLE Orders LIMIT 50;
SHOW LINEAGE HISTORY FOR TABLE Orders INTO #history;

-- Remote Orchestrator
SHOW LINEAGE HISTORY FOR TABLE Orders AT ProdOrch;
SHOW LINEAGE HISTORY FOR TABLE Orders AT ProdOrch LIMIT 50 INTO #history;
```

Returns all lineage entries that targeted the named table, most recent run first. Columns: `Id`, `RunAt`, `JobName`, `TargetTable`, `TargetColumn`, `SourceTables`, `Operation`, `Tags`, `SourceFile`, `Line`.

### SHOW LINEAGE HISTORY FOR TAG

```sql
-- Local catalog
SHOW LINEAGE HISTORY FOR TAG pii;
SHOW LINEAGE HISTORY FOR TAG pii = 'true';
SHOW LINEAGE HISTORY FOR TAG classification = 'restricted' LIMIT 100 INTO #restricted;

-- Remote Orchestrator
SHOW LINEAGE HISTORY FOR TAG pii = 'true' AT ProdOrch;
SHOW LINEAGE HISTORY FOR TAG classification = 'restricted' AT ProdOrch LIMIT 100 INTO #restricted;
```

Returns all entries whose `Tags` JSON contains the given key, optionally filtered to a specific value.

References:
- [Specialized Operations](../../../../../Docs/Reference/Specialized_Operations.md)
