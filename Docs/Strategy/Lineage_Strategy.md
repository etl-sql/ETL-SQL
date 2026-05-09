# Lineage & Data Governance Strategy

**Status:** Phase 1 ready to start  
**Date:** 2026-05-04  
**Scope:** All enhancements to the ETL-SQL lineage tracking, tag system, and data governance capabilities. This is a priority feature — it is a core selling differentiator of ETL-SQL over generic ETL tools.

---

## Why This Matters

ETL-SQL already tracks column-level lineage and carries metadata tags through transformation chains. That foundation is ahead of most tools in this space. The gap is that the system is currently an island: lineage is human-readable (ASCII tree, Mermaid diagram) but not machine-readable, tags are completely ungoverned (any `@anything` is valid), transformations are invisible (you know *what* columns fed a result but not *what happened* to them), and the system has no awareness of database-side objects or reports as part of the lineage chain.

Closing these gaps turns ETL-SQL lineage from a developer convenience into an enterprise governance capability. The specific business cases this unlocks:

- **GDPR / CCPA compliance** — trace every column containing PII from source to output, including every transformation applied
- **SOX audit trails** — prove that financial data was not altered beyond documented transformations
- **Data catalog integration** — surface ETL-SQL lineage in tools like DataHub, Collibra, Alation, and Apache Atlas where the rest of the organization's metadata lives
- **Impact analysis** — "if I change the source table schema, what downstream outputs are affected?"
- **Data quality stewardship** — owner and quality tags create clear accountability chains

---

## Current State

### What Exists and Works Well

- **Column-level lineage** via dual static analysis (`LineageAnalyzer` runs before execution) and runtime recording in statement handlers. Captures SELECT, INSERT, UPDATE, MERGE, BULK INSERT, EXECUTE PUSHDOWN.
- **Metadata inheritance** — tags on source columns flow forward to derived columns automatically. If `@pii: true` is on a source column, the inheritance mechanism will carry it forward (once `pii` is defined as a standard tag).
- **`/* @tag: value; */` inline syntax** on both column references and table references in SELECT statements.
- **Script-level metadata** via file header comments; auto-injection of `author` and `engine_version`.
- **`SELECT * FROM LINEAGE`** virtual table — lineage is queryable as data.
- **ASCII tree + Mermaid diagram** output, with Markdown export.
- **SQLite session persistence** — lineage survives session reloads.
- **`SHOW TAGS`**, `GET_TAGS()`, `GET_TAG_VALUE()` for programmatic access.
- **Thread-safe, deduplicated** recording with position-based dedup keys.

### Known Gaps

1. **Tags are ungoverned** — `@pii: true`, `@PII: YES`, and `@is_pii: 1` are treated as three different tags. No standard catalog, no type enforcement, no required fields.
2. **Transformations are invisible** — `LineageEntry.Operation` captures the SQL statement type (SELECT, INSERT) but not what happened inside the expression. CASE expressions, function calls, and arithmetic are unrecorded.
3. **No standard tags defined** — only `@d` (description) is special-cased. `@pii`, `@sensitive`, `@owner`, etc. don't exist as recognized concepts.
4. **Tag query is awkward** — the `Metadata` column in the LINEAGE virtual table is a JSON string. Finding all PII columns requires `WHERE JSON_VALUE(Metadata, '$.pii') = 'true'`.
5. **Report definitions produce no lineage** — `CREATE VISUAL` and `CREATE DATASET` are invisible to the lineage graph.
6. **No OpenLineage export** — the only export is Markdown + Mermaid. No interoperability with DataHub, Airflow, Collibra, Alation, Marquez, or Apache Atlas.
7. **No database catalog import** — column descriptions, PK/FK relationships, and data classification labels sitting in connected databases are never imported as tags.
8. **Cycle detection uses a hardcoded limit of 20** — a silent failure with no user warning. Reachable in complex multi-stage pipelines.
9. **FOREACH/FOR loop lineage is incomplete** — iteration structure is collapsed; multiple source partitions appear as a flat source list.
10. **No view or stored procedure transparency** — lineage stops at the database object name. If a VIEW reads from 3 tables, ETL-SQL only knows about the view, not its upstream tables.

---

## Phase 1 — Standard Tag Library

**Priority: Start immediately**  
**Effort: Small (2–3 days)**  
**Deliverables:** `Docs/Reference/Lineage.md`, updated `Help/Operations/LINEAGE.md`, updated `Grammar.md`

