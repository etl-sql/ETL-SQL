# EXPORT LINEAGE

Exports captured column-level data provenance and governance metadata to OpenLineage JSONL files or human-readable Markdown/Mermaid diagrams.

## Syntax

```sql
-- Export the full session lineage as OpenLineage
EXPORT LINEAGE AS OPENLINEAGE TO 'exports/run.openlineage.jsonl';

-- Export lineage for a specific target table
EXPORT LINEAGE FOR #target_table AS OPENLINEAGE TO 'exports/target.openlineage.jsonl';
EXPORT LINEAGE FOR hospital.dbo.Patient AS OPENLINEAGE TO 'exports/patient.openlineage.jsonl';

-- Export lineage as a visual Markdown/Mermaid diagram
EXPORT LINEAGE FOR hospital.dbo.Patient AS MARKDOWN TO 'exports/patient-lineage.md';
EXPORT LINEAGE FOR hospital.dbo.Patient COLUMN date_of_birth AS MARKDOWN TO 'exports/dob.md';
```

## Formats

- **OpenLineage (`AS OPENLINEAGE`)**: Emits standard OpenLineage RunEvents, one JSON object per line (`.jsonl`). Machine-readable format suitable for integration with metadata platforms (Marquez, DataHub, OpenMetadata) and re-import via `IMPORT LINEAGE`.
- **Markdown / Mermaid (`AS MARKDOWN` / `AS MERMAID`)**: Generates human-readable Markdown containing a rendered Mermaid flowchart and detailed column transformation audit table.

## Lineage Settings

- `SET LINEAGE_NAMESPACE = 'namespace'`: Sets the OpenLineage job namespace written into exported events (default: `etl-sql`).
- `SET NO_SAVE_CONNECTION = ON`: Omits physical server hosts from identifiers in exported lineage.

## Examples

```sql
-- Set job namespace and export run lineage
SET LINEAGE_NAMESPACE = 'finance-monthly-close';

SELECT AccountId, SUM(Amount) AS TotalBalance
INTO #MonthlyBalances
FROM GeneralLedger.dbo.Entries
GROUP BY AccountId;

EXPORT LINEAGE FOR #MonthlyBalances AS OPENLINEAGE TO 'exports/monthly_close.jsonl';
EXPORT LINEAGE FOR #MonthlyBalances AS MARKDOWN TO 'exports/monthly_close.md';
```

## References

- [LINEAGE](lineage.md)
- [IMPORT LINEAGE](import-lineage.md)
- [Governance Tags](governance-tags.md)
- [Statement Reference](../README.md)
