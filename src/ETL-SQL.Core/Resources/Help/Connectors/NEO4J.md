# NEO4J
Connects to Neo4j graph databases using the official Neo4j.Driver. Supports querying virtual node and relationship tables, dynamic schema discovery, native Cypher pass-through blocks via the `EXECUTE` statement, and database writes.

Syntax:
  CREATE CONNECTION <name> AS NEO4J(
    CONNECTION_STRING = 'bolt://localhost:7687',
    USER              = 'neo4j',
    PASSWORD          = 'password',
    DATABASE          = 'neo4j',
    TIMEOUT_SECONDS   = 30
  );

Aliases:
  NEO

Options:
  CONNECTION_STRING      — Connection URI (e.g., bolt://localhost:7687 or neo4j://...) (required unless host/port specified)
  URI                    — Alias for CONNECTION_STRING
  DATABASE               — Target database name (default: neo4j)
  TIMEOUT_SECONDS        — Connection and query timeout limit in seconds (default: 30)
  HOST                   — Hostname for the Neo4j instance (alternative to CONNECTION_STRING)
  PORT                   — Port for the Neo4j instance (default: 7687)
  USER                   — Database username
  PASSWORD               — Database password

### Virtual Tabular Schema Mapping
To fit property graphs into the tabular `DataTable` model, the connector maps graph entities to "Virtual Tables":

1. **Virtual Node Tables (`NODE_<LABEL>`)**:
   - Every node label (e.g., `Person`, `Company`) represents a virtual table.
   - **System Columns**: `_id` (Neo4j element ID) and `_labels` (comma-separated labels on the node).
   - **Properties**: All node properties (e.g., `name`, `age`) map directly to columns.

2. **Virtual Relationship Tables (`EDGE_<TYPE>`)**:
   - Every relationship type (e.g., `FRIEND_OF`, `WORKS_FOR`) represents a virtual table.
   - **System Columns**: `_id` (relationship ID), `_from_id` (source node ID), `_to_id` (target node ID), `_from_label` (source node label), and `_to_label` (target node label).
   - **Properties**: All relationship properties map directly to columns.

### Examples

```sql
-- Connect using connection parameters
CREATE CONNECTION graph AS NEO4J(
  HOST     = 'localhost',
  PORT     = 7687,
  USER     = 'neo4j',
  PASSWORD = 'password'
);

-- Extract active customers to flat table
SELECT _id, name, email
  INTO #active_customers
  FROM graph.NODE_CUSTOMER
  WHERE status = 'Active';

-- Ingest customers back into the graph as nodes
INSERT INTO graph.NODE_CUSTOMER (name, email, status)
SELECT name, email, status FROM #staging;

-- Ingest relationships between customers
INSERT INTO graph.EDGE_FRIEND_OF (_from_id, _to_id, since)
SELECT customer_a_id, customer_b_id, '2025' FROM #friends;

-- Native Cypher Pass-Through using EXECUTE
DECLARE @minAge INT = 21;
EXECUTE graph INTO #fof_network WITH (@minAge)
BEGIN
    MATCH (p:Person)-[:FRIEND_OF]->()-[:FRIEND_OF]->(fof:Person)
    WHERE p.age >= ?1
    RETURN p.name AS source_name, fof.name AS fof_name
END;
```

References:
- [Data Connectors](../../../../../Docs/Reference/Data_Connectors.md)