No engine changes. This phase defines the standard vocabulary so everything built in subsequent phases has a consistent foundation to stand on.

### Standard Tag Catalog

See `Docs/Reference/Lineage.md` for the full reference. Summary of the 20 standard tags defined across five domains:

**Security & Privacy (engine will eventually enforce these)**

| Tag | Type | Values |
|-----|------|--------|
| `@pii` | boolean | `true` / `false` |
| `@phi` | boolean | `true` / `false` |
| `@pci` | boolean | `true` / `false` |
| `@sensitive` | boolean | `true` / `false` |
| `@classification` | enum | `public` / `internal` / `confidential` / `restricted` |
| `@encrypted_at_rest` | boolean | `true` / `false` |

**Ownership & Stewardship**

| Tag | Type |
|-----|------|
| `@owner` | string (team or individual) |
| `@domain` | string (Finance, HR, Sales, etc.) |
| `@steward` | string (accountable person) |
| `@contact` | string (email or Slack) |

**Quality & Freshness**

| Tag | Type | Values |
|-----|------|--------|
| `@freshness` | duration | `1h`, `24h`, `7d`, etc. |
| `@sla` | string | free-form SLA description |
| `@quality` | enum | `gold` / `silver` / `bronze` |
| `@nullable` | boolean | `true` / `false` |

**Documentation**

| Tag | Type | Notes |
|-----|------|-------|
| `@d` | string | Already special-cased; description with inheritance |
| `@example` | string | Canonical example value |
| `@unit` | string | USD, kg, percent, ISO-3166, etc. |
| `@format` | string | YYYY-MM-DD, E.164, etc. |

**Source Traceability**

| Tag | Type | Values |
|-----|------|--------|
| `@source_system` | string | Salesforce, SAP, Snowflake, etc. |
| `@source_table` | string | Original table in the source system |
| `@load_pattern` | enum | `full` / `incremental` / `cdc` |

### What "Recognized" Means at This Stage

In Phase 1, "recognized" means:
- Documented in `Docs/Reference/Lineage.md` and the help system
- Listed in the intellisense provider so TUI and VS Code autocomplete suggest them
- Displayed with distinct visual treatment in LINEAGE output (e.g., security tags highlighted in red)

Enforcement (type-checking, required-field validation) comes in Phase 3.

---

## Phase 2 — Transformation Recording

**Priority: High — start after Phase 1**  
**Effort: Medium (1–2 weeks)**  
**Files:** `ETL-SQL.Core/LineageTracker.cs` (LineageEntry and runtime state), `ETL-SQL.Analysis/Lineage/LineageAnalyzer.cs`, `ETL-SQL.Analysis/Lineage/LineageGraphRenderer.cs`, `ETL-SQL.Engine/Storage/LineageDataSource.cs`

### New Fields on LineageEntry

```csharp
public TransformationKind TransformationKind { get; init; } = TransformationKind.Unknown;
public string? TransformationExpression { get; init; }          // raw SQL text of expression
public IReadOnlyList<string>? FunctionsApplied { get; init; }  // e.g. ["UPPER", "TRIM", "CONCAT"]
```

```csharp
public enum TransformationKind
{
    Unknown,          // default — expression too complex to classify or analysis not run
    PassThrough,      // SELECT col  — no transformation, direct reference
    Rename,           // SELECT col AS other_name — identity with alias only
    Cast,             // CAST(col AS type) or implicit coercion
    FunctionCall,     // UPPER(col), TRIM(col), DATEADD(...)
    CaseExpression,   // CASE WHEN ... THEN ... END
    Arithmetic,       // col * 1.1, price - discount
    StringOperation,  // CONCAT, +, ||
    Aggregation,      // SUM, COUNT, AVG, MIN, MAX
    WindowFunction,   // ROW_NUMBER(), LAG(), LEAD(), RANK()
    Conditional,      // IIF, COALESCE, NULLIF, ISNULL
    Literal,          // 'hardcoded value' with no source column reference
    Subquery          // (SELECT ...) inline subquery
}
```

### LineageAnalyzer Changes

The analyzer already walks `SelectColumn.expression`. It needs to:

1. **Classify the expression** — inspect the root expression node type and the subtree:
   - `ColumnReferenceExpression` with no wrapping → `PassThrough` (or `Rename` if alias differs)
   - `FunctionCallExpression` at the root → `FunctionCall`; collect all function names in the subtree
   - `CaseExpression` anywhere in the subtree → `CaseExpression`
   - `BinaryExpression` with arithmetic operators → `Arithmetic`
   - `FunctionCallExpression` whose name is in the aggregation set → `Aggregation`
   - `FunctionCallExpression` whose name is in the window function set → `WindowFunction`
   - `LiteralExpression` with no column references → `Literal`

