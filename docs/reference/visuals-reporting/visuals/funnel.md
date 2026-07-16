Type: FUNNEL
A stacked funnel chart showing stage-by-stage drop-off in a pipeline or conversion process. Stages are ordered by their VALUE descending (or by row order if values are equal).

Mappings:
- **NAME** — stage label (e.g. 'Leads', 'Qualified', 'Closed')
- **VALUE** — count or metric at each stage

Options:
- **SHOW_PERCENT = ON|OFF** — show conversion % between stages (default ON)
  ORIENTATION  = VERTICAL|HORIZONTAL  (default VERTICAL)
- **COLORS** — explicit colour list, one per stage

```sql
SELECT 'Leads'      AS stage, 5000 AS count UNION ALL
SELECT 'Qualified',           2100          UNION ALL
SELECT 'Demo',                 980          UNION ALL
SELECT 'Proposal',             460          UNION ALL
SELECT 'Closed Won',           210
INTO #pipeline;

CREATE VISUAL SalesFunnel AS FUNNEL (
  SOURCE   = #pipeline,
  MAPPINGS (NAME = stage, VALUE = count),
  OPTIONS  (SHOW_PERCENT = ON, TITLE = 'Sales Pipeline')
);
```

References:
- [Report SQL Guide](../../../guides/report-sql.md)
