# NEO4J

Graph database connector supporting property-graph ingestion and Cypher pass-through querying via the
official Neo4j.Driver. Regular `SELECT` reads through a virtual node/relationship table layer; native
Cypher runs through `EXECUTE graph BEGIN ... END`.

Aliases: `NEO`

## Options

| Option | Description | Mandatory |
| :--- | :--- | :---: |
| `CONNECTION_STRING` / `URI` | Bolt/Neo4j connection URI (e.g. `bolt://localhost:7687`) | Yes (structured) |
| `USER` | Authentication username | No |
| `PASSWORD` | Authentication password | No |
| `DATABASE` | Target database name (default: `neo4j`) | No |
| `TIMEOUT_SECONDS` | Connection and query timeout in seconds (default: `30`) | No |
| `HOST` | Server host name (alternative to connection string) | No |
| `PORT` | Server port (default: `7687`) | No |
| `PROTOCOL` | URI scheme when `HOST`/`PORT` are used (default: `bolt`) | No |
| `KEY_COLUMNS` | Comma-separated properties used to `MERGE` nodes or relationships instead of always `CREATE` | No |
| `FROM_LABEL` / `TO_LABEL` | Source/target node labels for `EDGE_<TYPE>` writes that use `_from_key` and `_to_key` | No |
| `FROM_KEY_COLUMN` / `TO_KEY_COLUMN` | Source/target node property names matched against `_from_key` and `_to_key` (default: `id`) | No |
| `SKIP_MISSING_ENDPOINTS` | `TRUE` to skip edge rows with missing or unmatched endpoints instead of failing (default: `FALSE`) | No |
| `SCHEMA_SAMPLE_SIZE` | Rows sampled for virtual-table schema discovery; `0` scans all rows (default: `1000`) | No |

`USER` and `PASSWORD` are passed to the Neo4j driver as an auth token and are not embedded in the
stored connection URI.

Regular `SELECT` statements against `graph.NODE_*` and `graph.EDGE_*` read through the connector's
virtual table layer. Use `EXECUTE graph BEGIN ... END` for native Cypher pass-through. Truncating a
table-scoped source such as `graph.NODE_CUSTOMER` deletes only that label; truncating the root
connection deletes the whole graph. `BEGIN TRANSACTION` enlists the Neo4j connection for graph writes,
table-scoped truncates, and native Cypher executed through the connection; `COMMIT` persists those
graph changes and `ROLLBACK` discards them. Set `SCHEMA_SAMPLE_SIZE=0` only when complete sparse-property
discovery matters more than the cost of scanning every node or relationship of the requested virtual
table.

## Virtual schema mapping

Graph entities are mapped to virtual tables:

- **`NODE_<LABEL>`** — Virtual node table for the specified node label. Includes system columns `_id`
  (element ID) and `_labels` (comma-separated labels). Set `KEY_COLUMNS` for stable upserts via Cypher
  `MERGE`.
- **`EDGE_<TYPE>`** — Virtual relationship table for the specified relationship type. Includes system
  columns `_id` (relationship ID), `_from_id` (source element ID), `_to_id` (target element ID),
  `_from_label`, and `_to_label`. For portable ETL loads, provide `_from_key` and `_to_key` plus
  `FROM_LABEL`/`TO_LABEL` and optional key-column options.

## Write behavior

- Ingesting into `NODE_<LABEL>` or `EDGE_<TYPE>` uses parameterized `UNWIND` Cypher templates.
- If `KEY_COLUMNS` is set, writes use `MERGE` for idempotent upserts; otherwise writes use `CREATE`.
- Edge writes fail by default when endpoint columns are missing or endpoint matches are not found. Set
  `SKIP_MISSING_ENDPOINTS=TRUE` only when intentionally dropping those edge rows.
- `DBNull.Value` is written as `NULL`; dates/times and GUIDs are stored as strings; nested maps/rows are
  stored as JSON text.
- If `APPEND=FALSE` (the default), the delete-and-load operation runs inside a single Neo4j write
  transaction so failures roll back the replacement.
- Inside an engine `BEGIN TRANSACTION`, writes/truncates/native Cypher use the enlisted Neo4j
  transaction instead of their own per-operation transaction.
- In `SET WHAT_IF ON`, raw mutating Cypher in `EXECUTE` is skipped.

## Examples

```sql
-- Ingest customers as graph nodes
CREATE CONNECTION graph AS NEO4J(
    URI='bolt://localhost:7687',
    USER='neo4j',
    PASSWORD='password',
    KEY_COLUMNS='customer_id'
);
INSERT INTO graph.NODE_CUSTOMER (customer_id, name, city)
SELECT customer_id, name, city FROM #staging;

-- Native Cypher pass-through
DECLARE @minAge INT = 21;
EXECUTE graph INTO #fof_network WITH (@minAge)
BEGIN
    MATCH (p:Person)-[:FRIEND_OF]->()-[:FRIEND_OF]->(fof:Person)
    WHERE p.age >= ?1
    RETURN p.name AS source_name, fof.name AS fof_name
END;
```

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