2. **Capture the expression text** — use the `StartOffset` / `EndOffset` from the AST node to slice the raw script text. Store as `TransformationExpression`.

3. **Collect function names** — recursive walk of the expression AST, collect every `FunctionCallExpression.FunctionName`.

### Output Changes

**ASCII tree** (`LineageGraphRenderer`):
```
LINEAGE #order_summary
  ├── total_revenue
  │   ├── SOURCE: orders.amount
  │   │   TRANSFORM: Aggregation — SUM(amount)
  │   └── @unit: USD; @pii: false
  ├── customer_tier
  │   ├── SOURCE: customers.lifetime_value
  │   │   TRANSFORM: CaseExpression
  │   │   EXPR: CASE WHEN lifetime_value > 10000 THEN 'Gold' ...
  │   └── @d: Derived from lifetime spend thresholds
  └── normalized_name
      ├── SOURCE: customers.first_name
      │   TRANSFORM: StringOperation — UPPER, TRIM, CONCAT
      ├── SOURCE: customers.last_name
      │   TRANSFORM: StringOperation — CONCAT
      └── @pii: true (inherited)
```

**LINEAGE virtual table** — add new columns: `TransformationKind`, `TransformationExpression`, `FunctionsApplied` (pipe-delimited string or JSON array).

**Mermaid export** — add transformation type as edge label: `source_col -->|"SUM (Aggregation)"| target_col`

### OpenLineage Alignment

`TransformationKind` maps directly onto the OpenLineage `columnLineage` facet transformation subtypes:
- `PassThrough` / `Rename` → `DIRECT` / `IDENTITY`
- `FunctionCall`, `CaseExpression`, `Arithmetic`, etc. → `INDIRECT` with subtype and description

This makes Phase 2 a prerequisite for a complete Phase 5 export.

---

## Phase 3 — Tag Governance & Query Ergonomics

**Priority: Medium-high**  
**Effort: Medium (1–1.5 weeks)**  
**Files:** `LineageTracker.cs`, `LineageDataSource.cs`, new `LineageTagsDataSource.cs`, `IExecutionContext.cs`, `LineageStatementHandler.cs`, lint rules

### LINEAGE_TAGS Virtual Table

A flat key-value projection of all tag data, making tag queries first-class:

```sql
-- Current (awkward):
SELECT * FROM LINEAGE WHERE JSON_VALUE(Metadata, '$.pii') = 'true';

-- Phase 3:
SELECT * FROM LINEAGE_TAGS WHERE tag_name = 'pii' AND tag_value = 'true';

-- Find all PII columns and their owners:
SELECT lt.target_table, lt.target_column, lt.tag_value AS pii,
       lo.tag_value AS owner
FROM LINEAGE_TAGS lt
LEFT JOIN LINEAGE_TAGS lo
    ON lo.target_table = lt.target_table
    AND lo.target_column = lt.target_column
    AND lo.tag_name = 'owner'
WHERE lt.tag_name = 'pii' AND lt.tag_value = 'true';
```

Schema: `(target_table, target_column, tag_name, tag_value, scope)`  
`scope` is `column`, `table`, or `script`.

### HAS_TAG() Function

```sql
-- In WHERE clauses on the LINEAGE virtual table
SELECT * FROM LINEAGE WHERE HAS_TAG('pii', 'true');
SELECT * FROM LINEAGE WHERE HAS_TAG('classification');    -- any value
```

### Cycle Detection Fix

Replace the hardcoded `maxDepth = 20` in `GetAncestors()` with proper visited-set cycle detection. When a cycle is detected, surface a warning in the lineage output rather than silently truncating:

```
WARNING: Lineage cycle detected at #staging_table → #raw_data → #staging_table
         Ancestry walk terminated at cycle boundary.
```

### FOREACH / FOR Loop Lineage

Add iteration context to lineage recording inside loop statement handlers. The `LineageEntry` already has `Metadata` — record `@loop_iteration: N` and `@loop_variable: @var` when lineage is recorded inside a loop body. This preserves the structure rather than collapsing it.

### Standard Tag Intellisense

In `LanguageMetadata` or the suggestion providers, add the standard tag names as autocomplete candidates when the cursor is inside a `/* @` comment context. TUI and VS Code LSP both benefit.

