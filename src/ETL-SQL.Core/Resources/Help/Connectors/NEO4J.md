# NEO4J
Connects to Neo4j graph databases using the official Neo4j.Driver. Supports querying virtual node and relationship tables, dynamic schema discovery, native Cypher pass-through blocks via the `EXECUTE` statement, and database writes.

Syntax:
  CREATE CONNECTION <name> AS NEO4J(
    CONNECTION_STRING = 'bolt://localhost:7687',
    USER              = 'neo4j',
    PASSWORD          = 'password',
    DATABASE          = 'neo4j',
    TIMEOUT_SECONDS   = 30,
    KEY_COLUMNS       = 'customer_id'
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
  PROTOCOL               — URI scheme when HOST/PORT are used (default: bolt)
  KEY_COLUMNS            — Comma-separated properties used to MERGE nodes or relationships instead of always CREATE
  FROM_LABEL             — Source node label for EDGE_<TYPE> writes that use _from_key
  TO_LABEL               — Target node label for EDGE_<TYPE> writes that use _to_key
  FROM_KEY_COLUMN        — Source node property matched against _from_key (default: id)
  TO_KEY_COLUMN          — Target node property matched against _to_key (default: id)
  SKIP_MISSING_ENDPOINTS — TRUE to skip edge rows with missing/unmatched endpoints (default: FALSE)
  SCHEMA_SAMPLE_SIZE     — Rows sampled for virtual table schema discovery; 0 scans all rows (default: 1000)
  USER                   — Database username
  PASSWORD               — Database password

Credentials supplied with USER/PASSWORD are passed to the Neo4j driver as an auth token and are not embedded in the stored connection URI.

Regular `SELECT` statements against `graph.NODE_*` and `graph.EDGE_*` use the virtual table reader. Use `EXECUTE graph BEGIN ... END` for native Cypher pass-through.
Truncating a table-scoped source such as `graph.NODE_CUSTOMER` deletes only that label; truncating the root connection deletes the whole graph.
`BEGIN TRANSACTION` enlists the Neo4j connection for graph writes, table-scoped truncates, and native Cypher executed through the connection. `COMMIT` persists those graph changes; `ROLLBACK` discards them.
Set `SCHEMA_SAMPLE_SIZE = 0` only when complete sparse-property discovery matters more than the cost of scanning every node or relationship of the requested virtual table.

### Virtual Tabular Schema Mapping
To fit property graphs into the tabular `DataTable` model, the connector maps graph entities to "Virtual Tables":

1. **Virtual Node Tables (`NODE_<LABEL>`)**:
   - Every node label (e.g., `Person`, `Company`) represents a virtual table.
   - **System Columns**: `_id` (Neo4j element ID) and `_labels` (comma-separated labels on the node).
   - **Properties**: All node properties (e.g., `name`, `age`) map directly to columns.
   - **Keyed writes**: Set `KEY_COLUMNS` on the connection to use Cypher `MERGE` for stable upserts instead of duplicate `CREATE` operations.

2. **Virtual Relationship Tables (`EDGE_<TYPE>`)**:
   - Every relationship type (e.g., `FRIEND_OF`, `WORKS_FOR`) represents a virtual table.
   - **System Columns**: `_id` (relationship ID), `_from_id` (source node ID), `_to_id` (target node ID), `_from_label` (source node label), and `_to_label` (target node label).
   - **Keyed endpoints**: For portable ETL loads, provide `_from_key` and `_to_key` columns plus `FROM_LABEL`/`TO_LABEL` and optional `FROM_KEY_COLUMN`/`TO_KEY_COLUMN`; this avoids depending on Neo4j element IDs.
   - **Endpoint validation**: Edge writes fail by default when endpoint identifiers are missing or no endpoint pair matches. Set `SKIP_MISSING_ENDPOINTS = TRUE` only when intentionally dropping those edge rows.
   - **Properties**: All relationship properties map directly to columns.

Write-side property values are normalized before they are sent to Neo4j: `DBNull.Value` becomes `NULL`, dates/times and GUIDs are stored as strings, and nested maps/rows are stored as JSON text because Neo4j node and relationship properties do not support nested map values.

### Examples

```sql
-- Connect using connection parameters
CREATE CONNECTION graph AS NEO4J(
  HOST     = 'localhost',
  PORT     = 7687,
  USER     = 'neo4j',
  PASSWORD = 'password',
  KEY_COLUMNS = 'customer_id'
);

-- Extract active customers to flat table
SELECT _id, name, email
  INTO #active_customers
  FROM graph.NODE_CUSTOMER
  WHERE status = 'Active';

-- Ingest customers back into the graph as keyed nodes (MERGE on customer_id)
INSERT INTO graph.NODE_CUSTOMER (customer_id, name, email, status)
SELECT customer_id, name, email, status FROM #staging;

-- Ingest relationships between customers by stable endpoint keys
CREATE CONNECTION graph_edges AS NEO4J(
  HOST = 'localhost',
  PORT = 7687,
  USER = 'neo4j',
  PASSWORD = 'password',
  FROM_LABEL = 'CUSTOMER',
  TO_LABEL = 'CUSTOMER',
  FROM_KEY_COLUMN = 'customer_id',
  TO_KEY_COLUMN = 'customer_id',
  KEY_COLUMNS = 'friendship_id'
);

INSERT INTO graph_edges.EDGE_FRIEND_OF (_from_key, _to_key, friendship_id, since)
SELECT customer_a_id, customer_b_id, friendship_id, '2025' FROM #friends;

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
