# eng.stewardship_score

`eng.stewardship_score` reports transparent stewardship component scores over current-session and durable lineage. The same versioned calculation is used by the CLI scanner, local engine catalog, Orchestrator API, and remote Orchestrator connections.

```sql
SELECT scope_type, scope_name, component, numerator, denominator, percentage
FROM eng.stewardship_score
WHERE scope_type = 'JOB' AND scope_name = 'nightly_etl';

SELECT *
FROM ProdOrchestrator.eng.stewardship_score;
```

## Columns

| Column | Description |
| :--- | :--- |
| `scope_type` | `GLOBAL`, `JOB`, or `TABLE`. |
| `scope_name` | Scope identifier; `*` for the global scope. |
| `component` | `required_tag_completeness`, `protected_data_coverage`, or `quality_rule_coverage`. |
| `numerator` | Requirements satisfied in this component. |
| `denominator` | Requirements evaluated in this component. |
| `percentage` | `numerator / denominator * 100`, rounded to two decimals; an empty denominator is 100%. |
| `asset_count` | Distinct target tables evaluated in the scope. |
| `column_count` | Distinct target columns evaluated in the scope. |
| `weight` | Optional policy weight exposed for downstream use. ETL-SQL does not manufacture a composite score. |
| `evaluated_at_utc` | UTC evaluation timestamp. |
| `definition_version` | Calculation contract version. |

Weights and required-tag rules come from the nearest `etlsql-policy.json`. Without a workspace policy, the standard required tags are `@owner`, `@steward`, `@contact`, `@classification`, and `@quality`; a checked-in policy is authoritative and may replace that list. Protected-data coverage checks ownership (`@owner`, `@steward`, or `@contact`) and `@classification`; quality-rule coverage checks the `EXPECT` rules projected onto each column.

For any matching `scope_type`, `scope_name`, and `component`, `denominator - numerator` equals the number of rows in [`eng.stewardship_gaps`](stewardship-gaps.md).

## References

- [Engine Catalog](README.md)
- [PII schema scanner](../cli/scan.md)
- [Lineage](lineage.md)