### Optional: TAG SCHEMA Validation (Stretch Goal for Phase 3)

```sql
-- Define validation rules for the session
SET TAG VALIDATION ON;   -- enable; off by default for backward compatibility

-- Define acceptable values for classification (lint warning if violated)
TAG SCHEMA (
    pii            BOOLEAN,
    classification ENUM('public', 'internal', 'confidential', 'restricted'),
    owner          REQUIRED,
    quality        ENUM('gold', 'silver', 'bronze')
);
```

Violations emit lint warnings (not errors) so existing scripts are not broken. Mark this as a stretch goal — the rest of Phase 3 delivers without it.

---

## Phase 4 — Report Lineage

**Priority: Medium**  
**Effort: Small (3–4 days)**  
**Files:** `ReportParser.cs` / `ReportBuilder`, `LineageStatementHandler.cs`

### What to Record

When `CREATE VISUAL` and `CREATE DATASET` are parsed, emit lineage entries connecting the visual/dataset to its source:

```
CREATE VISUAL SalesChart AS BAR (
    SOURCE = (SELECT month, revenue FROM #monthly_summary),
    ...
)
```

Should produce:
- `TargetTable = "report:SalesChart"`, `Operation = "CREATE_VISUAL"`, `SourceTables = ["#monthly_summary"]`

And for datasets:
- `TargetTable = "dataset:MonthlyRevenue"`, `Operation = "CREATE_DATASET"`, `SourceTables = [...]`

### What This Unlocks

The lineage chain becomes complete end-to-end:

```
SourceDB.dbo.Orders → #raw_orders → #monthly_summary → report:SalesChart
```

A GDPR officer can now ask: "which reports display this customer's data?" and get an answer.

### LINEAGE Statement Update

The `LINEAGE` statement renderer should recognize `report:` and `dataset:` prefixed nodes and render them with a distinct visual style (e.g., `[SalesChart]` in Mermaid vs. a circle node for tables).

---

## Phase 5 — OpenLineage Export

**Priority: High (major differentiator)**  
**Effort: Medium (1–2 weeks)**  
**Files:** New `OpenLineageExporter.cs` in `ETL-SQL.Engine`, `LineageStatementHandler.cs`, `appsettings.json`

OpenLineage is the Linux Foundation standard for lineage interoperability. It is natively consumed by Apache Airflow, Apache Spark, dbt, Great Expectations, Marquez (open-source lineage catalog), DataHub, and most commercial data catalog vendors.

### Event Structure

An OpenLineage `RunEvent` is emitted at script completion (and optionally at each statement):

```json
{
  "eventType": "COMPLETE",
  "eventTime": "2026-05-04T12:00:00Z",
  "run": {
    "runId": "uuid-per-script-execution",
    "facets": {
      "nominalTime": { "nominalStartTime": "...", "nominalEndTime": "..." }
    }
  },
  "job": {
    "namespace": "etl-sql",
    "name": "script_name_from_author_tag"
  },
  "inputs": [
    {
      "namespace": "sqlserver://server/database",
      "name": "dbo.Customers",
      "facets": {
        "schema": {
          "fields": [
            { "name": "customer_id", "type": "INT" },
            { "name": "email", "type": "VARCHAR" }
          ]
        },
        "dataQualityMetrics": { ... }
      }
    }
  ],
  "outputs": [
    {
      "namespace": "etl-sql://session",
      "name": "#enriched_customers",
      "facets": {
        "columnLineage": {
          "fields": {
            "normalized_email": {
              "inputFields": [
                {
                  "namespace": "sqlserver://server/database",
                  "name": "dbo.Customers",
                  "field": "email",
                  "transformations": [
                    {
                      "type": "INDIRECT",
                      "subtype": "FUNCTION",
                      "description": "LOWER(TRIM(email))",
                      "masking": false
                    }
                  ]
                }
              ]
            }
          }
        },
        "ownership": {
          "owners": [{ "name": "data_team", "type": "TEAM" }]
        },
        "tags": {
          "fields": {
            "email": ["pii", "classification:confidential"]
          }
        }
      }
    }
  ]
}
```

### Export Modes

**Mode 1 — File export (`.jsonl`)**  
Each script run appends one event to a `.jsonl` file (one JSON object per line). Configured via `appsettings.json → Lineage:OpenLineageFile`. This is the zero-dependency option — the file can be imported into any catalog tool manually or by a downstream job.

