# FUNNEL

A stacked funnel or inverted pyramid chart showing stage-by-stage drop-off and conversion rates across sequential process stages. Commonly used for sales pipelines, onboarding flows, and user journey drop-off analysis.

## Syntax

```sql
CREATE VISUAL VisualName AS FUNNEL (
  SOURCE = #tableName,
  MAPPINGS (
    LABEL = StageColumn,
    VALUE = CountColumn
  ),
  OPTIONS (
    TITLE = 'Sales Pipeline Funnel',
    FUNNEL_SHAPE = FUNNEL,
    SORT = VALUE_DESC,
    SHOW_PERCENT = ON,
    PERCENT_MODE = STEP,
    DATA_LABELS = ON
  )
);
```

## Mappings

- **LABEL** — Stage name or category (e.g., `'Prospects'`, `'Qualified'`, `'Proposal'`). Aliases: `NAME`, `CATEGORY`.
- **VALUE** — Numeric measure representing stage volume or count.

## Options

- **FUNNEL_SHAPE = FUNNEL|PYRAMID** — Orientation of the visual geometry; `FUNNEL` narrows from top to bottom, while `PYRAMID` widens towards the base (default `FUNNEL`).
- **SORT = VALUE_DESC|VALUE_ASC|SOURCE** — Stage ordering rule. `VALUE_DESC` orders stages largest-to-smallest (default for `FUNNEL`), `VALUE_ASC` orders smallest-to-largest (default for `PYRAMID`), and `SOURCE` preserves query encounter order.
- **SHOW_PERCENT = ON|OFF** — Enables display of conversion percentages alongside stage labels and tooltips (default `OFF`).
- **PERCENT_MODE = STEP|TOTAL** — Determines conversion percentage calculation. `STEP` calculates relative stage-to-stage retention against the preceding stage, while `TOTAL` calculates absolute retention against the initial funnel stage (default `STEP`).
- **DATA_LABELS = ON|OFF** — Controls visibility of numeric values and badges alongside stage labels (default `OFF`).
- **COLORS** — Explicit color palette mapping stages to hex colors.
- **TITLE = 'text'** — Visual title displayed above the chart.

## Examples

### Stage-to-Stage Conversion Funnel

```sql
SELECT 'Leads' AS Stage, 5000 AS Count UNION ALL
SELECT 'Qualified', 2100 UNION ALL
SELECT 'Demo', 980 UNION ALL
SELECT 'Proposal', 460 UNION ALL
SELECT 'Closed Won', 210
INTO #sales;

CREATE VISUAL SalesFunnel AS FUNNEL (
  SOURCE   = #sales,
  MAPPINGS (LABEL = Stage, VALUE = Count),
  OPTIONS  (
    TITLE        = 'Sales Pipeline Conversion',
    SHOW_PERCENT = ON,
    PERCENT_MODE = STEP,
    DATA_LABELS  = ON
  )
);
```

### Inverted Pyramid with Total Pipeline Percentage

```sql
SELECT 'Executive' AS Tier, 15 AS Headcount UNION ALL
SELECT 'Management', 85 UNION ALL
SELECT 'Senior Staff', 240 UNION ALL
SELECT 'Staff', 650
INTO #org;

CREATE VISUAL OrgPyramid AS FUNNEL (
  SOURCE   = #org,
  MAPPINGS (LABEL = Tier, VALUE = Headcount),
  OPTIONS  (
    TITLE        = 'Organizational Pyramid',
    FUNNEL_SHAPE = PYRAMID,
    SORT         = SOURCE,
    SHOW_PERCENT = ON,
    PERCENT_MODE = TOTAL,
    DATA_LABELS  = ON
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Visual Reference](../README.md)
