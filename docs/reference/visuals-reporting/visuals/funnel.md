# FUNNEL
A stacked funnel chart showing stage-by-stage drop-off in a pipeline or conversion process. Stages are ordered by their VALUE descending (or by row order if values are equal).

## Syntax

```sql
CREATE VISUAL VisualName AS FUNNEL (
  SOURCE = #tableName,
  MAPPINGS (
    ...
  )
);
```

## Mappings

- **NAME** - stage label (e.g. 'Leads', 'Qualified', 'Closed')
- **VALUE** - count or metric at each stage

## Options

- **COLORS** — Explicit colour list, one per stage.
- **DATA_LABELS = ON|OFF WITH (...)** — Shows stage value badges with optional background and border styling.
  - **LABEL_BACKGROUND = '#rrggbb'** — Background color for the stage value badge (e.g. `'#f8fafc'`).
  - **LABEL_BORDER = 'width style color'** — Border for the stage value badge (e.g. `'1px solid #e2e8f0'`).
- **ORIENTATION = VERTICAL|HORIZONTAL** — Funnel direction (default `VERTICAL`).
- **SHOW_PERCENT = ON|OFF** — Show conversion % between stages (default `ON`).

## Examples

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
  OPTIONS  (
    SHOW_PERCENT = ON,
    DATA_LABELS  = ON WITH (LABEL_BACKGROUND = '#f8fafc', LABEL_BORDER = '1px solid #e2e8f0'),
    TITLE        = 'Sales Pipeline'
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