**Mode 2 — HTTP endpoint**  
POST each event to an OpenLineage-compatible API endpoint (Marquez, DataHub's OpenLineage receiver, Airflow's `lineage` API). Configured via `appsettings.json → Lineage:OpenLineageEndpoint`. Fire-and-forget with retry — a failed POST should warn but never block script execution.

**Mode 3 — LINEAGE EXPORT AS OPENLINEAGE**  
A new syntax option on the existing `LINEAGE` statement:
```sql
LINEAGE #target_table EXPORT AS OPENLINEAGE TO 'output.jsonl';
LINEAGE EXPORT AS OPENLINEAGE TO 'full_run.jsonl';   -- entire session
```

### Namespace Convention

| Object type | Namespace |
|-------------|-----------|
| Temp table (`#name`) | `etl-sql://session/{sessionId}` |
| External DB table | `{dialect}://{server}/{database}` |
| Report visual | `etl-sql://report/{reportName}` |
| File source | `file://{absolutePath}` |
| Variable | `etl-sql://variable/{scriptName}` |

### OpenLineage Dependency

The `OpenLineage.Client` NuGet package exists and handles serialization. If it is too heavy or opinionated, the serialization can be implemented manually — the format is simple JSON and the spec is public. Recommendation: start with manual serialization to avoid a dependency; revisit if the spec evolves.

---

## Phase 6 — Database Catalog Metadata Import

**Priority: Medium**  
**Effort: Medium (1–2 weeks per connector family)**  
**Files:** Connector base classes, new `ICatalogMetadataProvider` interface, `LineageTracker.cs`

### The Problem

When ETL-SQL reads `SELECT * FROM Northwind.dbo.Customers`, it records that table as a lineage source. But the connected database likely has metadata sitting in its catalog that ETL-SQL is ignoring: column descriptions from extended properties, data classification labels, primary key/foreign key relationships, nullable flags, and data types.

### Interface

```csharp
public interface ICatalogMetadataProvider
{
    Task<IReadOnlyList<CatalogColumn>> GetColumnMetadataAsync(
        string schema, string tableName, CancellationToken ct);
    Task<IReadOnlyList<CatalogRelationship>> GetRelationshipsAsync(
        string schema, string tableName, CancellationToken ct);
}

public record CatalogColumn(
    string ColumnName,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey,
    string? Description,        // from extended properties / column comments
    IReadOnlyDictionary<string, string> ExtraProperties);  // vendor-specific

public record CatalogRelationship(
    string ForeignKey, string ReferencedTable, string ReferencedColumn);
```

### Per-Connector Implementation

| Connector | Catalog source |
|-----------|---------------|
| SQL Server | `sys.extended_properties` + `sys.columns` + `INFORMATION_SCHEMA.TABLE_CONSTRAINTS` |
| PostgreSQL | `pg_catalog.obj_description()` + `information_schema.columns` + `pg_constraint` |
| MySQL / MariaDB | `INFORMATION_SCHEMA.COLUMNS.COLUMN_COMMENT` |
| BigQuery | Dataset/table description from the BigQuery API metadata |
| SQLite | `PRAGMA table_info()` (limited — no descriptions) |
| Snowflake | `INFORMATION_SCHEMA.COLUMNS` + `COMMENT` |

### Loading Strategy

**Lazy, on first table reference.** When the `LineageAnalyzer` records a source table from an external database, it triggers a catalog lookup for that table if one has not been done this session. The results are stored in `LineageTracker._latestTableMetadata` and `_latestColumnMetadata` using the same keys as user-defined tags — so catalog-imported tags appear alongside script-defined tags uniformly.

Catalog-imported tags are prefixed with `@db_` to distinguish them from user-defined tags:  
`@db_description`, `@db_type`, `@db_nullable`, `@db_is_pk`, `@db_referenced_by`

**Configuration:** `appsettings.json → Lineage:ImportCatalogMetadata: true/false` (default false — opt-in to avoid latency surprises).

---

## Phase 7 — View & Stored Procedure Transparency

**Priority: Low (hardest, most fragile)**  
**Effort: Large (3–4 weeks, heavily dialect-dependent)**  
**Defer until Phase 6 ships and is validated.**

### The Problem

When `SELECT * FROM dbo.vw_CustomerSummary` is the source, the lineage stops at the view name. The view likely reads from `dbo.Customers`, `dbo.Orders`, and `dbo.Products` — but ETL-SQL has no visibility into that.

### Approach

