# `eng.data_quality_rules`

Data-quality rules captured from `@expect` and `@fail` metadata in the current session.

```sql
SELECT * FROM eng.data_quality_rules WHERE Action = 'QUARANTINE';
```

Columns: `TargetTable`, `TargetColumn`, `RuleTag`, `Rule`, `Action`, `SourceFile`, `Line`.

## Over a `PORTAL` connection

The engine-local table describes the **current session**. To ask which rules protect a column in a
job someone else runs, query the same table through a `PORTAL` connection, naming the job:

```sql
SELECT * FROM prod_portal.eng.data_quality_rules('NightlyCustomerLoad');
```

The projected columns are the same, so one query reads the same shape beside the engine or against
the Portal. The job name is required: rules are enforcement directives bound to the statement that
declares them, so they are only answerable against the script a given job runs — there is no
catalog-wide rule list. Reading them needs Portal data-quality steward access.

Join it against [`eng.data_quality_failures`](data-quality-failures.md) to separate rules that are
failing from rules that are protecting silently.

## References

- [Data Quality Rules](../statements/dml/data-quality-rules.md)
- [Engine Catalog](README.md)
