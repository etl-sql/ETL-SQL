# NETWORK

A node-link relationship visual depicting entities and pairwise connections. Nodes are extracted from source and target identifiers, sized proportionally to metrics, styled by category groups or pinned coordinates, and positioned using deterministic force-directed or circular algorithms. Useful for data lineage graphs, organizational collaboration networks, dependency analysis, and transaction flows.

## Syntax

```sql
CREATE VISUAL VisualName AS NETWORK (
  SOURCE = #tableName,
  MAPPINGS (
    FROM = SourceColumn,
    TO = TargetColumn,
    VALUE = WeightColumn,
    NODE_SIZE = MetricColumn,
    NODE_GROUP = CategoryColumn
  ),
  OPTIONS (
    TITLE = 'Entity Relationship Graph',
    LAYOUT = FORCE,
    REPULSION = 500,
    DIRECTED = ON,
    NODE_LABELS = ON,
    NODE_LABEL_MIN_SIZE = 8
  )
);
```

## Mappings

- **FROM** — Source node name or entity identifier (required; alias `SOURCE`).
- **TO** — Target node name or destination entity identifier (required; alias `TARGET`).
- **VALUE** — Numeric edge weight or relationship strength; proportionally scales edge stroke width (optional; alias `WEIGHT`).
- **NODE_SIZE** — Numeric column used to scale node circle radius proportionally between 5px and 24px (optional; alias `SIZE`). Defaults to 9px fallback when omitted.
- **TARGET_SIZE** — Optional separate metric column used to scale target node circles when different from source nodes (alias `TO_SIZE`).
- **NODE_GROUP** — Category or cluster identifier used for node palette coloring (optional; alias `GROUP`).
- **NODE_X** — Explicit horizontal coordinate or metric used to fix and pin node positions (optional; alias `X`).
- **NODE_Y** — Explicit vertical coordinate or metric used to fix and pin node positions (optional; alias `Y`).

## Options

- **TITLE = 'text'** — Visual title rendered at the top of the canvas.
- **LAYOUT = FORCE|CIRCULAR** — Spatial positioning algorithm (default `FORCE`). `FORCE` executes a deterministic Fruchterman-Reingold simulation balancing edge attraction and node repulsion; `CIRCULAR` arranges nodes evenly along a radial ring.
- **REPULSION = n** — Repulsive force pushing nodes apart in `FORCE` layout (default `500`). Increase for dense graphs to expand clusters.
- **DIRECTED = ON|OFF** — Renders directional SVG arrowheads pointing toward target node boundaries (default `OFF`; alias `ARROWS = ON|OFF`).
- **NODE_LABELS = ON|OFF** — Controls rendering of entity name labels adjacent to nodes (default `ON`; alias `LABELS = ON|OFF`).
- **NODE_LABEL_MIN_SIZE = n** — Suppresses text labels on nodes whose circle radius is smaller than `n` pixels (default `0`).
- **NODE_COLOR = '#rrggbb'** — Base fill color applied to nodes when `NODE_GROUP` is omitted.

## Layout and Coordinate Control

NETWORK graphs dynamically resolve node coordinates using one of three modes:

- **Force-Directed (`LAYOUT = FORCE`)**: Nodes relax iteratively into a balanced layout. Connected entities attract each other according to edge weights, while non-connected nodes repel each other proportional to `REPULSION`. The simulation is deterministic and reproduces identical mark coordinates across executions.
- **Radial (`LAYOUT = CIRCULAR`)**: Distributes all distinct entities symmetrically around an inscribed circular perimeter. Ideal for ring topologies, dense meshes, and small cyclic networks.
- **Pinned Positioning (`NODE_X`, `NODE_Y`)**: When coordinates are mapped in the dataset, nodes are fixed at their authored locations. In mixed datasets, pinned nodes remain anchored while unpinned entities relax around them.

## Examples

### Directed Team Collaboration Network

```sql
SELECT a.Salesperson AS FromPerson,
       b.Salesperson AS ToPerson,
       COUNT(*)      AS SharedDeals,
       a.Team        AS TeamGroup,
       SUM(a.Revenue) AS DealVolume
  INTO #team_network
  FROM dbo.Sales a
  JOIN dbo.Sales b
    ON a.DealId = b.DealId
   AND a.Salesperson <> b.Salesperson
 GROUP BY a.Salesperson, b.Salesperson, a.Team;

CREATE VISUAL CollaborationMap AS NETWORK (
  SOURCE   = #team_network,
  TITLE    = 'Sales Team Collaboration & Deal Volume',
  MAPPINGS (
    FROM       = FromPerson,
    TO         = ToPerson,
    VALUE      = SharedDeals,
    NODE_SIZE  = DealVolume,
    NODE_GROUP = TeamGroup
  ),
  OPTIONS  (
    LAYOUT              = FORCE,
    REPULSION           = 650,
    DIRECTED            = ON,
    NODE_LABELS         = ON,
    NODE_LABEL_MIN_SIZE = 10
  )
);
```

### Pinned Pipeline Lineage Graph

```sql
SELECT 'RawIngest'   AS StageSource, 'StagingClean' AS StageTarget, 100 AS StageRows, 50.0 AS XPos, 150.0 AS YPos INTO #pipeline_lineage
UNION ALL SELECT 'StagingClean', 'AnalyticsMart', 95, 200.0, 150.0
UNION ALL SELECT 'AnalyticsMart', 'ExecutiveKPI', 10, 350.0, 100.0
UNION ALL SELECT 'AnalyticsMart', 'OperationsFeed', 85, 350.0, 200.0;

CREATE VISUAL LineageFlow AS NETWORK (
  SOURCE   = #pipeline_lineage,
  TITLE    = 'Data Transformation Lineage Flow',
  MAPPINGS (
    FROM   = StageSource,
    TO     = StageTarget,
    VALUE  = StageRows,
    NODE_X = XPos,
    NODE_Y = YPos
  ),
  OPTIONS  (
    DIRECTED = ON,
    NODE_LABELS = ON
  )
);
```

## References

- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
