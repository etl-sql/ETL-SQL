# Governance Tags

Attaches metadata tags (security classification, privacy, ownership, freshness SLAs) to tables and columns via inline SQL comments and tracks automatic inheritance across the lineage graph.

## Syntax

```sql
-- Column-level tags via inline comments
SELECT
  customer_id /* @pii: false; @owner: 'Sales Team'; */,
  email_address /* @pii: true; @sensitive: true; @classification: 'Confidential'; */
INTO #customers
FROM CRM.dbo.Customers;

-- Table-level tag
/* @domain: 'Finance'; @freshness: '1h'; */
CREATE TABLE #staged_sales (
  id INT,
  amount DECIMAL(18,2)
);
```

## Standard Tag Library

### Security & Privacy

| Tag | Type | Values | Purpose |
| :--- | :--- | :--- | :--- |
| `@pii` | boolean | `true` / `false` | Personal Identifiable Information; inherits as `true` if any source is `true` |
| `@phi` | boolean | `true` / `false` | Protected Health Information (HIPAA) |
| `@pci` | boolean | `true` / `false` | Payment Card data (PCI-DSS) |
| `@sensitive` | boolean | `true` / `false` | Sensitive data requiring access controls |
| `@classification` | string | `Public` / `Internal` / `Confidential` / `Restricted` | Data classification tier |
| `@encrypted_at_rest` | boolean | `true` / `false` | Column is stored encrypted |

### Ownership & Quality

| Tag | Type | Values | Purpose |
| :--- | :--- | :--- | :--- |
| `@owner` | string | team or person name | Accountable owner of this data |
| `@domain` | string | e.g. `Finance`, `HR`, `Sales` | Business domain |
| `@steward` | string | data steward name | Person responsible for data quality |
| `@freshness` | duration | e.g. `30m`, `1h`, `7d` | Maximum acceptable age for this data |
| `@sla` | string | e.g. `4h`, `T+1` | Delivery SLA |

## Examples

```sql
-- Query tags attached across the session
SELECT target_table, target_column, tag_name, tag_value, scope
FROM eng.tags
WHERE tag_name = 'pii' AND tag_value = 'true';

-- Check for untagged columns
SELECT table_name, column_name, missing_tag
FROM eng.missing_tags;
```

## References

- [LINEAGE](lineage.md)
- [EXPORT LINEAGE](export-lineage.md)
- [IMPORT LINEAGE](import-lineage.md)
- [`eng.tags` Table](../../eng/tags.md)
- [`eng.missing_tags` Table](../../eng/missing-tags.md)
- [Tag Functions](../../functions/tags/get_tags.md)
