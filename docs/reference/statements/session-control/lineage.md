# LINEAGE

Tracks column-level data provenance across all SELECT, INSERT, UPDATE, and MERGE operations in a script. Records how each output column was derived, including which source tables and columns it came from, what transformation was applied, and any governance tags attached along the way.

## Querying Lineage

```sql
-- Query lineage for all tables in the session
SELECT * FROM eng.lineage;

-- Query lineage for a specific table
SELECT * FROM eng.lineage WHERE TargetTable = '#target_table';

-- Query lineage for a specific column
SELECT * FROM eng.lineage
WHERE TargetTable = '#target_table' AND TargetColumn = 'revenue';

-- Store lineage rows in a temp table
SELECT * INTO #lineage FROM eng.lineage;
```

## Exporting Lineage

`eng.lineage` returns rows for inspection. Use `EXPORT LINEAGE` for file-writing
OpenLineage exports:

```sql
-- Export the full session lineage
EXPORT LINEAGE AS OPENLINEAGE TO 'exports/run.openlineage.jsonl';

-- Export lineage for a specific target
EXPORT LINEAGE FOR #target_table AS OPENLINEAGE TO 'exports/target.openlineage.jsonl';
```

### Report Nodes

`CREATE DATASET` and `CREATE VISUAL` statements are automatically tracked. Their nodes appear with distinct prefixes and rendering:

| Node type | Prefix in lineage | Text header | Mermaid shape |
| :--- | :--- | :--- | :--- |
| Temp table / DB table | `#temp` / `db.table` | `[Table: ...]` | Rectangle `[]` |
| Dataset | `dataset:&name` | `[Dataset: ...]` | Cylinder `[()]` |
| Report visual | `report:Name` | `[Visual: ...]` | Rounded rect `()` |

This enables end-to-end tracing across nodes such as `CRM.dbo.Orders`, `#orders`, `dataset:&daily_sales`, and `report:SalesChart`.

## Querying the `eng.lineage` Virtual Table

The `eng.lineage` virtual table exposes every recorded lineage event as rows:

```sql
SELECT * FROM eng.lineage;

-- Filter to a specific target
SELECT * FROM eng.lineage WHERE TargetTable = '#summary';

-- Find all PII-carrying columns across the session
SELECT TargetTable, TargetColumn, Metadata
FROM eng.lineage
WHERE JSON_VALUE(Metadata, '$.pii') = 'true';

-- Find all aggregations applied
SELECT TargetTable, TargetColumn, TransformationKind, FunctionsApplied
FROM eng.lineage
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

## Governance Tags

Tags are attached to columns and tables using inline SQL comments with the `/* @tagname: value; */` syntax. They are automatically inherited by downstream columns through the lineage graph.

### Standard Tag Library

#### Security & Privacy

| Tag | Type | Values | Purpose |
| :--- | :--- | :--- | :--- |
| `@pii` | boolean | `true` / `false` | Personal Identifiable Information; inherits as `true` if any source is `true` |
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
| `@freshness` | duration | e.g. `30m`, `1h`, `7d` | Maximum acceptable age for this data |
| `@sla` | string | e.g. `4h`, `T+1` | Delivery SLA |
| `@quality` | string | `gold` / `silver` / `bronze` | Confidence in data quality |
| `@nullable` | boolean | `true` / `false` | Whether this column can contain NULLs |
| `@expect` | string | e.g. `'NOT NULL'`, `'>= 0'` | Enforced data-quality rule — see [Data Quality Rules](../dml/data-quality-rules.md) |
| `@fail` | string | `THROW` / `WARN` / `QUARANTINE` | What happens to a row failing the paired `@expect` rule (default `WARN`) |

`@expect` and `@fail` are *enforced* at runtime, unlike the descriptive tags above: failing rows are
thrown on, warned about, or diverted to a quarantine table, and the per-run counts are recorded on
the job's history. Numbered variants (`@expect_1` / `@fail_1`, …) declare additional rule/action
pairs on the same column.

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
| `@load_pattern` | string | `full` / `incremental` / `cdc` | How data is loaded |

### Governed Stewardship Catalog

The standard tag catalog is typed and shared by linting, runtime `INSERT TAG` validation, editor
metadata hints, and durable lineage catalog queries. Standard tag values are checked case
insensitively. `@classification` accepts `public`, `internal`, `confidential`, or `restricted`;
`@quality` accepts `gold`, `silver`, or `bronze`; boolean tags accept `true` or `false`; and
duration tags use a number followed by `s`, `m`, `h`, or `d`.

Custom organization tags are allowed when their names start with `org_`, `x_`, or `custom_`.
Those tags are intentionally not type-checked by the built-in catalog. The deprecated
`@sensitivity` alias is recognized for compatibility, but linting warns to use
`@classification` instead.

Explicit metadata records use DML-style syntax:

```sql
INSERT TAG FOR TABLE #orders COLUMN customer_id (pii = 'true', owner = 'FinanceOps');
UPDATE TAG FOR TABLE #orders COLUMN customer_id (owner = 'Privacy Review');
DELETE TAG FOR TABLE #orders COLUMN customer_id (owner);
INSERT LINEAGE FOR TABLE #orders FROM 'lineage/openlineage.json';
DELETE LINEAGE FOR TABLE #orders;
```

`DELETE LINEAGE` removes only lineage rows imported with `INSERT LINEAGE`; lineage captured by
executing ETL-SQL statements remains immutable.

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

1. **Column-level overrides table-level**: column tags win when both exist.
2. **@pii: true wins**: if any upstream column carries `@pii: true`, the derived column inherits `true` regardless of what the expression sets.
3. **@d is not overwritten**: if a column has its own `@d` tag, inherited descriptions are stored in `DerivedFromDescriptions` instead.
4. **Tags accumulate through chains**: tags on `#raw.email` flow to `#cleaned.email` which flows to `#report.email`.

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

