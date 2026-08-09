# LINEAGE

Tracks column-level data provenance across all SELECT, INSERT, UPDATE, and MERGE operations in a script. Records how each output column was derived, including which source tables and columns it came from, what transformation was applied, and any governance tags attached along the way.

## Querying Lineage

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

## Exporting Lineage

`eng.lineage` returns rows for inspection. Use `EXPORT LINEAGE` to write lineage to a file.

### OpenLineage (portable, re-importable)

```sql
-- Export the full session lineage
EXPORT LINEAGE AS OPENLINEAGE TO 'exports/run.openlineage.jsonl';

-- Export lineage for a specific target
EXPORT LINEAGE FOR #target_table AS OPENLINEAGE TO 'exports/target.openlineage.jsonl';
EXPORT LINEAGE FOR hospital.dbo.Patient AS OPENLINEAGE TO 'exports/patient.openlineage.jsonl';
```

The output is [OpenLineage](https://openlineage.io) RunEvents, one JSON object per line (`.jsonl`),
carrying column-level edges, transformations, and tags. Appending to an existing file is intentional
— a run log accumulates.

### Markdown with a Mermaid diagram (for reading, not re-importing)

```sql
EXPORT LINEAGE FOR hospital.dbo.Patient AS MARKDOWN TO 'exports/patient-lineage.md';
EXPORT LINEAGE FOR hospital.dbo.Patient COLUMN date_of_birth AS MARKDOWN TO 'exports/dob.md';

-- The original spelling, still supported
LINEAGE(hospital.dbo.Patient) TO 'exports/patient-lineage.md';
LINEAGE(hospital.dbo.Patient, date_of_birth) TO 'exports/dob-lineage.md';
```

The file contains a Mermaid graph (renders in GitHub, VS Code, and most Markdown viewers) followed by
a detailed audit table. `AS MERMAID` is accepted as a synonym for `AS MARKDOWN`.

Use OpenLineage when the lineage needs to be read back by a machine, and Markdown when it needs to
be read by a person or pasted into a pull request.

## Importing Lineage

Lineage exported by one script can be picked up by another, so a column can be traced past the
boundary of the script that is running. Without this, a script that reads `EDW.dbo.Patient` can only
say the data came from `EDW.dbo.Patient`; with it, the trace continues back to the CSV that loaded
the table last week.

```sql
-- Canonical spelling: mirrors EXPORT LINEAGE
IMPORT LINEAGE FOR hospital.dbo.Patient AS OPENLINEAGE FROM 'exports/patient.openlineage.jsonl';

-- AS OPENLINEAGE and the file extension are optional
IMPORT LINEAGE FOR hospital.dbo.Patient FROM 'exports/patient.openlineage.jsonl';

-- The original spelling of the same statement, still supported
INSERT LINEAGE FOR TABLE hospital.dbo.Patient FROM 'exports/patient.openlineage.jsonl';

-- Inline JSON works too, so lineage can come from a variable
IMPORT LINEAGE FOR hospital.dbo.Patient FROM @lineage_json;
```

Imported rows carry the operation `IMPORTED`. They are a **seed**: anything the script records
afterwards accrues on top, last-writer-wins. `DELETE LINEAGE FOR TABLE <table>` removes only
imported rows — lineage captured by executing statements is immutable.

### Where imports can come from

| Source | How |
| :--- | :--- |
| A file | `IMPORT LINEAGE ... FROM 'path/to/file.openlineage.jsonl'` |
| Inline JSON / a variable | `IMPORT LINEAGE ... FROM @json_variable` |
| The durable catalog | Already automatic across runs — query `eng.lineage_history`, or see [`SET LINEAGE_IMPORT_CATALOG`](#set-lineage_import_catalog) for pulling database column comments in |

### Connection aliases do not have to match

An alias like `hospital` is a name local to one script. Export records the portable identity instead
(`mssql://localhost/EDW`), and import re-attaches whatever alias the *importing* script uses for that
same server and database. So this works even though the two scripts named the connection differently:

```sql
-- Script 1
CREATE CONNECTION hospital AS MSSQL('Server=localhost;Database=EDW;');
INSERT INTO hospital.dbo.Patient (name) SELECT name FROM pats.FILE;
EXPORT LINEAGE FOR hospital.dbo.Patient AS OPENLINEAGE TO 'C:\tmp\patient.jsonl';

-- Script 2 — same database, different alias
CREATE CONNECTION warehouse AS MSSQL('Server=localhost;Database=EDW;');
CREATE CONNECTION outfile AS FLATFILE(PATH='C:\tmp\output.csv');
IMPORT LINEAGE FOR warehouse.dbo.Patient AS OPENLINEAGE FROM 'C:\tmp\patient.jsonl';

INSERT INTO outfile.FILE (name) SELECT name FROM warehouse.dbo.Patient;
-- Lineage for outfile now traces: patients.csv -> EDW.dbo.Patient -> output.csv
```

File datasets are matched on their full path rather than on an alias, because every file connector
shares the one `file://` namespace.

## Lineage Settings

### `SET LINEAGE_NAMESPACE`

Sets the OpenLineage **job namespace** written into exported RunEvents. Defaults to `etl-sql`. Set it
to whatever groups your jobs in the collector you export to (Marquez, DataHub, OpenMetadata, …), so
events from this pipeline land together rather than in a generic bucket.

```sql
SET LINEAGE_NAMESPACE = 'finance-etl';
EXPORT LINEAGE AS OPENLINEAGE TO 'exports/run.openlineage.jsonl';
```

### `SET LINEAGE_IMPORT_CATALOG`

`ON` makes the engine read **column comments from the source database's own catalog** and record
them as lineage descriptions before dependent lineage is captured — so a comment maintained in SQL
Server or Postgres shows up as the `@d` description and is inherited by derived columns, without
being restated in the script. Off by default; best-effort and idempotent per table per session.

```sql
SET LINEAGE_IMPORT_CATALOG = ON;
SELECT customer_id, email INTO #c FROM CRM.dbo.Customers;
-- #c.email inherits the column comment from CRM.dbo.Customers.email
```

The equivalent configuration key is `Lineage:ImportCatalogMetadata` in `appsettings.json`.

### `SET NO_SAVE_CONNECTION`

`ON` omits the server from physical identifiers in lineage output (`EDW.dbo.Patient` rather than
`localhost:EDW.dbo.Patient`), so lineage can be shared without disclosing where it was read.

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
SELECT * FROM eng.lineage WHERE target_table = '#summary';

-- Find all PII-carrying columns across the session
SELECT target_table, target_column, Metadata
FROM eng.lineage
WHERE JSON_VALUE(metadata, '$.pii') = 'true';

-- Find all aggregations applied
SELECT target_table, target_column, transformation_kind, functions_applied
FROM eng.lineage
WHERE transformation_kind = 'Aggregation';
```

### LINEAGE Table Columns

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
| `transformation_kind` | Classification of the expression (see below) |
| `transformation_expression` | Raw SQL of the expression (omitted for PassThrough) |
| `functions_applied` | Comma-separated list of function names in the expression |

### transformation_kind Values

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

Tags go in SQL block comments on the column expression they annotate, using semicolons to separate multiple tags. The comment may sit on either side of the alias — before it reads as documenting the expression, after it as documenting the output column — and tags on both sides merge, with the later value winning if one is repeated:

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
3. **@d is not overwritten**: if a column has its own `@d` tag, inherited descriptions are stored in `derived_from_descriptions` instead.
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
SELECT * FROM eng.lineage WHERE target_table = '#customer_summary';

-- Query which columns carry PII
SELECT target_table, target_column
FROM eng.lineage
WHERE JSON_VALUE(metadata, '$.pii') = 'true';

-- Find what transformations were applied
SELECT target_column, transformation_kind, functions_applied
FROM eng.lineage
WHERE target_table = '#customer_summary'
  AND transformation_kind <> 'PassThrough';
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

The result includes `target_table`, `target_column`, `missing_tags`, `present_tags`, `run_at`,
`job_name`, and `script_path`.

## `eng.tags` Virtual Table

`eng.tags` exposes every tag as a flat row, one row per tag per lineage entry. This eliminates the need for `JSON_VALUE` gymnastics on the `metadata` column of `eng.lineage`.

```sql
-- Find all PII columns in the session
SELECT target_table, target_column
INTO #pii_columns
FROM eng.tags
WHERE tag_name = 'pii' AND tag_value = 'true';

-- Audit all classification levels
SELECT DISTINCT tag_value AS classification
INTO #levels
FROM eng.tags
WHERE tag_name = 'classification'
ORDER BY classification;

-- Find columns without an owner tag
SELECT DISTINCT l.target_table, l.target_column
INTO #unowned
FROM eng.lineage l
WHERE l.target_column IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM eng.tags t
      WHERE t.target_table = l.target_table
        AND t.target_column = l.target_column
        AND t.tag_name = 'owner'
  );
```

### `eng.tags` Columns

| Column | Description |
| :--- | :--- |
| `target_table` | Table the tag is on |
| `target_column` | Column the tag is on (null for table-level tags) |
| `operation` | SELECT, TABLE_TAGS, UPDATE COLUMN, etc. |
| `tag_name` | Tag name (e.g. `pii`, `owner`, `classification`) |
| `tag_value` | Tag value |
| `scope` | `column` or `table` |
| `line` | Source line number |
| `source_file` | Script file name |

## HAS_TAG() Function

`HAS_TAG(table, column, tag_name [, expected_value])` is a predicate that returns 1 if the tag exists, optionally matching a specific value, and 0 otherwise. It is useful in `WHERE` clauses against `eng.lineage`.

```sql
-- Filter lineage to PII-tagged columns
SELECT target_table, target_column, transformation_kind
INTO #pii_transforms
FROM eng.lineage
WHERE HAS_TAG(target_table, target_column, 'pii', 'true') = 1
  AND transformation_kind <> 'PassThrough';

-- Check if a specific column has a tag
SELECT HAS_TAG('#orders', 'email', 'pii') AS is_pii;  -- returns 1 or 0
SELECT HAS_TAG('#orders', 'amount', 'unit', 'USD') AS is_usd;
```

## Cross-Run Lineage Catalog

`eng.lineage` is scoped to the current session. The catalog stores lineage across orchestrated runs so you can answer stewardship questions that span many executions.

### History for a table

```sql
-- Local catalog
SELECT * FROM eng.lineage_history WHERE target_table = 'Orders';
SELECT * FROM eng.lineage_history WHERE target_table = 'Orders' LIMIT 50;
SELECT * INTO #history FROM eng.lineage_history WHERE target_table = 'Orders';

-- Remote Orchestrator
SELECT * FROM ProdOrch.eng.lineage_history WHERE target_table = 'Orders';
SELECT * INTO #remote_history FROM ProdOrch.eng.lineage_history
WHERE target_table = 'Orders' LIMIT 50;
```

Returns all lineage entries that targeted the named table, most recent run first. Columns: `id`, `run_at`, `job_name`, `target_table`, `target_column`, `source_tables`, `operation`, `tags`, `source_file`, `line`.

### History for a tag

```sql
-- Local catalog
SELECT * FROM eng.lineage_history WHERE JSON_VALUE(tags, '$.pii') IS NOT NULL;
SELECT * FROM eng.lineage_history WHERE JSON_VALUE(tags, '$.pii') = 'true';
SELECT * INTO #restricted FROM eng.lineage_history
WHERE JSON_VALUE(tags, '$.classification') = 'restricted' LIMIT 100;

-- Remote Orchestrator
SELECT * FROM ProdOrch.eng.lineage_history WHERE JSON_VALUE(tags, '$.pii') = 'true';
SELECT * INTO #remote_restricted FROM ProdOrch.eng.lineage_history
WHERE JSON_VALUE(tags, '$.classification') = 'restricted' LIMIT 100;
```

Returns all entries whose `tags` JSON contains the given key, optionally filtered to a specific value.

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

Returns protected lineage entries from the local or remote lineage catalog. A row is protected when it has a truthy `@pii`, `@phi`, `@pci`, or `@sensitive` tag, or `@classification` is `confidential` or `restricted`. Result columns include `target_table`, `target_column`, `source_tables`, `operation`, `protection_tags`, `owner`, `steward`, `contact`, `domain`, `classification`, `quality`, `tags`, `source_file`, and `line`.

Use `eng.protected_data_suggestions` for non-authoritative review findings. Suggestions come from target/source column names, catalog metadata such as `@format` or `@semantic_type`, and supported sampled-value callers. The table never writes or changes tags. Result columns include `suggested_tag`, `suggested_value`, `confidence`, `evidence_kind`, `evidence`, `reason`, and `existing_tags` so a steward can decide whether to add tags in source-controlled scripts.

References:
- [Specialized Operations](../../../administration/platform/README.md)
