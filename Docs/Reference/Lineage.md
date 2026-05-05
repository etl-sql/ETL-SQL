# Lineage & Data Governance Reference

ETL-SQL tracks column-level data lineage automatically as scripts execute. Tags let you attach governance metadata to any column or table at the point it is defined. Both lineage and tags are queryable as data, exportable, and persist across session reloads.

---

## Contents

- [How Lineage Works](#how-lineage-works)
- [Attaching Tags](#attaching-tags)
- [Standard Tag Catalog](#standard-tag-catalog)
- [Tag Inheritance](#tag-inheritance)
- [Querying Lineage](#querying-lineage)
- [Querying Tags](#querying-tags)
- [Exporting Lineage](#exporting-lineage)
- [Script-Level Metadata](#script-level-metadata)
- [Best Practices](#best-practices)
- [Examples](#examples)

---

## How Lineage Works

ETL-SQL records lineage in two passes:

1. **Static analysis** — before execution, the engine walks the script AST and records which source tables and columns produce each target column. This captures SELECT, INSERT, UPDATE, MERGE, and BULK INSERT operations.
2. **Runtime recording** — during execution, statement handlers record actual operations as they complete, including results from EXECUTE PUSHDOWN.

Both passes write to the in-memory `LineageTracker`, which is persisted to SQLite at session end. Lineage is deduplicated by source position so repeated execution does not create duplicate entries.

### What Is Captured

For each transformation, lineage records:
- **Target** — the table and column being written to
- **Sources** — all source tables and columns that contributed to the target
- **Operation** — the SQL statement type (SELECT, INSERT, UPDATE, MERGE, etc.)
- **Transformation** — what happened to the data (pass-through, function call, CASE expression, aggregation, arithmetic, etc.)
- **Tags** — any metadata attached to the source columns is inherited by the target
- **Location** — source file, line number, and column for traceability
- **Timestamp** — UTC time the entry was recorded

### Visibility Boundary

ETL-SQL lineage is complete and accurate for everything the *script* does. The lineage chain stops at the boundary of external database objects:

- If a source is a **VIEW**, lineage records the view name. Lineage through the view to its underlying tables requires Phase 7 (view transparency) to be enabled.
- If a source is a **stored procedure**, lineage records the procedure call. Internal operations inside the procedure are not visible.
- If you use **EXECUTE PUSHDOWN**, the entire pushed-down block is recorded as a single lineage event with no column-level detail.

---

## Attaching Tags

Tags are attached inline using `/* @tagname: value; */` comments placed immediately after a column reference or table reference in a SELECT statement.

### Column-Level Tags

```sql
SELECT
    customer_id   /*@pii: true; @d: Unique customer identifier*/,
    email         /*@pii: true; @classification: confidential; @d: Primary contact email*/,
    region        /*@d: Sales region; @owner: sales_ops*/,
    total_spend   /*@unit: USD; @d: Lifetime spend in USD*/
FROM #customers
INTO #customer_report;
```

### Table-Level Tags

Tags on a table reference apply to all columns read from that table that do not have their own column-level tag for the same key:

```sql
SELECT *
FROM Northwind.dbo.Customers /*@source_system: Northwind; @classification: internal*/
INTO #raw_customers;
```

### Explicit TAG Statement

Apply tags to a temp table after it has been created:

```sql
SELECT * INTO #raw FROM SourceDB.dbo.Transactions;

TAG #raw WITH (
    source_system = 'SourceDB',
    classification = 'confidential',
    owner = 'finance_team',
    load_pattern = 'incremental'
);
```

### Boolean Tag Shorthand

A tag with no value is treated as `true`:

```sql
SELECT customer_id /*@pii; @sensitive*/ FROM #customers;
-- equivalent to: @pii: true; @sensitive: true
```

---

## Standard Tag Catalog

ETL-SQL defines 20 standard tags across five governance domains. Custom tags are always allowed — the standard tags are recognized by the engine for enhanced rendering, intellisense, and future enforcement.

### Security & Privacy

These tags carry the highest governance weight. They are inherited through transformation chains — if a source column is tagged `@pii: true`, every column derived from it inherits that tag automatically unless explicitly overridden.

| Tag | Type | Values | Purpose |
|-----|------|--------|---------|
| `@pii` | boolean | `true` / `false` | Personally identifiable information (GDPR, CCPA, etc.) |
| `@phi` | boolean | `true` / `false` | Protected health information (HIPAA) |
| `@pci` | boolean | `true` / `false` | Payment card data subject to PCI DSS |
| `@sensitive` | boolean | `true` / `false` | Sensitive data that requires care but is not necessarily PII/PHI/PCI |
| `@classification` | enum | `public` `internal` `confidential` `restricted` | Data classification tier. `restricted` is the highest — it implies legal or regulatory access controls. |
| `@encrypted_at_rest` | boolean | `true` / `false` | Whether the source stores this data encrypted at rest |

**Classification tiers:**
- `public` — can be freely shared outside the organization
- `internal` — for internal use only, no external sharing
- `confidential` — limited distribution even internally; needs justification to share
- `restricted` — regulatory or legal controls apply (PII, PHI, PCI, trade secrets)

### Ownership & Stewardship

| Tag | Type | Purpose |
|-----|------|---------|
| `@owner` | string | Team or individual responsible for this data. Usually a team name (e.g. `finance_team`, `platform`). |
| `@domain` | string | Business domain the data belongs to. E.g. `Finance`, `HR`, `Sales`, `Product`. |
| `@steward` | string | Named person accountable for data quality and definitions. E.g. `jane.smith`. |
| `@contact` | string | Email or Slack handle for questions about this column. |

### Quality & Freshness

| Tag | Type | Values | Purpose |
|-----|------|--------|---------|
| `@freshness` | duration string | `1h`, `24h`, `7d`, `30d` | Maximum acceptable age of the data before it is considered stale. |
| `@sla` | string | free-form | SLA commitment to downstream consumers. E.g. `"Available by 06:00 daily"`. |
| `@quality` | enum | `gold` `silver` `bronze` | Data quality tier. `gold` = certified, reviewed, production-grade. `bronze` = raw, uncertified. |
| `@nullable` | boolean | `true` / `false` | Whether NULL values are expected and acceptable. When `false`, NULLs are unexpected and may indicate a data quality issue. |

### Documentation

| Tag | Type | Purpose |
|-----|------|---------|
| `@d` | string | Human-readable description. This tag is special: it is stored separately and displayed prominently. When multiple sources contribute descriptions, ETL-SQL builds a `derived-from` chain showing all upstream descriptions. |
| `@example` | string | A canonical example value. E.g. `"CUST-00123"` for a customer ID. |
| `@unit` | string | Unit of measure. E.g. `USD`, `kg`, `percent`, `ISO-3166-1-alpha-2`, `milliseconds`. |
| `@format` | string | Format specification. E.g. `YYYY-MM-DD`, `E.164` (phone), `UUID-v4`. |

### Source Traceability

| Tag | Type | Values | Purpose |
|-----|------|--------|---------|
| `@source_system` | string | free-form | The originating system. E.g. `Salesforce`, `SAP`, `Snowflake`, `Oracle-ERP`. |
| `@source_table` | string | free-form | The original table name in the source system, before any ETL renaming. |
| `@load_pattern` | enum | `full` `incremental` `cdc` | How the data was loaded. `cdc` = change data capture. |

---

## Tag Inheritance

Tags flow forward through the transformation chain automatically. When a column is derived from one or more source columns, its tags are computed by merging the tags of all contributing sources:

1. **Table-level tags** are applied first (lowest priority).
2. **Column-level tags** override table-level tags for the same key.
3. **Explicitly set tags** on the output column override all inherited values.
4. **`@pii`, `@phi`, `@pci`, `@sensitive`** use `true`-wins inheritance: if *any* source is tagged `true`, the derived column is `true` regardless of other sources.
5. **`@classification`** uses highest-tier inheritance: the derived column gets the most restrictive classification of all contributing sources.
6. **`@d`** (description) does not merge — the output column's description is its own. The inheritance chain is preserved separately as `derived-from` metadata, showing the descriptions of all upstream columns.

### Inheritance Example

```sql
-- Source table with column tags
SELECT
    first_name  /*@pii: true; @d: Given name*/,
    last_name   /*@pii: true; @d: Family name*/
FROM raw.customers
INTO #names;

-- Derived column inherits @pii: true from both sources
SELECT CONCAT(first_name, ' ', last_name) AS full_name
        /*@d: Full display name*/
FROM #names
INTO #display;

-- LINEAGE #display shows:
-- full_name
--   @pii: true  (inherited)
--   @d: Full display name
--   derived-from: first_name: "Given name"; last_name: "Family name"
--   TRANSFORM: StringOperation — CONCAT
```

### Overriding Inherited Tags

To suppress inheritance for a specific tag, set the value explicitly on the output column:

```sql
SELECT REPLACE(email, SUBSTRING(email, 1, CHARINDEX('@', email) - 1), '***')
       AS masked_email /*@pii: false; @d: Masked email — local part redacted*/
FROM #customers;
```

---

## Querying Lineage

### LINEAGE Statement

Display the lineage graph for a table or column:

```sql
LINEAGE #target_table;                    -- all columns
LINEAGE #target_table.column_name;        -- single column
LINEAGE #target_table EXPORT TO 'out.md'; -- save Markdown + Mermaid to file
```

Output includes the ancestry tree, transformation types, and all tags.

### LINEAGE Virtual Table

Query lineage as structured data:

```sql
-- All lineage entries
SELECT * FROM LINEAGE;

-- Entries for a specific table
SELECT * FROM LINEAGE(#monthly_summary);

-- Find all columns with PII
SELECT target_table, target_column
FROM LINEAGE
WHERE JSON_VALUE(Metadata, '$.pii') = 'true';

-- Find all aggregations
SELECT target_table, target_column, transformation_expression
FROM LINEAGE
WHERE transformation_kind = 'Aggregation';
```

**Columns:** `Timestamp`, `Operation`, `TargetTable`, `TargetColumn`, `SourceTables`, `SourceColumns`, `Description`, `Metadata`, `DerivedFromDescriptions`, `TransformationKind`, `TransformationExpression`, `FunctionsApplied`, `SourceFile`, `Line`, `Column`

### LINEAGE_TAGS Virtual Table

Flat key-value projection — the ergonomic way to query tags:

```sql
-- Find all PII columns
SELECT target_table, target_column
FROM LINEAGE_TAGS
WHERE tag_name = 'pii' AND tag_value = 'true';

-- Find all columns and their owners
SELECT target_table, target_column,
       MAX(CASE WHEN tag_name = 'owner'          THEN tag_value END) AS owner,
       MAX(CASE WHEN tag_name = 'classification' THEN tag_value END) AS classification,
       MAX(CASE WHEN tag_name = 'pii'            THEN tag_value END) AS pii
FROM LINEAGE_TAGS
GROUP BY target_table, target_column;

-- Find all confidential columns without an owner
SELECT t.target_table, t.target_column
FROM LINEAGE_TAGS t
WHERE t.tag_name = 'classification' AND t.tag_value IN ('confidential', 'restricted')
  AND NOT EXISTS (
      SELECT 1 FROM LINEAGE_TAGS o
      WHERE o.target_table = t.target_table
        AND o.target_column = t.target_column
        AND o.tag_name = 'owner'
  );
```

**Columns:** `target_table`, `target_column`, `tag_name`, `tag_value`, `scope` (`column`/`table`/`script`)

---

## Querying Tags

### SHOW TAGS Statement

```sql
SHOW TAGS FOR TABLE #customers;            -- all table-level tags
SHOW TAGS FOR COLUMN #customers.email;     -- column-level tags
SHOW TAGS INTO #tag_results;               -- capture as temp table
```

### GET_TAGS() and GET_TAG_VALUE() Functions

```sql
-- All tag keys for a column (returns LIST)
SELECT GET_TAGS('#customers', 'email') AS tag_keys;

-- Specific tag value
SELECT GET_TAG_VALUE('#customers', 'email', 'classification') AS cls;

-- Use in a query
SELECT column_name,
       GET_TAG_VALUE('#customers', column_name, 'pii') AS is_pii
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'customers';
```

### SHOW SCRIPT TAGS

Script-level metadata tags (from the file header):

```sql
SHOW SCRIPT TAGS;
-- Returns: author, engine_version, environment, and any custom header tags
```

---

## Exporting Lineage

### Markdown Export

```sql
LINEAGE #target_table EXPORT TO 'lineage_report.md';
```

Produces a Markdown file with a Mermaid directed graph diagram and a full audit table of all lineage entries.

### OpenLineage Export

```sql
-- Export session lineage as OpenLineage events
LINEAGE EXPORT AS OPENLINEAGE TO 'run_lineage.jsonl';

-- Export lineage for a specific table
LINEAGE #target_table EXPORT AS OPENLINEAGE TO 'table_lineage.jsonl';
```

OpenLineage `.jsonl` files are importable into Marquez, DataHub, Apache Airflow (with the OpenLineage provider), Collibra, Alation, and any tool that implements the OpenLineage specification.

Configure automatic export on every script run in `appsettings.json`:
```json
"Lineage": {
  "OpenLineageFile": "logs/lineage.jsonl",
  "OpenLineageEndpoint": "http://localhost:5000/api/v1/lineage"
}
```

---

## Script-Level Metadata

Add a metadata header to any `.etlsql` script. The engine auto-injects `author` and `engine_version` if not present.

```sql
-- @author: Jane Smith;
-- @environment: production;
-- @domain: Finance;
-- @version: 2.1;
-- @description: Monthly revenue reconciliation;
-- @schedule: daily;
```

View with:
```sql
SHOW SCRIPT TAGS;
```

Script-level tags appear in OpenLineage exports under the job facet and are available in the `LINEAGE_TAGS` virtual table with `scope = 'script'`.

---

## Best Practices

### Tag at the Source, Not the Output

Tag columns when they first enter the system — at the raw load step. Tags then propagate forward automatically. Do not wait until the final output to add governance tags; by then you may have lost track of which source introduced the PII.

```sql
-- Good: tag at the raw load
SELECT
    customer_id  /*@pii: true*/,
    email        /*@pii: true; @classification: confidential*/,
    region
FROM SourceDB.dbo.Customers
INTO #raw_customers;

-- Tags on #raw_customers.email will flow to every derived column automatically
```

### Use @d for Every Non-Obvious Column

The `@d` description tag is the most valuable tag for long-term maintainability. A brief description of what a column means — especially for computed or transformed columns — pays dividends when someone else (or future you) reads the script.

```sql
SELECT
    revenue - cost AS gross_margin  /*@d: Revenue minus direct cost; excludes overhead*/,
    gross_margin / NULLIF(revenue, 0) AS margin_pct /*@d: Gross margin as percent of revenue*/
```

### Use Standard Tags Consistently

Use the exact canonical names from the standard catalog (`@pii`, not `@PII` or `@is_pii`). Tags are case-insensitive in the engine, but consistent naming makes queries and reports reliable across teams.

### Keep TAG statements close to the source

```sql
SELECT * INTO #raw FROM SourceDB.dbo.Transactions;
TAG #raw WITH (source_system = 'SourceDB', owner = 'finance_team'); -- immediately after load
```

### Review Lineage Before Shipping to Production

```sql
-- At end of script, dump full lineage to review
SELECT target_table, target_column, tag_name, tag_value
FROM LINEAGE_TAGS
WHERE tag_name IN ('pii', 'classification', 'owner')
ORDER BY target_table, target_column, tag_name;
```

---

## Examples

### Full Pipeline with Governance Tags

```sql
-- @author: jane.smith; @domain: Finance; @classification: confidential;

-- ── Step 1: Load raw transactions ────────────────────────────────────────
SELECT
    transaction_id  /*@d: Unique transaction key; @nullable: false*/,
    customer_id     /*@pii: true; @d: Owning customer*/,
    card_number     /*@pci: true; @classification: restricted; @d: Masked PAN*/,
    amount          /*@unit: USD; @d: Transaction amount in USD*/,
    transaction_date
FROM PaymentDB.dbo.Transactions
INTO #raw_txn;

TAG #raw_txn WITH (
    source_system = 'PaymentDB',
    load_pattern  = 'incremental',
    owner         = 'payments_team',
    freshness     = '1h'
);

-- ── Step 2: Aggregate by customer ────────────────────────────────────────
SELECT
    customer_id,
    COUNT(*)        AS txn_count   /*@d: Number of transactions*/,
    SUM(amount)     AS total_spend /*@d: Total spend in period; @unit: USD*/
FROM #raw_txn
GROUP BY customer_id
INTO #customer_spend;

-- customer_id inherits @pii: true from #raw_txn automatically
-- total_spend gets TransformationKind = Aggregation (SUM)

-- ── Review governance state ───────────────────────────────────────────────
LINEAGE #customer_spend;

SELECT target_column, tag_name, tag_value
FROM LINEAGE_TAGS
WHERE target_table = '#customer_spend'
ORDER BY target_column, tag_name;
```

### Finding All PII Columns in a Session

```sql
SELECT DISTINCT target_table, target_column
FROM LINEAGE_TAGS
WHERE tag_name = 'pii' AND tag_value = 'true'
ORDER BY target_table, target_column;
```

### Exporting Lineage for a Compliance Audit

```sql
-- Full session lineage as OpenLineage events
LINEAGE EXPORT AS OPENLINEAGE TO 'audit_2026_Q2.jsonl';

-- Human-readable Markdown with Mermaid graph
LINEAGE #final_output EXPORT TO 'audit_2026_Q2_lineage.md';

-- Flat tag report
SELECT target_table, target_column, tag_name, tag_value
FROM LINEAGE_TAGS
WHERE tag_name IN ('pii', 'phi', 'pci', 'classification', 'owner')
ORDER BY target_table, target_column, tag_name
INTO #audit_tags;

SELECT * FROM #audit_tags;
```

### Checking for Unowned Sensitive Columns

```sql
-- Flag any sensitive column that has no owner assigned
SELECT s.target_table, s.target_column,
       s.tag_value AS sensitivity
FROM LINEAGE_TAGS s
WHERE s.tag_name IN ('pii', 'phi', 'pci') AND s.tag_value = 'true'
  AND NOT EXISTS (
      SELECT 1 FROM LINEAGE_TAGS o
      WHERE o.target_table   = s.target_table
        AND o.target_column  = s.target_column
        AND o.tag_name       = 'owner'
  );
```