-- Transformation step; pii inherited automatically
SELECT
    customer_id,
    SUM(amount) /* @d: Monthly revenue per customer; @unit: USD */ AS monthly_revenue,
    COUNT(*)    /* @d: Number of orders */ AS order_count,
    MAX(order_date) AS last_order_date
INTO #customer_summary
FROM #orders_raw
GROUP BY customer_id;

-- View the lineage rows
SELECT * FROM eng.lineage WHERE TargetTable = '#customer_summary';

-- Query which columns carry PII
SELECT TargetTable, TargetColumn
FROM eng.lineage
WHERE JSON_VALUE(Metadata, '$.pii') = 'true';

-- Find what transformations were applied
SELECT TargetColumn, TransformationKind, FunctionsApplied
FROM eng.lineage
WHERE TargetTable = '#customer_summary'
  AND TransformationKind <> 'PassThrough';
```

## Durable Stewardship Queries

Persisted lineage history can be queried for stewardship gaps across runs. This uses the
`ILineageCatalogStore` backing the Orchestrator/Portal lineage catalog and returns the newest row
per target table or column that is missing one or more required stewardship tags:
`@owner`, `@steward`, `@contact`, `@classification`, and `@quality`.

```sql
-- Query the local lineage catalog
SELECT * INTO #missing_stewardship FROM eng.missing_tags LIMIT 100;

-- Query a remote Orchestrator/Portal administration connection
SELECT * INTO #remote_missing FROM prod_orch.eng.missing_tags LIMIT 100;
```

The result includes `TargetTable`, `TargetColumn`, `MissingTags`, `PresentTags`, `RunAt`,
`JobName`, and `ScriptPath`.

## `eng.tags` Virtual Table

`eng.tags` exposes every tag as a flat row, one row per tag per lineage entry. This eliminates the need for `JSON_VALUE` gymnastics on the `Metadata` column of `eng.lineage`.

```sql
-- Find all PII columns in the session
SELECT TargetTable, TargetColumn
INTO #pii_columns
FROM eng.tags
WHERE TagName = 'pii' AND TagValue = 'true';

-- Audit all classification levels
SELECT DISTINCT TagValue AS classification
INTO #levels
FROM eng.tags
WHERE TagName = 'classification'
ORDER BY classification;

-- Find columns without an owner tag
SELECT DISTINCT l.TargetTable, l.TargetColumn
INTO #unowned
FROM eng.lineage l
WHERE l.TargetColumn IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM eng.tags t
      WHERE t.TargetTable = l.TargetTable
        AND t.TargetColumn = l.TargetColumn
        AND t.TagName = 'owner'
  );
