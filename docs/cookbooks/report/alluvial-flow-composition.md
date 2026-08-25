# Sankey & Alluvial Flow Diagrams

**Pattern**: Multi-stage flow and transition analysis representing weighted flows between discrete stages (e.g. Traffic Source → Landing Page → Signup → Paid Customer). Built from coordinated named visuals — a `SANKEY` flow diagram alongside supporting visuals over the same staged `#temp` tables.

**Demonstrates**: `SANKEY`, `MAPPINGS (FROM, TO, VALUE)`, `OPTIONS (NODE_WIDTH, NODE_PADDING)`, and multi-stage pipeline flow modeling.

> Every visual here is a named visual. Ribbon geometry is not composed in the `CHART` grammar — `SANKEY` owns its own layout — so the recipe stages each flow stage into a `#temp` table and lets the named visuals read from it.

```sql
SET REPORT TITLE = 'User Acquisition & Conversion Alluvial Flow';
SET REPORT DESCRIPTION = 'Multi-stage funnel flow visualization tracing visitor conversion across acquisition channels.';

-- ── 1. Mock Data Generation (Multi-Stage Conversion Path) ────────────────────
SELECT 'Organic Search' AS FlowFrom, 'Home Page' AS FlowTo, 4500 AS FlowValue INTO #alluvial_flows
UNION ALL SELECT 'Organic Search', 'Blog Post', 3200
UNION ALL SELECT 'Paid Ads', 'Landing Page A', 3800
UNION ALL SELECT 'Paid Ads', 'Landing Page B', 2100
UNION ALL SELECT 'Referral', 'Home Page', 1200
UNION ALL SELECT 'Referral', 'Blog Post', 900
-- Stage 2 -> Stage 3 Transitions
UNION ALL SELECT 'Home Page', 'Free Trial Signup', 3500
UNION ALL SELECT 'Home Page', 'Drop Off', 2200
UNION ALL SELECT 'Blog Post', 'Free Trial Signup', 1800
UNION ALL SELECT 'Blog Post', 'Drop Off', 2300
UNION ALL SELECT 'Landing Page A', 'Free Trial Signup', 2600
UNION ALL SELECT 'Landing Page A', 'Drop Off', 1200
UNION ALL SELECT 'Landing Page B', 'Free Trial Signup', 1400
UNION ALL SELECT 'Landing Page B', 'Drop Off', 700
-- Stage 3 -> Stage 4 Transitions
UNION ALL SELECT 'Free Trial Signup', 'Paid Enterprise', 2400
UNION ALL SELECT 'Free Trial Signup', 'Paid Starter', 4500
UNION ALL SELECT 'Free Trial Signup', 'Expired Trial', 2400;

-- ── 2. Native Sankey Flow Diagram ─────────────────────────────────────────────
CREATE VISUAL UserConversionFlow AS SANKEY (
  SOURCE = #alluvial_flows,
  TITLE  = 'Visitor Journey: Channel → Page → Trial → Plan Conversion',
  MAPPINGS (
    FROM  = FlowFrom,
    TO    = FlowTo,
    VALUE = FlowValue
  ),
  OPTIONS (
    NODE_WIDTH   = 20,
    NODE_PADDING = 14
  )
);

-- ── 3. Page Layout ────────────────────────────────────────────────────────────
CREATE PAGE AlluvialPage AS DASHBOARD (
  STRUCTURE = 'A',
  MAP (
    'A' = UserConversionFlow
  )
);

CREATE NAVIGATION MainNav AS TAB (DEFAULT = AlluvialPage, PAGES (AlluvialPage));
```
