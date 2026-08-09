Type: NETWORK
A force-directed graph showing relationships between entities. Nodes are auto-detected from the FROM and TO columns; edges connect them with optional weights. Useful for lineage graphs, collaboration networks, and co-occurrence analysis.

Mappings:
- **FROM** - source node name (required)
- **TO** - destination node name (required)
- **VALUE** - edge weight; affects line thickness (optional)
- **NODE_GROUP** - category used for node coloring and legend; alias GROUP accepted (optional)

Options:
  TITLE     = 'text'
  REPULSION = 1000     -- force strength pushing nodes apart (integer; increase to spread dense clusters)
  LAYOUT    = FORCE    -- FORCE (default) or CIRCULAR

Note: All node names from both FROM and TO are placed in the same node set. CIRCULAR arranges nodes in a ring, which works well for small symmetric graphs.

```sql
-- Salesperson co-sell network (who shares a product category)
SELECT a.Salesperson AS FromPerson,
       b.Salesperson AS ToPerson,
       COUNT(*)      AS SharedDeals,
       a.Category    AS NodeGroup
  INTO #network
  FROM dbo.Sales a
  JOIN dbo.Sales b
    ON a.Category     = b.Category
   AND a.Salesperson  < b.Salesperson
  GROUP BY a.Salesperson, b.Salesperson, a.Category;

CREATE VISUAL CollabNetwork AS NETWORK (
  SOURCE   = #network,
  TITLE    = 'Salesperson Collaboration Network',
  MAPPINGS (
    FROM       = FromPerson,
    TO         = ToPerson,
    VALUE      = SharedDeals,
    NODE_GROUP = NodeGroup
  ),
  OPTIONS  (REPULSION = 800)
);
```

References:
- [Report SQL Guide](../../../guides/feature-guides/report-sql.md)
