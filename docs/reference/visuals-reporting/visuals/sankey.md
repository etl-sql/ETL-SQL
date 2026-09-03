# SANKEY

A directed flow visual depicting volume transitions and proportional movements between stages or categories. Commonly used for budget allocations, user journey transitions, energy balance diagrams, and supply chain logistics.

## Syntax

```sql
CREATE VISUAL VisualName AS SANKEY (
  SOURCE = #tableName,
  MAPPINGS (
    FROM = SourceColumn,
    TO = TargetColumn,
    VALUE = VolumeColumn,
    NODE_COLOR = ColorColumn
  ),
  OPTIONS (
    TITLE = 'Flow Diagram',
    NODE_ALIGN = JUSTIFY,
    NODE_PADDING = 12,
    LINK_OPACITY = 0.55
  )
);
```

## Mappings

- **FROM** — Source node name or identifier. Alias: `SOURCE`.
- **TO** — Destination or target node name or identifier. Alias: `TARGET`.
- **VALUE** — Numeric flow volume or magnitude between the source and destination.
- **NODE_COLOR** — Optional column containing custom hex codes or palette color names for nodes. Alias: `SOURCE_COLOR`.
- **TARGET_COLOR** — Optional column containing custom color assignments for target nodes. Alias: `TO_COLOR`.

## Options

- **NODE_ALIGN = JUSTIFY|LEFT|RIGHT|CENTER** — Horizontal node alignment across flow layers. `JUSTIFY` aligns leaf sink nodes to the right edge (default), `LEFT` aligns nodes strictly by topological depth from roots, `RIGHT` aligns nodes to the right by distance from sinks, and `CENTER` centers intermediate nodes between input and output layers.
- **NODE_PADDING = n** — Vertical padding in pixels between adjacent nodes in the same column (default `12`).
- **LINK_OPACITY = 0.0..1.0** — Visual opacity for curved flow bands connecting nodes (default `0.55`).
- **COLORS** — Explicit color palette mapping node names to hex colors.
- **TITLE = 'text'** — Visual title displayed above the diagram.

## Multi-Level Flows

Sankey diagrams consume pairwise directed edges (`FROM → TO`). Multi-stage flows spanning three or more steps (such as `Region → Category → Product`) must be pre-exploded into adjacent transition pairs using `UNION ALL`:

```sql
-- Step 1: Region to Category
SELECT Region AS FromNode, Category AS ToNode, SUM(Revenue) AS FlowValue
INTO #multi_stage_flow
FROM dbo.Sales
GROUP BY Region, Category
UNION ALL
-- Step 2: Category to Segment
SELECT Category AS FromNode, Segment AS ToNode, SUM(Revenue) AS FlowValue
FROM dbo.Sales
GROUP BY Category, Segment;
```

## Examples

### Enterprise Budget Allocation

```sql
SELECT 'Total Budget' AS SourceNode, 'Engineering' AS TargetNode, 450 AS Amount UNION ALL
SELECT 'Total Budget', 'Marketing', 250 UNION ALL
SELECT 'Total Budget', 'Operations', 200 UNION ALL
SELECT 'Engineering', 'Infrastructure', 180 UNION ALL
SELECT 'Engineering', 'Product Dev', 220 UNION ALL
SELECT 'Engineering', 'Security', 50 UNION ALL
SELECT 'Marketing', 'Digital Ads', 150 UNION ALL
SELECT 'Marketing', 'Events', 100
INTO #budget_flow;

CREATE VISUAL BudgetAllocation AS SANKEY (
  SOURCE   = #budget_flow,
  MAPPINGS (
    FROM  = SourceNode,
    TO    = TargetNode,
    VALUE = Amount
  ),
  OPTIONS  (
    TITLE        = 'Budget Allocation Flow',
    NODE_ALIGN   = JUSTIFY,
    NODE_PADDING = 16,
    LINK_OPACITY = 0.65
  )
);
```

### Flow with Data-Driven Node Colors

```sql
SELECT 'Direct' AS Channel, 'Signup' AS Stage, 1200 AS Users, '#2563eb' AS NodeColor UNION ALL
SELECT 'Organic Search', 'Signup', 2400, '#0284c7' UNION ALL
SELECT 'Referral', 'Signup', 800, '#10b981' UNION ALL
SELECT 'Signup', 'Activated', 3200, '#16a34a' UNION ALL
SELECT 'Signup', 'Churned', 1200, '#ef4444'
INTO #user_journey;

CREATE VISUAL FunnelSankey AS SANKEY (
  SOURCE   = #user_journey,
  MAPPINGS (
    FROM       = Channel,
    TO         = Stage,
    VALUE      = Users,
    NODE_COLOR = NodeColor
  ),
  OPTIONS  (
    TITLE        = 'User Acquisition & Activation Flow',
    NODE_ALIGN   = LEFT,
    NODE_PADDING = 20,
    LINK_OPACITY = 0.70
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
- [Visual Reference](../README.md)