1. When a view is encountered as a source (detected via `INFORMATION_SCHEMA.VIEWS.TABLE_TYPE = 'VIEW'`), fetch `VIEW_DEFINITION` from the catalog.
2. Attempt to parse the view definition using ETL-SQL's own parser. For views written in standard SQL this will work. For views using vendor extensions it will fail.
3. On successful parse, recursively analyze the view's AST and add the resulting lineage entries with the view as an intermediate node.
4. On parse failure, record the view as an opaque node with a `@db_view_definition_unparseable: true` tag and move on. Never fail the main script for a catalog parsing error.

**Stored procedures:** Follow the same pattern using `sys.sql_modules` (SQL Server) or `pg_proc` (Postgres). Even more likely to contain vendor-specific syntax. Mark as best-effort.

**The risk:** Partial view lineage (you see two levels deep but not three) can be more confusing than no view lineage. Add a `LINEAGE DEPTH <n>` option to control how many levels of view expansion to perform. Default: 1 (expand one level of views; don't recurse into views of views).

---

## Open Questions

These need answers before or during implementation:

### 1. Cross-Session Lineage Registry

Currently lineage lives in SQLite per-session. There is no cross-session registry. A question like "what scripts write to table `dbo.FactSales`?" has no answer today. Should the Orchestrator accumulate lineage events across job runs into a persistent lineage store? This would be a significant but high-value addition — essentially a lightweight in-house data catalog. The OpenLineage file export (Phase 5, Mode 1) provides an interim answer via a `.jsonl` log, but a proper queryable registry is a longer-term question.

### 2. @pii Enforcement — Lint Rules

Once `@pii: true` is a recognized standard tag, the engine can do things with it. The most useful behavior: a lint rule that fires when a `@pii: true` column flows into a write operation that goes outside the ETL-SQL session boundary — an INSERT into an external database, a BULK INSERT to a file, or a report visual. The rule would warn: "PII column `email` is written to external destination `output.csv`." Is this in scope for Phase 3, or a separate lint rule phase?

### 3. Portal Data Access Lineage

When a user views a report in the Report Portal, that is a data access event — potentially relevant for GDPR access logging and SOX audit trails. Should the portal log "user X viewed report Y at time T, which read from tables A, B, C"? This is different from data flow lineage (it's access lineage) but the OpenLineage format supports it via `DatasetAccessEvent`. Should this be connected to the lineage system or kept separate in the portal's own audit log?

### 4. Lineage in the Report Portal UI

The Portal currently has no lineage UI. A "Data Lineage" tab on the report detail page — showing a visual lineage graph from database sources through to the report's visuals — would be a compelling selling feature. The data is all there after Phase 4; it's a front-end rendering question. Worth planning alongside Phase 4.

---

## Testing Strategy

Lineage is particularly vulnerable to silent regression — a change to the AST walker or expression evaluator can silently drop lineage for certain patterns without any test failing. Recommended test structure:

- **Lineage completeness tests:** For each statement type (SELECT, INSERT, UPDATE, MERGE, CREATE VISUAL), assert that a lineage entry with the expected source/target/column is present. These should be the most numerous lineage tests.
- **Transformation classification tests:** For each `TransformationKind`, assert that a known expression produces the correct classification and `FunctionsApplied` list.
- **Metadata inheritance tests:** Assert that tags on a source column appear on a derived column after a multi-step pipeline.
- **Cycle detection test:** A script where table A derives from B which derives from A — assert a warning is emitted and the ancestry walk completes without infinite recursion.
- **OpenLineage schema validation:** Assert that the exported JSON validates against the published OpenLineage JSON Schema.
- **Performance test (`Category=Performance`):** Full lineage analysis on a 500-statement script completes within a reasonable bound.

---

## Documentation Plan

| Document | Phase | Notes |
|----------|-------|-------|
| `Docs/Reference/Lineage.md` | Phase 1 | Standard tag catalog, usage guide, examples |
| `Help/Operations/LINEAGE.md` | Phase 1 | Update with standard tags, transformation output |
| `Docs/Reference/Grammar.md` | Phase 1, 3, 5 | Tag syntax, LINEAGE_TAGS, LINEAGE EXPORT syntax |
| `Docs/Architecture/Engine.md` | Phase 2 | Document TransformationKind, LineageAnalyzer changes |
| `Docs/Report_Cookbook.md` | Phase 2, 4 | Lineage cookbook recipes |
| OpenLineage integration guide | Phase 5 | How to connect ETL-SQL to Marquez/DataHub/Airflow |
| Database catalog import guide | Phase 6 | Per-connector setup, appsettings config |
