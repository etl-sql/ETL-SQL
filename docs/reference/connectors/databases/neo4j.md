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

> [!NOTE]
> `CONNECTION_STRING` / `URI` and structured `HOST` / `PORT` / `PROTOCOL` options are mutually
> exclusive: when `CONNECTION_STRING` or `URI` is supplied, `HOST`, `PORT`, and `PROTOCOL` are
> ignored.

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

## Write Path & Transactions

- **Bulk / write path** — `INSERT INTO` and `COPY INTO` against `NODE_<LABEL>` or `EDGE_<TYPE>` send batched parameterized Cypher `UNWIND $rows AS row` templates (`CREATE` by default, or `MERGE` when `KEY_COLUMNS` is set). Edge writes fail by default on missing/unmatched endpoints unless `SKIP_MISSING_ENDPOINTS=TRUE`. `DBNull.Value` writes as `NULL`; dates/times and GUIDs write as strings; nested maps/rows write as JSON text. `COPY INTO ... APPEND=FALSE` deletes existing nodes (`MATCH (n:Label) DETACH DELETE n`) or relationships (`MATCH ()-[r:TYPE]->() DELETE r`) before inserting, inside the same write transaction.
- **`MERGE` behavior** — SQL `MERGE` against a Neo4j virtual table runs in the engine's bounded in-memory merge and rewrites the virtual table via `WriteBatches`. For native graph upserts without reading the full label into engine memory, set `KEY_COLUMNS` on the `NEO4J` connection (which switches `INSERT INTO` to Cypher `UNWIND ... MERGE`) or write Cypher `MERGE` directly in `EXECUTE graph BEGIN ... END`.
- **Transactions** — Full `ITransactionalDataSource` support. Outside `BEGIN TRANSACTION`, each write runs inside `session.ExecuteWriteAsync` so an `APPEND=FALSE` delete-and-load rolls back atomically on failure. Inside `BEGIN TRANSACTION`, writes, truncates, and `EXECUTE graph BEGIN ... END` share the enlisted Neo4j session transaction (`COMMIT` persists; `ROLLBACK` discards).
- **`WHAT_IF` behavior** — Under `SET WHAT_IF ON`, `WriteBatches` and `TruncateAsync` are no-ops, and mutating Cypher inside `EXECUTE graph BEGIN ... END` is skipped.
- **Idempotent-load pattern** — Set `KEY_COLUMNS` on the connection and load with `INSERT INTO graph.NODE_CUSTOMER ...` (which emits Cypher `UNWIND ... MERGE ... SET n += row`), or run parameterized Cypher `MERGE` inside `EXECUTE graph BEGIN ... END` wrapped in `BEGIN TRANSACTION ... COMMIT`.

## Authentication

Neo4j supports basic authentication and URI-scheme TLS:
- **Basic Auth**: Supply `USER` (default `neo4j`) and `PASSWORD` options (supports `'ENC:...'`, `'SECRET:...'`, or `'${ENV}'` with `Security:AllowedEnvVars`).
- **Encryption**: TLS comes from the URI scheme (`neo4j+s://`, `bolt+s://`, `neo4j+ssc://`, or `bolt+ssc://`, or `PROTOCOL='neo4j+s'` / `'bolt+s'`) rather than a separate option.

## Examples

```sql
-- Ingest customers as graph nodes using TLS URI and a secret reference
CREATE CONNECTION graph AS NEO4J(
    URI='neo4j+s://graph.internal:7687',
    USER='neo4j',
    PASSWORD='SECRET:prod/neo4j/password',
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

## Troubleshooting

- **Invalid Credentials**: Verify password against Neo4j Browser or Aura console.
- **Bolt Protocol Error**: Ensure connecting to bolt/neo4j URI protocol (e.g. `bolt://host:7687` or `neo4j+s://host:7687`).
- **Cypher Pushdown**: Write native Cypher within `EXECUTE neo_conn BEGIN ... END`.

## References

- [Database Connectors](README.md)
- [Connectors](../README.md)
