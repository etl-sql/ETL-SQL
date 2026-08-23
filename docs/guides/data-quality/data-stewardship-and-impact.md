# Data Stewardship and Lineage Impact Analysis

Data stewardship in ETL-SQL is script-first: ownership, classification, and privacy metadata live directly inside `.etlsql` and `.rptsql` files as tags. As pipelines execute, the engine captures column-level lineage and writes metadata into the catalog, enabling automated gap detection, protected data discovery, and upstream/downstream impact analysis.

---

> **Applies to:** every deployment profile (Solo, Team, Enterprise, SaaS). Lineage is captured automatically by the local CLI and shared across Portal and Orchestrator environments.

## Script-First Stewardship Tags

Stewardship tags are declared in block comments at the top of scripts or attached to specific tables and columns:

```sql
/* @owner: FinanceOps
   @steward: Maria Chen
   @contact: finance-data@example.com
   @domain: finance
   @classification: restricted
   @quality: gold
   @pii: false
   @freshness: 1d */

SELECT
  order_id,
  customer_id,
  net_amount
INTO #orders_curated
FROM sales.Orders;
```

### Direct Table and Column Tagging Statements

Attach metadata explicitly using `INSERT TAG`:

```sql
INSERT TAG FOR TABLE #orders_curated (
  owner = 'FinanceOps',
  steward = 'Maria Chen',
  classification = 'restricted',
  quality = 'gold'
);

INSERT TAG FOR TABLE #orders_curated COLUMN customer_id (
  pii = 'true',
  classification = 'restricted'
);
```

---

## Example 1: Finding Metadata Gaps in CI/CD

Query `eng.missing_tags` to fail pre-release checks if published outputs are missing required ownership or classification metadata.

```sql
SELECT
  target_table,
  target_column,
  missing_tags,
  script_path
FROM eng.missing_tags
LIMIT 100;
```

---

## Example 2: Auditing Protected Data (PII / PHI / PCI)

Query `eng.protected_data` to locate all sensitive or restricted fields across pipelines and reports:

```sql
SELECT
  target_table,
  target_column,
  protection_tags,
  owner,
  steward
FROM eng.protected_data
LIMIT 500;
```

---

## Example 3: Running Upstream and Downstream Impact Analysis

Before modifying a table schema, renaming a column, or dropping a dataset, inspect downstream dependencies using the Portal API:

```text
GET /api/catalog/impact?kind=table&name=sales.Orders&direction=downstream&depth=4
```

The response returns all affected reports, datasets, subscriptions, and steward-owned assets.

---

## Related Topics

- [Column Quality Rules](column-quality-rules.md) — Declaring `@expect` rules.
- [Lineage Reference](../../reference/statements/session-control/lineage.md) — Complete `TAG` and `LINEAGE` statement syntax.
- [Governance Core](../../administration/platform/governance.md) — Organization-wide governance policies.