```

### `eng.tags` Columns

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

`HAS_TAG(table, column, tag_name [, expected_value])` is a predicate that returns 1 if the tag exists, optionally matching a specific value, and 0 otherwise. It is useful in `WHERE` clauses against `eng.lineage`.

```sql
-- Filter lineage to PII-tagged columns
SELECT TargetTable, TargetColumn, TransformationKind
INTO #pii_transforms
FROM eng.lineage
WHERE HAS_TAG(TargetTable, TargetColumn, 'pii', 'true') = 1
  AND TransformationKind <> 'PassThrough';

-- Check if a specific column has a tag
SELECT HAS_TAG('#orders', 'email', 'pii') AS is_pii;  -- returns 1 or 0
SELECT HAS_TAG('#orders', 'amount', 'unit', 'USD') AS is_usd;
```

## Cross-Run Lineage Catalog

`eng.lineage` is scoped to the current session. The catalog stores lineage across orchestrated runs so you can answer stewardship questions that span many executions.

### History for a table

```sql
-- Local catalog
SELECT * FROM eng.lineage_history WHERE TargetTable = 'Orders';
SELECT * FROM eng.lineage_history WHERE TargetTable = 'Orders' LIMIT 50;
SELECT * INTO #history FROM eng.lineage_history WHERE TargetTable = 'Orders';

-- Remote Orchestrator
SELECT * FROM ProdOrch.eng.lineage_history WHERE TargetTable = 'Orders';
SELECT * INTO #remote_history FROM ProdOrch.eng.lineage_history
WHERE TargetTable = 'Orders' LIMIT 50;
```

Returns all lineage entries that targeted the named table, most recent run first. Columns: `Id`, `RunAt`, `JobName`, `TargetTable`, `TargetColumn`, `SourceTables`, `Operation`, `Tags`, `SourceFile`, `Line`.

### History for a tag

```sql
-- Local catalog
SELECT * FROM eng.lineage_history WHERE JSON_VALUE(Tags, '$.pii') IS NOT NULL;
SELECT * FROM eng.lineage_history WHERE JSON_VALUE(Tags, '$.pii') = 'true';
SELECT * INTO #restricted FROM eng.lineage_history
WHERE JSON_VALUE(Tags, '$.classification') = 'restricted' LIMIT 100;

-- Remote Orchestrator
SELECT * FROM ProdOrch.eng.lineage_history WHERE JSON_VALUE(Tags, '$.pii') = 'true';
SELECT * INTO #remote_restricted FROM ProdOrch.eng.lineage_history
WHERE JSON_VALUE(Tags, '$.classification') = 'restricted' LIMIT 100;
```

Returns all entries whose `Tags` JSON contains the given key, optionally filtered to a specific value.

### Missing stewardship tags

```sql
-- Local catalog
SELECT * INTO #missing FROM eng.missing_tags LIMIT 100;

-- Remote Orchestrator
SELECT * INTO #remote_missing FROM ProdOrch.eng.missing_tags LIMIT 100;
```

Returns the newest catalog targets missing one or more required stewardship tags:
`@owner`, `@steward`, `@contact`, `@classification`, and `@quality`.

### Protected data

```sql
-- Local catalog
SELECT * INTO #protected FROM eng.protected_data LIMIT 100;
SELECT * INTO #protected_review FROM eng.protected_data_suggestions LIMIT 100;

-- Remote Orchestrator or Portal
SELECT * INTO #orch_protected FROM ProdOrch.eng.protected_data LIMIT 100;
SELECT * INTO #portal_protected FROM ProdPortal.eng.protected_data LIMIT 100;
SELECT * INTO #portal_review FROM ProdPortal.eng.protected_data_suggestions LIMIT 100;
```

Returns protected lineage entries from the local or remote lineage catalog. A row is protected when it has a truthy `@pii`, `@phi`, `@pci`, or `@sensitive` tag, or `@classification` is `confidential` or `restricted`. Result columns include `TargetTable`, `TargetColumn`, `SourceTables`, `Operation`, `ProtectionTags`, `Owner`, `Steward`, `Contact`, `Domain`, `Classification`, `Quality`, `Tags`, `SourceFile`, and `Line`.

Use `eng.protected_data_suggestions` for non-authoritative review findings. Suggestions come from target/source column names, catalog metadata such as `@format` or `@semantic_type`, and supported sampled-value callers. The table never writes or changes tags. Result columns include `SuggestedTag`, `SuggestedValue`, `Confidence`, `EvidenceKind`, `Evidence`, `Reason`, and `ExistingTags` so a steward can decide whether to add tags in source-controlled scripts.

References:
- [Specialized Operations](../../../administration/platform/README.md)
