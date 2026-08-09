# Flow and Hierarchy Analysis

**Pattern**: Use newer relationship-oriented visuals together: a `SANKEY` chart for weighted flow, a `SUNBURST` chart for hierarchical contribution, and a `NETWORK` chart for connections between entities.

```sql
SET REPORT TITLE = 'Order Flow Analysis';
SET REPORT DESCRIPTION = 'Flow, hierarchy, and relationship views for order routing.';

CREATE TABLE #flows (
    source_stage VARCHAR(30),
    target_stage VARCHAR(30),
    order_count INT
);

INSERT INTO #flows VALUES
    ('Web', 'Review', 420),
    ('Partner', 'Review', 180),
    ('Review', 'Approved', 510),
    ('Review', 'Rejected', 90),
    ('Approved', 'Fulfilled', 470),
    ('Approved', 'Backorder', 40);

CREATE TABLE #hierarchy (
    channel VARCHAR(30),
    region VARCHAR(30),
    outcome VARCHAR(30),
    order_count INT
);

INSERT INTO #hierarchy VALUES
    ('Web', 'North', 'Fulfilled', 190),
    ('Web', 'South', 'Fulfilled', 170),
    ('Web', 'South', 'Backorder', 35),
    ('Partner', 'North', 'Fulfilled', 110),
    ('Partner', 'West', 'Rejected', 55);

CREATE TABLE #relationships (
    source_team VARCHAR(30),
    target_team VARCHAR(30),
    handoff_count INT,
    team_group VARCHAR(30)
);

INSERT INTO #relationships VALUES
    ('Sales', 'Review', 600, 'Commercial'),
    ('Review', 'Warehouse', 470, 'Operations'),
    ('Review', 'Support', 90, 'Operations'),
    ('Warehouse', 'Carrier', 430, 'Fulfillment');

CREATE VISUAL OrderFlow AS SANKEY (
    SOURCE = #flows,
    TITLE = 'Orders Through Fulfillment',
    MAPPINGS (
        FROM = source_stage,
        TO = target_stage,
        VALUE = order_count
    )
);

CREATE VISUAL OutcomeHierarchy AS SUNBURST (
    SOURCE = #hierarchy,
    TITLE = 'Orders by Channel, Region, and Outcome',
    MAPPINGS (
        LEVEL1 = channel,
        LEVEL2 = region,
        LEVEL3 = outcome,
        VALUE = order_count
    )
);

CREATE VISUAL TeamNetwork AS NETWORK (
    SOURCE = #relationships,
    TITLE = 'Operational Handoffs',
    MAPPINGS (
        FROM = source_team,
        TO = target_team,
        VALUE = handoff_count,
        NODE_GROUP = team_group
    ),
    OPTIONS (LAYOUT = FORCE, REPULSION = 500)
);

CREATE PAGE FlowOverview AS DASHBOARD (
    TITLE = 'Flow Overview',
    STRUCTURE = 'A A / B C',
    MAP (
        'A' = OrderFlow,
        'B' = OutcomeHierarchy,
        'C' = TeamNetwork
    )
);
```

Use `SANKEY` when direction and weight matter, `SUNBURST` when the data has ordered hierarchy levels, and `NETWORK` when the relationship graph itself is the subject. Keep node labels stable across refreshes so users can compare shapes over time.
